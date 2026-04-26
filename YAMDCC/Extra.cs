using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using YAMDCC.GUI.UI;
using YAMDCC.GUI.Helpers;
using YAMDCC.Common.Configs;

namespace YAMDCC.GUI
{
    public partial class Extra : RForm
    {
        private YAMDCC_Config? _config;

        /// <summary>Path to the language preference file.</summary>
        private static readonly string LanguageFilePath = System.IO.Path.Combine(YAMDCC.Common.Paths.Data, "Language.txt");

        /// <summary>Supported languages: display name → culture name.</summary>
        private static readonly (string DisplayName, string CultureName)[] SupportedLanguages = new[]
        {
            ("Auto", ""),
            ("English", "en"),
            ("中文(简体)", "zh-CN"),
            ("中文(繁體)", "zh-TW"),
            ("日本語", "ja"),
            ("한국어", "ko"),
            ("Deutsch", "de"),
            ("Français", "fr"),
            ("Español", "es"),
            ("Italiano", "it"),
            ("Português (BR)", "pt-BR"),
            ("Português (PT)", "pt-PT"),
            ("Polski", "pl"),
            ("Magyar", "hu"),
            ("Română", "ro"),
            ("Lietuvių", "lt"),
            ("Čeština", "cs"),
            ("Dansk", "da"),
            ("Svenska", "sv"),
            ("Nederlands", "nl"),
            ("Tiếng Việt", "vi"),
            ("Bahasa Indonesia", "id"),
            ("Українська", "uk"),
            ("العربية", "ar"),
            ("Türkçe", "tr"),
        };

        private Dictionary<string, string> PROCESSES = new Dictionary<string, string>
        {
            { "Feature Manager", "Feature_Manager.exe" },
            { "Feature Manager Service", "Feature_Manager.exe" }
        };

        private Dictionary<string, string> SERVICES = new Dictionary<string, string>
        {
            { "MSI Foundation Service", "MSI Foundation Service" },
            { "MSI SCM Service (32 位)", "Micro Star SCM" }
        };

        public Extra()
        {
            InitializeComponent();

            Text = Properties.Strings.ExtraSettings;

            checkWinFnSwap.CheckedChanged += CheckWinFnSwap_CheckedChanged;

            buttonStopMSIService.Click += ButtonStopMSIService_Click;
            buttonStartMSIService.Click += ButtonStartMSIService_Click;

            buttonStopFanControl.Click += ButtonStopFanControl_Click;
            buttonStartFanControl.Click += ButtonStartFanControl_Click;

            // Language combo
            InitLanguageCombo();
            comboLanguage.SelectedIndexChanged += ComboLanguage_SelectedIndexChanged;

            this.Load += Extra_Load;
        }

        public void SetConfig(YAMDCC_Config config)
        {
            _config = config;
            InitUIFromConfig();
            UpdateMSIServiceStatus();
            UpdateFanControlStatus();
        }

        private void Extra_Load(object? sender, EventArgs e)
        {
            LoadConfig();
            UpdateMSIServiceStatus();
            UpdateFanControlStatus();
        }

        private void UpdateMSIServiceStatus()
        {
            try
            {
                bool isRunning = false;

                foreach (var process in PROCESSES)
                {
                    if (IsProcessRunning(process.Value))
                    {
                        isRunning = true;
                        break;
                    }
                }

                if (!isRunning)
                {
                    foreach (var service in SERVICES)
                    {
                        if (YAMDCC.Common.Utils.ServiceExists(service.Value) && YAMDCC.Common.Utils.ServiceRunning(service.Value))
                        {
                            isRunning = true;
                            break;
                        }
                    }
                }

                if (isRunning)
                {
                    labelMSIServiceStatus.Text = Properties.Strings.MSIServiceRunning;
                    labelMSIServiceStatus.ForeColor = System.Drawing.Color.FromArgb(40, 160, 80);
                }
                else
                {
                    labelMSIServiceStatus.Text = Properties.Strings.MSIServiceNotRunning;
                    labelMSIServiceStatus.ForeColor = SystemColors.ControlDark;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"更新 MSI 服务状态失败: {ex.Message}");
                labelMSIServiceStatus.Text = Properties.Strings.MSIServiceUnknown;
            }
        }

        private bool IsProcessRunning(string exeName)
        {
            return YAMDCC.Common.Utils.IsProcessRunning(exeName);
        }

        private string GetProcessPid(string exeName)
        {
            return YAMDCC.Common.Utils.GetProcessPid(exeName);
        }

        private bool StartProcess(string displayName, string exeName)
        {
            try
            {
                string pid = GetProcessPid(exeName);
                if (!string.IsNullOrEmpty(pid))
                {
                    Logger.WriteLine($"{displayName} 已运行 (PID: {pid})");
                    return true;
                }

                Logger.WriteLine($"正在启动 {displayName}...");
                string exePath = System.IO.Path.Combine(@"C:\Program Files (x86)\Feature Manager", exeName);
                if (System.IO.File.Exists(exePath))
                {
                    System.Diagnostics.Process.Start(exePath);
                    System.Threading.Thread.Sleep(2000);
                    string newPid = GetProcessPid(exeName);
                    if (!string.IsNullOrEmpty(newPid))
                    {
                        Logger.WriteLine($"{displayName} 启动成功 (PID: {newPid})");
                        return true;
                    }
                }
                else
                {
                    Logger.WriteLine($"{displayName} 启动失败，未找到可执行文件: {exePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"启动 {displayName} 失败: {ex.Message}");
            }
            return false;
        }

        private bool StopProcess(string displayName, string exeName)
        {
            return YAMDCC.Common.Utils.StopProcessByName(displayName, exeName);
        }

        private void LoadConfig()
        {
            try
            {
                YAMDCC.Common.Paths.EnsureCurrentConfigExists();
                if (System.IO.File.Exists(YAMDCC.Common.Paths.CurrentConf))
                {
                    _config = Program.FanRunner?.GetConfig() ?? YAMDCC_Config.Load(YAMDCC.Common.Paths.CurrentConf);
                    InitUIFromConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to load config: {ex.Message}");
                YAMDCC.Common.Utils.ShowWarning($"附加设置配置加载失败: {ex.Message}", "MSI Flux");
            }
        }

        private void InitUIFromConfig()
        {
            if (_config == null) return;

            if (_config.KeySwapConf != null)
            {
                checkWinFnSwap.Checked = _config.KeySwapConf.Enabled;
                checkWinFnSwap.Enabled = true;
            }
            else
            {
                checkWinFnSwap.Enabled = false;
            }
        }

        private void CheckWinFnSwap_CheckedChanged(object? sender, EventArgs e)
        {
            if (_config?.KeySwapConf != null)
            {
                _config.KeySwapConf.Enabled = checkWinFnSwap.Checked;
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            if (_config == null) return;
            try
            {
                _config.Save(YAMDCC.Common.Paths.CurrentConf);
                Program.FanRunner?.LoadConfig();
                Program.FanRunner?.ApplyConfig();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to save config: {ex.Message}");
            }
        }

        private void StopAllMSIServices()
        {
            Logger.WriteLine("正在停止所有MSI服务和进程...");

            foreach (var process in PROCESSES.Reverse())
            {
                StopProcess(process.Key, process.Value);
                System.Threading.Thread.Sleep(500);
            }

            foreach (var service in SERVICES.Reverse())
            {
                StopService(service.Key, service.Value);
                System.Threading.Thread.Sleep(500);
            }

            Logger.WriteLine("MSI服务和进程已全部停止");
            UpdateMSIServiceStatus();
        }

        private string CheckServiceStatus(string displayName, string serviceName)
        {
            return YAMDCC.Common.Utils.CheckServiceStatus(displayName, serviceName);
        }

        private bool StartService(string displayName, string serviceName)
        {
            Logger.WriteLine($"检查服务: {displayName}, 服务名: {serviceName}");

            if (!YAMDCC.Common.Utils.ServiceExists(serviceName))
            {
                Logger.WriteLine($"服务不存在: {displayName} ({serviceName})");
                return false;
            }

            string status = CheckServiceStatus(displayName, serviceName);
            if (status == "运行中")
            {
                Logger.WriteLine($"{displayName} 已运行");
                return true;
            }

            Logger.WriteLine($"正在启动服务: {displayName}, 当前状态: {status}");

            try
            {
                bool success = YAMDCC.Common.Utils.StartService(serviceName);
                if (success)
                {
                    Logger.WriteLine($"{displayName} 启动成功");
                }
                else
                {
                    Logger.WriteLine($"{displayName} 启动失败");
                }
                return success;
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"启动 {displayName} 时发生异常: {ex.Message}");
                return false;
            }
        }

        private bool StopService(string displayName, string serviceName)
        {
            Logger.WriteLine($"检查服务: {displayName}, 服务名: {serviceName}");

            if (!YAMDCC.Common.Utils.ServiceExists(serviceName))
            {
                Logger.WriteLine($"服务不存在: {displayName} ({serviceName})");
                return false;
            }

            string status = CheckServiceStatus(displayName, serviceName);
            if (status == "已停止" || status == "查询失败")
            {
                Logger.WriteLine($"{displayName} 已停止或无法查询, 当前状态: {status}");
                return true;
            }

            Logger.WriteLine($"正在停止服务: {displayName}, 当前状态: {status}");

            try
            {
                bool success = YAMDCC.Common.Utils.StopService(serviceName);
                if (success)
                {
                    Logger.WriteLine($"{displayName} 停止成功");
                }
                else
                {
                    Logger.WriteLine($"{displayName} 停止失败");
                }
                return success;
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"停止 {displayName} 时发生异常: {ex.Message}");
                return false;
            }
        }

        private void CheckAllStatus()
        {
            Logger.WriteLine("\n当前 MSI 组件最终状态:");
            Logger.WriteLine("-" + new string('-', 79));

            foreach (var process in PROCESSES)
            {
                string pid = GetProcessPid(process.Value);
                string status = !string.IsNullOrEmpty(pid) ? $"运行中 (PID: {pid})" : "未运行";
                Logger.WriteLine($"{process.Key,-45} | {status}");
            }

            foreach (var service in SERVICES)
            {
                string status = CheckServiceStatus(service.Key, service.Value);
                Logger.WriteLine($"{service.Key,-45} | {status}");
            }

            Logger.WriteLine("-" + new string('-', 79) + "\n");
        }

        private bool IsAdmin()
        {
            return YAMDCC.Common.Utils.IsAdmin();
        }

        private void RestartAsAdmin()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.FileName = System.Windows.Forms.Application.ExecutablePath;
                startInfo.Verb = "runas";
                startInfo.UseShellExecute = true;
                System.Diagnostics.Process.Start(startInfo);
                System.Windows.Forms.Application.Exit();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Logger.WriteLine($"以管理员身份重启失败: {ex.Message}");
            }
        }

        private void ButtonStopMSIService_Click(object? sender, EventArgs e)
        {
            try
            {
                if (!IsAdmin())
                {
                    YAMDCC.Common.Utils.ShowInfo(Properties.Strings.NeedAdminForMSI, Properties.Strings.InsufficientPermissions);
                    RestartAsAdmin();
                    return;
                }

                int stoppedCount = 0;
                int alreadyStopped = 0;

                Logger.WriteLine("\n正在执行【终极强力清理】...");

                // Kill processes first
                foreach (var process in PROCESSES.Reverse())
                {
                    if (!IsProcessRunning(process.Value))
                    {
                        alreadyStopped++;
                        continue;
                    }
                    if (StopProcess(process.Key, process.Value))
                    {
                        stoppedCount++;
                    }
                    System.Threading.Thread.Sleep(500);
                }

                // Stop and disable services to prevent auto-restart
                foreach (var service in SERVICES.Reverse())
                {
                    if (!YAMDCC.Common.Utils.ServiceExists(service.Value))
                    {
                        alreadyStopped++;
                        continue;
                    }
                    string status = CheckServiceStatus(service.Key, service.Value);
                    if (status == "已停止")
                    {
                        // Even if stopped, disable it to prevent auto-start
                        YAMDCC.Common.Utils.StopAndDisableService(service.Value);
                        alreadyStopped++;
                        continue;
                    }
                    if (YAMDCC.Common.Utils.StopAndDisableService(service.Value))
                    {
                        Logger.WriteLine($"{service.Key} 已停止并禁用");
                        stoppedCount++;
                    }
                    else
                    {
                        Logger.WriteLine($"{service.Key} 停止失败");
                    }
                    System.Threading.Thread.Sleep(500);
                }

                Logger.WriteLine("\n清理完成，最终状态确认:");
                CheckAllStatus();

                if (stoppedCount > 0)
                {
                    YAMDCC.Common.Utils.ShowInfo($"成功停止 {stoppedCount} 个 MSI 组件", "MSI 服务管理");
                }
                else if (alreadyStopped > 0)
                {
                    YAMDCC.Common.Utils.ShowInfo("所有 MSI 组件已处于停止状态", "MSI 服务管理");
                }
                else
                {
                    YAMDCC.Common.Utils.ShowInfo("没有需要停止的 MSI 组件", "MSI 服务管理");
                }

                UpdateMSIServiceStatus();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"停止 MSI 服务失败: {ex.Message}");
                YAMDCC.Common.Utils.ShowError($"停止 MSI 服务失败: {ex.Message}");
            }
        }

        private void ButtonStartMSIService_Click(object? sender, EventArgs e)
        {
            try
            {
                if (!IsAdmin())
                {
                    YAMDCC.Common.Utils.ShowInfo(Properties.Strings.NeedAdminForMSI, Properties.Strings.InsufficientPermissions);
                    RestartAsAdmin();
                    return;
                }

                // Stop fan control first to avoid conflicts with MSI services
                if (ServiceManager.IsRunning())
                {
                    var result = MessageBox.Show(
                        "启动 MSI 服务需要先停止 MSI Flux 风扇控制服务，否则会冲突。是否继续？",
                        "MSI Flux",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (result != DialogResult.Yes) return;

                    StopFanControlInternal();
                    Logger.WriteLine("已停止 MSI Flux 服务以避免与 MSI 组件冲突");
                }

                int startedCount = 0;

                Logger.WriteLine("\n正在按依赖顺序启动所有 MSI 组件...");

                foreach (var service in SERVICES)
                {
                    if (!YAMDCC.Common.Utils.ServiceExists(service.Value))
                    {
                        Logger.WriteLine($"服务不存在: {service.Key} ({service.Value})");
                        continue;
                    }
                    if (YAMDCC.Common.Utils.EnableAndStartService(service.Value))
                    {
                        Logger.WriteLine($"{service.Key} 启动成功");
                        startedCount++;
                    }
                    else
                    {
                        Logger.WriteLine($"{service.Key} 启动失败");
                    }
                    System.Threading.Thread.Sleep(500);
                }

                foreach (var process in PROCESSES)
                {
                    if (IsProcessRunning(process.Value))
                    {
                        Logger.WriteLine($"{process.Key} 已运行");
                        continue;
                    }
                    if (StartProcess(process.Key, process.Value))
                    {
                        startedCount++;
                    }
                    System.Threading.Thread.Sleep(1000);
                }

                CheckAllStatus();

                if (startedCount > 0)
                {
                    YAMDCC.Common.Utils.ShowInfo($"成功启动 {startedCount} 个 MSI 组件", "MSI 服务管理");
                }
                else
                {
                    YAMDCC.Common.Utils.ShowInfo("没有需要启动的 MSI 组件", "MSI 服务管理");
                }

                UpdateMSIServiceStatus();
                UpdateFanControlStatus();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"启动 MSI 服务失败: {ex.Message}");
                YAMDCC.Common.Utils.ShowError($"启动 MSI 服务失败: {ex.Message}");
            }
        }

        #region Fan Control

        private void UpdateFanControlStatus()
        {
            try
            {
                // 新架构下: "风扇控制" 状态 = Windows 服务 MSIFluxService 的状态.
                // FanRunner 只是 GUI 侧的 IPC 代理, 是否 "在工作" 取决于服务是否在跑.
                if (ServiceManager.IsRunning())
                {
                    labelServiceStatus.Text = Properties.Strings.FanControlRunning;
                    labelServiceStatus.ForeColor = System.Drawing.Color.FromArgb(40, 160, 80);
                    buttonStopFanControl.Enabled = true;
                    buttonStartFanControl.Enabled = false;
                }
                else
                {
                    labelServiceStatus.Text = Properties.Strings.FanControlNotRunning;
                    labelServiceStatus.ForeColor = System.Drawing.Color.FromArgb(220, 60, 60);
                    buttonStopFanControl.Enabled = false;
                    buttonStartFanControl.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"更新风扇控制状态失败: {ex.Message}");
                labelServiceStatus.Text = Properties.Strings.FanControlStatusUnknown;
                labelServiceStatus.ForeColor = System.Drawing.Color.Gray;
            }
        }

        private void ButtonStopFanControl_Click(object? sender, EventArgs e)
        {
            try
            {
                StopFanControlInternal();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"停止风扇控制失败: {ex.Message}");
                YAMDCC.Common.Utils.ShowError($"停止风扇控制失败: {ex.Message}");
            }
            UpdateFanControlStatus();
        }

        private void StopFanControlInternal()
        {
            if (!ServiceManager.IsRunning())
            {
                return;
            }

            // 停止 Windows 服务需要管理员. 若当前是普通用户, 自我提权调用一次性 --uninstall-service
            // 会太粗暴 (会卸载服务); 这里改用 sc stop 等价的 ServiceController.Stop.
            if (!ServiceManager.IsCurrentProcessElevated())
            {
                YAMDCC.Common.Utils.ShowInfo(
                    "停止 MSI Flux 风扇控制服务需要管理员权限.\n请右键 MSI Flux 以管理员身份运行后重试.",
                    "权限不足");
                return;
            }

            if (ServiceManager.Stop(TimeSpan.FromSeconds(10)))
            {
                Logger.WriteLine("MSI Flux 服务已停止 (风扇控制结束)");
                YAMDCC.Common.Utils.ShowInfo(Properties.Strings.FanControlStopped, "MSI Flux");
            }
            else
            {
                YAMDCC.Common.Utils.ShowError("停止服务失败, 请查看日志.");
            }
        }

        private void ButtonStartFanControl_Click(object? sender, EventArgs e)
        {
            try
            {
                if (ServiceManager.IsRunning())
                {
                    // 已经在跑, 什么都不用做
                    return;
                }

                // 1. 若 MSI 官方服务在跑, 先停 (沿用既有冲突检测逻辑)
                if (IsAdmin())
                {
                    StopAllMSIServicesSilent();
                }

                // 2. 启动 Windows 服务
                bool ok;
                if (ServiceManager.IsInstalled())
                {
                    ok = ServiceManager.Start(TimeSpan.FromSeconds(10));
                    if (!ok && !ServiceManager.IsCurrentProcessElevated())
                    {
                        // 可能是权限不够, 尝试提权
                        ServiceManager.RelaunchElevated("--install-service");
                        ok = ServiceManager.WaitUntilRunning(TimeSpan.FromSeconds(10));
                    }
                }
                else
                {
                    // 未安装 → 提权安装
                    int code = ServiceManager.RelaunchElevated("--install-service");
                    ok = code == 0 && ServiceManager.WaitUntilRunning(TimeSpan.FromSeconds(10));
                }

                if (ok)
                {
                    Logger.WriteLine("MSI Flux 服务已启动, 风扇控制已恢复");
                    YAMDCC.Common.Utils.ShowInfo(Properties.Strings.FanControlStarted, "MSI Flux");

                    // 让 FanRunner 的 IPC 代理重新建立连接 + 拉一次温度
                    try
                    {
                        if (Program.FanRunner == null)
                        {
                            var logger = Program.FanLogger ?? new YAMDCC.Common.Logs.Logger
                            {
                                LogDir = YAMDCC.Common.Paths.Logs,
                                LogName = "GUI"
                            };
                            Program.FanRunner = new FanControlRunner(logger);
                            Program.FanLogger = logger;
                            Program.FanRunner.Start();
                        }
                        else
                        {
                            // 已有 Runner 在自动重连, 触发一次 LoadConfig/ApplyConfig
                            Program.FanRunner.LoadConfig();
                            Program.FanRunner.ApplyConfig();
                        }
                        RefreshSettingsForm();
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"恢复 IPC 代理失败: {ex.Message}");
                    }
                }
                else
                {
                    YAMDCC.Common.Utils.ShowError(Properties.Strings.FanControlStartFailed);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"启动风扇控制失败: {ex.Message}");
                YAMDCC.Common.Utils.ShowError($"启动风扇控制失败: {ex.Message}");
            }
            UpdateFanControlStatus();
            UpdateMSIServiceStatus();
        }

        /// <summary>
        /// Silently stops all MSI services and processes without showing dialogs.
        /// Used before starting fan control to avoid conflicts.
        /// </summary>
        private void StopAllMSIServicesSilent()
        {
            try
            {
                foreach (var process in PROCESSES.Reverse())
                {
                    if (IsProcessRunning(process.Value))
                    {
                        StopProcess(process.Key, process.Value);
                        System.Threading.Thread.Sleep(300);
                    }
                }

                foreach (var service in SERVICES.Reverse())
                {
                    if (YAMDCC.Common.Utils.ServiceExists(service.Value))
                    {
                        string status = CheckServiceStatus(service.Key, service.Value);
                        if (status != "已停止")
                        {
                            YAMDCC.Common.Utils.StopAndDisableService(service.Value);
                            System.Threading.Thread.Sleep(300);
                        }
                        else
                        {
                            // Ensure it's disabled even if already stopped
                            YAMDCC.Common.Utils.StopAndDisableService(service.Value);
                        }
                    }
                }

                Logger.WriteLine("已自动停止所有 MSI 服务以避免冲突");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"自动停止 MSI 服务失败: {ex.Message}");
            }
        }

        private void RefreshSettingsForm()
        {
            try
            {
                var mainForm = this.Owner as SettingsForm;
                if (mainForm != null)
                {
                    // Re-subscribe TempUpdated event
                    Program.FanRunner.TempUpdated -= mainForm.FanRunner_TempUpdated;
                    Program.FanRunner.TempUpdated += mainForm.FanRunner_TempUpdated;

                    // Reload config so SettingsForm gets the new runner's config
                    mainForm.ReloadConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"刷新设置窗体失败: {ex.Message}");
            }
        }

        #endregion

        #region Language

        private void InitLanguageCombo()
        {
            comboLanguage.Items.Clear();
            foreach (var lang in SupportedLanguages)
            {
                comboLanguage.Items.Add(lang.DisplayName);
            }

            // Load saved language preference
            string savedCulture = LoadLanguagePreference();
            int selectedIndex = 0; // Default: Auto
            for (int i = 0; i < SupportedLanguages.Length; i++)
            {
                if (SupportedLanguages[i].CultureName == savedCulture)
                {
                    selectedIndex = i;
                    break;
                }
            }
            comboLanguage.SelectedIndex = selectedIndex;
        }

        private void ComboLanguage_SelectedIndexChanged(object? sender, EventArgs e)
        {
            int index = comboLanguage.SelectedIndex;
            if (index < 0 || index >= SupportedLanguages.Length) return;

            string selectedCulture = SupportedLanguages[index].CultureName;
            string currentCulture = LoadLanguagePreference();

            if (selectedCulture == currentCulture) return;

            SaveLanguagePreference(selectedCulture);

            // Ask user to restart for language change to take effect
            var result = MessageBox.Show(
                Properties.Strings.LanguageChangeRestart,
                "YAMDCC",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                RestartApplication();
            }
        }

        /// <summary>Save language preference to file.</summary>
        internal static void SaveLanguagePreference(string cultureName)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(LanguageFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(LanguageFilePath, cultureName ?? "");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save language preference: {ex.Message}");
            }
        }

        /// <summary>Load language preference from file. Returns empty string for "Auto".</summary>
        internal static string LoadLanguagePreference()
        {
            try
            {
                if (File.Exists(LanguageFilePath))
                {
                    return File.ReadAllText(LanguageFilePath).Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load language preference: {ex.Message}");
            }
            return "";
        }

        /// <summary>Apply saved language preference to the current thread.</summary>
        internal static void ApplySavedLanguage()
        {
            string cultureName = LoadLanguagePreference();
            if (!string.IsNullOrEmpty(cultureName))
            {
                try
                {
                    var ci = new CultureInfo(cultureName);
                    CultureInfo.DefaultThreadCurrentUICulture = ci;
                    System.Threading.Thread.CurrentThread.CurrentUICulture = ci;
                }
                catch (CultureNotFoundException ex)
                {
                    Debug.WriteLine($"Culture not found: {cultureName}: {ex.Message}");
                    // Reset invalid language preference to Auto
                    SaveLanguagePreference("");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to apply language '{cultureName}': {ex.Message}");
                    // Reset invalid language preference to Auto
                    SaveLanguagePreference("");
                }
            }
        }

        private void RestartApplication()
        {
            try
            {
                // Start new process with --restart flag (waits for old process to exit)
                var startInfo = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Arguments = "--restart"
                };
                Process.Start(startInfo);

                // Force terminate the current process immediately
                // Environment.Exit bypasses FormClosing events that might hide the window
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to restart application: {ex.Message}");
            }
        }

        #endregion
    }
}