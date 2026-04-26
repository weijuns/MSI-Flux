// This file is part of YAMDCC (Yet Another MSI Dragon Center Clone).
// Licensed under GPL-3.0-or-later.
//
// ServiceManager: 封装 Windows 服务的安装/卸载/启停/状态查询.
// 用于 "单 exe 双角色" 架构 —— 同一个 MSI Flux.exe 在 GUI 模式下通过此类
// 管理另一个以 SYSTEM 身份运行的自身实例 (--service).

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace YAMDCC.GUI.Helpers;

internal static class ServiceManager
{
    /// <summary>服务内部名 (sc 查询键). 请勿随意更改, 否则旧装机残留无法识别.</summary>
    public const string ServiceName = "MSIFluxService";

    /// <summary>服务显示名 (服务管理器里可见).</summary>
    public const string DisplayName = "MSI Flux Service";

    /// <summary>服务描述.</summary>
    public const string Description =
        "MSI Flux - MSI 笔记本风扇与性能控制服务 (fork of YAMDCC)";

    /// <summary>当前进程是否以管理员身份运行.</summary>
    public static bool IsCurrentProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>返回当前可执行文件的绝对路径.</summary>
    public static string GetExecutablePath()
    {
        // 单文件发布时 Application.ExecutablePath 指向解包后的真实路径,
        // 而 Environment.ProcessPath 才是启动的那个 exe. 我们要后者.
        return Environment.ProcessPath ?? Application.ExecutablePath;
    }

    /// <summary>服务是否已注册到 SCM.</summary>
    public static bool IsInstalled()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            // 访问 Status 属性会在服务不存在时抛异常
            _ = sc.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>服务当前是否处于 Running 状态.</summary>
    public static bool IsRunning()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取服务当前注册的 ImagePath (binPath). 用于检测版本/路径是否和当前 exe 一致,
    /// 如果升级后 exe 路径变了, 需要重装服务.
    /// </summary>
    public static string? GetRegisteredBinPath()
    {
        try
        {
            const string keyPath = @"SYSTEM\CurrentControlSet\Services\" + ServiceName;
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue("ImagePath") as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检查已安装的服务 binPath 是否指向当前 exe (处理用户把软件移动到其他目录的情况).
    /// </summary>
    public static bool IsServicePathOutOfDate()
    {
        string? registered = GetRegisteredBinPath();
        if (string.IsNullOrEmpty(registered)) return false;

        // ImagePath 通常形如: "C:\path\MSI Flux.exe" --service
        // 取出第一段路径并 Trim 引号
        string path = registered.Trim();
        if (path.StartsWith("\""))
        {
            int end = path.IndexOf('"', 1);
            if (end > 0) path = path.Substring(1, end - 1);
        }
        else
        {
            int sp = path.IndexOf(' ');
            if (sp > 0) path = path.Substring(0, sp);
        }

        try
        {
            string current = Path.GetFullPath(GetExecutablePath());
            string regFull = Path.GetFullPath(path);
            return !string.Equals(current, regFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 用 sc.exe 安装服务. 必须以管理员身份运行, 否则返回 false.
    /// binPath 指向当前 exe, 参数为 --service.
    /// </summary>
    public static bool Install()
    {
        if (!IsCurrentProcessElevated())
        {
            Debug.WriteLine("[ServiceManager] Install() 需要管理员权限");
            return false;
        }

        string exe = GetExecutablePath();
        string binPath = $"\"{exe}\" --service";

        // sc create MSIFluxService binPath= "..." start= auto DisplayName= "MSI Flux Service"
        // 注意 sc 的语法: key= value 之间必须有空格, = 和 value 之间不能有空格之外的字符
        int code = RunSc($"create {ServiceName} binPath= {Quote(binPath)} start= auto DisplayName= {Quote(DisplayName)}");
        if (code != 0)
        {
            Debug.WriteLine($"[ServiceManager] sc create 失败, 退出码 {code}");
            return false;
        }

        // 设置描述 (失败不致命)
        RunSc($"description {ServiceName} {Quote(Description)}");

        // 设置故障恢复策略: 连续 3 次失败后分别延时 60s/60s/120s 后重启
        RunSc($"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/120000");

        return true;
    }

    /// <summary>停止并卸载服务.</summary>
    public static bool Uninstall()
    {
        if (!IsCurrentProcessElevated())
        {
            Debug.WriteLine("[ServiceManager] Uninstall() 需要管理员权限");
            return false;
        }

        if (IsRunning())
        {
            Stop(TimeSpan.FromSeconds(10));
        }

        int code = RunSc($"delete {ServiceName}");
        return code == 0;
    }

    /// <summary>启动服务. 需要当前用户对该服务有启动权限.</summary>
    public static bool Start(TimeSpan? timeout = null)
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running) return true;

            if (sc.Status == ServiceControllerStatus.StartPending ||
                sc.Status == ServiceControllerStatus.ContinuePending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, timeout ?? TimeSpan.FromSeconds(15));
                return sc.Status == ServiceControllerStatus.Running;
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, timeout ?? TimeSpan.FromSeconds(15));
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceManager] Start 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>停止服务.</summary>
    public static bool Stop(TimeSpan? timeout = null)
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Stopped) return true;
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout ?? TimeSpan.FromSeconds(15));
            return sc.Status == ServiceControllerStatus.Stopped;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceManager] Stop 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 阻塞等待服务达到 Running 状态. 用于安装/启动后, GUI 连接 IPC 之前.
    /// </summary>
    public static bool WaitUntilRunning(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (IsRunning()) return true;
            Thread.Sleep(250);
        }
        return IsRunning();
    }

    /// <summary>
    /// 以提权方式重启自身, 让其执行 --install-service 或 --uninstall-service.
    /// </summary>
    /// <returns>提权子进程的退出码; 用户拒绝 UAC 时返回 -1.</returns>
    public static int RelaunchElevated(string arg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = GetExecutablePath(),
                Arguments = arg,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return -1;
            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 用户在 UAC 弹窗点了否
            return -1;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceManager] RelaunchElevated 失败: {ex.Message}");
            return -1;
        }
    }

    private static int RunSc(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return -1;
            proc.WaitForExit(10000);
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServiceManager] sc.exe 调用失败: {ex.Message}");
            return -1;
        }
    }

    private static string Quote(string s)
    {
        // sc 的 key= value 语法要求 value 里若含空格则用引号包裹
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
