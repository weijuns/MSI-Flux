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
using System.Timers;
using MSIFlux.Common;
using MSIFlux.Common.Configs;
using MSIFlux.Common.Logs;
using MSIFlux.ECAccess;
using MSIFlux.IPC;

namespace MSIFlux.Service;

internal sealed class FanControlService : ServiceBase
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



    private EcInfo EcInfo;

    private bool FullBlastEnabled;
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
        // use SDDL descriptor since not everyone uses english Windows.
        // the SDDL descriptor should be roughly equivalent to the old
        // behaviour (commented out below):
        // security.AddAccessRule(new PipeAccessRule(
        //     "Administrators", PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.SetSecurityDescriptorSddlForm("O:BAG:SYD:(A;;GA;;;SY)(A;;GRGW;;;BA)");

        CooldownTimer.Elapsed += new ElapsedEventHandler(CooldownElapsed);

        IPCServer = new NamedPipeServer<ServiceCommand, ServiceResponse>("MSIFlux-Server", security);
        IPCServer.ClientConnected += new EventHandler<PipeConnectionEventArgs<ServiceCommand, ServiceResponse>>(IPCClientConnect);
        IPCServer.ClientDisconnected += new EventHandler<PipeConnectionEventArgs<ServiceCommand, ServiceResponse>>(IPCClientDisconnect);
        IPCServer.Error += new EventHandler<PipeErrorEventArgs<ServiceCommand, ServiceResponse>>(IPCServerError);
    }

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
                    sb.Append($"- {svc}");
                }

                ExitCode = 1;
                throw new InvalidOperationException(
                    $"The following MSI Center services are running:\n{sb}\n" +
                    "Uninstall MSI Center or disable the above services to use MSIFlux.");
            }

            // Install WinRing0 to get EC access
            try
            {
                Log.Info(Strings.GetString("drvLoad"));
                if (!_EC.LoadDriver())
                {
                    throw new Win32Exception(_EC.GetDriverError());
                }
            }
            catch (Win32Exception)
            {
                Log.Fatal(Strings.GetString("drvLoadFail"));
                _EC.UnloadDriver();
                ExitCode = 1;
                throw;
            }
            Log.Info(Strings.GetString("drvLoadSuccess"));

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
        bool parseSuccess = false,
            cmdSuccess = false,
            sendSuccessMsg = true;

        Command cmd = e.Message.Command;
        object[] args = e.Message.Arguments;
        int id = e.Connection.ID;

        switch (cmd)
        {
            case Command.Nothing:
                Log.Warn("Empty command received!");
                return;
            case Command.GetServiceVer:
                IPCServer.PushMessage(new ServiceResponse(
                    Response.ServiceVer, Utils.GetRevision()), id);
                return;
            case Command.GetFirmVer:
            {
                parseSuccess = true;
                sendSuccessMsg = false;
                cmdSuccess = GetFirmVer(id);
                break;
            }
            case Command.ReadECByte:
            {
                if (args.Length == 1 && args[0] is byte reg)
                {
                    parseSuccess = true;
                    sendSuccessMsg = false;
                    cmdSuccess = LogECReadByte(reg, out byte value);
                    if (cmdSuccess)
                    {
                        IPCServer.PushMessage(new ServiceResponse(
                            Response.ReadResult, reg, value), id);
                    }
                }
                break;
            }
            case Command.WriteECByte:
            {
                if (args.Length == 2 && args[0] is byte reg && args[1] is byte value)
                {
                    parseSuccess = true;
                    cmdSuccess = LogECWriteByte(reg, value);
                }
                break;
            }
            case Command.ApplyConf:
                parseSuccess = true;
                cmdSuccess = LoadConf() && ApplyConf();
                break;
            case Command.SetFullBlast:
            {
                if (args.Length == 1 && args[0] is int enable)
                {
                    parseSuccess = true;
                    cmdSuccess = SetFullBlast(enable);
                }
                break;
            }
            case Command.GetFanSpeed:
            {
                if (args.Length == 1 && args[0] is int fan)
                {
                    parseSuccess = true;
                    sendSuccessMsg = false;
                    cmdSuccess = GetFanSpeed(id, fan);
                }
                break;
            }
            case Command.GetFanRPM:
            {
                if (args.Length == 1 && args[0] is int fan)
                {
                    parseSuccess = true;
                    sendSuccessMsg = false;
                    cmdSuccess = GetFanRPM(id, fan);
                }
                break;
            }
            case Command.GetTemp:
            {
                if (args.Length == 1 && args[0] is int fan)
                {
                    parseSuccess = true;
                    sendSuccessMsg = false;
                    cmdSuccess = GetTemp(id, fan);
                }
                break;
            }
            case Command.GetKeyLightBright:
                parseSuccess = true;
                sendSuccessMsg = false;
                cmdSuccess = GetKeyLight(id);
                break;
            case Command.SetKeyLightBright:
            {
                if (args.Length == 1 && args[0] is byte brightness)
                {
                    parseSuccess = true;
                    cmdSuccess = SetKeyLight(brightness);
                }
                break;
            }
            case Command.SetWinFnSwap:
            {
                if (args.Length == 1 && args[0] is int enable)
                {
                    parseSuccess = true;
                    KeySwapConf cfg = Config.KeySwapConf;
                    if (enable == -1)
                    {
                        cfg.Enabled = !cfg.Enabled;
                    }
                    else if (enable == 0)
                    {
                        cfg.Enabled = false;
                    }
                    else if (enable == 1)
                    {
                        cfg.Enabled = true;
                    }
                    else
                    {
                        parseSuccess = false;
                    }
                    if (parseSuccess)
                    {
                        cmdSuccess = SetWinFnSwap(cfg);
                    }
                }
                break;
            }
            case Command.SetFanProf:
            {
                if (args.Length == 1 && args[0] is int fanProf)
                {
                    parseSuccess = true;
                    foreach (FanConf cfg in Config.FanConfs)
                    {
                        // Bug #9 fix: 空曲线集直接跳过, 避免 -1 下标以及写入无效 CurveSel
                        int count = cfg.FanCurveConfs?.Count ?? 0;
                        if (count == 0) continue;

                        if (fanProf < 0)
                        {
                            // 循环切换: 已在最后一个 → 回到 0, 否则 ++
                            cfg.CurveSel = cfg.CurveSel >= count - 1 ? 0 : cfg.CurveSel + 1;
                        }
                        else
                        {
                            // 指定值: clamp 到 [0, count-1]
                            cfg.CurveSel = Math.Max(0, Math.Min(fanProf, count - 1));
                        }
                    }
                    cmdSuccess = ApplyConf();
                }
                break;
            }
            case Command.SetPerfMode:
            {
                if (args.Length == 1 && args[0] is int perfMode)
                {
                    parseSuccess = true;
                    if (Config.PerfModeConf is not null)
                    {
                        PerfModeConf cfg = Config.PerfModeConf;
                        // Bug #9 fix: PerfModes.Count == 0 时 Count-1=-1, 旧逻辑会设 ModeSel=0 倒致后续 ApplyConf 越界
                        int count = cfg.PerfModes?.Count ?? 0;
                        if (count == 0)
                        {
                            cmdSuccess = false;
                            break;
                        }

                        if (perfMode < 0)
                        {
                            cfg.ModeSel = cfg.ModeSel >= count - 1 ? 0 : cfg.ModeSel + 1;
                        }
                        else
                        {
                            cfg.ModeSel = Math.Max(0, Math.Min(perfMode, count - 1));
                        }
                        cmdSuccess = ApplyConf();
                    }
                }
                break;
            }
            case Command.SetGpuMode:
            {
                if (args.Length == 1 && args[0] is int gpuMode && (gpuMode >= 0 && gpuMode <= 2))
                {
                    parseSuccess = true;
                    // 0=Hybrid, 1=Discrete, 2=Eco/iGPU
                    cmdSuccess = SetGpuMode(gpuMode);
                }
                break;
            }
            case Command.GetGpuMode:
            {
                parseSuccess = true;
                sendSuccessMsg = false;
                int mode = GetGpuMode();
                Log.Debug($"IPC GetGpuMode result: {mode}");
                if (mode >= 0)
                {
                    IPCServer.PushMessage(new ServiceResponse(
                        Response.GpuModeResult, mode), id);
                    cmdSuccess = true;
                }
                break;
            }

            case Command.ReportGpuMode:
            {
                if (args.Length == 1 && args[0] is int gpuMode && gpuMode is >= 0 and <= 2)
                {
                    parseSuccess = true;
                    SetCachedGpuMode(gpuMode);
                    cmdSuccess = true;
                }
                break;
            }

            default:    // Unknown command
                Log.Error(Strings.GetString("errBadCmd", cmd));
                break;
        }

        if (!cmdSuccess)
        {
            if (!parseSuccess)
            {
                Log.Error(Strings.GetString("errBadArgs", cmd, args));
            }
            IPCServer.PushMessage(new ServiceResponse(
                Response.Error, (int)cmd), id);
        }
        else if (sendSuccessMsg)
        {
            IPCServer.PushMessage(new ServiceResponse(
                Response.Success, (int)cmd), id);
        }
    }
    #endregion

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
                    byte downT = Config.OffsetDT
                        ? (byte)(t.UpThreshold - t.DownThreshold)
                        : t.DownThreshold;

                    if (!LogECWriteByte(cfg.DownThresholdRegs[j - 1], downT))
                    {
                        success = false;
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
    private static int ComputeRpm(FanRPMConf rpmCfg, ushort rpmValue)
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

    #region GPU MUX Switch

    private const string MsiRegPath = @"SOFTWARE\WOW6432Node\MSI\Feature Manager\Component\Base Module\User Scenario";
    private const string WmiScope = @"root\wmi";
    private const string AcpiClass = "MSI_ACPI";

    // P/Invoke for EnumDisplayDevices — used to detect which GPU drives the display.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;

    // P/Invoke for service configuration (replaces sc.exe config calls).
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool ChangeServiceConfigW(IntPtr hService, uint dwServiceType, uint dwStartType,
        uint dwErrorControl, string? lpBinaryPathName, string? lpLoadOrderGroup, IntPtr lpdwTagId,
        string? lpDependencies, string? lpServiceStartName, string? lpPassword, string? lpDisplayName);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_CHANGE_CONFIG = 0x0002;
    private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    private const uint SERVICE_DISABLED = 0x00000004;
    private const uint SERVICE_DEMAND_START = 0x00000003;

    /// <summary>Changes a Windows service's start type via P/Invoke (avoids sc.exe fork).</summary>
    private static bool SetServiceStartType(string serviceName, uint startType)
    {
        IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            IntPtr svc = OpenServiceW(scm, serviceName, SERVICE_CHANGE_CONFIG);
            if (svc == IntPtr.Zero) return false;
            try
            {
                return ChangeServiceConfigW(svc, SERVICE_NO_CHANGE, startType, SERVICE_NO_CHANGE,
                    null, null, IntPtr.Zero, null, null, null, null);
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    /// <summary>
    /// Gets the current GPU MUX mode.
    /// 0=Hybrid, 1=Discrete, 2=Eco/iGPU, -1=error.
    /// Primary: registry FW_GPU_CH (simple, reliable, reflects BIOS POST mode).
    /// Fallback: WMI ACPI Get_AP + GPU enumeration (only when registry is unavailable).
    /// </summary>
    // Cached GPU mode reported by GUI (runs in user session, can use EnumDisplayDevices).
    // -1 = not yet reported.
    private volatile int _gpuModeFromGui = -1;

    /// <summary>Sets the GPU mode as detected by the GUI. Called via IPC.</summary>
    internal void SetCachedGpuMode(int mode)
    {
        if (mode is >= 0 and <= 2)
        {
            _gpuModeFromGui = mode;
            Log.Debug($"GPU mode cached from GUI: {mode}");
        }
    }

    private int GetGpuMode()
    {
        // Primary: use the mode reported by the GUI (runs in user session,
        // uses EnumDisplayDevices to check which GPU drives the display).
        if (_gpuModeFromGui >= 0)
        {
            string modeName = _gpuModeFromGui switch { 1 => "Discrete", 2 => "Eco", _ => "Hybrid" };
            Log.Debug($"GPU mode from GUI cache: {modeName} ({_gpuModeFromGui})");
            return _gpuModeFromGui;
        }

        // Fallback: registry FW_GPU_CH (may be stale after a failed switch,
        // but better than nothing when GUI hasn't reported yet).
        int regMode = ReadRegistryGpuMode();
        Log.Debug($"GPU mode fallback to registry: {regMode}");
        return regMode;
    }

    /// <summary>
    /// Checks if the NVIDIA GPU is actively running (present and enabled).
    /// In Hybrid mode, NVIDIA is active (Optimus rendering).
    /// In Eco mode, NVIDIA is powered off.
    /// </summary>
    private bool IsNvidiaGpuActive()
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Status FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%' OR Name LIKE '%GeForce%' OR Name LIKE '%RTX%' OR Name LIKE '%GTX%'");
            foreach (ManagementObject mo in s.Get())
            {
                var status = mo["Status"]?.ToString();
                if (!string.IsNullOrEmpty(status))
                {
                    Log.Debug($"NVIDIA GPU status: {status}");
                    return status.Equals("OK", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"NVIDIA GPU check failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>Reads FW_GPU_CH from registry. Returns 0/1/2 or -1 if unavailable.</summary>
    private int ReadRegistryGpuMode()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(MsiRegPath, writable: false);
            if (k is not null)
            {
                object? val = k.GetValue("FW_GPU_CH");
                if (val is int m && m is >= 0 and <= 2)
                    return m;
            }
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// Sets the GPU mode using WMI ACPI Set_Data/Get_AP calls.
    /// Mode 0=Hybrid, 1=Discrete, 2=Eco/iGPU (GpuSwitch-compatible).
    /// Requires MSI Foundation Service and Feature Manager Service.exe running.
    /// A reboot is required after calling this.
    /// </summary>
    /// <summary>
    /// 用 sc.exe 启动服务并带重试. InstallUtil 刚注册的服务,
    /// ServiceController.Start() 经常报 "Cannot start service",
    /// 但 sc.exe start 更可靠.
    /// </summary>
    private bool StartServiceWithRetry(string serviceName, int maxRetries, int delayMs)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // 先检查是否已经在运行
                using (var svc = new ServiceController(serviceName))
                {
                    if (svc.Status == ServiceControllerStatus.Running)
                        return true;
                }

                // 用 sc.exe start 代替 ServiceController.Start()
                var p = Process.Start(new ProcessStartInfo("sc.exe")
                {
                    Arguments = $"start \"{serviceName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                p!.WaitForExit(10000);
                Log.Info($"sc.exe start '{serviceName}' attempt {i + 1}: exit={p.ExitCode}");

                // 等待服务进入 Running 状态
                Thread.Sleep(2000);
                using (var svc2 = new ServiceController(serviceName))
                {
                    svc2.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
                    if (svc2.Status == ServiceControllerStatus.Running)
                        return true;
                }

                Log.Warn($"Service '{serviceName}' not running after attempt {i + 1}, retrying...");
                Thread.Sleep(delayMs);
            }
            catch (Exception ex)
            {
                Log.Warn($"StartServiceWithRetry attempt {i + 1} failed: {ex.Message}");
                Thread.Sleep(delayMs);
            }
        }
        return false;
    }

    /// <summary>
    /// 在交互式用户会话中启动进程. 解决 Session 0 隔离问题:
    /// Windows 服务在 Session 0 运行, 直接启动的子进程也在 Session 0,
    /// 没有 GUI 桌面. WPF/WinForms 应用在 Session 0 会崩溃.
    /// 此方法找到当前登录用户的 Session, 用 CreateProcessAsUser 在该 Session 启动.
    /// </summary>
    private bool StartProcessInUserSession(string exePath)
    {
        try
        {
            // 找到活跃的交互式用户 Session
            int sessionId = -1;
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    if (proc.SessionId > 0)
                    {
                        sessionId = proc.SessionId;
                        break;
                    }
                }
                catch { }
            }

            if (sessionId < 0)
            {
                Log.Error("No interactive user session found");
                return false;
            }

            Log.Info($"Starting '{exePath}' in user session {sessionId}");

            // 用 WTS API 获取用户 token, 然后 CreateProcessAsUser
            // 简化方案: 用 schtasks 创建一次性任务, 在用户 Session 运行
            string taskName = "MSIFlux_StartFMSvc";
            try
            {
                // 先删除可能残留的同名任务
                Process.Start(new ProcessStartInfo("schtasks")
                {
                    Arguments = $"/delete /tn \"{taskName}\" /f",
                    CreateNoWindow = true, UseShellExecute = false
                })!.WaitForExit(5000);
            }
            catch { }

            // 用 cmd /c 执行 schtasks, 避免 PowerShell 引号转义问题
            // /tr 参数: 路径含空格, 需要内外双引号: "\"path with spaces\""
            string schtasksCreate = $"schtasks /create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\"\" /sc once /st 00:00 /it /f";
            var createP = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c \"{schtasksCreate}\"",
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            createP!.WaitForExit(5000);
            if (createP.ExitCode != 0)
            {
                Log.Error($"schtasks create failed: exit={createP.ExitCode}");
                return false;
            }

            // 立即运行任务
            var runP = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c \"schtasks /run /tn \"{taskName}\"\"",
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            runP!.WaitForExit(5000);

            // 清理任务
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe")
                {
                    Arguments = $"/c \"schtasks /delete /tn \"{taskName}\" /f\"",
                    CreateNoWindow = true, UseShellExecute = false
                })!.WaitForExit(5000);
            }
            catch { }

            Log.Info($"schtasks run exit={runP.ExitCode}");
            return runP.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error($"StartProcessInUserSession failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 确保 MSI 注册表键存在. Feature Manager Service 正常运行时会创建这些键,
    /// 但 FM Service 是 WPF 应用, 在没有 MSI Center 的环境下会崩溃.
    /// 此方法手动创建 GPU 切换所需的注册表路径和默认值.
    /// </summary>
    private void EnsureMsiRegistryKeys()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(MsiRegPath, writable: false);
            if (k is not null) return;  // 已存在
        }
        catch { }

        // 创建完整的注册表路径
        Log.Info("Creating MSI registry keys (FM Service not available)");
        try
        {
            // MsiRegPath = "SOFTWARE\WOW6432Node\MSI\Feature Manager\Component\Base Module\User Scenario"
            // 需要逐级创建每个子键
            string[] parts = MsiRegPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            Microsoft.Win32.RegistryKey? current = null;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == 0)
                {
                    // 第一级: 在 HKLM 下创建
                    current = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(parts[i]);
                }
                else
                {
                    current = current?.CreateSubKey(parts[i]);
                }

                if (current is null)
                {
                    Log.Error($"Failed to create registry key at level {i}: {parts[i]}");
                    return;
                }
            }

            // 设置 GPU 切换所需的默认值
            using var gpuKey = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(MsiRegPath);
            if (gpuKey is not null)
            {
                // 只在值不存在时设置默认值
                if (gpuKey.GetValue("FW_GPU_CH") is null)
                    gpuKey.SetValue("FW_GPU_CH", 0, Microsoft.Win32.RegistryValueKind.DWord);
                if (gpuKey.GetValue("FW_CurrentNewGPU") is null)
                    gpuKey.SetValue("FW_CurrentNewGPU", 0, Microsoft.Win32.RegistryValueKind.DWord);
                Log.Info("MSI registry keys created successfully");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create MSI registry keys: {ex.Message}");
        }
    }

    /// <summary>
    /// 写 OS 在线心跳 (EC 0xD9 bit0=1).
    /// MSIAPService.OnStart 的关键握手, 让 BIOS 知道 OS 端就绪.
    /// </summary>
    private bool WriteOsHeartbeat()
    {
        try
        {
            if (!LogECReadByte(0xD9, out byte cur))
            {
                Log.Warn("OS heartbeat: Get_Data(0xD9) read failed");
                return false;
            }
            byte target = (byte)(cur | 0x01);
            if (cur == target)
            {
                Log.Info("OS heartbeat: EC[0xD9] bit0 already 1");
                return true;
            }
            Log.Info($"OS heartbeat: EC[0xD9] 0x{cur:X2} -> 0x{target:X2}");
            return LogECWriteByte(0xD9, target);
        }
        catch (Exception ex)
        {
            Log.Warn($"OS heartbeat failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 切换完成后清理 MSI 辅助进程, 避免关机时 FM Service 抛出 0xe0434352 异常.
    /// </summary>
    private void CleanupMsiHelpers()
    {
        foreach (var proc in Process.GetProcessesByName("Feature Manager Service"))
        {
            try
            {
                proc.Kill();
                proc.WaitForExit(3000);
                Log.Info("Terminated Feature Manager Service.exe");
            }
            catch (Exception ex)
            {
                Log.Warn($"Kill FM Service failed: {ex.Message}");
            }
        }

        try
        {
            using var svc = new ServiceController("MSI Foundation Service");
            if (svc.Status != ServiceControllerStatus.Stopped)
            {
                svc.Stop();
                svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                Log.Info("Stopped MSI Foundation Service");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Stop MSI Foundation Service failed: {ex.Message}");
        }
    }

    /// <param name="mode">0=Hybrid, 1=Discrete, 2=Eco/iGPU</param>
    /// <returns>true if the switch succeeded</returns>
    private bool SetGpuMode(int mode)
    {
        string modeName = mode switch
        {
            2 => "Eco/iGPU",
            1 => "Discrete",
            _ => "Hybrid"
        };
        Log.Info($"Setting GPU mode to {modeName}");

        // Step 0: Configure FM services - disable conflicting services, set MSI Foundation to manual
        try
        {
            // Disable Micro Star SCM (MSI Center service) - conflicts with MSI Flux
            using var scmSvc = new ServiceController("Micro Star SCM");
            bool needDisable = false;
            if (scmSvc.Status != ServiceControllerStatus.Stopped)
            {
                Log.Info("Stopping Micro Star SCM service...");
                scmSvc.Stop();
                scmSvc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                needDisable = true;
            }
            if (scmSvc.StartType != ServiceStartMode.Disabled)
                needDisable = true;
            if (needDisable)
            {
                Log.Info("Disabling Micro Star SCM service...");
                if (!SetServiceStartType("Micro Star SCM", SERVICE_DISABLED))
                    Log.Warn($"Failed to disable Micro Star SCM service (Win32Error={System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
            }
        }
        catch { /* Service not found or already stopped */ }

        try
        {
            // Ensure MSI Foundation Service is set to Manual (not auto-start)
            using var mfsSvc = new ServiceController("MSI Foundation Service");
            if (mfsSvc.StartType != ServiceStartMode.Manual)
            {
                Log.Info("Setting MSI Foundation Service to Manual start...");
                if (!SetServiceStartType("MSI Foundation Service", SERVICE_DEMAND_START))
                    Log.Warn($"Failed to set MSI Foundation Service to Manual (Win32Error={System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
            }
        }
        catch { /* Service not found yet */ }

        // Step 1: Ensure MSI Foundation Service (MSIAPService.exe) is running
        // Look for FeatureManager folder in multiple locations:
        //   1. C:\ProgramData\MSI Flux\FeatureManager (auto-extracted by GUI)
        //   2. Bundled with MSIFlux (FeatureManager/ next to service dir)
        //   3. System install (C:\Program Files (x86)\Feature Manager\)
        string serviceDir = AppContext.BaseDirectory;
        string[] featureManagerDirCandidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MSI Flux", "FeatureManager"),  // Auto-extracted
            Path.GetFullPath(Path.Combine(serviceDir, "..", "..", "..", "..", "FeatureManager")),  // Bundled with MSIFlux
            @"C:\Program Files (x86)\Feature Manager",                                            // System install
        };
        string featureManagerDir = featureManagerDirCandidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "MSIAPService.exe")))
            ?? featureManagerDirCandidates[0]; // Default to bundled path
        string msiApSvcPath = Path.Combine(featureManagerDir, "MSIAPService.exe");

        // Step 0.5: Ensure WMI ACPI bootstrap (msiapcfg.dll + MofImagePath) is installed.
        // This is the *real* foundation for WMI ACPI calls — without it, even FM can't make
        // Get_AP/Set_Data work. With it, we don't need FM installed at all.
        // First-time install requires a reboot before WMI calls actually succeed.
        try
        {
            WmiAcpiBootstrap.EnsureInstalled(featureManagerDir, Log);
        }
        catch (Exception ex)
        {
            Log.Warn($"WMI ACPI bootstrap install failed (non-fatal): {ex.Message}");
        }
        bool msiFoundationReady = false;
        try
        {
            using var svc = new ServiceController("MSI Foundation Service");
            if (svc.Status == ServiceControllerStatus.Running)
            {
                msiFoundationReady = true;
            }
            else
            {
                // Check if the registered binary path is stale (points to a non-existent file)
                try
                {
                    string keyPath = @"SYSTEM\CurrentControlSet\Services\MSI Foundation Service";
                    using var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                    string? registeredBinPath = regKey?.GetValue("ImagePath") as string;
                    if (!string.IsNullOrEmpty(registeredBinPath))
                    {
                        string cleanPath = registeredBinPath.Trim('"');
                        if (!File.Exists(cleanPath) && File.Exists(msiApSvcPath))
                        {
                            Log.Warn($"MSI Foundation Service binary path is stale: {registeredBinPath}");
                            Log.Info($"Re-registering with correct path: {msiApSvcPath}");
                            try
                            {
                                var delP = Process.Start(new ProcessStartInfo("sc.exe")
                                {
                                    Arguments = "delete \"MSI Foundation Service\"",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                });
                                delP!.WaitForExit(5000);
                                Thread.Sleep(1000);
                            }
                            catch { }
                            // Fall through to InstallUtil re-registration below
                        }
                    }
                }
                catch { }

                // Try starting the Windows service first
                try
                {
                    Log.Info("Starting MSI Foundation Service...");
                    svc.Start();
                    svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    msiFoundationReady = true;
                }
                catch
                {
                    // Service not registered or stale; try InstallUtil then start
                    if (File.Exists(msiApSvcPath))
                    {
                        string installUtil = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                            @"Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe");

                        if (File.Exists(installUtil))
                        {
                            Log.Info($"Installing MSI Foundation Service via InstallUtil...");
                            try
                            {
                                var p = Process.Start(new ProcessStartInfo(installUtil)
                                {
                                    Arguments = $"/i \"{msiApSvcPath}\"",
                                    CreateNoWindow = true,
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true
                                });
                                p!.WaitForExit(15000);
                                Log.Info($"InstallUtil exit code: {p.ExitCode}");

                                // InstallUtil 注册服务后, 用 sc.exe start 启动,
                                // 比 ServiceController.Start() 更可靠, 且带重试.
                                msiFoundationReady = StartServiceWithRetry("MSI Foundation Service", 3, 5000);
                                if (msiFoundationReady)
                                {
                                    Log.Info("MSI Foundation Service installed and started");
                                }
                            }
                            catch (Exception ex3)
                            {
                                Log.Error($"InstallUtil failed: {ex3.Message}");
                            }
                        }
                        else
                        {
                            Log.Error($"InstallUtil.exe not found at {installUtil}");
                        }
                    }
                    else
                    {
                        Log.Error("MSI Foundation Service not available");
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to access MSI Foundation Service: {ex.Message}");

            // Service not registered at all; try InstallUtil
            if (File.Exists(msiApSvcPath))
            {
                string installUtil = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe");

                if (File.Exists(installUtil))
                {
                    Log.Info($"Installing MSI Foundation Service via InstallUtil...");
                    try
                    {
                        var p = Process.Start(new ProcessStartInfo(installUtil)
                        {
                            Arguments = $"/i \"{msiApSvcPath}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        });
                        p!.WaitForExit(15000);

                        msiFoundationReady = StartServiceWithRetry("MSI Foundation Service", 3, 5000);
                        if (msiFoundationReady)
                        {
                            Log.Info("MSI Foundation Service installed and started");
                        }
                    }
                    catch (Exception ex4)
                    {
                        Log.Error($"InstallUtil install+start failed: {ex4.Message}");
                    }
                }
            }
        }

        if (!msiFoundationReady)
        {
            // Fallback check: service might be running from another source
            try
            {
                using var svc4 = new ServiceController("MSI Foundation Service");
                msiFoundationReady = svc4.Status == ServiceControllerStatus.Running;
            }
            catch { }
        }

        if (!msiFoundationReady)
        {
            Log.Error("MSI Foundation Service is not running after startup attempt");
            return false;
        }

        // Step 2: Check Feature Manager Service.exe is running
        // Feature Manager Service.exe 是 WPF 应用, 需要交互式桌面.
        // 它必须由 GUI 侧 (用户会话) 启动, 服务端无法在 Session 0 启动 WPF 进程.
        // 如果 FM Service 无法运行, 我们自己创建它负责的注册表键.
        string fmSvcPath = Path.Combine(featureManagerDir, "Feature Manager Service.exe");
        bool fmSvcRunning = Process.GetProcessesByName("Feature Manager Service").Length > 0;
        if (!fmSvcRunning)
        {
            Log.Warn("Feature Manager Service.exe is not running (GUI should start it)");
        }

        // Feature Manager Service 的核心职责之一是创建 MSI 注册表键.
        // 如果它没在运行, 注册表键不存在, 我们自己创建.
        EnsureMsiRegistryKeys();

        if (!WriteOsHeartbeat())
            Log.Warn("OS heartbeat write failed, continuing with switch.");

        // Step 3: Write registry (FW_CurrentNewGPU must differ from FW_GPU_CH)
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(MsiRegPath, writable: true);
            if (k is null)
            {
                Log.Error("Cannot open MSI registry key");
                return false;
            }
            // FW_GPU_CH: 0=Hybrid, 1=dGPU, 2=Eco/iGPU
            // FW_CurrentNewGPU must differ from FW_GPU_CH to trigger switch
            int targetChVal = mode;
            // Read current FW_GPU_CH to use as FW_CurrentNewGPU (ensure it differs from target)
            object? existingCh = k.GetValue("FW_GPU_CH");
            int currentGpuVal = existingCh is int v ? v : 0;
            if (currentGpuVal == targetChVal)
                currentGpuVal = targetChVal == 0 ? 1 : 0;
            k.SetValue("FW_CurrentNewGPU", currentGpuVal, Microsoft.Win32.RegistryValueKind.DWord);
            k.SetValue("FW_GPU_CH", targetChVal, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to write GPU registry: {ex.Message}");
            return false;
        }

        // Step 4+: WMI ACPI calls
        // MSI_ACPI WMI 类需要通过 mofcomp 注册 MOF schema.
        // Feature Manager 安装时会注册, 卸载后会丢失.
        // 我们自带 MSI_ACPI.mof, 如果类不存在则自动注册.
        bool msiAcpiExists = false;
        try
        {
            using var checkSearcher = new ManagementObjectSearcher(WmiScope, $"SELECT * FROM {AcpiClass}");
            foreach (ManagementObject _ in checkSearcher.Get()) { msiAcpiExists = true; break; }
        }
        catch (Exception ex)
        {
            Log.Warn($"MSI_ACPI WMI class check failed: {ex.Message}");
        }

        if (!msiAcpiExists)
        {
            Log.Warn("MSI_ACPI WMI class not found or no instances. Registering MOF schema...");
            string mofPath = Path.Combine(featureManagerDir, "MSI_ACPI.mof");
            if (File.Exists(mofPath))
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("mofcomp.exe")
                    {
                        Arguments = $"\"{mofPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    p!.WaitForExit(10000);
                    string output = p.StandardOutput.ReadToEnd();
                    if (p.ExitCode == 0)
                    {
                        Log.Info("MSI_ACPI MOF schema registered successfully");
                        // Re-check after registration
                        try
                        {
                            using var recheck = new ManagementObjectSearcher(WmiScope, $"SELECT * FROM {AcpiClass}");
                            foreach (ManagementObject _ in recheck.Get()) { msiAcpiExists = true; break; }
                        }
                        catch { }
                    }
                    else
                    {
                        Log.Warn($"mofcomp exit code: {p.ExitCode}, output: {output}");
                    }
                }
                catch (Exception ex2)
                {
                    Log.Warn($"mofcomp failed: {ex2.Message}");
                }
            }
            else
            {
                Log.Warn($"MOF file not found at {mofPath}");
            }
        }

        // Step 4.5: Commit UEFI variable BEFORE the EC sequence.
        // BIOS may check the UEFI variable when processing EC commands;
        // writing it first (matching GPUSwitch tool order) ensures it's visible.
        bool uefiOk = false;
        try
        {
            uefiOk = UefiVariable.CommitGpuMode(mode, Log);
            if (!uefiOk)
                Log.Warn("UEFI MsiDCVarData write failed. GPU MUX may not switch on cold boot.");
        }
        catch (Exception ex)
        {
            Log.Warn($"UEFI commit threw: {ex.Message}");
        }

        byte[]? ap0 = WmiCallGet("Get_AP", 0x00);
        if (ap0 is null || ap0.Length < 2 || ap0[0] != 0x01)
        {
            Log.Warn("WMI Get_AP not available after MOF registration attempt. Registry-only mode: reboot required.");
            Log.Info($"GPU mode switch to {modeName} completed (registry-only). Reboot required.");
            return true;
        }
        Log.Info($"Get_AP(0) byte[1]=0x{ap0[1]:X2}");

        // Step 5-9: EC write sequence with retry.
        // Discrete→Hybrid needs the BIOS to see the EC command before it will acknowledge.
        const int maxAttempts = 3;
        bool ecSuccess = false;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Read current AP state
            byte[]? ap0_cur = WmiCallGet("Get_AP", 0x00);
            if (ap0_cur is null || ap0_cur.Length < 2 || ap0_cur[0] != 0x01)
            {
                Log.Warn($"Attempt {attempt}: Get_AP(0) failed");
                if (attempt < maxAttempts) { Thread.Sleep(1000); continue; }
                break;
            }

            // Modify byte[1]: clear bit0, clear bit1, then set bit0
            byte orig = ap0_cur[1];
            byte mod = (byte)((orig & ~0x03) | 0x01);  // bit0=1, bit1=0
            Log.Info($"Attempt {attempt}: Get_AP(0) byte[1]=0x{orig:X2} -> 0x{mod:X2}");

            // Set_Data(0xD1)
            var pkg1 = new byte[32];
            pkg1[0] = 0xD1;
            pkg1[1] = mod;
            byte[]? r1 = WmiCallSet("Set_Data", pkg1);
            if (r1 is null || r1.Length == 0 || r1[0] != 0x01)
            {
                Log.Error($"Attempt {attempt}: Set_Data(0xD1) failed, ACK=0x{(r1 is { Length: > 0 } ? r1[0] : 0):X2}");
                if (attempt < maxAttempts) { Thread.Sleep(1000); continue; }
                return false;
            }
            Log.Info($"Attempt {attempt}: Set_Data(0xD1) ACK=0x{r1[0]:X2}");

            // Wait for BIOS to process (first attempt gets extra time)
            int waitMs = attempt == 1 ? 3000 : 2000;
            Thread.Sleep(waitMs);

            // Re-read Get_AP(0) to check BIOS response
            byte[]? ap0_after = WmiCallGet("Get_AP", 0x00);
            byte checkByte = 0;
            if (ap0_after is not null && ap0_after.Length >= 3)
            {
                checkByte = ap0_after[2];
                Log.Info($"Attempt {attempt}: Re-read byte[2]=0x{checkByte:X2} (bit1={(checkByte >> 1) & 1})");
            }
            else
            {
                Log.Warn($"Attempt {attempt}: Re-read Get_AP(0) failed");
            }

            // Always send Set_Data(0xBE, 0x02) to confirm/commit the EC write.
            {
                var pkg2 = new byte[32];
                pkg2[0] = 0xBE;
                pkg2[1] = 0x02;
                byte[]? r2 = WmiCallSet("Set_Data", pkg2);
                if (r2 is not null && r2.Length > 0)
                    Log.Info($"Attempt {attempt}: Set_Data(0xBE) ACK=0x{r2[0]:X2}");
                else
                    Log.Warn($"Attempt {attempt}: Set_Data(0xBE) returned null/empty");
            }

            if (((checkByte >> 1) & 1) == 1)
            {
                ecSuccess = true;
                break;
            }
            Log.Warn($"Attempt {attempt}: BIOS did not acknowledge (bit1 not set)");

            if (attempt < maxAttempts)
                Thread.Sleep(1000);
        }
        if (!ecSuccess)
            Log.Warn("BIOS did not acknowledge after all attempts. UEFI variable is set — cold boot may still apply the switch.");

        try { CleanupMsiHelpers(); }
        catch (Exception ex) { Log.Warn($"CleanupMsiHelpers failed: {ex.Message}"); }

        if (!uefiOk)
        {
            Log.Error($"GPU mode switch to {modeName} failed: UEFI variable write failed.");
            return false;
        }

        if (ecSuccess)
            Log.Info($"GPU mode switch to {modeName} completed. *Cold boot* (shutdown + power on) required, NOT a warm reboot.");
        else
            Log.Warn($"GPU mode switch to {modeName}: EC acknowledgment missing, but UEFI variable is set. Cold boot should still apply the switch.");

        return true;
    }

    private static readonly TimeSpan WmiCallTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Calls a Get_* WMI ACPI method with a single-byte command.
    /// Wrapped with a timeout to prevent indefinite hangs if WMI is stuck.
    /// </summary>
    private byte[]? WmiCallGet(string methodName, byte cmd)
    {
        try
        {
            var task = Task.Run(() =>
            {
                using var pkgClass = new ManagementClass(
                    new ManagementScope(WmiScope),
                    new ManagementPath("Package_32"), null);
                var pkg = pkgClass.CreateInstance();
                var input = new byte[32];
                input[0] = cmd;
                pkg["Bytes"] = input;

                using var searcher = new ManagementObjectSearcher(
                    WmiScope, $"SELECT * FROM {AcpiClass}");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var inParams = mo.GetMethodParameters(methodName);
                    inParams["Data"] = pkg;
                    var outParams = mo.InvokeMethod(methodName, inParams, null);
                    var dataOut = outParams?["Data"] as ManagementBaseObject;
                    if (dataOut is null) return null;
                    return ExtractPackageBytes(dataOut);
                }
                return null;
            });

            if (task.Wait(WmiCallTimeout))
                return task.Result;

            Log.Error($"WMI {methodName} timed out after {WmiCallTimeout.TotalSeconds}s");
            return null;
        }
        catch (AggregateException ae) when (ae.InnerException is not null)
        {
            Log.Error($"WMI {methodName} failed: {ae.InnerException.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"WMI {methodName} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Calls a Set_* WMI ACPI method with a 32-byte input package.
    /// Wrapped with a timeout to prevent indefinite hangs if WMI is stuck.
    /// </summary>
    private byte[]? WmiCallSet(string methodName, byte[] inputBytes)
    {
        try
        {
            var task = Task.Run(() =>
            {
                using var pkgClass = new ManagementClass(
                    new ManagementScope(WmiScope),
                    new ManagementPath("Package_32"), null);
                var pkg = pkgClass.CreateInstance();
                pkg["Bytes"] = inputBytes;

                using var searcher = new ManagementObjectSearcher(
                    WmiScope, $"SELECT * FROM {AcpiClass}");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var inParams = mo.GetMethodParameters(methodName);
                    inParams["Data"] = pkg;
                    var outParams = mo.InvokeMethod(methodName, inParams, null);
                    var dataOut = outParams?["Data"] as ManagementBaseObject;
                    if (dataOut is null) return null;
                    return ExtractPackageBytes(dataOut);
                }
                return null;
            });

            if (task.Wait(WmiCallTimeout))
                return task.Result;

            Log.Error($"WMI {methodName} timed out after {WmiCallTimeout.TotalSeconds}s");
            return null;
        }
        catch (AggregateException ae) when (ae.InnerException is not null)
        {
            Log.Error($"WMI {methodName} failed: {ae.InnerException.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"WMI {methodName} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts byte[] from a Package_32 WMI object.
    /// </summary>
    private static byte[]? ExtractPackageBytes(ManagementBaseObject pkg)
    {
        foreach (PropertyData pd in pkg.Properties)
        {
            if (pd.IsArray && pd.Type == CimType.UInt8 && pd.Value is byte[] bytes)
            {
                return bytes;
            }
        }
        return null;
    }

    #endregion
}
