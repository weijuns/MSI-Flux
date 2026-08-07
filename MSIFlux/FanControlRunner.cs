// This file is part of MSIFlux, based on YAMDCC.
// Licensed under GPL-3.0-or-later.
//
// FanControlRunner: GUI 侧 IPC 代理门面, 为 SettingsForm / Fans / Extra 提供公共 API.
// 内部全部走命名管道 IPC, 不再直接访问 EC/驱动.

using System;
using System.Diagnostics;
using System.Threading;
using System.Timers;
using MSIFlux.Common;
using MSIFlux.Common.Configs;
using MSIFlux.Common.Logs;
using MSIFlux.GUI.Helpers;
using MSIFlux.IPC;

namespace MSIFlux.GUI;

// ====================================================================
// FanControlRunner: IPC 代理版本
// ====================================================================
//
// 公共 API 与原版保持一致, 以便 SettingsForm / Fans / Extra 代码不需改动:
//   属性: CpuTemp, GpuTemp, CpuFanRpm, GpuFanRpm
//   事件: TempUpdated
//   方法: Start(), Stop(), Dispose(), LoadConfig(), ApplyConfig(),
//         SetFullBlast(int), SetPerfMode(int), SetFanProfile(int),
//         ReadECByte(byte, out byte), WriteECByte(byte, byte),
//         GetConfig(), SaveConfig()
//
// 内部实现全部走 IPC, 不再直接访问 EC/驱动.
// ====================================================================
public sealed class FanControlRunner : IDisposable
{
    private readonly MSIFlux.Common.Logs.Logger _log;
    private readonly ServiceIpcProxy _ipc;
    private MSIFlux_Config? _config;
    private System.Timers.Timer? _pollTimer;
    private readonly object _lock = new();
    private bool _disposed;
    private int _consecutivePollFailures;

    // Temporarily suppress polling during mode switches to avoid
    // IPC contention when the service is busy with EC writes.
    private volatile bool _pollSuspended;

    public event EventHandler<TempEventArgs>? TempUpdated;
    public event EventHandler? ConfigChanged;

    public int CpuTemp { get; private set; }
    public int GpuTemp { get; private set; }
    public int CpuFanRpm { get; private set; }
    public int GpuFanRpm { get; private set; }

    /// <summary>
    /// 对外可见的连接状态, 供 UI 判断"服务是否可达".
    /// </summary>
    public bool IsServiceConnected => _ipc.IsConnected;

    public FanControlRunner(MSIFlux.Common.Logs.Logger logger)
    {
        _log = logger;
        _ipc = new ServiceIpcProxy();
        _ipc.ServerMessage += OnServerMessage;
        _ipc.Disconnected += (_, _) => SafeLog("IPC 连接断开", LogLevel.Warn);
        _ipc.Connected += (_, _) => SafeLog("IPC 连接成功", LogLevel.Info);
    }

    /// <summary>
    /// 启动: 连接 IPC, 加载配置, 启动轮询定时器.
    /// 注意: 不再加载 WinRing0 驱动 (由服务负责).
    /// </summary>
    public bool Start()
    {
        try
        {
            SafeLog("连接 MSI Flux 服务 (IPC)...");
            _ipc.Start();

            // 等待最多 3 秒建立连接, 不阻塞过久
            bool connected = _ipc.WaitForConnection(TimeSpan.FromSeconds(3));
            if (!connected)
            {
                SafeLog("IPC 连接暂未建立, 稍后会自动重连", LogLevel.Warn);
            }

            LoadConfig();

            // 服务端已经在自己应用配置了. GUI 侧再触发一次确保一致
            if (connected) _ipc.ApplyConf();

            StartPolling();
            return connected;
        }
        catch (Exception ex)
        {
            SafeLog($"Start 失败: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private void StartPolling()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();

        _pollTimer = new System.Timers.Timer(1000) { AutoReset = false };
        _pollTimer.Elapsed += (_, _) =>
        {
            try
            {
                PollOnce();
            }
            catch (Exception ex)
            {
                SafeLog($"Poll tick error: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                if (!_disposed) _pollTimer?.Start();
            }
        };
        _pollTimer.Start();
    }

    /// <summary>
    /// 向服务索要一次 温度/RPM 数据并触发 TempUpdated.
    /// Bug #6 fix: 不再假设 FanConfs[0]=CPU / [1]=GPU, 而是按 FanConf.Name 判定.
    /// 部分机型 XML 里第一个风扇是 GPU, 硬编码会把 CPU/GPU 的数据对调.
    /// </summary>
    private void PollOnce()
    {
        if (_pollSuspended || _config == null) return;

        // 连接断开: 尝试重连 (NamedPipeClient.AutoReconnect 可能因
        // 空闲太久无法检测到断开, 需要主动触发)
        if (!_ipc.IsConnected)
        {
            _consecutivePollFailures++;
            if (_consecutivePollFailures >= 3)
            {
                SafeLog($"IPC 连续 {_consecutivePollFailures} 次失败, 强制重连...", LogLevel.Warn);
                _ipc.ForceReconnect();
                _consecutivePollFailures = 0;
            }
            return;
        }

        var fans = _config.FanConfs;
        if (fans == null || fans.Count == 0) return;

        // 先分辨每个 FanConf 对应的角色 (CPU/GPU), 再按索引去服务端取数据.
        // 服务端仍按 FanConf 索引寻址, 这里只是 GUI 把数据对到 CpuXxx/GpuXxx 属性.
        int cpuIdx = -1, gpuIdx = -1;
        for (int i = 0; i < fans.Count; i++)
        {
            string name = fans[i]?.Name ?? string.Empty;
            if (cpuIdx < 0 && IsCpuFanName(name)) cpuIdx = i;
            else if (gpuIdx < 0 && IsGpuFanName(name)) gpuIdx = i;
        }
        // 回退: 未能按名识别时沿用 0=CPU / 1=GPU 的传统约定
        if (cpuIdx < 0) cpuIdx = 0;
        if (gpuIdx < 0 && fans.Count > 1) gpuIdx = (cpuIdx == 0) ? 1 : 0;

        if (cpuIdx >= 0 && cpuIdx < fans.Count)
        {
            int t = _ipc.GetTemp(cpuIdx, TimeSpan.FromMilliseconds(500));
            if (t >= 0) CpuTemp = t;
            int r = _ipc.GetFanRPM(cpuIdx, TimeSpan.FromMilliseconds(500));
            if (r >= 0) CpuFanRpm = r;
        }
        if (gpuIdx >= 0 && gpuIdx < fans.Count && gpuIdx != cpuIdx)
        {
            int t = _ipc.GetTemp(gpuIdx, TimeSpan.FromMilliseconds(500));
            if (t >= 0) GpuTemp = t;
            int r = _ipc.GetFanRPM(gpuIdx, TimeSpan.FromMilliseconds(500));
            if (r >= 0) GpuFanRpm = r;
        }

        TempUpdated?.Invoke(this, new TempEventArgs(CpuTemp, GpuTemp, CpuFanRpm, GpuFanRpm));
    }

    private static bool IsCpuFanName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.ToUpperInvariant().Contains("CPU");
    }

    private static bool IsGpuFanName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string n = name.ToUpperInvariant();
        return n.Contains("GPU") || n.Contains("VGA");
    }

    private void OnServerMessage(object? sender, ServiceResponse resp)
    {
        if (resp.Response == Response.CamToggled)
        {
            try { HotkeyHook.ToggleCamOsd(); } catch { }
        }
    }

    public void Stop()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
        try { _ipc.Stop(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        try { _ipc.Dispose(); } catch { }
    }

    // ----------------------------------------------------------------
    // 公共 API (GUI 侧调用)
    // ----------------------------------------------------------------

    public bool LoadConfig()
    {
        lock (_lock)
        {
            try
            {
                Paths.EnsureCurrentConfigExists();
                Paths.EnsureFeatureManagerExtracted();
                _config = MSIFlux_Config.Load(Paths.CurrentConf);
                SafeLog($"配置已加载: FanConfs={_config?.FanConfs?.Count ?? -1}");
                return true;
            }
            catch (Exception ex)
            {
                SafeLog($"LoadConfig 失败: {ex.Message}", LogLevel.Error);
                _config = null;
                return false;
            }
        }
    }

    /// <summary>
    /// 让服务重新加载并应用当前配置文件.
    /// 注意: GUI 代码习惯先就地修改 <see cref="_config"/> 再调本方法,
    /// 在旧架构里 Runner 和 UI 共享同一个内存对象能直接生效; 新架构下
    /// 服务端从磁盘读配置, 所以这里必须先把内存对象落盘一次, 否则
    /// 服务看到的是旧 XML.
    /// </summary>
    public bool ApplyConfig()
    {
        lock (_lock)
        {
            if (_config != null)
            {
                try
                {
                    _config.Save(Paths.CurrentConf);
                }
                catch (Exception ex)
                {
                    SafeLog($"ApplyConfig 落盘失败: {ex.Message}", LogLevel.Warn);
                }
            }
        }

        if (!_ipc.IsConnected)
        {
            SafeLog("ApplyConfig: IPC 未连接, 尝试重连...", LogLevel.Warn);
            _ipc.ForceReconnect();
            Thread.Sleep(500);
        }

        bool ok = _ipc.ApplyConf(TimeSpan.FromSeconds(3));
        if (!ok)
        {
            SafeLog("ApplyConfig 首次失败, 重连后重试...", LogLevel.Warn);
            _ipc.ForceReconnect();
            Thread.Sleep(1000);
            ok = _ipc.ApplyConf(TimeSpan.FromSeconds(3));
            if (!ok)
                SafeLog("ApplyConfig 重试仍然失败", LogLevel.Error);
        }
        return ok;
    }

    public void SetFullBlast(int enable) => _ipc.SetFullBlast(enable);

    public void ToggleMuteLed() => _ipc.ToggleMuteLed();
    public void ToggleMicLed() => _ipc.ToggleMicLed();
    public void SetAudioMuteLed(bool on) => _ipc.SetAudioMuteLed(on);
    public void SetMicMuteLed(bool on) => _ipc.SetMicMuteLed(on);

    public void SetPerfMode(int mode)
    {
        _pollSuspended = true;
        try
        {
            bool ok = _ipc.SetPerfMode(mode);
            if (!ok)
            {
                SafeLog("SetPerfMode 首次失败, 重连后重试...", LogLevel.Warn);
                _ipc.ForceReconnect();
                Thread.Sleep(1000);
                _ipc.SetPerfMode(mode);
            }
            // Service 后台执行 ApplyConf, 需等待写盘完成
            Thread.Sleep(800);
            lock (_lock)
            {
                try
                {
                    Paths.EnsureCurrentConfigExists();
                    _config = MSIFlux_Config.Load(Paths.CurrentConf);
                }
                catch { }
            }
            ConfigChanged?.Invoke(this, EventArgs.Empty);

            // 弹出屏幕提示 (OSD Toast)
            ShowPerfModeToast(mode);
        }
        finally
        {
            _pollSuspended = false;
        }
    }

    private void ShowPerfModeToast(int mode)
    {
        try
        {
            string rawName = "";
            if (_config?.PerfModeConf?.PerfModes != null && mode >= 0 && mode < _config.PerfModeConf.PerfModes.Count)
            {
                rawName = _config.PerfModeConf.PerfModes[mode].Name ?? "";
            }

            string displayName;
            System.Drawing.Image? icon = null;

            switch (rawName.ToLowerInvariant())
            {
                case "maximum battery life":
                case "eco":
                case "省电模式":
                    displayName = "省电模式";
                    icon = Properties.Resources.icons8_batterie_voll_geladen_48;
                    break;
                case "silent":
                case "静音模式":
                    displayName = "静音模式";
                    icon = Properties.Resources.icons8_bicycle_48__1_;
                    break;
                case "balanced":
                case "平衡模式":
                    displayName = "平衡模式";
                    icon = Properties.Resources.icons8_fiat_500_48;
                    break;
                case "high performance":
                case "turbo":
                case "增强模式":
                    displayName = "增强模式";
                    icon = Properties.Resources.icons8_rocket_48;
                    break;
                default:
                    displayName = string.IsNullOrEmpty(rawName) ? $"模式 {mode + 1}" : rawName;
                    break;
            }

            OsdToastForm.ShowToast(displayName, icon);
        }
        catch { }
    }

    public void NextPerfMode()
    {
        lock (_lock)
        {
            if (_config?.PerfModeConf?.PerfModes is null || _config.PerfModeConf.PerfModes.Count == 0) return;
            int count = _config.PerfModeConf.PerfModes.Count;
            int current = _config.PerfModeConf.ModeSel;
            int next = (current + 1) % count;
            SetPerfMode(next);
        }
    }

    public void SetFanProfile(int profile)
    {
        _pollSuspended = true;
        try
        {
            bool ok = _ipc.SetFanProf(profile);
            if (!ok)
            {
                SafeLog("SetFanProfile 首次失败, 重连后重试...", LogLevel.Warn);
                _ipc.ForceReconnect();
                Thread.Sleep(1000);
                _ipc.SetFanProf(profile);
            }
            if (_config != null && profile >= 0)
            {
                foreach (var fan in _config.FanConfs)
                {
                    if (profile < fan.FanCurveConfs.Count)
                        fan.CurveSel = profile;
                }
            }
        }
        finally
        {
            _pollSuspended = false;
        }
    }

    public bool ReadECByte(byte reg, out byte value)
        => _ipc.ReadECByte(reg, out value);

    public bool WriteECByte(byte reg, byte value)
        => _ipc.WriteECByte(reg, value);

    /// <summary>Sets GPU MUX mode (0=Hybrid, 1=Discrete). Requires reboot.</summary>
    public bool SetGpuMode(int mode) => _ipc.SetGpuMode(mode);

    /// <summary>Gets current GPU MUX mode. 0=Hybrid, 1=Discrete, 2=Eco, -1=error.</summary>
    public int GetGpuMode()
    {
        // Detect locally using EnumDisplayDevices (works in user session).
        int mode = DetectGpuModeLocal();
        if (mode >= 0)
        {
            // Report to service for caching.
            _ipc.ReportGpuMode(mode);
            return mode;
        }
        // Fallback to service-side detection.
        return _ipc.GetGpuMode();
    }

    private static int _cachedGpuMode = -1;
    private static DateTime _gpuModeCacheTime = DateTime.MinValue;
    private static readonly TimeSpan GpuModeCacheTtl = TimeSpan.FromSeconds(10);

    /// <summary>Invalidates the GPU mode cache (call after a GPU switch).</summary>
    public static void InvalidateGpuModeCache()
    {
        _cachedGpuMode = -1;
        _gpuModeCacheTime = DateTime.MinValue;
    }

    /// <summary>
    /// Detects GPU mode by checking which GPU drives the display via EnumDisplayDevices.
    /// Must be called from the user session (not Session 0).
    /// Results are cached for 10 seconds.
    /// </summary>
    private static int DetectGpuModeLocal()
    {
        if (_cachedGpuMode >= 0 && (DateTime.UtcNow - _gpuModeCacheTime) < GpuModeCacheTtl)
            return _cachedGpuMode;

        try
        {
            bool nvidiaDriving = false;
            bool intelDriving = false;

            for (uint i = 0; ; i++)
            {
                var adapter = new NativeInterop.DISPLAY_DEVICE
                {
                    cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeInterop.DISPLAY_DEVICE>()
                };
                if (!NativeInterop.EnumDisplayDevices(null, i, ref adapter, 0x00000001))
                    break;

                if ((adapter.StateFlags & 0x00000001) == 0) // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                    continue;

                if (adapter.DeviceString.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    nvidiaDriving = true;
                else if (adapter.DeviceString.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    intelDriving = true;
            }

            int result = -1;
            if (nvidiaDriving) result = 1; // Discrete
            else if (intelDriving)
            {
                // Intel drives display — check if NVIDIA is active for Hybrid vs Eco.
                try
                {
                    using var s = new System.Management.ManagementObjectSearcher(
                        "root\\cimv2",
                        "SELECT Status FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%'");
                    foreach (System.Management.ManagementObject mo in s.Get())
                    {
                        var status = mo["Status"]?.ToString();
                        if (status?.Equals("OK", StringComparison.OrdinalIgnoreCase) == true)
                        { result = 0; break; } // Hybrid
                    }
                }
                catch { }
                if (result < 0) result = 2; // Eco
            }

            if (result >= 0)
            {
                _cachedGpuMode = result;
                _gpuModeCacheTime = DateTime.UtcNow;
            }
            return result;
        }
        catch { }
        return -1;
    }

    public MSIFlux_Config? GetConfig() => _config;

    public void SaveConfig()
    {
        lock (_lock)
        {
            _config?.Save(Paths.CurrentConf);
        }
    }

    private void SafeLog(string msg, LogLevel level = LogLevel.Info)
    {
        try
        {
            switch (level)
            {
                case LogLevel.Error: _log.Error(msg); break;
                case LogLevel.Warn: _log.Warn(msg); break;
                case LogLevel.Debug: _log.Debug(msg); break;
                default: _log.Info(msg); break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSIFlux] Log write failed: {ex.Message}");
        }
    }
}

public class TempEventArgs : EventArgs
{
    public int CpuTemp { get; }
    public int GpuTemp { get; }
    public int CpuFanRpm { get; }
    public int GpuFanRpm { get; }

    public TempEventArgs(int cpuTemp, int gpuTemp, int cpuFanRpm, int gpuFanRpm)
    {
        CpuTemp = cpuTemp;
        GpuTemp = gpuTemp;
        CpuFanRpm = cpuFanRpm;
        GpuFanRpm = gpuFanRpm;
    }
}
