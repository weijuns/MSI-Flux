// This file is part of MSIFlux.
// Licensed under GPL-3.0-or-later.
//
// "单 exe 双角色" 架构入口:
//   MSI Flux.exe                      → GUI 模式 (asInvoker, 普通用户)
//   MSI Flux.exe --silent             → GUI 模式, 静默启动 (任务计划程序开机自启用)
//   MSI Flux.exe --service            → Windows 服务模式 (由 SCM 以 SYSTEM 启动)
//   MSI Flux.exe --install-service    → 一次性安装器 (由 UAC 提权调用)
//   MSI Flux.exe --uninstall-service  → 一次性卸载器 (由 UAC 提权调用)
//
// GUI 首次启动时会检测服务是否已安装, 未装则自我提权安装服务. 之后日常 GUI
// 启动不再需要管理员权限. 风扇/EC 的全部硬件访问都发生在 Windows 服务内,
// GUI 通过命名管道 (MSIFlux-Server) 与服务通信.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using MSIFlux.Common;
using MSIFlux.Common.Configs;
using MSIFlux.Common.Logs;
using MSIFlux.GUI.Helpers;
using MSIFlux.IPC;
using MSIFlux.Service;

namespace MSIFlux.GUI
{
    internal static class Program
    {
        // ====== Win32 互操作 (用于现有实例窗口激活) ======
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        // ====== 全局状态: FanRunner 由 SettingsForm/Fans/Extra 引用, 保持公共 API 不变 ======
        internal static FanControlRunner? FanRunner { get; set; }
        internal static MSIFlux.Common.Logs.Logger? FanLogger { get; set; }

        // Bug #10 fix: 用全局命名 Mutex 做单实例检测, 比按进程名判断可靠得多
        // - 进程名方式会把同名其它软件当作自家实例
        // - 进程名方式在终端服务/快速切换用户时, 别的用户会话里的实例会被当作"已有实例"
        // Mutex 名里加一个 GUID 做唯一性标识; "Local\" 前缀表示作用域限当前登录会话,
        // 这样不同用户可以各自开一个 GUI (但同一用户仍然单实例).
        private const string SingleInstanceMutexName =
            @"Local\MSIFlux-SingleInstance-{B8F3A2E1-9D7C-4F56-A3B4-1E8D7C6F5A4B}";
        private static Mutex? _singleInstanceMutex;

        // 命名事件, 用于跨进程 "请已运行实例唤起主窗口" 的信号.
        // 新进程拿不到 Mutex 时, 把这个事件 Set 一下再退出;
        // 已运行的实例启动时会起一个后台线程 WaitOne 这个事件, 收到则 ShowMainWindow().
        private const string ShowWindowEventName =
            @"Local\MSIFlux-ShowWindow-{B8F3A2E1-9D7C-4F56-A3B4-1E8D7C6F5A4B}";
        private static EventWaitHandle? _showWindowEvent;
        private static Thread? _showWindowListenerThread;
        private static volatile bool _showWindowListenerStop;
        internal static SettingsForm? MainForm { get; set; }

        // ====== 入口分派 ======
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                // 极早诊断: 确认进程是否以提权方式启动 (UAC 若在进程启动前触发, 这里可能根本到不了)
                try
                {
                    var diagPath = Path.Combine(MSIFlux.Common.Paths.Logs, "diag_ensure.txt");
                    using (var sw = System.IO.File.AppendText(diagPath))
                    {
                        sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Main() 进入, args=[{string.Join(" ", args)}], elevated={ServiceManager.IsCurrentProcessElevated()}");
                    }
                }
                catch { }

                if (args.Contains("--service"))
                {
                    return RunAsService();
                }

                if (args.Contains("--install-service"))
                {
                    return InstallServiceEntry();
                }

                if (args.Contains("--stop-service"))
                {
                    return StopServiceEntry();
                }

                if (args.Contains("--uninstall-service"))
                {
                    return UninstallServiceEntry();
                }

                if (args.Contains("--service-autostart"))
                {
                    return SetServiceAutoStartEntry(true);
                }

                if (args.Contains("--service-manual"))
                {
                    return SetServiceAutoStartEntry(false);
                }

                if (args.Contains("--enable-autostart"))
                {
                    return SetAutoStartEntry(true);
                }

                if (args.Contains("--disable-autostart"))
                {
                    return SetAutoStartEntry(false);
                }

                return RunAsGui(args);
            }
            catch (Exception ex)
            {
                // 终极兜底: 不要让未捕获异常静默终止进程
                try
                {
                    MessageBox.Show(
                        $"MSI Flux 启动失败:\n{ex.Message}\n\n{ex.StackTrace}",
                        "MSI Flux", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
                return 1;
            }
        }

        // ================================================================
        // 角色 1: Windows 服务
        // ================================================================
        private static int RunAsService()
        {
            // 由 SCM 调用. 不能创建 WinForms 组件.
            var log = new MSIFlux.Common.Logs.Logger
            {
                LogDir = Paths.Logs,
                LogName = "Service",
                ConsoleLevel = LogLevel.None,
                FileLevel = LogLevel.Debug,
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    log.Fatal($"未捕获异常: {ex?.Message}\n{ex?.StackTrace}");
                }
                catch { }
            };

            try
            {
                log.Info($"OS: {Environment.OSVersion}, Svc version: {Application.ProductVersion}");
                log.FileLevel = CommonConfig.GetLogLevel();
                ServiceBase.Run(new FanControlService(log));
                return 0;
            }
            catch (Exception ex)
            {
                try { log.Fatal($"服务主循环异常: {ex}"); } catch { }
                return 1;
            }
        }

        // ================================================================
        // 角色 2: 安装器 (由 UAC 提权调用)
        // ================================================================
        private static int InstallServiceEntry()
        {
            if (!ServiceManager.IsCurrentProcessElevated())
            {
                return 2;
            }

            // 如果装过但 binPath 过期 (用户把软件移到别处), 先卸载重装
            if (ServiceManager.IsInstalled() && ServiceManager.IsServicePathOutOfDate())
            {
                ServiceManager.Uninstall();
                Thread.Sleep(500);
            }

            if (!ServiceManager.IsInstalled())
            {
                if (!ServiceManager.Install())
                {
                    return 3;
                }
            }

            // 启动服务 (失败不是致命错误, GUI 侧会再检查)
            ServiceManager.Start(TimeSpan.FromSeconds(15));
            return 0;
        }

        private static int UninstallServiceEntry()
        {
            if (!ServiceManager.IsCurrentProcessElevated()) return 2;
            return ServiceManager.Uninstall() ? 0 : 3;
        }

        private static int StopServiceEntry()
        {
            if (!ServiceManager.IsCurrentProcessElevated()) return 2;
            return ServiceManager.Stop(TimeSpan.FromSeconds(15)) ? 0 : 3;
        }

        private static int SetServiceAutoStartEntry(bool auto)
        {
            if (!ServiceManager.IsCurrentProcessElevated()) return 2;
            return ServiceManager.SetStartType(auto) ? 0 : 3;
        }

        /// <summary>
        /// 提权子进程入口: 一次性完成 计划任务 + 服务启动类型 的设置.
        /// 由 GUI 点击开机自启开关时通过 UAC 提权调用.
        /// </summary>
        private static int SetAutoStartEntry(bool enable)
        {
            if (!ServiceManager.IsCurrentProcessElevated()) return 2;

            int code = 0;
            try
            {
                if (enable)
                {
                    MSIFlux.GUI.Helpers.Startup.DoSchedule();
                    if (!ServiceManager.SetStartType(true)) code = 3;
                }
                else
                {
                    MSIFlux.GUI.Helpers.Startup.DoUnSchedule();
                    if (!ServiceManager.SetStartType(false)) code = 3;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoStartEntry] 失败: {ex.Message}");
                code = 4;
            }
            return code;
        }

        // ================================================================
        // 角色 3: GUI (默认)
        // ================================================================
        private static int RunAsGui(string[] args)
        {
            bool isRestart = args.Contains("--restart");
            bool silentMode = args.Contains("--silent");

            // 诊断: 记录启动参数与静默模式, 用于排查开机自启是否误触发提权
            var _bootLog = new MSIFlux.Common.Logs.Logger { LogDir = MSIFlux.Common.Paths.Logs, LogName = "GUI" };
            try
            {
                _bootLog.Info($"[GUI] 启动: silent={silentMode}, args=[{string.Join(" ", args)}], elevated={ServiceManager.IsCurrentProcessElevated()}");
            }
            catch { }

            if (isRestart)
            {
                // 自重启: 等旧进程先退
                Thread.Sleep(1500);
            }

            // --- 单实例检测: 命名 Mutex 优先, 失败再回退到按进程名 (兼容 Mutex 被安全软件屏蔽场景) ---
            bool gotMutex = false;
            try
            {
                _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out gotMutex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MSIFlux] 创建单实例 Mutex 失败, 回退到进程名检测: {ex.Message}");
            }

            if (!gotMutex)
            {
                // 已经有实例在运行. 给它发 "显示窗口" 信号后退出.
                // 即便已有实例处于托盘 Hidden 状态, 信号也能把它唤起, 避免
                // 用户双击 exe 无响应的糟糕体验.
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(ShowWindowEventName);
                    signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // 已有实例还没来得及创建事件 (或者版本不匹配), 回退到旧的
                    // Win32 窗口激活路径. 拿到主窗口句柄就 ActivateProcessWindow.
                    Process? existing = FindExistingMSIFluxProcess();
                    if (existing != null)
                    {
                        try
                        {
                            if (!existing.HasExited && existing.MainWindowHandle != IntPtr.Zero)
                            {
                                ActivateProcessWindow(existing);
                            }
                        }
                        catch { }
                        finally { existing.Dispose(); }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MSI Flux] 发送 ShowWindow 信号失败: {ex.Message}");
                }
                return 0;
            }

            // 本进程是"首启"实例, 创建命名事件并起监听线程
            try
            {
                _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
                StartShowWindowListener();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MSI Flux] 创建 ShowWindow 事件失败: {ex.Message}");
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 确保配置目录与默认配置文件存在
            Paths.EnsureCurrentConfigExists();

            Extra.ApplySavedLanguage();

            // 自动装载高精度定时器设置 (0.5ms)
            if (MSIFlux.Common.Configs.CommonConfig.GetEnable05msTimer())
            {
                MSIFlux.GUI.Helpers.TimerResolution.SetState(true);
            }

            // --- 确保 Windows 服务存在并在运行 ---
            if (!EnsureServiceReady(silentMode))
            {
                // 没有服务就没法控风扇. 给用户一个明确的失败提示.
                if (!silentMode)
                {
                    MessageBox.Show(
                        "MSI Flux 后台服务未能启动. 软件将以降级模式打开 (只能查看/编辑配置, 无法实际控制风扇).\n\n" +
                        "你可以稍后从设置页面里手动重新安装服务.",
                        "MSI Flux", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                // 不退出, 降级运行 UI
            }

            // --- 初始化 IPC 代理 (FanRunner 的内部依赖) ---
            var logger = new MSIFlux.Common.Logs.Logger
            {
                LogDir = Paths.Logs,
                LogName = "GUI",
            };
            FanLogger = logger;
        FanRunner = new FanControlRunner(logger);
        HotkeyHook? hotkeyHook = null;

        try
        {
            // Start() 只是连接 IPC, 无论成败都继续跑 UI.
            // 失败时 FanRunner 的各方法返回 false, 不会抛异常, UI 仍可用.
            FanRunner.Start();

            // 启动 Fn 热键检测 (键盘钩子 → IPC → Service)
            hotkeyHook = new HotkeyHook(FanRunner, logger);
            hotkeyHook.Start();

            using var form = new SettingsForm();
            form.Text = "MSI Flux";
            if (silentMode) form.SilentStart = true;
            MainForm = form;

            Application.Run(form);
            MainForm = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"运行时错误: {ex.Message}\n\n{ex.StackTrace}",
                "MSI Flux", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            hotkeyHook?.Dispose();
            try
            {
                    FanRunner?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MSIFlux] FanRunner.Dispose failed: {ex.Message}");
                }

                // 停掉事件监听线程并释放命名事件
                try
                {
                    _showWindowListenerStop = true;
                    _showWindowEvent?.Set();    // 把阻塞中的 WaitOne 踢醒
                    _showWindowListenerThread?.Join(500);
                    _showWindowEvent?.Dispose();
                    _showWindowEvent = null;
                }
                catch { }

                // 释放单实例 Mutex, 让下一次启动能正常获取
                try
                {
                    if (_singleInstanceMutex != null)
                    {
                        try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                        _singleInstanceMutex.Dispose();
                        _singleInstanceMutex = null;
                    }
                }
                catch { }
            }
            return 0;
        }

        /// <summary>
        /// 起后台线程监听命名事件; 收到信号则请求主窗口显示.
        /// 调用 ShowMainWindow 时若 MainForm 尚未创建 (早期启动阶段), 直接忽略.
        /// </summary>
        private static void StartShowWindowListener()
        {
            if (_showWindowEvent == null) return;

            _showWindowListenerStop = false;
            _showWindowListenerThread = new Thread(() =>
            {
                while (!_showWindowListenerStop)
                {
                    try
                    {
                        if (_showWindowEvent == null) break;
                        if (_showWindowEvent.WaitOne(1000))
                        {
                            if (_showWindowListenerStop) break;
                            var form = MainForm;
                            if (form != null && !form.IsDisposed)
                            {
                                form.ShowMainWindow();
                            }
                        }
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MSI Flux] ShowWindow listener 异常: {ex.Message}");
                        Thread.Sleep(200);
                    }
                }
            })
            {
                IsBackground = true,
                Name = "MSIFlux-ShowWindowListener",
            };
            _showWindowListenerThread.Start();
        }

        /// <summary>
        /// 确保 Windows 服务已安装并运行. 未装则自我提权安装.
        /// </summary>
        private static bool EnsureServiceReady(bool silent)
        {
            // 顺带检测 MSI 官方服务冲突 (保留既有行为但更温和: 只检测不强杀)
            WarnIfMSIServicesRunning();

            DiagEnsure($"EnsureServiceReady 开始: silent={silent}");

            // 1. 未安装 → 提权安装
            if (!ServiceManager.IsInstalled())
            {
                DiagEnsure($"分支1 未安装, silent={silent}");
                if (silent)
                {
                    // 静默启动 (开机自启场景) 下不弹 UAC, 等用户交互时再说
                    return false;
                }

                var ans = MessageBox.Show(
                    "首次运行需要安装 MSI Flux 后台服务 (仅需一次, 之后日常打开软件不再弹出管理员提示).\n\n" +
                    "是否继续?",
                    "MSI Flux - 首次设置", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (ans != DialogResult.OK) return false;

                int code = ServiceManager.RelaunchElevated("--install-service");
                if (code != 0)
                {
                    string reason = code switch
                    {
                        -1 => "用户取消了 UAC 提权, 或提权进程未能启动.",
                        2  => "提权失败: 当前进程未以管理员身份运行.",
                        3  => "服务安装失败 (sc.exe create 返回错误). 请检查是否有安全软件拦截.",
                        _  => $"安装器返回未知错误码: {code}.",
                    };
                    MessageBox.Show(
                        $"后台服务安装失败:\n{reason}\n\n" +
                        "请尝试以管理员身份手动运行 MSI Flux, 或在设置页面重新安装.",
                        "MSI Flux - 安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            else if (ServiceManager.IsServicePathOutOfDate())
            {
                DiagEnsure($"分支2 路径过期, silent={silent}");
                // 软件被移动过. 提示用户重装.
                if (!silent)
                {
                    var ans = MessageBox.Show(
                        "检测到 MSI Flux 已被移动到新位置. 需要重新安装后台服务才能正常工作.\n\n" +
                        "是否立即重新安装?",
                        "MSI Flux", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (ans == DialogResult.OK)
                    {
                        ServiceManager.RelaunchElevated("--install-service");
                    }
                }
            }
            else if (!ServiceManager.HasNonAdminStartPermission())
            {
                DiagEnsure($"分支3 无普通用户启动权限, silent={silent}");
                // 服务已装且路径正确, 但 SDDL 权限不对.
                // 普通用户双击 exe 时无法启动服务 (SCM 返回 拒绝访问).
                if (!silent)
                {
                    var ans = MessageBox.Show(
                        "检测到后台服务的权限配置不完整 (普通用户无启动权限).\n\n" +
                        "这会导致非管理员身份运行时无法启动服务. 是否立即修复?",
                        "MSI Flux - 权限修复", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (ans == DialogResult.OK)
                    {
                        ServiceManager.RelaunchElevated("--install-service");
                    }
                }
            }

            // 2. 已装但没跑 → 启动
            DiagEnsure($"进入启动检查: running={ServiceManager.IsRunning()}");
            if (!ServiceManager.IsRunning())
            {
                // 服务启动可能较慢 (加载 WinRing0 驱动 / 写入 EC 寄存器), 首次 Start 给足 15s
                DiagEnsure($"尝试启动服务...");
                bool started = ServiceManager.Start(TimeSpan.FromSeconds(15));
                DiagEnsure($"Start() 结果: {started}, running={ServiceManager.IsRunning()}");
                if (!started)
                {
                    // 再宽限 15s, 避免启动慢导致误判失败而弹 UAC
                    started = ServiceManager.WaitUntilRunning(TimeSpan.FromSeconds(15));
                    DiagEnsure($"宽限等待后: {started}");
                }

                if (!started && !silent)
                {
                    // 已装、路径正确、权限正确, 但服务确实无法启动:
                    // 不弹 UAC, 让软件以降级模式运行, 用户可在设置页手动处理
                    DiagEnsure("服务启动失败 (非权限问题), 降级运行, 不弹 UAC");
                }
            }
            else
            {
                DiagEnsure("服务已在运行");
            }

            return ServiceManager.WaitUntilRunning(TimeSpan.FromSeconds(10));
        }

        /// <summary>把 EnsureServiceReady 的每个分支写到一个固定诊断文件, 便于排查 UAC 触发点.</summary>
        private static void DiagEnsure(string msg)
        {
            try
            {
                var dir = Path.Combine(MSIFlux.Common.Paths.Logs, "..", "Logs");
                string path = Path.Combine(MSIFlux.Common.Paths.Logs, "diag_ensure.txt");
                System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { }
        }

        /// <summary>
        /// 检测 MSI Center 残留的服务是否在跑. 只记录, 不强制停止 (不再是启动时的副作用).
        /// 如需停用, 由 SettingsForm / Extra 的 UI 按钮显式触发.
        /// </summary>
        private static void WarnIfMSIServicesRunning()
        {
            try
            {
                var conflicts = new List<string>();
                string[] svcNames = { "MSI Foundation Service", "Micro Star SCM" };
                foreach (var svc in svcNames)
                {
                    if (Utils.ServiceExists(svc))
                    {
                        try
                        {
                            using var sc = new ServiceController(svc);
                            if (sc.Status == ServiceControllerStatus.Running)
                            {
                                conflicts.Add(svc);
                            }
                        }
                        catch { }
                    }
                }
                if (conflicts.Count > 0)
                {
                    Debug.WriteLine($"[MSIFlux] 检测到 MSI 官方服务在运行: {string.Join(", ", conflicts)}");
                }
            }
            catch { }
        }

        // ================================================================
        // 单实例 / 窗口激活辅助
        // ================================================================
        private static Process? FindExistingMSIFluxProcess()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("MSI Flux");
                using Process currentProcess = Process.GetCurrentProcess();
                foreach (Process p in processes)
                {
                    try
                    {
                        if (p.Id != currentProcess.Id)
                        {
                            return p;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MSIFlux] Error checking process: {ex.Message}");
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MSIFlux] Error finding existing process: {ex.Message}");
                return null;
            }
        }

        private static bool ActivateProcessWindow(Process process)
        {
            try
            {
                if (process.HasExited) return false;

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return BringWindowToFront(process.MainWindowHandle);
                }

                IntPtr hwnd = FindWindowForProcess(process.Id);
                if (hwnd != IntPtr.Zero)
                {
                    return BringWindowToFront(hwnd);
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MSIFlux] Error activating process window: {ex.Message}");
                return false;
            }
        }

        private static IntPtr FindWindowForProcess(int processId)
        {
            IntPtr shellWindow = GetShellWindow();
            IntPtr foundHwnd = IntPtr.Zero;

            EnumWindowsDelegate callback = (hwnd, lParam) =>
            {
                if (hwnd == shellWindow) return true;

                GetWindowThreadProcessId(hwnd, out uint windowProcessId);
                if (windowProcessId == (uint)processId)
                {
                    bool visible = IsWindowVisible(hwnd);
                    if (foundHwnd == IntPtr.Zero)
                    {
                        foundHwnd = hwnd;
                        if (visible) return false;
                    }
                }
                return true;
            };

            EnumWindows(callback, IntPtr.Zero);
            return foundHwnd;
        }

        private static bool BringWindowToFront(IntPtr hwnd)
        {
            try
            {
                if (IsIconic(hwnd))
                    ShowWindow(hwnd, SW_RESTORE);

                ShowWindow(hwnd, SW_SHOW);
                SetForegroundWindow(hwnd);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MSIFlux] Error bringing window to front: {ex.Message}");
                return false;
            }
        }
    }
}
