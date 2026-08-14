// This file is part of MSIFlux, based on YAMDCC.
// Original Copyright © 2023-2025 Sparronator9999
// Modifications Copyright © 2026 weijuns.
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
// more details.
//
// You should have received a copy of the GNU General Public License along with
// This program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using MSIFlux.Common;
using MSIFlux.Common.Configs;
using MSIFlux.Common.Logs;
using MSIFlux.ECAccess;
using MSIFlux.IPC;

namespace MSIFlux.Service;

internal sealed partial class FanControlService : ServiceBase
{
    #region Fields

    /// <summary>
    /// The currently loaded MSIFlux config.
    /// </summary>
    private MSIFlux_Config Config;

    /// <summary>
    /// The named message pipe server that MSIFlux connects to.
    /// </summary>
    private readonly NamedPipeServer<ServiceCommand, ServiceResponse> IPCServer;

    private readonly Logger Log;

    private readonly EC _EC;

    private readonly System.Timers.Timer CooldownTimer = new(1000);

    /// <summary>Fn 热键轮询任务取消令牌</summary>
    private CancellationTokenSource? _hotkeyCts;

    /// <summary>上次 EC[0xC0] 的值, 用于去重</summary>
    private int _lastHotkeyCode = -1;

    /// <summary>热键处理后冷却截止时间 (UTC), 防止 ApplyConf 的 EC 写入触发反馈环路</summary>
    private DateTime _hotkeyCooldownUntil = DateTime.MinValue;

    private EcInfo EcInfo;

    private bool FullBlastEnabled;

    // ====== EC 热键调试寄存器常量 (引用自 Constants) ======
    private const byte EC_HOTKEY_DEBUG = EcRegs.HotkeyDebug;
    private const byte EC_HOTKEY_CTRL  = EcRegs.HotkeyCtrl;

    // ====== Fn 键 EC 编码 → 动作映射 ======
    private static readonly Dictionary<int, string> HotkeyCodeMap = HotkeyCodes.Map;
    #endregion

    /// <summary>
    /// Initialises a new instance of the <see cref="FanControlService"/> class.
    /// </summary>
    /// <param name="logger">
    /// The <see cref="Logger"/> instance to write logs to.
    /// </param>
    public FanControlService(Logger logger)
    {
        CanHandlePowerEvent = true;
        CanShutdown = true;

        Log = logger;
        _EC = new EC();

        PipeSecurity security = new();
        // 设置 IPC 管道 SDDL 权限: 允许 SYSTEM (SY)、管理员 (BA) 和普通已登录用户 (AU) 读写管道
        // 彻底解决普通用户双击打开 GUI 时连接管道被拒绝 (Access Denied) 的问题
        security.SetSecurityDescriptorSddlForm("O:BAG:SYD:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;AU)");

        CooldownTimer.Elapsed += new ElapsedEventHandler(CooldownElapsed);
        // HotkeyPollElapsed is now called from a Task loop in StartHotkeyPoller()

        IPCServer = new NamedPipeServer<ServiceCommand, ServiceResponse>("MSIFlux-Server", security);
        IPCServer.ClientConnected += new EventHandler<PipeConnectionEventArgs<ServiceCommand, ServiceResponse>>(IPCClientConnect);
        IPCServer.ClientDisconnected += new EventHandler<PipeConnectionEventArgs<ServiceCommand, ServiceResponse>>(IPCClientDisconnect);
        IPCServer.Error += new EventHandler<PipeErrorEventArgs<ServiceCommand, ServiceResponse>>(IPCServerError);
    }

    private bool _msiServicesRunning;

    #region Events
    protected override void OnStart(string[] args)
    {
        try
        {
            Log.Info(Strings.GetString("svcStarting"));

            // Don't try and start if MSI Center's services are running.
            // It is still possible to start MSI Center *after* MSIFlux Service,
            // but it is not recommended and will cause issues.
            if (Utils.IsMSIServiceRunning(out string[] svcs))
            {
                StringBuilder sb = new();
                foreach (string svc in svcs)
                {
                    sb.Append($"- {svc} ");
                }
                _msiServicesRunning = true;
                Log.Warn($"检测到官方 MSI 服务在运行 ({sb})。为避免 EC 寄存器争抢导致系统关机, EC 热键轮询与 Direct EC 写入将受限。");
            }

            // Install WinRing0 to get EC access (软加载: 被杀软拦截时降级走 WMI ACPI, 不抛异常崩服务)
            try
            {
                Log.Info(Strings.GetString("drvLoad"));
                if (!_EC.LoadDriver())
                {
                    Log.Warn("WinRing0 驱动加载受阻 (可能被杀软拦截或无硬件访问权限)。系统将自动降级为 WMI ACPI 模式运行。");
                }
                else
                {
                    Log.Info(Strings.GetString("drvLoadSuccess"));
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"WinRing0 驱动加载异常 ({ex.Message})。系统将自动降级为 WMI ACPI 模式运行。");
            }

            // Load the last applied MSIFlux config.
            bool confLoaded = LoadConf();

            // Set up IPC server
            Log.Info("Starting IPC server...");
            IPCServer.Start();

            Log.Info(Strings.GetString("svcStarted"));

            // Attempt to read default fan profile if it's pending:
            if (CommonConfig.GetECtoConfState() == ECtoConfState.PostReboot)
            {
                ECtoConf();
            }

            // Apply the fan profiles and charging threshold:
            if (confLoaded)
            {
                ApplyConf();
            }

            // 启动 EC 热键调试模式 + 400ms 轮询 EC[0xC0] (检测 Fn+F7 等硬件热键)
            StartHotkeyPoller();

            // 启动 WMI 热键事件监听 (root\WMI, 仿 BabaConsole StartMsiHotkeyWatchers)
            StartWmiHotkeyWatcher();

            // 启动 EC 硬件性能模式同步定时器 (500ms 轮询 EC 210 寄存器, 兜底检测)
            StartEcSyncTimer();
        }
        catch (Exception ex)
        {
            Log.Fatal(Strings.GetString("svcException", ex));
            throw;
        }
    }

    private void CooldownElapsed(object sender, ElapsedEventArgs e)
    {
        CooldownTimer.Stop();
    }



    protected override void OnStop()
    {
        StopSvc();
    }

    protected override void OnShutdown()
    {
        if (CommonConfig.GetECtoConfState() == ECtoConfState.PendingReboot)
        {
            CommonConfig.SetECtoConfState(ECtoConfState.PostReboot);
        }
        StopSvc();
    }

    private void StopSvc()
    {
        // disable Full Blast if it was enabled while running
        SetFullBlast(0);

        Log.Info(Strings.GetString("svcStopping"));

        // Stop Fn hotkey poller, WMI watcher, and EC state sync timer
        StopHotkeyPoller();
        StopWmiHotkeyWatcher();
        StopEcSyncTimer();

        // Stop the IPC server:
        Log.Info("Stopping IPC server...");
        IPCServer.Stop();

        // Uninstall WinRing0 to keep things clean
        Log.Info(Strings.GetString("drvUnload"));
        _EC.UnloadDriver();

        Log.Info(Strings.GetString("svcStopped"));
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        switch (powerStatus)
        {
            case PowerBroadcastStatus.ResumeCritical:
            case PowerBroadcastStatus.ResumeSuspend:
            case PowerBroadcastStatus.ResumeAutomatic:
                if (!CooldownTimer.Enabled)
                {
                    // fan settings get reset on sleep/restart
                    FullBlastEnabled = false;
                    // Re-apply the fan profiles after waking up from sleep:
                    Log.Info(Strings.GetString("svcWake"));
                    ApplyConf();
                    // 恢复后确保 EC 热键调试模式处于关闭状态
                    EnsureHotkeyDebugDisabled();
                    CooldownTimer.Start();
                }
                break;
        }
        return true;
    }

    private void IPCClientConnect(object sender, PipeConnectionEventArgs<ServiceCommand, ServiceResponse> e)
    {
        e.Connection.ReceiveMessage += new EventHandler<PipeMessageEventArgs<ServiceCommand, ServiceResponse>>(IPCClientMessage);
        Log.Info(Strings.GetString("ipcConnect", e.Connection.ID));
    }

    private void IPCClientDisconnect(object sender, PipeConnectionEventArgs<ServiceCommand, ServiceResponse> e)
    {
        e.Connection.ReceiveMessage -= new EventHandler<PipeMessageEventArgs<ServiceCommand, ServiceResponse>>(IPCClientMessage);
        Log.Info(Strings.GetString("ipcDC", e.Connection.ID));
    }

    private void IPCServerError(object sender, PipeErrorEventArgs<ServiceCommand, ServiceResponse> e)
    {
        Log.Error(Strings.GetString("ipcError", e.Connection.ID, e.Exception));
    }

    private void IPCClientMessage(object sender, PipeMessageEventArgs<ServiceCommand, ServiceResponse> e)
    {
        Command cmd = e.Message.Command;
        object[] args = e.Message.Arguments;
        int id = e.Connection.ID;

        switch (cmd)
        {
            case Command.Nothing:
                Log.Warn("Empty command received!");
                return;
            case Command.GetServiceVer:
                IPCServer.PushMessage(new ServiceResponse(Response.ServiceVer, Utils.GetRevision()), id);
                return;
            case Command.GetFirmVer:            HandleGetFirmVer(id); break;
            case Command.ReadECByte:            HandleReadECByte(id, args); break;
            case Command.WriteECByte:           HandleWriteECByte(id, args); break;
            case Command.ApplyConf:             HandleApplyConf(id); break;
            case Command.SetFullBlast:          HandleSetFullBlast(id, args); break;
            case Command.GetFanSpeed:           HandleGetFanSpeed(id, args); break;
            case Command.GetFanRPM:             HandleGetFanRPM(id, args); break;
            case Command.GetTemp:               HandleGetTemp(id, args); break;
            case Command.GetKeyLightBright:      HandleGetKeyLightBright(id); break;
            case Command.SetKeyLightBright:      HandleSetKeyLightBright(id, args); break;
            case Command.SetWinFnSwap:          HandleSetWinFnSwap(id, args); break;
            case Command.SetFanProf:            HandleSetFanProf(id, args); break;
            case Command.SetPerfMode:           HandleSetPerfMode(id, args); break;
            case Command.SetGpuMode:            HandleSetGpuMode(id, args); break;
            case Command.GetGpuMode:            HandleGetGpuMode(id); break;
            case Command.ReportGpuMode:         HandleReportGpuMode(id, args); break;
            case Command.ToggleMuteLed:
            case Command.ToggleMicLed:          HandleToggleLed(id, cmd); break;
            case Command.SetAudioMuteLed:       HandleSetLed(id, cmd, args, isMic: false); break;
            case Command.SetMicMuteLed:         HandleSetLed(id, cmd, args, isMic: true); break;
            default:
                Log.Error(Strings.GetString("errBadCmd", cmd));
                IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)cmd), id);
                break;
        }
    }
    #endregion

    // ====== IPC 命令处理方法 (从 IPCClientMessage switch 提取) ======

    private void HandleGetFirmVer(int id) { GetFirmVer(id); }

    private void HandleReadECByte(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not byte reg)
        { SendBadArgs(Command.ReadECByte, args, id); return; }
        if (LogECReadByte(reg, out byte value))
            IPCServer.PushMessage(new ServiceResponse(Response.ReadResult, reg, value), id);
        else
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.ReadECByte), id);
    }

    private void HandleWriteECByte(int id, object[] args)
    {
        if (args.Length != 2 || args[0] is not byte || args[1] is not byte)
        { SendBadArgs(Command.WriteECByte, args, id); return; }
        if (LogECWriteByte((byte)args[0], (byte)args[1]))
            IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.WriteECByte), id);
        else
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.WriteECByte), id);
    }

    private void HandleApplyConf(int id)
    {
        bool loaded = LoadConf();
        if (!loaded) { SendError(Command.ApplyConf, id); return; }
        IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.ApplyConf), id);
        _ = Task.Run(() =>
        {
            try { if (!ApplyConf()) Log.Warn("ApplyConf failed"); }
            catch (Exception ex) { Log.Error($"ApplyConf exception: {ex.Message}"); }
        });
    }

    private void HandleSetFullBlast(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int enable)
        { SendBadArgs(Command.SetFullBlast, args, id); return; }
        if (SetFullBlast(enable))
            IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetFullBlast), id);
        else
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.SetFullBlast), id);
    }

    private void HandleGetFanSpeed(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int fan)
        { SendBadArgs(Command.GetFanSpeed, args, id); return; }
        if (!GetFanSpeed(id, fan))
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.GetFanSpeed), id);
    }

    private void HandleGetFanRPM(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int fan)
        { SendBadArgs(Command.GetFanRPM, args, id); return; }
        if (!GetFanRPM(id, fan))
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.GetFanRPM), id);
    }

    private void HandleGetTemp(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int fan)
        { SendBadArgs(Command.GetTemp, args, id); return; }
        if (!GetTemp(id, fan))
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.GetTemp), id);
    }

    private void HandleGetKeyLightBright(int id) { GetKeyLight(id); }

    private void HandleSetKeyLightBright(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not byte brightness)
        { SendBadArgs(Command.SetKeyLightBright, args, id); return; }
        if (SetKeyLight(brightness))
            IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetKeyLightBright), id);
        else
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.SetKeyLightBright), id);
    }

    private void HandleSetWinFnSwap(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int enable)
        { SendBadArgs(Command.SetWinFnSwap, args, id); return; }
        if (Config.KeySwapConf is null) { SendError(Command.SetWinFnSwap, id); return; }
        var cfg = Config.KeySwapConf;
        switch (enable)
        {
            case -1: cfg.Enabled = !cfg.Enabled; break;
            case 0:  cfg.Enabled = false; break;
            case 1:  cfg.Enabled = true; break;
            default: SendBadArgs(Command.SetWinFnSwap, args, id); return;
        }
        if (SetWinFnSwap(cfg))
            IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetWinFnSwap), id);
        else
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.SetWinFnSwap), id);
    }

    private void HandleSetFanProf(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int fanProf)
        { SendBadArgs(Command.SetFanProf, args, id); return; }
        foreach (FanConf cfg in Config.FanConfs)
        {
            int count = cfg.FanCurveConfs?.Count ?? 0;
            if (count == 0) continue;
            cfg.CurveSel = fanProf < 0
                ? (cfg.CurveSel >= count - 1 ? 0 : cfg.CurveSel + 1)
                : Math.Max(0, Math.Min(fanProf, count - 1));
        }
        IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetFanProf), id);
        _ = Task.Run(() =>
        {
            try { if (!ApplyConf()) Log.Warn("SetFanProf ApplyConf failed"); }
            catch (Exception ex) { Log.Error($"SetFanProf ApplyConf exception: {ex.Message}"); }
        });
    }

    private void HandleSetPerfMode(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int perfMode)
        { SendBadArgs(Command.SetPerfMode, args, id); return; }
        if (Config.PerfModeConf is null) { SendError(Command.SetPerfMode, id); return; }
        var cfg = Config.PerfModeConf;
        int count = cfg.PerfModes?.Count ?? 0;
        if (count == 0) { SendError(Command.SetPerfMode, id); return; }

        _lastEcWriteTime = DateTime.UtcNow;
        cfg.ModeSel = perfMode < 0
            ? (cfg.ModeSel >= count - 1 ? 0 : cfg.ModeSel + 1)
            : Math.Max(0, Math.Min(perfMode, count - 1));

        IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetPerfMode), id);
        try { Config.Save(Paths.CurrentConf); }
        catch (Exception ex) { Log.Warn($"SetPerfMode save config failed: {ex.Message}"); }

        _ = Task.Run(() =>
        {
            try { if (!ApplyConf()) Log.Warn($"SetPerfMode ApplyConf failed (mode={perfMode})"); }
            catch (Exception ex) { Log.Error($"SetPerfMode ApplyConf exception: {ex.Message}"); }
        });
    }

    private void HandleSetGpuMode(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int gpuMode || gpuMode < 0 || gpuMode > 2)
        { SendBadArgs(Command.SetGpuMode, args, id); return; }
        var gpuTask = Task.Run(() => SetGpuMode(gpuMode));
        if (gpuTask.Wait(TimeSpan.FromSeconds(120)))
        {
            if (!gpuTask.Result)
                IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.SetGpuMode), id);
            else
                IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetGpuMode), id);
        }
        else
        {
            Log.Error("SetGpuMode timed out after 120s");
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)Command.SetGpuMode), id);
        }
    }

    private void HandleGetGpuMode(int id)
    {
        int mode = GetGpuMode();
        Log.Debug($"IPC GetGpuMode result: {mode}");
        if (mode >= 0)
            IPCServer.PushMessage(new ServiceResponse(Response.GpuModeResult, mode), id);
        else
            SendError(Command.GetGpuMode, id);
    }

    private void HandleReportGpuMode(int id, object[] args)
    {
        if (args.Length != 1 || args[0] is not int gpuMode || gpuMode < 0 || gpuMode > 2)
        { SendBadArgs(Command.ReportGpuMode, args, id); return; }
        SetCachedGpuMode(gpuMode);
        IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.ReportGpuMode), id);
    }

    private void HandleToggleLed(int id, Command cmd)
    {
        bool isMute = cmd == Command.ToggleMuteLed;
        IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)cmd), id);
        _ = Task.Run(() => { try { WmiToggleLed(isMute); } catch { } });
    }

    private void HandleSetLed(int id, Command cmd, object[] args, bool isMic)
    {
        if (args == null || args.Length == 0 || !(args[0] is bool on))
        {
            SendBadArgs(cmd, args, id);
            return;
        }
        IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)cmd), id);
        _ = Task.Run(() => { try { WmiSetLed(isMic, on); } catch { } });
    }

    private void SendBadArgs(Command cmd, object[] args, int? clientId = null)
    {
        Log.Error(Strings.GetString("errBadArgs", cmd, args));
        if (clientId.HasValue)
            IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)cmd), clientId.Value);
    }

    private void SendError(Command cmd, int clientId)
        => IPCServer.PushMessage(new ServiceResponse(Response.Error, (int)cmd), clientId);

    private bool LogECReadByte(byte reg, out byte value)
    {
        bool success = _EC.ReadByte(reg, out value);
        if (success)
        {
            Log.Debug(Strings.GetString("svcECRead", reg, value));
        }
        else
        {
            Log.Error(Strings.GetString("errECRead", reg, GetWin32Error(_EC.GetDriverError())));
        }
        return success;
    }

    private bool LogECReadWord(byte reg, out ushort value, bool bigEndian)
    {
        bool success = _EC.ReadWord(reg, out value, bigEndian);
        if (success)
        {
            Log.Debug(Strings.GetString("svcECRead", reg, value));
        }
        else
        {
            Log.Error(Strings.GetString("errECRead", reg, GetWin32Error(_EC.GetDriverError())));
        }
        return success;
    }

    private bool LogECWriteByte(byte reg, byte value)
    {
        bool success = _EC.WriteByte(reg, value);
        if (success)
        {
            Log.Debug(Strings.GetString("svcECWrote", reg));
        }
        else
        {
            Log.Error(Strings.GetString("errECWrite", reg, GetWin32Error(_EC.GetDriverError())));
        }
        return success;
    }

    private bool LoadConf(int? clientID = null)
    {
        Log.Info(Strings.GetString("cfgLoading"));

        try
        {
            Paths.EnsureCurrentConfigExists();
            Config = MSIFlux_Config.Load(Paths.CurrentConf);
            Log.Info(Strings.GetString("cfgLoaded"));

            if (clientID is not null)
            {
                IPCServer?.PushMessage(new ServiceResponse(
                    Response.ConfLoaded, clientID.Value));
            }

            if (Config.FirmVerSupported)
            {
                EcInfo = new();
                if (_EC.ReadString(0xA0, 0xC, out string ecVer) && ecVer.Length == 0xC)
                {
                    EcInfo.Version = ecVer;
                    Log.Debug($"EC firmware version: {ecVer}");
                }
                if (_EC.ReadString(0xAC, 0x10, out string ecDate) && ecDate.Length == 0x10)
                {
                    try
                    {
                        string temp = $"{ecDate.Substring(4, 4)}-{ecDate.Substring(0, 2)}-{ecDate.Substring(2, 2)}" +
                    $"T{ecDate.Substring(8, 2).Replace(' ', '0')}:{ecDate.Substring(11, 2)}:{ecDate.Substring(14, 2)}";
                        EcInfo.Date = DateTime.ParseExact(temp, "s", CultureInfo.InvariantCulture);
                        Log.Debug($"EC firmware date: {EcInfo.Date:G}");
                    }
                    catch (FormatException ex)
                    {
                        Log.Error($"Failed to parse EC firmware date: {ex.Message}");
                        Log.Debug($"EC firmware date (raw): {ecDate}");
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            if (ex is InvalidConfigException or InvalidOperationException)
            {
                Log.Error(Strings.GetString("cfgInvalid"));
            }
            else if (ex is FileNotFoundException)
            {
                Log.Warn(Strings.GetString("cfgNotFound"));
            }
            else
            {
                throw;
            }
            Config = null;
            return false;
        }
    }

    private bool ApplyConf()
    {
        if (Config is null)
        {
            return false;
        }

        Log.Info(Strings.GetString("cfgApplying"));
        bool success = true;

        // Write custom register values, if configured:
        if (Config.RegConfs?.Count > 0)
        {
            // RegConfs are deprecated and will be removed in a future release
            Log.Warn(Strings.GetString("warnRegConf"));

            int numRegConfs = Config.RegConfs.Count;
            for (int i = 0; i < numRegConfs; i++)
            {
                RegConf cfg = Config.RegConfs[i];
                Log.Info(Strings.GetString("svcWriteRegConfs", i + 1, numRegConfs));
                if (!LogECWriteByte(cfg.Reg, cfg.Enabled ? cfg.OnVal : cfg.OffVal))
                {
                    success = false;
                }
            }
        }

        // Write the fan profile to the appropriate registers for each fan:
        int numFanConfs = Config.FanConfs.Count;
        for (int i = 0; i < numFanConfs; i++)
        {
            FanConf cfg = Config.FanConfs[i];
            Log.Info(Strings.GetString("svcWriteFanConfs", cfg.Name, i + 1, numFanConfs));

            // Bug #8 fix: CurveSel 越界 / FanCurveConfs 为空的防御式检查, 避免后续索引崩溃
            int curveCount = cfg.FanCurveConfs?.Count ?? 0;
            if (curveCount == 0)
            {
                Log.Warn($"风扇 {cfg.Name} 无可用曲线配置, 跳过");
                continue;
            }
            int curveIndex = cfg.CurveSel;
            if (curveIndex < 0 || curveIndex >= curveCount)
            {
                Log.Warn($"风扇 {cfg.Name} 的 CurveSel={curveIndex} 越界 [0,{curveCount - 1}], 回落到 0");
                curveIndex = 0;
                cfg.CurveSel = 0;
            }

            FanCurveConf curveCfg = cfg.FanCurveConfs[curveIndex];
            if (curveCfg.TempThresholds == null)
            {
                Log.Warn($"风扇 {cfg.Name} 曲线 {curveCfg.Name} 无阈值数据, 跳过");
                continue;
            }

            // 优先尝试 WMI ACPI 写入，以便提供最优平滑重载与滞后退档
            bool wmiWritten = false;
            try
            {
                wmiWritten = WmiWriteFanCurve(cfg.Name, curveCfg);
            }
            catch (Exception ex)
            {
                Log.Warn($"[WMI Fan] 风扇 {cfg.Name} 曲线通过 WMI 写入异常: {ex.Message}");
            }

            if (wmiWritten)
            {
                Log.Info($"[WMI Fan] 风扇 {cfg.Name} 曲线已通过 WMI 成功应用，跳过物理寄存器改写。");
            }
            else
            {
                Log.Info($"[WMI Fan] 无法通过 WMI 应用，正在回退到 Direct EC 物理寄存器改写...");
                for (int j = 0; j < curveCfg.TempThresholds.Count; j++)
                {
                    TempThreshold t = curveCfg.TempThresholds[j];
                    if (!LogECWriteByte(cfg.FanCurveRegs[j], t.FanSpeed))
                    {
                        success = false;
                    }
                    if (j > 0)
                    {
                        if (!LogECWriteByte(cfg.UpThresholdRegs[j - 1], t.UpThreshold))
                        {
                            success = false;
                        }
                        // DownThreshold 寄存器也必须写入，否则 EC 固件的升降档阈值不匹配，
                        // 可能导致风扇无法正确响应温度变化，最终触发硬件热保护断电。
                        // 必须保证 Down < Up, 否则 EC 硬件比较器会判定迟滞环倒置。
                        byte downVal;
                        if (Config.OffsetDT)
                        {
                            int diff = t.UpThreshold - t.DownThreshold;
                            downVal = (diff > 0 && diff < 30) ? (byte)diff : (byte)4;
                        }
                        else
                        {
                            downVal = (t.DownThreshold < t.UpThreshold && t.DownThreshold > 0)
                                ? (byte)t.DownThreshold
                                : (byte)Math.Max(0, t.UpThreshold - 4);
                        }
                        if (!LogECWriteByte(cfg.DownThresholdRegs[j - 1], downVal))
                        {
                            success = false;
                        }
                    }
                }
            }

            // Write the performance mode - 单独处理，避免影响风扇设置
            if (i == numFanConfs - 1)
            {
                PerfModeConf pModeCfg = Config.PerfModeConf;
                if (pModeCfg is not null)
                {
                    Log.Info(Strings.GetString("svcWritePerfMode"));
                    int idx = pModeCfg.ModeSel;

                    if (!LogECWriteByte(pModeCfg.Reg, pModeCfg.PerfModes[idx].Value))
                    {
                        success = false;
                    }
                }
            }
        }

        // Write the charge threshold:
        ChargeLimitConf chgLimCfg = Config.ChargeLimitConf;
        if (chgLimCfg is not null)
        {
            Log.Info(Strings.GetString("svcWriteChgLim"));
            if (!LogECWriteByte(chgLimCfg.Reg, (byte)(chgLimCfg.MinVal + chgLimCfg.CurVal)))
            {
                success = false;
            }
        }

        // Write the fan mode
        FanModeConf fModeCfg = Config.FanModeConf;
        if (fModeCfg is not null)
        {
            Log.Info(Strings.GetString("svcWriteFanMode"));
            if (!LogECWriteByte(fModeCfg.Reg, fModeCfg.FanModes[fModeCfg.ModeSel].Value))
            {
                success = false;
            }
        }

        // Write the Win/Fn key swap setting
        KeySwapConf keySwapCfg = Config.KeySwapConf;
        if (keySwapCfg is not null)
        {
            if (!SetWinFnSwap(keySwapCfg))
            {
                success = false;
            }
        }
        return success;
    }

    private bool SetWinFnSwap(KeySwapConf cfg)
    {
        Log.Info(Strings.GetString("svcWriteKeySwap"));
        return LogECWriteByte(cfg.Reg,
            cfg.Enabled ? cfg.OnVal : cfg.OffVal);
    }

    private bool GetFanSpeed(int clientId, int fan)
    {
        if (Config is null)
        {
            return false;
        }

        fan = GetValidFanIndex(fan);
        FanConf cfg = Config.FanConfs[fan];

        if (LogECReadByte(cfg.SpeedReadReg, out byte speed))
        {
            IPCServer.PushMessage(new ServiceResponse(
                Response.FanSpeed, fan, (int)speed), clientId);
            return true;
        }
        return false;
    }

    private bool GetFanRPM(int clientId, int fan)
    {
        if (Config is null)
        {
            return false;
        }

        fan = GetValidFanIndex(fan);
        FanConf cfg = Config.FanConfs[fan];
        if (cfg.RPMConf is null)
        {
            return false;
        }
        FanRPMConf rpmCfg = cfg.RPMConf;
        bool success;
        ushort rpmValue;

        if (rpmCfg.Is16Bit)
        {
            success = LogECReadWord(rpmCfg.ReadReg, out rpmValue, rpmCfg.IsBigEndian);
        }
        else
        {
            success = LogECReadByte(rpmCfg.ReadReg, out byte rpmValByte);
            rpmValue = rpmValByte;
        }

        if (success)
        {
            int rpm = ComputeRpm(rpmCfg, rpmValue);
            IPCServer.PushMessage(new ServiceResponse(
                Response.FanRPM, fan, rpm), clientId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 根据 <see cref="FanRPMConf"/> 的乘除/反相设置把 EC 读回的原始值换算成 RPM.
    /// 做了以下保护:
    /// - RPMMult==0 时不会除零 (DivideByMult)
    /// - Invert 模式下仅当结果>0 才取倒数
    /// - 所有 NaN/Infinity 统一归零
    /// </summary>
    internal static int ComputeRpm(FanRPMConf rpmCfg, ushort rpmValue)
    {
        if (rpmValue == 0) return 0;

        float rpm;
        if (rpmCfg.DivideByMult)
        {
            if (rpmCfg.RPMMult == 0) return 0;
            rpm = (float)rpmValue / rpmCfg.RPMMult;
        }
        else
        {
            rpm = (float)rpmValue * rpmCfg.RPMMult;
        }

        if (rpmCfg.Invert)
        {
            if (rpm <= 0) return 0;
            rpm = 1f / rpm;
        }

        if (float.IsNaN(rpm) || float.IsInfinity(rpm) || rpm < 0) return 0;
        return (int)rpm;
    }

    private bool GetTemp(int clientId, int fan)
    {
        if (Config is null)
        {
            return false;
        }

        fan = GetValidFanIndex(fan);
        FanConf cfg = Config.FanConfs[fan];

        if (LogECReadByte(cfg.TempReadReg, out byte temp))
        {
            IPCServer.PushMessage(new ServiceResponse(
                Response.Temp, fan, (int)temp), clientId);
            return true;
        }
        return false;
    }

    private bool SetFullBlast(int enable)
    {
        if (Config?.FullBlastConf is null)
        {
            return false;
        }

        FullBlastConf fbCfg = Config.FullBlastConf;
        if (LogECReadByte(fbCfg.Reg, out byte value))
        {
            bool oldFbEnable = FullBlastEnabled;

            if (enable == -1)
            {
                FullBlastEnabled = !FullBlastEnabled;
            }
            else if (enable == 0)
            {
                FullBlastEnabled = false;
            }
            else if (enable == 1)
            {
                FullBlastEnabled = true;
            }
            else
            {
                // invalid Full Blast value
                return false;
            }

            if (FullBlastEnabled)
            {
                Log.Debug("Enabling Full Blast...");
                value |= fbCfg.Mask;
            }
            else
            {
                Log.Debug("Disabling Full Blast...");
                value &= (byte)~fbCfg.Mask;
            }

            if (LogECWriteByte(fbCfg.Reg, value))
            {
                return true;
            }
            // failed to change full blast state; revert to old full blast enabled
            FullBlastEnabled = oldFbEnable;
        }
        return false;
    }

    private bool GetKeyLight(int clientId)
    {
        if (Config?.KeyLightConf is null)
        {
            return false;
        }

        Log.Debug(Strings.GetString("svcGetKeyLight"));

        KeyLightConf klCfg = Config.KeyLightConf;
        if (LogECReadByte(klCfg.Reg, out byte value) &&
            value >= klCfg.MinVal && value <= klCfg.MaxVal)
        {
            int brightness = value - klCfg.MinVal;

            IPCServer.PushMessage(new ServiceResponse(
                Response.KeyLightBright, brightness), clientId);
            return true;
        }
        return false;
    }

    private bool SetKeyLight(byte brightness)
    {
        if (Config?.KeyLightConf is null)
        {
            return false;
        }

        Log.Debug(Strings.GetString("svcSetKeyLight", brightness));

        KeyLightConf klCfg = Config.KeyLightConf;
        byte value = (byte)(brightness + klCfg.MinVal);
        return value >= klCfg.MinVal && value <= klCfg.MaxVal &&
            LogECWriteByte(klCfg.Reg, value);
    }

    private bool GetFirmVer(int clientId)
    {
        if (Config is null || !Config.FirmVerSupported)
        {
            return false;
        }

        Log.Debug(Strings.GetString("svcGerFirmVer", clientId));
        IPCServer.PushMessage(new ServiceResponse(Response.FirmVer, EcInfo), clientId);
        return true;
    }

    private bool ECtoConf()
    {
        if (Config is null)
        {
            return false;
        }

        try
        {
            Log.Info(Strings.GetString("svcReadModel"));

            string pcManufacturer = Utils.GetPCManufacturer(),
                pcModel = Utils.GetPCModel();

            if (string.IsNullOrEmpty(pcManufacturer))
            {
                Log.Error(Strings.GetString("errReadManufacturer"));
            }
            else
            {
                Config.Manufacturer = pcManufacturer;
            }

            if (string.IsNullOrEmpty(pcModel))
            {
                Log.Error(Strings.GetString("errReadModel"));
            }
            else
            {
                Config.Model = pcModel;
            }

            if (Config.FirmVerSupported)
            {
                Config.FirmVer = EcInfo.Version;
                Config.FirmDate = EcInfo.Date;
            }
            else
            {
                Config.FirmVer = null;
                Config.FirmDate = null;
            }

            for (int i = 0; i < Config.FanConfs.Count; i++)
            {
                Log.Info(Strings.GetString("svcReadProfs", i + 1, Config.FanConfs.Count));

                FanConf cfg = Config.FanConfs[i];

                // look for an already existing Default fan profile
                FanCurveConf curveCfg = null;
                for (int j = 0; j < cfg.FanCurveConfs.Count; j++)
                {
                    if (cfg.FanCurveConfs[j].Name == "Default")
                    {
                        curveCfg = cfg.FanCurveConfs[j];
                    }
                }

                // there isn't already a Default fan profile in this config,
                // make one and insert it at the start
                if (curveCfg is null)
                {
                    // Bug #11 fix: original code only set List capacity, leaving Count=0,
                    // so the subsequent for-loop iterated 0 times and EC registers were never
                    // read -> the "Default" curve was permanently empty.
                    // Here we pre-populate FanCurveRegs.Length default TempThreshold items.
                    int thresholdCount = cfg.FanCurveRegs?.Length ?? 0;
                    var thresholds = new List<TempThreshold>(thresholdCount);
                    for (int k = 0; k < thresholdCount; k++)
                    {
                        thresholds.Add(new TempThreshold());
                    }
                    curveCfg = new()
                    {
                        Name = "Default",
                        TempThresholds = thresholds,
                    };
                    cfg.FanCurveConfs.Insert(0, curveCfg);
                    cfg.CurveSel++;
                }

                // reset each fan's first fan profile descriptions
                curveCfg.Desc = Strings.GetString("DefaultDesc");

                for (int j = 0; j < curveCfg.TempThresholds.Count; j++)
                {
                    curveCfg.TempThresholds[j] ??= new();
                    TempThreshold t = curveCfg.TempThresholds[j];

                    if (LogECReadByte(cfg.FanCurveRegs[j], out byte value))
                    {
                        if (value < cfg.MinSpeed || value > cfg.MaxSpeed)
                        {
                            CommonConfig.SetECtoConfState(ECtoConfState.Fail);
                            return false;
                        }
                        t.FanSpeed = value;
                    }

                    if (j == 0)
                    {
                        t.UpThreshold = 0;
                        t.DownThreshold = 0;
                    }
                    else
                    {
                        if (LogECReadByte(cfg.UpThresholdRegs[j - 1], out value))
                        {
                            t.UpThreshold = value;
                        }
                        if (LogECReadByte(cfg.DownThresholdRegs[j - 1], out value))
                        {
                            t.DownThreshold = Config.OffsetDT
                                ? (byte)(t.UpThreshold - value)
                                : value;
                        }
                    }
                }
            }

            Log.Info("Saving config...");
            Config.Save(Paths.CurrentConf);

            CommonConfig.SetECtoConfState(ECtoConfState.Success);
            return true;
        }
        catch
        {
            CommonConfig.SetECtoConfState(ECtoConfState.Fail);
            return false;
        }
    }

    private static string GetWin32Error(int error)
    {
        return new Win32Exception(error).Message;
    }

    private int GetValidFanIndex(int i)
    {
        // clamp provided i value to valid FanConf range
        return i >= Config.FanConfs.Count
            ? Config.FanConfs.Count - 1
            : i > 0 ? i : 0;
    }

    // ==================================================================
    // WMI 热键事件监听 (仿 BabaConsole StartMsiHotkeyWatchers)
    // 监听 root\WMI 下的 MSI 热键事件类, 检测 Fn+F7 (code=118)
    // ==================================================================

    private readonly List<System.Management.ManagementEventWatcher> _wmiHotkeyWatchers = new();
    private static readonly string[] WmiHotkeyClasses = { "WMIEvent", "MSIEvent", "MSI_ACPI" };

    private void StartWmiHotkeyWatcher()
    {
        StopWmiHotkeyWatcher();
        try
        {
            var scope = new System.Management.ManagementScope(@"\\.\root\WMI");
            scope.Options.EnablePrivileges = true;
            scope.Connect();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string cls in WmiHotkeyClasses)
            {
                if (!added.Add(cls)) continue;
                try
                {
                    var q = new System.Management.WqlEventQuery("SELECT * FROM " + cls);
                    var w = new System.Management.ManagementEventWatcher(scope, q);
                    w.EventArrived += OnWmiHotkeyEvent;
                    w.Start();
                    _wmiHotkeyWatchers.Add(w);
                    Log.Info($"WMI 热键: 已监听 {cls}");
                }
                catch (Exception ex)
                {
                    Log.Debug($"WMI 热键: {cls} 监听失败 - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"WMI 热键: 初始化失败 - {ex.Message}");
        }
    }

    private void StopWmiHotkeyWatcher()
    {
        foreach (var w in _wmiHotkeyWatchers)
        {
            try { w.EventArrived -= OnWmiHotkeyEvent; w.Stop(); w.Dispose(); } catch { }
        }
        _wmiHotkeyWatchers.Clear();
    }

    private void OnWmiHotkeyEvent(object sender, System.Management.EventArrivedEventArgs e)
    {
        try
        {
            // 提取事件中的所有数值属性
            var nums = new List<int>();
            foreach (System.Management.PropertyData pd in e.NewEvent.Properties)
            {
                try
                {
                    if (pd.Value is int i) nums.Add(i);
                    else if (pd.Value is uint u) nums.Add((int)u);
                    else if (pd.Value is byte b) nums.Add(b);
                    else if (pd.Value is short s) nums.Add(s);
                }
                catch { }
            }

            string cls = e.NewEvent.ClassPath?.ClassName ?? "?";
            Log.Info($"WMI 热键事件: {cls}, 数值=[{string.Join(",", nums)}]");

            // 检测 Fn+F6 (code=87) → 摄像头
            if (nums.Contains(87))
            {
                Log.Info("WMI 热键: Fn+F6 (87) → 摄像头切换");
                IPCServer.PushMessage(new ServiceResponse(Response.CamToggled), -1);
            }

            // 检测 Fn+F7 (code=118)
            if (nums.Contains(118))
            {
                Log.Info("WMI 热键: Fn+F7 (118) → 切换性能模式");
                CyclePerfModeFromHotkey();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"WMI 热键事件处理异常: {ex.Message}");
        }
    }

    private void CyclePerfModeFromHotkey()
    {
        try
        {
            if (Config?.PerfModeConf is null) return;
            var cfg = Config.PerfModeConf;
            int cnt = cfg.PerfModes?.Count ?? 0;
            if (cnt == 0) return;
            _lastEcWriteTime = DateTime.UtcNow;
            int oldSel = cfg.ModeSel;
            cfg.ModeSel = cfg.ModeSel >= cnt - 1 ? 0 : cfg.ModeSel + 1;
            try { Config.Save(Paths.CurrentConf); } catch { }
            Log.Info($"Fn+F7: {cfg.PerfModes[oldSel].Name} → {cfg.PerfModes[cfg.ModeSel].Name}");
            IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetPerfMode), -1);
            _ = Task.Run(() => { try { ApplyConf(); } catch { } });
        }
        catch (Exception ex) { Log.Warn($"CyclePerfMode 异常: {ex.Message}"); }
    }

    // ==================================================================
    // Fn 热键检测 — EC 调试寄存器轮询 (零依赖 FM / BabaConsole)
    // ==================================================================

    /// <summary>开启 EC 热键调试模式并启动 Task 轮询循环 (替代 Timer, 在服务中更可靠)</summary>
    private void StartHotkeyPoller()
    {
        try
        {
            if (_msiServicesRunning)
            {
                Log.Info("Fn 热键: 检测到官方 MSI 服务在运行, 跳过 EC 热键轮询以避免双写冲突");
                return;
            }

            _hotkeyCts?.Cancel();
            _hotkeyCts = new CancellationTokenSource();
            var token = _hotkeyCts.Token;

            // 验证 EC 可访问
            if (!_EC.ReadByte(EC_HOTKEY_CTRL, out byte c1Init))
            {
                Log.Warn("Fn 热键: 无法读取 EC[0xC1], 轮询取消");
                return;
            }
            Log.Info($"Fn 热键: EC[0xC1] 初始值 0x{c1Init:X2}, 启动 Task 轮询循环 (400ms)");
            _lastHotkeyCode = 0;

            _ = Task.Run(async () =>
            {
                int loopCount = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        HotkeyPollElapsed();
                        // 每 25 次 (10秒) 打印一次心跳, 证明轮询在运行
                        if (++loopCount % 25 == 0)
                            Log.Debug($"Fn 热键: 轮询心跳 #{loopCount}, EC[0xC1] 就绪");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Fn 热键: 轮询异常 - {ex.Message}");
                    }
                    await Task.Delay(400, token).ConfigureAwait(false);
                }
                Log.Info("Fn 热键: 轮询循环已退出");
            }, token);
        }
        catch (Exception ex)
        {
            Log.Warn($"Fn 热键: 初始化失败 - {ex.Message}");
        }
    }

    /// <summary>停止轮询并关闭 EC 热键调试模式</summary>
    private void StopHotkeyPoller()
    {
        try
        {
            _hotkeyCts?.Cancel();
            Log.Info("Fn 热键: 轮询已停止");
        }
        catch { }

        try
        {
            if (_EC.ReadByte(EC_HOTKEY_CTRL, out byte c1) && (c1 & 0x80) != 0)
            {
                _EC.WriteByte(EC_HOTKEY_CTRL, (byte)(c1 & 0x7F));
                Log.Info("Fn 热键: 调试模式已关闭");
            }
        }
        catch { }
    }

    /// <summary>确保 EC 热键调试模式处于关闭状态 (清除 0xC1 bit7)</summary>
    private void EnsureHotkeyDebugDisabled()
    {
        try
        {
            if (_EC.ReadByte(EC_HOTKEY_CTRL, out byte c1) && (c1 & 0x80) != 0)
            {
                _EC.WriteByte(EC_HOTKEY_CTRL, (byte)(c1 & 0x7F));
                Log.Info("Fn 热键: 调试模式 0xC1 bit7 已关闭, 恢复硬件键盘正常状态");
            }
        }
        catch { }
    }

    // ==================================================================
    // EC 硬件性能模式状态同步器 (500ms 轮询 EC 210 寄存器)
    // 零键盘钩子、零驱动调试模式、零误触 'W' 键风险。
    // 当用户按下笔记本键盘的 Fn+F7 时，硬件 EC 会直接更新 210 寄存器。
    // 此同步器检测到 EC[210] 变化后，自动同步 MSI Flux 的 Config 并通知 GUI。
    // ==================================================================

    private readonly System.Timers.Timer EcSyncTimer = new(2000);
    private DateTime _lastEcWriteTime = DateTime.MinValue;

    private void StartEcSyncTimer()
    {
        EcSyncTimer.Elapsed -= OnEcSyncElapsed;
        EcSyncTimer.Elapsed += OnEcSyncElapsed;
        EcSyncTimer.AutoReset = true;
        EcSyncTimer.Start();
        Log.Info("EC 硬件状态同步定时器已启动 (2000ms 轮询 EC 210 寄存器)");
    }

    private void StopEcSyncTimer()
    {
        EcSyncTimer.Stop();
    }

    private void OnEcSyncElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            // 如果最近 1.5 秒内服务刚主动写过 EC，跳过硬件同步，避免竞态
            if ((DateTime.UtcNow - _lastEcWriteTime).TotalSeconds < 1.5) return;

            if (Config?.PerfModeConf is null) return;
            var cfg = Config.PerfModeConf;
            if (cfg.PerfModes is null || cfg.PerfModes.Count == 0) return;

            // 读取 EC 寄存器 210
            if (_EC.ReadByte(cfg.Reg, out byte val))
            {
                // 查找与当前 EC 寄存器值匹配的模式索引
                for (int i = 0; i < cfg.PerfModes.Count; i++)
                {
                    if (cfg.PerfModes[i].Value == val)
                    {
                        if (cfg.ModeSel != i)
                        {
                            int oldIdx = cfg.ModeSel;
                            cfg.ModeSel = i;
                            try { Config.Save(Paths.CurrentConf); } catch { }
                            Log.Info($"EC 硬件同步: 检测到硬件 Fn+F7 切换性能模式 ➔ {cfg.PerfModes[i].Name} ({cfg.PerfModes[oldIdx].Name} → {cfg.PerfModes[i].Name}, EC[0x{cfg.Reg:X2}]=0x{val:X2})");
                            IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetPerfMode), -1);
                        }
                        break;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>轮询回调: 读 EC[0xC0], 匹配编码 → 执行对应动作 (由 Task 循环调用)</summary>
    private int _hotkeyPollCount;
    private bool _hotkeyDebugEnabled;
    private void HotkeyPollElapsed()
    {
        try
        {
            // 只在首次或 bit7 被意外清除时启用热键调试模式, 不每轮都强制写回
            // 高频改写 EC[0xC1] 可能与 MSI Center 产生寄存器冲突导致 EC 层强制断电
            if (!_EC.ReadByte(EC_HOTKEY_CTRL, out byte c1))
            {
                if (++_hotkeyPollCount % 30 == 0)
                    Log.Warn($"Fn 热键: EC[0xC1] 读取失败 ({_hotkeyPollCount} 次)");
                return;
            }
            if ((c1 & 0x80) == 0)
            {
                if (!_hotkeyDebugEnabled)
                {
                    byte c1New = (byte)(c1 | 0x80);
                    _EC.WriteByte(EC_HOTKEY_CTRL, c1New);
                    _hotkeyDebugEnabled = true;
                    Log.Info($"Fn 热键: 调试模式已开启 (EC[0xC1]: 0x{c1:X2} → 0x{c1New:X2})");
                }
                else
                {
                    // MSI Center 可能已清除 bit7, 不争夺, 避免双向冲突
                    Log.Debug($"Fn 热键: EC[0xC1] bit7 已被清除 (0x{c1:X2}), 放弃轮询");
                    return;
                }
            }
            // 诊断: 每 25 次打印一次 C1 和 C0 的值 (Info 级别确保可见)
            if (_hotkeyPollCount % 25 == 0)
                Log.Info($"Fn 热键诊断: EC[0xC1]=0x{c1:X2}, 轮询 #{_hotkeyPollCount}");
            _hotkeyPollCount++;

            if (!_EC.ReadByte(EC_HOTKEY_DEBUG, out byte code)) return;

            // 非零值始终打印 (Info级别)
            if (code != 0)
                Log.Info($"Fn 热键: EC[0xC0]=0x{code:X2} (非零!)");

            // EC 值为 0 表示无热键或已清零, 忽略
            if (code == 0)
            {
                _lastHotkeyCode = 0;
                return;
            }

            // 去重: 同一个编码只处理一次 (清零后 _lastHotkeyCode 会归零, 下次才处理新值)
            if (code == _lastHotkeyCode) return;
            _lastHotkeyCode = code;

            // 未知编码记录以便将来添加支持
            if (!HotkeyCodeMap.TryGetValue(code, out string? name))
            {
                Log.Info($"Fn 热键: 未映射编码 0x{code:X2}");
                // 仍然清零, 避免残留值影响下次检测
                _EC.WriteByte(EC_HOTKEY_DEBUG, 0);
                return;
            }

            Log.Info($"Fn 热键: 0x{code:X2} → {name}");

            switch (code)
            {
                case 87: // Fn+F6 → 摄像头: 通知 GUI 显示 OSD
                    Log.Info($"Fn 热键: 0x{code:X2} → {name} → 摄像头切换");
                    IPCServer.PushMessage(new ServiceResponse(Response.CamToggled), -1);
                    break;

                case 118: // Fn+F7 → 场景模式: 服务端直接循环切换
                    Log.Info($"Fn 热键: 0x{code:X2} → {name} → 执行性能模式切换");
                    try
                    {
                        if (Config?.PerfModeConf is not null)
                        {
                            var pmCfg = Config.PerfModeConf;
                            int cnt = pmCfg.PerfModes?.Count ?? 0;
                            if (cnt > 0)
                            {
                                _lastEcWriteTime = DateTime.UtcNow;
                                int oldSel = pmCfg.ModeSel;
                                pmCfg.ModeSel = pmCfg.ModeSel >= cnt - 1 ? 0 : pmCfg.ModeSel + 1;
                                try { Config.Save(Paths.CurrentConf); } catch { }
                                Log.Info($"Fn+F7: {pmCfg.PerfModes[oldSel].Name} → {pmCfg.PerfModes[pmCfg.ModeSel].Name}");
                                IPCServer.PushMessage(new ServiceResponse(Response.Success, (int)Command.SetPerfMode), -1);
                                _ = Task.Run(() => { try { ApplyConf(); } catch { } });
                            }
                        }
                    }
                    catch (Exception ex) { Log.Warn($"Fn+F7 切换异常: {ex.Message}"); }
                    break;

                case 38: // Fn+↑ → Cooler Boost 切换
                    SetFullBlast(-1);
                    break;

                case 119: // Fn+F8 → 键盘背光循环
                    if (Config?.KeyLightConf is not null)
                    {
                        int curLevel = GetKeyLightLevel();
                        int nextLevel = (curLevel + 1) % 4;
                        byte nextVal = (byte)(Config.KeyLightConf.MinVal + nextLevel);
                        if (LogECWriteByte(Config.KeyLightConf.Reg, nextVal))
                            Log.Info($"Fn 热键: 键盘背光 → {nextLevel}/3");
                    }
                    break;

                case 27: // Fn+Esc → Win/Fn 互换
                    if (Config?.KeySwapConf is not null)
                    {
                        Config.KeySwapConf.Enabled = !Config.KeySwapConf.Enabled;
                        SetWinFnSwap(Config.KeySwapConf);
                        Log.Info($"Fn 热键: Win/Fn → {(Config.KeySwapConf.Enabled ? "已互换" : "正常")}");
                    }
                    break;
            }

            // 清除 EC[0xC0] 通知 BIOS 热键已被消费 (仿 BabaConsole TryClearMsiHotkeyDebugValue)
            // 清零后 _lastHotkeyCode 保持当前值, 下次 EC[0xC0] 归零后 _lastHotkeyCode 也归零,
            // 从而能正确检测同一热键的下次按下.
            _EC.WriteByte(EC_HOTKEY_DEBUG, 0);
        }
        catch (Exception ex)
        {
            Log.Warn($"Fn 热键: 轮询异常 - {ex.Message}");
        }
    }

    /// <summary>读当前键盘背光亮度级别 (0-3), 失败返回 0</summary>
    private int GetKeyLightLevel()
    {
        if (Config?.KeyLightConf is null) return 0;
        if (_EC.ReadByte(Config.KeyLightConf.Reg, out byte val)
            && val >= Config.KeyLightConf.MinVal
            && val <= Config.KeyLightConf.MaxVal)
        {
            return val - Config.KeyLightConf.MinVal;
        }
        return 0;
    }

    // ====== WMI ACPI LED 控制 (SYSTEM 权限) ======
    private static readonly byte[] LedRegs = EcRegs.LedRegs;

    private void WmiToggleLed(bool muteLed)
    {
        int moCount = 0, okCount = 0, failCount = 0;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\wmi", "SELECT * FROM MSI_ACPI");
            foreach (System.Management.ManagementObject mo in searcher.Get())
            {
                moCount = 1;
                foreach (byte reg in LedRegs)
                {
                    try
                    {
                        var rPkg = new System.Management.ManagementClass(@"root\wmi:Package_32", null).CreateInstance();
                        var rBuf = new byte[32]; rBuf[0] = reg;
                        rPkg["Bytes"] = rBuf;
                        var rIn = mo.GetMethodParameters("Get_Data"); rIn["Data"] = rPkg;
                        var rOut = mo.InvokeMethod("Get_Data", rIn, null);
                        byte curVal = 0;
                        if (rOut?["Data"] is System.Management.ManagementBaseObject rObj)
                            foreach (System.Management.PropertyData pd in rObj.Properties)
                                if (pd.IsArray && pd.Value is byte[] r && r.Length > 1) { curVal = r[1]; break; }
                        byte bit = muteLed ? (byte)0 : (byte)1;
                        byte next = (byte)(curVal ^ (byte)(1 << bit));
                        var wPkg = new System.Management.ManagementClass(@"root\wmi:Package_32", null).CreateInstance();
                        var wBuf = new byte[32]; wBuf[0] = reg; wBuf[1] = next;
                        wPkg["Bytes"] = wBuf;
                        var wIn = mo.GetMethodParameters("Set_Data"); wIn["Data"] = wPkg;
                        mo.InvokeMethod("Set_Data", wIn, null);
                        okCount++;
                    }
                    catch { failCount++; }
                }
                break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"WMI LED exception: {ex.Message}");
            return;
        }
        Log.Info($"WMI LED: {moCount} MSI_ACPI, ok={okCount} fail={failCount}, muteLed={(muteLed ? "F1" : "F5")}");
    }

    private void WmiSetLed(bool isMic, bool on)
    {
        // 控制 F1 (主音量静音) 与 F5 (麦克风禁用) 物理按键指示灯:
        //   Mic  Mute LED (F5): 逻辑/EC 地址 44 (0x2C), bit 1 (0x02 亮, 0x00 灭)
        //   Audio Mute LED (F1): 逻辑/EC 地址 45 (0x2D), bit 1 (0x02 亮, 0x00 灭)
        byte wmiAddr = isMic ? (byte)44 : (byte)45;
        string label = isMic ? "Mic(F5)" : "Audio(F1)";

        bool wmiSuccess = false;

        // 1. 优先尝试 WMI ACPI (支持 MSI_ACPI 与 MSI_ACPI2)
        try
        {
            string[] wmiClasses = new string[] { "MSI_ACPI", "MSI_ACPI2" };
            foreach (var cls in wmiClasses)
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\wmi", $"SELECT * FROM {cls}");
                var list = searcher.Get();
                if (list == null || list.Count == 0) continue;

                foreach (System.Management.ManagementObject mo in list)
                {
                    var rPkg = new System.Management.ManagementClass(@"root\wmi:Package_32", null).CreateInstance();
                    var rBuf = new byte[32]; rBuf[0] = wmiAddr;
                    rPkg["Bytes"] = rBuf;
                    var rIn = mo.GetMethodParameters("Get_Data"); rIn["Data"] = rPkg;
                    var rOut = mo.InvokeMethod("Get_Data", rIn, null);
                    byte curVal = 0;
                    if (rOut?["Data"] is System.Management.ManagementBaseObject rObj)
                        foreach (System.Management.PropertyData pd in rObj.Properties)
                            if (pd.IsArray && pd.Value is byte[] r && r.Length > 1) { curVal = r[1]; break; }

                    byte next = on ? (byte)(curVal | 0x02) : (byte)(curVal & ~0x02);

                    var wPkg = new System.Management.ManagementClass(@"root\wmi:Package_32", null).CreateInstance();
                    var wBuf = new byte[32]; wBuf[0] = wmiAddr; wBuf[1] = next;
                    wPkg["Bytes"] = wBuf;
                    var wIn = mo.GetMethodParameters("Set_Data"); wIn["Data"] = wPkg;
                    mo.InvokeMethod("Set_Data", wIn, null);

                    Log.Info($"LED {label} -> {(on ? "ON" : "OFF")} via WMI {cls} (0x{wmiAddr:X2} bit1, 0x{curVal:X2}->0x{next:X2})");
                    wmiSuccess = true;
                    break;
                }
                if (wmiSuccess) break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"LED {label} WMI 控制未成功 ({ex.Message})，准备回退到 Direct EC 改写...");
        }

        // 2. 若 WMI 不可用或类不存在（ManagementException - 无效类），自动回退到 Direct EC 物理寄存器改写
        if (!wmiSuccess)
        {
            try
            {
                if (_EC.ReadByte(wmiAddr, out byte ecVal))
                {
                    byte nextEc = on ? (byte)(ecVal | 0x02) : (byte)(ecVal & ~0x02);
                    if (_EC.WriteByte(wmiAddr, nextEc))
                    {
                        Log.Info($"LED {label} -> {(on ? "ON" : "OFF")} via Direct EC (0x{wmiAddr:X2} bit1, 0x{ecVal:X2}->0x{nextEc:X2})");
                        return;
                    }
                }

                // 读失败时直接强改 0x02 / 0x00
                byte directVal = on ? (byte)0x02 : (byte)0x00;
                if (_EC.WriteByte(wmiAddr, directVal))
                {
                    Log.Info($"LED {label} -> {(on ? "ON" : "OFF")} via Direct EC 强制写入 0x{wmiAddr:X2}=0x{directVal:X2}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"LED {label} Direct EC 改写异常: {ex.Message}");
            }
        }
    }

    private byte[] WmiGetAcpiBytes(string methodName, byte parameter)
    {
        try
        {
            string[] wmiClasses = new string[] { "MSI_ACPI", "MSI_ACPI2" };
            foreach (var cls in wmiClasses)
            {
                using var searcher = new System.Management.ManagementObjectSearcher(@"root\wmi", $"SELECT * FROM {cls}");
                var list = searcher.Get();
                if (list == null || list.Count == 0) continue;
                foreach (System.Management.ManagementObject mo in list)
                {
                    var pkg = new System.Management.ManagementClass($"root\\wmi:Package_32", null).CreateInstance();
                    var buf = new byte[32];
                    buf[0] = parameter;
                    pkg["Bytes"] = buf;
                    
                    var inParams = mo.GetMethodParameters(methodName);
                    inParams["Data"] = pkg;
                    var outParams = mo.InvokeMethod(methodName, inParams, null);
                    if (outParams?["Data"] is System.Management.ManagementBaseObject outObj)
                    {
                        foreach (System.Management.PropertyData pd in outObj.Properties)
                        {
                            if (pd.IsArray && pd.Value is byte[] result)
                            {
                                return result;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"WmiGetAcpiBytes {methodName}({parameter}) 失败: {ex.Message}");
        }
        return null;
    }

    private bool WmiSetAcpiBytes(string methodName, byte parameter, byte[] data)
    {
        try
        {
            string[] wmiClasses = new string[] { "MSI_ACPI", "MSI_ACPI2" };
            bool success = false;
            foreach (var cls in wmiClasses)
            {
                using var searcher = new System.Management.ManagementObjectSearcher(@"root\wmi", $"SELECT * FROM {cls}");
                var list = searcher.Get();
                if (list == null || list.Count == 0) continue;
                
                foreach (System.Management.ManagementObject mo in list)
                {
                    var pkg = new System.Management.ManagementClass($"root\\wmi:Package_32", null).CreateInstance();
                    var buf = new byte[32];
                    buf[0] = parameter;
                    if (data != null)
                    {
                        int len = Math.Min(data.Length, buf.Length - 1);
                        Array.Copy(data, 0, buf, 1, len);
                    }
                    pkg["Bytes"] = buf;
                    
                    var inParams = mo.GetMethodParameters(methodName);
                    inParams["Data"] = pkg;
                    
                    mo.InvokeMethod(methodName, inParams, null);
                    success = true;
                }
            }
            return success;
        }
        catch (Exception ex)
        {
            Log.Error($"WmiSetAcpiBytes {methodName}({parameter}) 失败: {ex.Message}");
            return false;
        }
    }

    private bool WmiWriteFanCurve(string fanName, FanCurveConf curveCfg)
    {
        bool isGpu = fanName.Contains("GPU", StringComparison.OrdinalIgnoreCase);
        byte fanId = (byte)(isGpu ? 2 : 1);

        Log.Info($"[WMI Fan] 尝试通过 WMI ACPI 写入风扇 {fanName} 曲线...");

        if (curveCfg.TempThresholds == null || curveCfg.TempThresholds.Count == 0)
        {
            Log.Warn($"[WMI Fan] 风扇 {fanName} 无可用曲线阈值数据");
            return false;
        }

        try
        {
            byte thermalLimit = 85;
            byte controlByte = 0;
            
            byte[] existingTemps = WmiGetAcpiBytes("Get_Temperature", fanId);
            if (existingTemps != null && existingTemps.Length > 2)
            {
                thermalLimit = existingTemps[2];
            }

            byte[] existingFan = WmiGetAcpiBytes("Get_Fan", fanId);
            if (existingFan != null && existingFan.Length > 1)
            {
                controlByte = existingFan[1];
            }

            // 构造 DisplayTemps (7 个点)
            byte[] displayTemps = new byte[7];
            for (int k = 0; k < 7; k++)
            {
                if (k < curveCfg.TempThresholds.Count)
                {
                    displayTemps[k] = (byte)curveCfg.TempThresholds[k].UpThreshold;
                }
                else
                {
                    displayTemps[k] = (byte)curveCfg.TempThresholds[curveCfg.TempThresholds.Count - 1].UpThreshold;
                }
            }

            byte[] tempData = new byte[8];
            tempData[0] = displayTemps[0];
            tempData[1] = displayTemps[6];
            tempData[2] = thermalLimit;
            tempData[3] = displayTemps[1];
            tempData[4] = displayTemps[2];
            tempData[5] = displayTemps[3];
            tempData[6] = displayTemps[4];
            tempData[7] = displayTemps[5];

            // 构造 Speeds (7 个点)
            byte[] speeds = new byte[7];
            for (int k = 0; k < 7; k++)
            {
                if (k < curveCfg.TempThresholds.Count)
                {
                    speeds[k] = (byte)curveCfg.TempThresholds[k].FanSpeed;
                }
                else
                {
                    speeds[k] = (byte)curveCfg.TempThresholds[curveCfg.TempThresholds.Count - 1].FanSpeed;
                }
            }
            
            byte[] fanData = new byte[8];
            fanData[0] = controlByte;
            Array.Copy(speeds, 0, fanData, 1, 7);

            // 调用 WMI (参照 Feature Manager 官方做法: 只写 Set_Temperature + Set_Fan, 不写 Set_Thermal)
            // EC 固件内置退档迟滞逻辑, 软件层不应插手, 否则可能写入错误偏置导致风扇无法退档暴转
            bool tOk = WmiSetAcpiBytes("Set_Temperature", fanId, tempData);
            bool fOk = WmiSetAcpiBytes("Set_Fan", fanId, fanData);

            if (tOk && fOk)
            {
                Log.Info($"[WMI Fan] 成功通过 WMI ACPI 应用风扇 {fanName} 曲线，thermalLimit={thermalLimit}, controlByte=0x{controlByte:X2}");
                return true;
            }
            else
            {
                Log.Warn($"[WMI Fan] WMI 下发曲线失败，Set_Temp={tOk}, Set_Fan={fOk}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[WMI Fan] WmiWriteFanCurve {fanName} 遇到异常: {ex.Message}");
        }
        return false;
    }
}
