// This file is part of MSIFlux, based on YAMDCC.
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
                if (args.Contains("--service"))
                {
                    return RunAsService();
                }

                if (args.Contains("--install-service"))
                {
                    return InstallServiceEntry();
                }

                if (args.Contains("--uninstall-service"))
                {
                    return UninstallServiceEntry();
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

        // ================================================================
        // 角色 3: GUI (默认)
        // ================================================================
        private static int RunAsGui(string[] args)
        {
            bool isRestart = args.Contains("--restart");
            bool silentMode = args.Contains("--silent");

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

            Extra.ApplySavedLanguage();

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

            try
            {
                // Start() 只是连接 IPC, 无论成败都继续跑 UI.
                // 失败时 FanRunner 的各方法返回 false, 不会抛异常, UI 仍可用.
                FanRunner.Start();

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

            // 1. 未安装 → 提权安装
            if (!ServiceManager.IsInstalled())
            {
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
                    return false;
                }
            }
            else if (ServiceManager.IsServicePathOutOfDate())
            {
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

            // 2. 已装但没跑 → 启动
            if (!ServiceManager.IsRunning())
            {
                if (!ServiceManager.Start(TimeSpan.FromSeconds(10)))
                {
                    // 启动失败可能是权限问题, 再试一次提权
                    if (!silent)
                    {
                        ServiceManager.RelaunchElevated("--install-service");
                    }
                }
            }

            return ServiceManager.WaitUntilRunning(TimeSpan.FromSeconds(10));
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

        public event EventHandler<TempEventArgs>? TempUpdated;

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
            if (!_ipc.IsConnected || _config == null) return;

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
            string n = name.ToUpperInvariant();
            return n.Contains("CPU");
        }

        private static bool IsGpuFanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToUpperInvariant();
            return n.Contains("GPU") || n.Contains("VGA");
        }

        private void OnServerMessage(object? sender, ServiceResponse resp)
        {
            // 留空. 当前 Service 只在响应时推消息; 未来若加 "主动推送"
            // (例如温度变化广播), 可以在这里缓存结果省掉一次轮询.
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
            if (!_ipc.IsConnected) return false;

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

            return _ipc.ApplyConf(TimeSpan.FromSeconds(3));
        }

        public void SetFullBlast(int enable) => _ipc.SetFullBlast(enable);

        public void SetPerfMode(int mode)
        {
            _ipc.SetPerfMode(mode);
            // 服务端内部会应用; 本地缓存里同步一下以便 UI 显示
            if (_config?.PerfModeConf != null &&
                mode >= 0 && mode < _config.PerfModeConf.PerfModes.Count)
            {
                _config.PerfModeConf.ModeSel = mode;
            }
        }

        public void SetFanProfile(int profile)
        {
            _ipc.SetFanProf(profile);
            if (_config != null && profile >= 0)
            {
                foreach (var fan in _config.FanConfs)
                {
                    if (profile < fan.FanCurveConfs.Count)
                        fan.CurveSel = profile;
                }
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
}
