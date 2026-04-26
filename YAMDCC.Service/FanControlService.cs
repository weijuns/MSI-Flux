// This file is part of YAMDCC (Yet Another MSI Dragon Center Clone).
// Copyright © YAMDCC_Config and Contributors 2023-2026.
//
// YAMDCC is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// YAMDCC is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
// more details.
//
// You should have received a copy of the GNU General Public License along with
// YAMDCC. If not, see <https://www.gnu.org/licenses/>.

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
using YAMDCC.Common;
using YAMDCC.Common.Configs;
using YAMDCC.Common.Logs;
using YAMDCC.ECAccess;
using YAMDCC.IPC;

namespace YAMDCC.Service;

internal sealed class FanControlService : ServiceBase
{
    #region Fields

    /// <summary>
    /// The currently loaded YAMDCC config.
    /// </summary>
    private YAMDCC_Config Config;

    /// <summary>
    /// The named message pipe server that YAMDCC connects to.
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

        IPCServer = new NamedPipeServer<ServiceCommand, ServiceResponse>("YAMDCC-Server", security);
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
            // It is still possible to start MSI Center *after* YAMDCC Service,
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
                    "Uninstall MSI Center or disable the above services to use YAMDCC.");
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

            // Load the last applied YAMDCC config.
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
                if (mode >= 0)
                {
                    IPCServer.PushMessage(new ServiceResponse(
                        Response.GpuModeResult, mode), id);
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
            Config = YAMDCC_Config.Load(Paths.CurrentConf);
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

    /// <summary>
    /// Gets the current GPU MUX mode.
    /// 0=Hybrid, 1=Discrete, 2=Eco/iGPU, -1=error.
    /// Uses FW_GPU_CH (target mode = actual mode after reboot).
    /// Note: FW_CurrentNewGPU is unreliable — it's set to the OPPOSITE of the
    /// target during switch and is NOT updated after reboot.
    /// </summary>
    private int GetGpuMode()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(MsiRegPath, writable: false);
            if (k is not null)
            {
                object? val = k.GetValue("FW_GPU_CH");
                if (val is int mode)
                {
                    // FW_GPU_CH: 0=Hybrid, 1=Discrete, 2=Eco/iGPU
                    // Other values (e.g. 5) may be set by MSI Center's Feature Manager
                    return mode switch
                    {
                        0 => 0,
                        1 => 1,
                        2 => 2,
                        _ => 0  // unknown -> default to Hybrid
                    };
                }
            }
        }
        catch { }

        // Fallback: try WMI Get_AP
        byte[]? ap0 = WmiCallGet("Get_AP", 0x00);
        if (ap0 != null && ap0.Length > 1)
        {
            return (ap0[1] & 0x01) != 0 ? 1 : 0;
        }
        return -1;
    }

    /// <summary>
    /// Sets the GPU mode using WMI ACPI Set_Data/Get_AP calls.
    /// Mode 0=Hybrid, 1=Discrete, 2=Eco/iGPU (GpuSwitch-compatible).
    /// Requires MSI Foundation Service and Feature Manager Service.exe running.
    /// A reboot is required after calling this.
    /// </summary>
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

        // Step 1: Ensure MSI Foundation Service (MSIAPService.exe) is running
        // Look for FeatureManager folder in multiple locations:
        //   1. Bundled with YAMDCC (FeatureManager/ next to service dir)
        //   2. System install (C:\Program Files (x86)\Feature Manager\)
        //   3. Relative path (legacy fallback)
        string serviceDir = AppContext.BaseDirectory;
        string[] featureManagerDirCandidates =
        {
            Path.GetFullPath(Path.Combine(serviceDir, "..", "..", "..", "..", "FeatureManager")),  // Bundled with YAMDCC
            @"C:\Program Files (x86)\Feature Manager",                                        // System install
        };
        string featureManagerDir = featureManagerDirCandidates.First(d => File.Exists(Path.Combine(d, "MSIAPService.exe")))
            ?? featureManagerDirCandidates[0]; // Default to bundled path
        string msiApSvcPath = Path.Combine(featureManagerDir, "MSIAPService.exe");
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
                    // Service not registered; try InstallUtil then start
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

                                try
                                {
                                    using var svc2 = new ServiceController("MSI Foundation Service");
                                    svc2.Start();
                                    svc2.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                                    msiFoundationReady = true;
                                    Log.Info("MSI Foundation Service installed and started");
                                }
                                catch (Exception ex2)
                                {
                                    Log.Error($"Failed to start after install: {ex2.Message}");
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

                        using var svc3 = new ServiceController("MSI Foundation Service");
                        svc3.Start();
                        svc3.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        msiFoundationReady = true;
                        Log.Info("MSI Foundation Service installed and started");
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

        // Step 2: Start Feature Manager Service.exe
        string fmSvcPath = Path.Combine(featureManagerDir, "Feature Manager Service.exe");
        bool fmSvcRunning = Process.GetProcessesByName("Feature Manager Service").Length > 0;
        if (!fmSvcRunning)
        {
            if (!File.Exists(fmSvcPath))
            {
                Log.Error($"Feature Manager Service.exe not found at {fmSvcPath}");
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo(fmSvcPath)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
                Thread.Sleep(2000);
                fmSvcRunning = Process.GetProcessesByName("Feature Manager Service").Length > 0;
                if (!fmSvcRunning)
                {
                    Log.Error("Feature Manager Service.exe exited immediately");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start Feature Manager Service.exe: {ex.Message}");
                return false;
            }
        }

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

        // Step 4: Read Get_AP(0) to get current state
        byte[]? ap0 = WmiCallGet("Get_AP", 0x00);
        if (ap0 is null || ap0.Length < 2 || ap0[0] != 0x01)
        {
            Log.Error("Get_AP(0) failed or returned unexpected data");
            return false;
        }
        Log.Info($"Get_AP(0) byte[1]=0x{ap0[1]:X2}");

        // Step 5: Modify byte[1]: clear bit0, clear bit1, then set bit0
        byte orig = ap0[1];
        byte mod = (byte)((orig & ~0x03) | 0x01);  // bit0=1, bit1=0
        Log.Info($"Modified byte[1]: 0x{orig:X2} -> 0x{mod:X2}");

        // Step 6: Call Set_Data(cmd=0xD1, byte[1]=mod)
        var pkg1 = new byte[32];
        pkg1[0] = 0xD1;
        pkg1[1] = mod;
        byte[]? r1 = WmiCallSet("Set_Data", pkg1);
        if (r1 is null || r1.Length == 0 || r1[0] != 0x01)
        {
            Log.Error($"Set_Data(0xD1) failed, ACK=0x{(r1 is { Length: > 0 } ? r1[0] : 0):X2}");
            return false;
        }
        Log.Info($"Set_Data(0xD1) ACK=0x{r1[0]:X2} (success)");

        // Step 7: Wait 2s for BIOS to process
        Thread.Sleep(2000);

        // Step 8: Re-read Get_AP(0) to check BIOS response
        byte[]? ap0_after = WmiCallGet("Get_AP", 0x00);
        if (ap0_after is null || ap0_after.Length < 3)
        {
            Log.Error("Re-read Get_AP(0) failed");
            return false;
        }
        byte checkByte = ap0_after[2];
        Log.Info($"Re-read Get_AP(0) byte[2]=0x{checkByte:X2} (bit1={(checkByte >> 1) & 1})");

        // Step 9: If bit1 is set, BIOS acknowledged; write Set_Data(0xBE, 0x02)
        if (((checkByte >> 1) & 1) == 1)
        {
            var pkg2 = new byte[32];
            pkg2[0] = 0xBE;
            pkg2[1] = 0x02;
            byte[]? r2 = WmiCallSet("Set_Data", pkg2);
            if (r2 is not null && r2.Length > 0)
            {
                Log.Info($"Set_Data(0xBE) ACK=0x{r2[0]:X2}");
            }
            else
            {
                Log.Warn("Set_Data(0xBE) returned null/empty");
            }
        }
        else
        {
            Log.Warn("BIOS did not acknowledge (bit1 not set), skipping Set_Data(0xBE)");
        }

        Log.Info($"GPU mode switch to {modeName} completed. Reboot required.");
        return true;
    }

    /// <summary>
    /// Calls a Get_* WMI ACPI method with a single-byte command.
    /// </summary>
    private byte[]? WmiCallGet(string methodName, byte cmd)
    {
        try
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
        }
        catch (Exception ex)
        {
            Log.Error($"WMI {methodName} failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Calls a Set_* WMI ACPI method with a 32-byte input package.
    /// </summary>
    private byte[]? WmiCallSet(string methodName, byte[] inputBytes)
    {
        try
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
        }
        catch (Exception ex)
        {
            Log.Error($"WMI {methodName} failed: {ex.Message}");
        }
        return null;
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
