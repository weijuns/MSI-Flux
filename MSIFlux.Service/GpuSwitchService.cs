// This file is part of MSIFlux.
// Copyright © 2026 weijuns.
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
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using MSIFlux.Common;
using MSIFlux.Common.Logs;

namespace MSIFlux.Service;

internal sealed partial class FanControlService
{
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
    /// 确保 MSI Foundation Service 在运行. 按优先级尝试:
    /// 1. 已在运行 → 直接返回
    /// 2. 已注册但路径过期 → 删除后重装
    /// 3. 已注册 → 直接启动
    /// 4. 未注册 → InstallUtil 注册 + 启动
    /// </summary>
    private bool EnsureMsiFoundationServiceRunning(string msiApSvcPath)
    {
        const string svcName = "MSI Foundation Service";

        // 1. 已在运行?
        try
        {
            using var svc = new ServiceController(svcName);
            if (svc.Status == ServiceControllerStatus.Running)
                return true;
        }
        catch { /* 服务不存在 */ }

        // 2. 已注册但路径过期?
        try
        {
            string keyPath = $@"SYSTEM\CurrentControlSet\Services\{svcName}";
            using var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
            string? registeredBinPath = regKey?.GetValue("ImagePath") as string;
            if (!string.IsNullOrEmpty(registeredBinPath))
            {
                string cleanPath = registeredBinPath.Trim('"');
                if (!File.Exists(cleanPath) && File.Exists(msiApSvcPath))
                {
                    Log.Warn($"Service binary path is stale: {registeredBinPath}");
                    try
                    {
                        var delP = Process.Start(new ProcessStartInfo("sc.exe")
                        {
                            Arguments = $"delete \"{svcName}\"",
                            UseShellExecute = false, CreateNoWindow = true
                        });
                        delP!.WaitForExit(5000);
                        Thread.Sleep(1000);
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 3. 尝试直接启动
        try
        {
            Log.Info($"Starting {svcName}...");
            using var svc = new ServiceController(svcName);
            svc.Start();
            svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            Log.Info($"{svcName} started");
            return true;
        }
        catch { /* 未注册或启动失败 */ }

        // 4. InstallUtil 注册 + 启动
        return InstallAndStartMsiFoundationService(msiApSvcPath);
    }

    /// <summary>
    /// 通过 InstallUtil 注册并启动 MSI Foundation Service.
    /// </summary>
    private bool InstallAndStartMsiFoundationService(string msiApSvcPath)
    {
        if (!File.Exists(msiApSvcPath))
        {
            Log.Error("MSIAPService.exe not found, cannot register service");
            return false;
        }

        string installUtil = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe");

        if (!File.Exists(installUtil))
        {
            Log.Error($"InstallUtil.exe not found at {installUtil}");
            return false;
        }

        Log.Info("Installing MSI Foundation Service via InstallUtil...");
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

            bool started = StartServiceWithRetry("MSI Foundation Service", 3, 5000);
            if (started)
                Log.Info("MSI Foundation Service installed and started");
            return started;
        }
        catch (Exception ex)
        {
            Log.Error($"InstallUtil failed: {ex.Message}");
            return false;
        }
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
                using (var svc2 = new ServiceController(serviceName))
                {
                    try
                    {
                        svc2.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        if (svc2.Status == ServiceControllerStatus.Running)
                            return true;
                    }
                    catch (System.ServiceProcess.TimeoutException) { }
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

    // P/Invoke for CreateProcessAsUser (Session 0 → user session process launch).
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess,
        IntPtr lpTokenAttributes, int impersonationLevel, int tokenType, out IntPtr phNewToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateProfile(string pszUserSid, string pszUserName,
        System.Text.StringBuilder pszProfilePath, uint cchProfilePath);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CreateProcessAsUserW(IntPtr hToken, string? lpApplicationName,
        string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFOW lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    private const uint TOKEN_ALL_ACCESS = 0x000F01FF;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    /// <summary>
    /// 在交互式用户会话中启动进程. 解决 Session 0 隔离问题:
    /// Windows 服务在 Session 0 运行, 直接启动的子进程也在 Session 0,
    /// 没有 GUI 桌面. WPF/WinForms 应用在 Session 0 会崩溃.
    /// 此方法用 WTSQueryUserToken + CreateProcessAsUserW 直接在用户 Session 启动.
    /// </summary>
    private bool StartProcessInUserSession(string exePath)
    {
        IntPtr userToken = IntPtr.Zero;
        IntPtr dupToken = IntPtr.Zero;
        IntPtr envBlock = IntPtr.Zero;
        try
        {
            // 找到活跃的交互式用户 Session
            uint sessionId = 0;
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    if (proc.SessionId > 0)
                    {
                        sessionId = (uint)proc.SessionId;
                        break;
                    }
                }
                catch { }
            }

            if (sessionId == 0)
            {
                Log.Error("No interactive user session found");
                return false;
            }

            Log.Info($"Starting '{exePath}' in user session {sessionId}");

            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                Log.Error($"WTSQueryUserToken failed: Win32={Marshal.GetLastWin32Error()}");
                return false;
            }

            if (!DuplicateTokenEx(userToken, TOKEN_ALL_ACCESS, IntPtr.Zero,
                SecurityImpersonation, TokenPrimary, out dupToken))
            {
                Log.Error($"DuplicateTokenEx failed: Win32={Marshal.GetLastWin32Error()}");
                return false;
            }

            CreateEnvironmentBlock(out envBlock, dupToken, false);

            var si = new STARTUPINFOW();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "winsta0\\default";

            string cmdLine = $"\"{exePath}\"";
            if (!CreateProcessAsUserW(dupToken, null, cmdLine,
                IntPtr.Zero, IntPtr.Zero, false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE,
                envBlock, null, ref si, out var pi))
            {
                Log.Error($"CreateProcessAsUserW failed: Win32={Marshal.GetLastWin32Error()}");
                return false;
            }

            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            Log.Info($"Process started in user session: PID={pi.dwProcessId}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"StartProcessInUserSession failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
            if (dupToken != IntPtr.Zero) CloseHandle(dupToken);
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
        }
    }

    /// <summary>
    /// 确保 MSI 注册表键存在 (FW_GPU_CH, FW_CurrentNewGPU).
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
        bool msiFoundationReady = EnsureMsiFoundationServiceRunning(msiApSvcPath);
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
        // 通常由 WMI ACPI 引导器 (msiapcfg.dll + MofImagePath) 自动完成.
        // 如果 WMI 仓库损坏, 则用内置 MSI_ACPI.mof 修复.
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
}
