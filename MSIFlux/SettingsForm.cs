using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using MSIFlux.GUI.Properties;
using MSIFlux.GUI.UI;
using MSIFlux.GUI.Helpers;
using MSIFlux.Common.Configs;
using MSIFlux.GUI.Display;
using static MSIFlux.GUI.Properties.Strings;

namespace MSIFlux.GUI
{
    public partial class SettingsForm : RForm
    {
        ContextMenuStrip contextMenuStrip = new CustomContextMenu();

        public Fans? fansForm;
        public Extra? extraForm;

        public bool SilentStart { get; set; } = false;

        private MSIFlux_Config? _config;
        
        private System.Windows.Forms.Timer tempRefreshTimer;

        // 服务离线提示条: 当 IPC 连不上后台服务时显示红色 banner, 点按钮可修复
        private Panel? _offlineBanner;
        private Label? _offlineBannerLabel;
        private Button? _offlineBannerFixButton;
        
        private int _cpuTemp = 0;
        private int _gpuTemp = 0;
        private int _cpuFanRpm = 0;
        private int _gpuFanRpm = 0;

        private static readonly System.Drawing.Color colorGpuEco = System.Drawing.Color.FromArgb(255, 6, 180, 138);      // green
        private static readonly System.Drawing.Color colorGpuStandard = System.Drawing.Color.FromArgb(255, 58, 174, 239);   // blue
        private static readonly System.Drawing.Color colorGpuUltimate = System.Drawing.Color.FromArgb(255, 255, 32, 32);    // red
        
        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayContextMenu;
        private ToolStripMenuItem ecoModeMenuItem;
        private ToolStripMenuItem silentModeMenuItem;
        private ToolStripMenuItem balancedModeMenuItem;
        private ToolStripMenuItem turboModeMenuItem;

        public SettingsForm()
        {
            InitializeComponent();
            InitTheme(true);

            tempRefreshTimer = new System.Windows.Forms.Timer();
            tempRefreshTimer.Interval = 1000;
            tempRefreshTimer.Tick += TempRefreshTimer_Tick;
            tempRefreshTimer.Start();

            InitOfflineBanner();

            StartPosition = FormStartPosition.Manual;
            this.Shown += (s, e) =>
            {
                Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                Rectangle workingArea = screen.WorkingArea;
                int targetX = workingArea.Right - this.Width;
                int targetY = workingArea.Bottom - this.Height;
                this.Location = new Point(targetX, targetY);
                
                if (SilentStart)
                {
                    this.Hide();
                }
            };

            buttonEcoMode.Text = Strings.EcoMode;
            buttonSilent.Text = Strings.Silent;
            buttonBalanced.Text = Strings.Balanced;
            buttonTurbo.Text = Strings.Turbo;
            buttonFans.Text = Strings.FanCurves;

            buttonScreenAuto.Text = Strings.AutoMode;

            buttonKeyboard.Text = Strings.ExtraSettings;

            buttonEco.Text = Strings.GPUModeEco;
            buttonStandard.Text = Strings.GPUModeStandard;
            buttonStandard.Visible = true;
            buttonUltimate.Text = Strings.GPUModeUltimate;
            buttonEco.Visible = true;
            buttonEco.Enabled = true;
            buttonOptimized.Visible = false;
            buttonStopGPU.Visible = false;
            buttonXGM.Visible = false;

            labelPerf.Text = Strings.PerformanceMode;
            labelGPU.Text = Strings.GPUMode;
            labelSreen.Text = Strings.ScreenRefreshRate;
            labelKeyboard.Text = Strings.Keyboard;
            labelBatteryTitle.Text = Strings.BatteryChargeLimit;

            checkStartup.Text = Strings.RunOnStartup;

            buttonQuit.Text = Strings.Quit;

            FormClosing += SettingsForm_FormClosing;

            buttonEcoMode.BorderColor = colorEco;
            buttonSilent.BorderColor = colorStandard;
            buttonBalanced.BorderColor = colorStandard;
            buttonTurbo.BorderColor = colorTurbo;
            buttonFans.BorderColor = colorCustom;

            button60Hz.BorderColor = colorGray;
            button120Hz.BorderColor = colorGray;
            buttonScreenAuto.BorderColor = colorGray;

            buttonEco.BorderColor = colorGpuEco;
            buttonStandard.BorderColor = colorGpuStandard;
            buttonUltimate.BorderColor = colorGpuUltimate;

            buttonEcoMode.Click += ButtonEcoMode_Click;
            buttonSilent.Click += ButtonSilent_Click;
            buttonBalanced.Click += ButtonBalanced_Click;
            buttonTurbo.Click += ButtonTurbo_Click;
            buttonFans.Click += ButtonFans_Click;

            buttonEco.Click += ButtonGpuEco_Click;
            buttonStandard.Click += ButtonGpuStandard_Click;
            buttonUltimate.Click += ButtonGpuUltimate_Click;

            button60Hz.Click += Button60Hz_Click;
            button120Hz.Click += Button120Hz_Click;
            buttonScreenAuto.Click += ButtonScreenAuto_Click;

            buttonQuit.Click += ButtonQuit_Click;

            buttonKeyboard.Click += ButtonKeyboard_Click;

            this.Load += SettingsForm_Load;

            sliderBattery.MouseUp += SliderBattery_MouseUp;
            sliderBattery.ValueChanged += SliderBattery_ValueChanged;

            checkStartup.CheckedChanged += CheckStartup_CheckedChanged;

            Text = "MSI Flux";

            // 单 exe 分发: 直接从 exe 自身的 ApplicationIcon 提取图标,
            // 不再依赖外部 MSIFlux.ico 文件.
            try
            {
                var ico = LoadEmbeddedAppIcon();
                if (ico != null) this.Icon = ico;
            }
            catch { }

            checkStartup.Checked = Startup.IsScheduled();

            InitTrayIcon();

            LoadConfig();
            InitScreen();
            
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

            if (Program.FanRunner != null)
            {
                Program.FanRunner.TempUpdated += FanRunner_TempUpdated;
            }
        }

        internal void FanRunner_TempUpdated(object? sender, TempEventArgs e)
        {
            if (labelCPUFan.InvokeRequired)
            {
                labelCPUFan.Invoke(new Action(() =>
                {
                    _cpuTemp = e.CpuTemp;
                    _gpuTemp = e.GpuTemp;
                    _cpuFanRpm = e.CpuFanRpm;
                    _gpuFanRpm = e.GpuFanRpm;
                    UpdateTempDisplay();
                }));
            }
            else
            {
                _cpuTemp = e.CpuTemp;
                _gpuTemp = e.GpuTemp;
                _cpuFanRpm = e.CpuFanRpm;
                _gpuFanRpm = e.GpuFanRpm;
                UpdateTempDisplay();
            }
        }
        
        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.StatusChange && _isAutoMode)
            {
                ApplyAutoScreenRefreshRate();
            }
        }
        
        /// <summary>
        /// 从当前 exe 内嵌的 ApplicationIcon 提取 Icon. 单 exe 分发时
        /// 无需额外 .ico 文件伴随.
        /// </summary>
        private static Icon? LoadEmbeddedAppIcon()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath))
                    return null;
                return Icon.ExtractAssociatedIcon(exePath);
            }
            catch
            {
                return null;
            }
        }

        private void InitTrayIcon()
        {
            notifyIcon = new NotifyIcon();
            
            try
            {
                notifyIcon.Icon = LoadEmbeddedAppIcon() ?? SystemIcons.Application;
            }
            catch
            {
                notifyIcon.Icon = SystemIcons.Application;
            }
            
            notifyIcon.Text = "MSI Flux";
            notifyIcon.Visible = true;
            
            trayContextMenu = new CustomContextMenu();
            
            ecoModeMenuItem = new ToolStripMenuItem(Strings.EcoMode);
            ecoModeMenuItem.Click += EcoModeMenuItem_Click;
            trayContextMenu.Items.Add(ecoModeMenuItem);

            silentModeMenuItem = new ToolStripMenuItem(Strings.Silent);
            silentModeMenuItem.Click += SilentModeMenuItem_Click;
            trayContextMenu.Items.Add(silentModeMenuItem);

            balancedModeMenuItem = new ToolStripMenuItem(Strings.Balanced);
            balancedModeMenuItem.Click += BalancedModeMenuItem_Click;
            trayContextMenu.Items.Add(balancedModeMenuItem);

            turboModeMenuItem = new ToolStripMenuItem(Strings.Turbo);
            turboModeMenuItem.Click += TurboModeMenuItem_Click;
            trayContextMenu.Items.Add(turboModeMenuItem);

            trayContextMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem(Strings.Quit);
            exitMenuItem.Click += ExitMenuItem_Click;
            trayContextMenu.Items.Add(exitMenuItem);
            
            notifyIcon.ContextMenuStrip = trayContextMenu;
            notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        }

        private string? _laptopScreen;
        private int _currentRefreshRate;
        private int _maxRefreshRate;
        private bool _isAutoMode = false;
        private int _manualRefreshRate = 60;
        
        private Dictionary<int, int> _perfModeCPUBoostMap = new Dictionary<int, int>
        {
            { 0, 0 },
            { 1, 2 },
            { 2, 2 },
            { 3, 2 }
        };

        private void InitScreen()
        {
            _laptopScreen = ScreenNative.FindLaptopScreen();
            _currentRefreshRate = ScreenNative.GetRefreshRate(_laptopScreen);
            _maxRefreshRate = ScreenNative.GetMaxRefreshRate(_laptopScreen);

            button60Hz.Text = "60Hz";
            if (_maxRefreshRate > 0)
            {
                button120Hz.Text = $"{_maxRefreshRate}Hz";
            }
            else
            {
                button120Hz.Text = "120Hz";
            }

            VisualiseScreen();
        }

        private void VisualiseScreen()
        {
            button60Hz.Activated = !_isAutoMode && _currentRefreshRate == 60;
            button120Hz.Activated = !_isAutoMode && _currentRefreshRate > 60 && _currentRefreshRate == _maxRefreshRate;
            buttonScreenAuto.Activated = _isAutoMode;
        }

        private void ButtonEcoMode_Click(object? sender, EventArgs e) => SetPerformanceMode(0);
        private void ButtonSilent_Click(object? sender, EventArgs e) => SetPerformanceMode(1);
        private void ButtonBalanced_Click(object? sender, EventArgs e) => SetPerformanceMode(2);
        private void ButtonTurbo_Click(object? sender, EventArgs e) => SetPerformanceMode(3);

        private void ButtonGpuEco_Click(object? sender, EventArgs e) => SetGpuMode(2);
        private void ButtonGpuStandard_Click(object? sender, EventArgs e) => SetGpuMode(0);
        private void ButtonGpuUltimate_Click(object? sender, EventArgs e) => SetGpuMode(1);

        private void SetGpuMode(int mode)
        {
            string modeName = mode switch
            {
                2 => Strings.GPUModeEco,
                1 => Strings.GPUModeUltimate,
                _ => Strings.GPUModeStandard
            };

            // Feature Manager Service.exe 是 WPF 应用, 需要在用户会话 (有桌面) 中启动.
            // 服务端 (SYSTEM, Session 0) 无法直接启动 WPF 进程 (会崩溃).
            // 所以由 GUI 侧负责启动 FM Service, 服务端只负责 MSI Foundation Service.
            EnsureFeatureManagerServiceRunning();

            bool ok = Program.FanRunner?.SetGpuMode(mode) ?? false;
            if (ok)
            {
                VisualiseGpuMode(mode);

                var rebootResult = MessageBox.Show(
                    $"{modeName} 切换命令执行成功，MSI Foundation Service / Feature Manager Service 已通过检测。\n是否立即重启生效？",
                    Strings.GPUMode,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);

                if (rebootResult == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 0")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                    catch { }
                }
            }
            else
            {
                MessageBox.Show(
                    "显卡模式切换失败：MSI Foundation Service 或 Feature Manager Service 可能未正常启动。",
                    Strings.GPUMode,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void VisualiseGpuMode(int mode)
        {
            buttonEco.Activated = mode == 2;
            buttonStandard.Activated = mode == 0;
            buttonUltimate.Activated = mode == 1;
        }

        /// <summary>
        /// 确保 Feature Manager Service.exe 在用户会话中运行.
        /// 它是 WPF 应用, 在 Session 0 (服务端) 会崩溃, 必须由 GUI 侧启动.
        /// 如果启动失败也不影响 GPU 切换 — 服务端会自己创建注册表键.
        /// </summary>
        private void EnsureFeatureManagerServiceRunning()
        {
            if (System.Diagnostics.Process.GetProcessesByName("Feature Manager Service").Length > 0)
                return;  // 已在运行

            // 查找 FeatureManager 目录
            string[] fmDirCandidates =
            {
                System.IO.Path.Combine(MSIFlux.Common.Paths.AppInstallDir, "FeatureManager"),
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "FeatureManager"),
                @"C:\Program Files (x86)\Feature Manager",
            };

            string? fmDir = fmDirCandidates.FirstOrDefault(d =>
                System.IO.File.Exists(System.IO.Path.Combine(d, "Feature Manager Service.exe")));

            if (fmDir == null) return;  // 没有就不启动, 不影响 GPU 切换

            try
            {
                string fmExe = System.IO.Path.Combine(fmDir, "Feature Manager Service.exe");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fmExe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = fmDir
                });
            }
            catch { }  // 启动失败不致命, 服务端会自己创建注册表键
        }

        private void SetPerformanceMode(int modeIndex)
        {
            if (_config != null && _config.PerfModeConf != null)
            {
                if (modeIndex >= 0 && modeIndex < _config.PerfModeConf.PerfModes.Count)
                {
                    _config.PerfModeConf.ModeSel = (byte)modeIndex;
                    VisualisePerfMode(modeIndex);
                    
                    ApplyPerfModeFanConfig(modeIndex);
                    ApplyCPUBoostForPerfMode(modeIndex);
                    
                    if (_isAutoMode)
                    {
                        ApplyAutoScreenRefreshRate();
                    }
                    
                    SaveConfig();
                    ApplyConfig();
                    
                    if (fansForm != null && fansForm.Visible)
                    {
                        fansForm.SetPerfMode(modeIndex);
                    }
                }
            }
        }

        private void ApplyConfig()
        {
            Program.FanRunner?.ApplyConfig();
        }

        private void ApplyCPUBoostForPerfMode(int modeIndex)
        {
            if (_perfModeCPUBoostMap.TryGetValue(modeIndex, out int boostValue))
            {
                PowerNative.SetCPUBoost(boostValue);
                
                if (fansForm != null && fansForm.Visible)
                {
                    fansForm.SetCPUBoost(boostValue);
                }
            }
        }

        public void UpdatePerfModeCPUBoost(int modeIndex, int boostValue)
        {
            _perfModeCPUBoostMap[modeIndex] = boostValue;
        }

        public int GetPerfModeCPUBoost(int modeIndex)
        {
            return _perfModeCPUBoostMap.TryGetValue(modeIndex, out int boostValue) ? boostValue : 0;
        }

        private void ApplyPerfModeFanConfig(int modeIndex)
        {
            if (_config == null || _config.FanConfs.Count < 2) return;

            if (modeIndex < _config.FanConfs[0].FanCurveConfs.Count)
            {
                _config.FanConfs[0].CurveSel = (byte)modeIndex;
            }
            if (modeIndex < _config.FanConfs[1].FanCurveConfs.Count)
            {
                _config.FanConfs[1].CurveSel = (byte)modeIndex;
            }
        }

        private void Button60Hz_Click(object? sender, EventArgs e)
        {
            _isAutoMode = false;
            _manualRefreshRate = 60;
            SetScreenRefreshRate(60);
        }

        private void Button120Hz_Click(object? sender, EventArgs e)
        {
            _isAutoMode = false;
            if (_maxRefreshRate > 0)
            {
                _manualRefreshRate = _maxRefreshRate;
                SetScreenRefreshRate(_maxRefreshRate);
            }
            else
            {
                _manualRefreshRate = 120;
                SetScreenRefreshRate(120);
            }
        }

        private void ButtonScreenAuto_Click(object? sender, EventArgs e)
        {
            _isAutoMode = true;
            ApplyAutoScreenRefreshRate();
        }

        private void ApplyAutoScreenRefreshRate()
        {
            if (!_isAutoMode) return;

            int currentPerfMode = _config?.PerfModeConf?.ModeSel ?? -1;
            bool isOnBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            
            if (currentPerfMode == 0 && isOnBattery)
            {
                SetScreenRefreshRate(60);
            }
            else
            {
                SetScreenRefreshRate(_maxRefreshRate > 0 ? _maxRefreshRate : 120);
            }
        }

        private void SetScreenRefreshRate(int frequency)
        {
            if (_laptopScreen != null)
            {
                var savedBounds = this.Bounds;

                try
                {
                    SuspendLayout();

                    ScreenNative.SetRefreshRate(_laptopScreen, frequency);
                    _currentRefreshRate = ScreenNative.GetRefreshRate(_laptopScreen);
                    VisualiseScreen();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Failed to set screen refresh rate: {ex.Message}");
                }

                System.Windows.Forms.Timer restoreTimer = new();
                restoreTimer.Interval = 50;
                restoreTimer.Tick += (s, e) =>
                {
                    restoreTimer.Stop();
                    restoreTimer.Dispose();
                    this.Bounds = savedBounds;
                    ResumeLayout(true);
                };
                restoreTimer.Start();
            }
        }

        private void ButtonQuit_Click(object? sender, EventArgs e)
        {
            Close();
            // Use Environment.Exit to guarantee process termination,
            // even if the WinRing0 driver is stuck in kernel mode.
            Environment.Exit(0);
        }

        private void ButtonFans_Click(object? sender, EventArgs e) => FansToggle();

        private void ButtonKeyboard_Click(object? sender, EventArgs e)
        {
            if (extraForm == null || extraForm.IsDisposed)
            {
                extraForm = new Extra();
                if (_config != null)
                {
                    extraForm.SetConfig(_config);
                }
                AddOwnedForm(extraForm);
            }

            if (extraForm.Visible)
            {
                extraForm.Close();
            }
            else
            {
                int extraFormLeft = this.Left - extraForm.Width;
                int extraFormTop = this.Top;
                
                Screen screen = Screen.FromControl(this);
                extraFormLeft = Math.Max(screen.Bounds.Left, extraFormLeft);
                extraFormTop = Math.Max(screen.Bounds.Top, extraFormTop);
                
                extraForm.Location = new Point(extraFormLeft, extraFormTop);
                extraForm.Show();
            }
        }

        private void SliderBattery_ValueChanged(object? sender, EventArgs e)
        {
            VisualiseBatteryTitle(sliderBattery.Value);
        }

        private void SliderBattery_MouseUp(object? sender, MouseEventArgs e)
        {
            if (_config != null && _config.ChargeLimitConf != null)
            {
                _config.ChargeLimitConf.CurVal = (byte)sliderBattery.Value;
                SaveConfig();
            }
        }

        private void CheckStartup_CheckedChanged(object? sender, EventArgs e)
        {
            if (checkStartup.Checked)
                Startup.Schedule();
            else
                Startup.UnSchedule();
        }

        private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                CloseAllChildForms();
                this.Hide();
            }
            else
            {
                CloseAllChildForms();
                
                tempRefreshTimer.Stop();
                tempRefreshTimer.Dispose();
                
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
                
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }
            }
        }
        
        private void CloseAllChildForms()
        {
            if (fansForm != null && !fansForm.IsDisposed)
            {
                if (fansForm.Visible)
                    fansForm.Close();
                else
                    fansForm.Dispose();
            }
            
            if (extraForm != null && !extraForm.IsDisposed)
            {
                if (extraForm.Visible)
                    extraForm.Close();
                else
                    extraForm.Dispose();
            }
        }
        
        private void EcoModeMenuItem_Click(object sender, EventArgs e) => ButtonEcoMode_Click(sender, e);
        private void SilentModeMenuItem_Click(object sender, EventArgs e) => ButtonSilent_Click(sender, e);
        private void BalancedModeMenuItem_Click(object sender, EventArgs e) => ButtonBalanced_Click(sender, e);
        private void TurboModeMenuItem_Click(object sender, EventArgs e) => ButtonTurbo_Click(sender, e);
        
        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            CloseAllChildForms();
            // Use Environment.Exit to guarantee process termination,
            // even if the WinRing0 driver is stuck in kernel mode.
            Environment.Exit(0);
        }
        
        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            // 双击托盘: 可见则隐藏, 否则稳健地唤起主窗口
            if (this.Visible && this.WindowState != FormWindowState.Minimized)
            {
                this.Hide();
            }
            else
            {
                ShowMainWindow();
            }
        }

        // Win32 API: 只用 ShowWindow + SetForegroundWindow, 不碰 TopMost.
        // 之前用 TopMost=true/false toggle 去置顶, 会破坏 Windows 对该窗口的
        // "任务栏按钮点击" 行为判定 —— 导致点任务栏唤起后再点一次无法最小化.
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        /// <summary>
        /// 稳健地把主窗口置顶/还原. 处理所有可能的 "看不见" 状态:
        /// - 窗口 Hidden (Visible=false)
        /// - 窗口最小化 (WindowState=Minimized, Visible=true 但看不见)
        /// - 窗口被其它窗口遮住
        /// 线程安全: 可以从任意线程调用.
        /// </summary>
        public void ShowMainWindow()
        {
            if (this.IsDisposed) return;

            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(ShowMainWindow)); } catch { }
                return;
            }

            try
            {
                // 1. 从 Hidden (托盘) 状态恢复
                if (!this.Visible)
                {
                    this.Show();
                }

                // 2. 从最小化恢复: 用 Win32 SW_RESTORE, 保留窗口原先 Normal/Maximized 状态
                IntPtr hWnd = this.Handle;
                if (IsIconic(hWnd) || this.WindowState == FormWindowState.Minimized)
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                else
                {
                    ShowWindow(hWnd, SW_SHOW);
                }

                // 3. 请求成为前台窗口. 注意: 如果调用进程不持有前台焦点, Windows
                //    有时会拒绝并只是在任务栏闪烁. 这是 Windows 的安全策略,
                //    在 "双击 exe 从托盘唤起" 场景下我们是从用户操作链过来的,
                //    通常会被允许.
                SetForegroundWindow(hWnd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MSI Flux] ShowMainWindow failed: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            try
            {
                MSIFlux.Common.Paths.EnsureCurrentConfigExists();
                if (System.IO.File.Exists(MSIFlux.Common.Paths.CurrentConf))
                {
                    Program.FanRunner?.LoadConfig();
                    _config = Program.FanRunner?.GetConfig();
                    Program.FanRunner?.ApplyConfig();
                    InitUIFromConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to load config: {ex.Message}");
                MessageBox.Show($"{Strings.ConfigLoadFailed}: {ex.Message}", "MSI Flux", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InitUIFromConfig()
        {
            if (_config == null) return;

            if (_config.ChargeLimitConf != null)
            {
                if (_config.ChargeLimitConf.CurVal == 0)
                {
                    _config.ChargeLimitConf.CurVal = 90;
                    SaveConfig();
                }
                sliderBattery.Value = _config.ChargeLimitConf.CurVal;
                VisualiseBatteryTitle(_config.ChargeLimitConf.CurVal);
            }

            if (_config.PerfModeConf != null)
            {
                VisualisePerfMode(_config.PerfModeConf.ModeSel);
            }

            // Initialize GPU mode visualization from EC
            int gpuMode = Program.FanRunner?.GetGpuMode() ?? -1;
            if (gpuMode >= 0)
            {
                VisualiseGpuMode(gpuMode);
            }
        }

        private void VisualisePerfMode(int modeIndex)
        {
            buttonEcoMode.Activated = modeIndex == 0;
            buttonSilent.Activated = modeIndex == 1;
            buttonBalanced.Activated = modeIndex == 2;
            buttonTurbo.Activated = modeIndex == 3;
            buttonFans.Activated = false;
            
            ecoModeMenuItem.Text = modeIndex == 0 ? $"✓ {Strings.EcoMode}" : $"  {Strings.EcoMode}";
            silentModeMenuItem.Text = modeIndex == 1 ? $"✓ {Strings.Silent}" : $"  {Strings.Silent}";
            balancedModeMenuItem.Text = modeIndex == 2 ? $"✓ {Strings.Balanced}" : $"  {Strings.Balanced}";
            turboModeMenuItem.Text = modeIndex == 3 ? $"✓ {Strings.Turbo}" : $"  {Strings.Turbo}";
        }

        private void TempRefreshTimer_Tick(object? sender, EventArgs e)
        {
            UpdateTempDisplay();
            UpdateOfflineBanner();
        }

        /// <summary>创建服务离线提示条 (程序化, 不动 Designer.cs).</summary>
        private void InitOfflineBanner()
        {
            _offlineBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = System.Drawing.Color.FromArgb(220, 53, 69),  // Bootstrap danger red
                Visible = false,
                Padding = new Padding(12, 0, 12, 0),
            };

            _offlineBannerLabel = new Label
            {
                Text = "⚠  后台服务未运行,风扇控制已停止",
                ForeColor = System.Drawing.Color.White,
                Font = new Font(this.Font.FontFamily, 10F, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
            };

            _offlineBannerFixButton = new Button
            {
                Text = "立即修复",
                Dock = DockStyle.Right,
                Width = 100,
                FlatStyle = FlatStyle.Flat,
                BackColor = System.Drawing.Color.White,
                ForeColor = System.Drawing.Color.FromArgb(220, 53, 69),
                Font = new Font(this.Font.FontFamily, 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
            };
            _offlineBannerFixButton.FlatAppearance.BorderSize = 0;
            _offlineBannerFixButton.Click += OfflineBannerFixButton_Click;

            _offlineBanner.Controls.Add(_offlineBannerLabel);
            _offlineBanner.Controls.Add(_offlineBannerFixButton);

            // Dock Top 到 Form, 会出现在所有其他 Dock.Top 控件的"最外层" (先添加的更靠内),
            // 这里希望 banner 在最顶, 所以用 BringToFront
            this.Controls.Add(_offlineBanner);
            _offlineBanner.BringToFront();
        }

        /// <summary>按服务连接状态更新 banner 可见性. 由 1s Timer 驱动, 不会卡 UI.</summary>
        private void UpdateOfflineBanner()
        {
            if (_offlineBanner == null) return;

            bool connected = Program.FanRunner?.IsServiceConnected ?? false;
            bool shouldShow = !connected;

            if (_offlineBanner.Visible != shouldShow)
            {
                _offlineBanner.Visible = shouldShow;
            }
        }

        private void OfflineBannerFixButton_Click(object? sender, EventArgs e)
        {
            if (_offlineBannerFixButton == null) return;

            _offlineBannerFixButton.Enabled = false;
            _offlineBannerFixButton.Text = "修复中...";

            try
            {
                // 策略 1: 服务已安装 → 尝试启动 (需要管理员; 这里先试用户权限,
                //         失败再 RelaunchElevated)
                if (Helpers.ServiceManager.IsInstalled())
                {
                    if (Helpers.ServiceManager.Start(TimeSpan.FromSeconds(10)))
                    {
                        // 启动成功; IPC AutoReconnect 会自动接上, banner 会在下个 Tick 消失
                        return;
                    }

                    // 用户权限启不动 (常见于 "启动本服务" ACL 没授权给 Users), 走提权
                    int code = Helpers.ServiceManager.RelaunchElevated("--install-service");
                    if (code != 0 && code != -1)
                    {
                        MessageBox.Show($"启动服务失败, 退出码 {code}",
                            "MSI Flux", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return;
                }

                // 策略 2: 服务未安装 → 提权安装
                int installCode = Helpers.ServiceManager.RelaunchElevated("--install-service");
                if (installCode == -1)
                {
                    // 用户拒绝 UAC
                    MessageBox.Show("安装后台服务需要管理员权限. 您已取消.",
                        "MSI Flux", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (installCode != 0)
                {
                    MessageBox.Show($"安装后台服务失败, 退出码 {installCode}",
                        "MSI Flux", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"修复过程中发生错误: {ex.Message}",
                    "MSI Flux", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _offlineBannerFixButton.Enabled = true;
                _offlineBannerFixButton.Text = "立即修复";
            }
        }
        
        private void UpdateTempDisplay()
        {
            string cpuText = "";
            string gpuText = "";
            
            if (_cpuTemp > 0)
            {
                cpuText = ": " + _cpuTemp + "°C";
                if (_cpuFanRpm > 0)
                    cpuText += " " + _cpuFanRpm + " RPM";
            }
            
            if (_gpuTemp > 0)
            {
                gpuText = ": " + _gpuTemp + "°C";
                if (_gpuFanRpm > 0)
                    gpuText += " " + _gpuFanRpm + " RPM";
            }
            
            labelCPUFan.Text = "CPU" + cpuText;
            labelGPUFan.Text = "GPU" + gpuText;
        }

        public void ReloadConfig()
        {
            Program.FanRunner?.LoadConfig();
            _config = Program.FanRunner?.GetConfig();
            Program.FanRunner?.ApplyConfig();
            InitUIFromConfig();
        }

        private void SaveConfig()
        {
            if (_config == null) return;
            try
            {
                Program.FanRunner?.SaveConfig();
                Program.FanRunner?.ApplyConfig();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to save config: {ex.Message}");
            }
        }

        private void VisualiseBatteryTitle(int value)
        {
            labelBatteryTitle.Text = $"{Strings.BatteryChargeLimit}: {value}%";
        }

        private void SettingsForm_Load(object? sender, EventArgs e)
        {
            SetWindowPositionToBottomRight();
        }

        private void SetWindowPositionToBottomRight()
        {
            Screen screen = Screen.PrimaryScreen;
            if (screen != null)
            {
                this.PerformLayout();
                this.Refresh();
                
                int screenWidth = screen.Bounds.Width;
                int screenHeight = screen.Bounds.Height;
                
                int newLeft = screenWidth - this.Width;
                int newTop = screenHeight - this.Height - 85;
                
                newLeft = Math.Max(0, newLeft);
                newTop = Math.Max(0, newTop);
                
                this.Location = new Point(newLeft, newTop);
                this.Refresh();
                this.BringToFront();
            }
        }

        public void FansToggle(int index = 0)
        {
            if (fansForm == null || fansForm.Text == "")
            {
                fansForm = new Fans();
                AddOwnedForm(fansForm);
            }

            if (fansForm.Visible)
            {
                fansForm.Close();
            }
            else
            {
                if (_config == null)
                {
                    LoadConfig();
                }
                
                if (_config != null)
                {
                    fansForm.SetConfig(_config, this);
                    if (_config.PerfModeConf != null)
                    {
                        fansForm.SetPerfMode(_config.PerfModeConf.ModeSel);
                    }
                }
                
                // 让 Fans 窗口与主窗口"底边对齐"而不是顶边对齐 ——
                // Fans 窗口通常比主窗口更高, 顶边对齐会让底边掉到任务栏下.
                int fanFormLeft = this.Left - fansForm.Width;
                int fanFormTop = this.Bottom - fansForm.Height;

                Screen screen = Screen.FromControl(this);
                Rectangle wa = screen.WorkingArea;  // 排除任务栏的可用区域

                fanFormLeft = Math.Max(wa.Left, fanFormLeft);
                // 先保底: 不能掉到任务栏下方
                if (fanFormTop + fansForm.Height > wa.Bottom)
                    fanFormTop = wa.Bottom - fansForm.Height;
                // 再保顶: 不能顶出屏幕
                fanFormTop = Math.Max(wa.Top, fanFormTop);

                fansForm.Location = new Point(fanFormLeft, fanFormTop);
                fansForm.Show();
            }
        }
    }
}