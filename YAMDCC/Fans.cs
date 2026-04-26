using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using YAMDCC.Common.Configs;
using YAMDCC.GUI.UI;

namespace YAMDCC.GUI
{
    public partial class Fans : RForm
    {
        private YAMDCC_Config? _config;
        private SettingsForm? _settingsForm;

        int curIndex = -1;
        DataPoint? curPoint = null;

        // 仅在 Chart_MouseDown 命中数据点期间为 true. 用于 WndProc 屏蔽 WM_NCHITTEST
        // 的边框识别, 防止用户把曲线点拖过 Chart 右边界时拉动窗口.
        private bool _isDraggingCurvePoint = false;

        Series seriesCPU;
        Series seriesGPU;

        const int tempMin = 20;
        const int tempMax = 100;
        const int fansMax = 150;

        private int _currentPerfMode = 0;

        private Color colorStandard = Color.FromArgb(52, 152, 219);
        private Color colorTurbo = Color.FromArgb(231, 76, 60);
        private Color chartGrid = Color.FromArgb(230, 230, 230);

        public Fans()
        {
            InitializeComponent();
            InitTheme(true);

            Text = "风扇调节";

            labelTip.Visible = false;
            labelTip.BackColor = Color.Transparent;

            seriesCPU = chartCPU.Series.Add("CPU");
            seriesGPU = chartGPU.Series.Add("GPU");

            seriesCPU.Color = colorStandard;
            seriesGPU.Color = colorTurbo;

            chartCPU.MouseDown += Chart_MouseDown;
            chartCPU.MouseMove += Chart_MouseMove;
            chartCPU.MouseUp += Chart_MouseUp;
            chartCPU.MouseLeave += Chart_MouseLeave;

            chartGPU.MouseDown += Chart_MouseDown;
            chartGPU.MouseMove += Chart_MouseMove;
            chartGPU.MouseUp += Chart_MouseUp;
            chartGPU.MouseLeave += Chart_MouseLeave;

            buttonReset.Click += ButtonReset_Click;
            chkFullBlast.CheckedChanged += ChkFullBlast_CheckedChanged;
            buttonImport.Click += ButtonImport_Click;

            comboPowerMode.SelectedIndexChanged += ComboPowerMode_SelectedIndexChanged;
            comboBoost.SelectedIndexChanged += ComboBoost_SelectedIndexChanged;

            buttonCPU.Click += ButtonCPU_Click;
            buttonGPU.Click += ButtonGPU_Click;

            InitChart();
        }

        private void InitChart()
        {
            InitSingleChart(chartCPU, "CPU Fan");
            InitSingleChart(chartGPU, "GPU Fan");

            seriesCPU.ChartType = SeriesChartType.Line;
            seriesCPU.BorderWidth = 2;
            seriesCPU.MarkerSize = 8;
            seriesCPU.MarkerStyle = MarkerStyle.Circle;
            seriesCPU.Color = colorStandard;

            seriesGPU.ChartType = SeriesChartType.Line;
            seriesGPU.BorderWidth = 2;
            seriesGPU.MarkerSize = 8;
            seriesGPU.MarkerStyle = MarkerStyle.Circle;
            seriesGPU.Color = colorTurbo;
        }

        private void InitSingleChart(Chart chart, string title)
        {
            chart.ChartAreas[0].AxisX.Minimum = tempMin;
            chart.ChartAreas[0].AxisX.Maximum = tempMax;
            chart.ChartAreas[0].AxisX.Interval = 10;
            chart.ChartAreas[0].AxisX.MajorGrid.LineColor = chartGrid;
            chart.ChartAreas[0].AxisX.LineColor = chartGrid;
            // X 轴标题: 贴在最右端, 表示单位 (°C)
            chart.ChartAreas[0].AxisX.Title = "(°C)";
            chart.ChartAreas[0].AxisX.TitleAlignment = StringAlignment.Far;
            chart.ChartAreas[0].AxisX.TitleFont = new Font("Arial", 8F, FontStyle.Bold);

            chart.ChartAreas[0].AxisY.Minimum = 0;
            chart.ChartAreas[0].AxisY.Maximum = fansMax;
            // Interval=25 而非 20: 0..150 以 20 为步长最后一格是 140, 150 不显示;
            // 改 25 后刻度为 0/25/50/75/100/125/150, 上限刻度可见.
            chart.ChartAreas[0].AxisY.Interval = 25;
            chart.ChartAreas[0].AxisY.MajorGrid.LineColor = chartGrid;
            chart.ChartAreas[0].AxisY.LineColor = chartGrid;
            chart.ChartAreas[0].AxisY.LabelStyle.Font = new Font("Arial", 8F);
            // Y 轴标题: 贴在最顶端, 表示单位 (%)
            chart.ChartAreas[0].AxisY.Title = "(%)";
            chart.ChartAreas[0].AxisY.TitleAlignment = StringAlignment.Far;
            chart.ChartAreas[0].AxisY.TitleFont = new Font("Arial", 8F, FontStyle.Bold);

            chart.Titles[0].Text = title;

            if (chart.Legends.Count > 0)
                chart.Legends[0].Enabled = false;
        }

        private void ButtonCPU_Click(object? sender, EventArgs e)
        {
            buttonCPU.Activated = true;
            buttonGPU.Activated = false;
        }

        private void ButtonGPU_Click(object? sender, EventArgs e)
        {
            buttonCPU.Activated = false;
            buttonGPU.Activated = true;
        }

        public void SetConfig(YAMDCC_Config config, SettingsForm? settingsForm = null)
        {
            _config = config;
            _settingsForm = settingsForm;
            LoadConfig();
        }

        public void SetPerfMode(int modeIndex)
        {
            _currentPerfMode = modeIndex;
            if (comboPowerMode.Items.Count > modeIndex)
            {
                comboPowerMode.SelectedIndex = modeIndex;
            }
            if (_settingsForm != null)
            {
                int boostValue = _settingsForm.GetPerfModeCPUBoost(_currentPerfMode);
                SetCPUBoost(boostValue);
            }
            UpdateCharts();
        }

        public void SetCPUBoost(int boostValue)
        {
            if (comboBoost.Items.Count > boostValue && boostValue >= 0)
            {
                comboBoost.SelectedIndexChanged -= ComboBoost_SelectedIndexChanged;
                comboBoost.SelectedIndex = boostValue;
                comboBoost.SelectedIndexChanged += ComboBoost_SelectedIndexChanged;
            }
        }

        private void LoadConfig()
        {
            if (_config == null) return;

            ApplyDefaultConfig();

            if (_config.PerfModeConf != null)
            {
                _currentPerfMode = _config.PerfModeConf.ModeSel;
            }

            comboPowerMode.Items.Clear();
            comboPowerMode.Items.Add("省电模式");
            comboPowerMode.Items.Add("静音模式");
            comboPowerMode.Items.Add("平衡模式");
            comboPowerMode.Items.Add("增强模式");
            comboPowerMode.SelectedIndex = _currentPerfMode;

            comboBoost.Items.Clear();
            comboBoost.Items.Add("Disabled");
            comboBoost.Items.Add("Enabled");
            comboBoost.Items.Add("Aggressive");

            int boostValue = 0;
            if (_settingsForm != null)
            {
                boostValue = _settingsForm.GetPerfModeCPUBoost(_currentPerfMode);
            }
            else
            {
                boostValue = PowerNative.GetCPUBoost();
            }
            comboBoost.SelectedIndex = Math.Min(boostValue, comboBoost.Items.Count - 1);

            UpdateCharts();
        }

        private void UpdateCharts()
        {
            UpdateChart(seriesCPU, chartCPU, 0);
            UpdateChart(seriesGPU, chartGPU, 1);
        }

        private void UpdateChart(Series series, Chart chart, int fanIndex)
        {
            if (_config == null || _config.FanConfs == null) return;
            if (fanIndex >= _config.FanConfs.Count) return;

            FanConf fanConf = _config.FanConfs[fanIndex];
            if (fanConf == null || fanConf.FanCurveConfs == null || fanConf.FanCurveConfs.Count == 0) return;

            int curveIndex = _currentPerfMode;
            if (curveIndex >= fanConf.FanCurveConfs.Count)
                curveIndex = 0;

            FanCurveConf curveConf = fanConf.FanCurveConfs[curveIndex];
            if (curveConf == null || curveConf.TempThresholds == null) return;

            series.Points.Clear();

            for (int i = 0; i < curveConf.TempThresholds.Count; i++)
            {
                TempThreshold threshold = curveConf.TempThresholds[i];
                series.Points.AddXY(threshold.UpThreshold, threshold.FanSpeed);
            }

            chart.ChartAreas[0].RecalculateAxesScale();
        }

        private void ComboPowerMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_config == null || _config.PerfModeConf == null || comboPowerMode.SelectedIndex < 0)
                return;

            _currentPerfMode = comboPowerMode.SelectedIndex;
            _config.PerfModeConf.ModeSel = (byte)_currentPerfMode;

            for (int i = 0; i < _config.FanConfs.Count && i < 2; i++)
            {
                if (_currentPerfMode < _config.FanConfs[i].FanCurveConfs.Count)
                {
                    _config.FanConfs[i].CurveSel = (byte)_currentPerfMode;
                }
            }

            if (_settingsForm != null)
            {
                int boostValue = _settingsForm.GetPerfModeCPUBoost(_currentPerfMode);
                SetCPUBoost(boostValue);
            }

            UpdateCharts();
            ApplyConfig();
        }

        private void ComboBoost_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (comboBoost.SelectedIndex >= 0)
            {
                PowerNative.SetCPUBoost(comboBoost.SelectedIndex);

                if (_settingsForm != null)
                {
                    _settingsForm.UpdatePerfModeCPUBoost(_currentPerfMode, comboBoost.SelectedIndex);
                }
            }
        }

        private void ApplyConfig()
        {
            SaveChartData();
            _config?.Save(YAMDCC.Common.Paths.CurrentConf);
            Program.FanRunner?.LoadConfig();
            Program.FanRunner?.ApplyConfig();
        }

        private void ChkFullBlast_CheckedChanged(object? sender, EventArgs e)
        {
            Program.FanRunner?.SetFullBlast(chkFullBlast.Checked ? 1 : 0);
        }

        private void ButtonReset_Click(object? sender, EventArgs e)
        {
            ResetToDefaultConfig();
            UpdateCharts();
            SaveConfig();
            Program.FanRunner?.LoadConfig();
            Program.FanRunner?.ApplyConfig();
            labelFansResult.Text = "已恢复默认配置";
            labelFansResult.ForeColor = Color.Green;
            labelFansResult.Visible = true;
        }

        private void ResetToDefaultConfig()
        {
            if (_config == null || _config.FanConfs == null) return;

            for (int fanIndex = 0; fanIndex < _config.FanConfs.Count && fanIndex < 2; fanIndex++)
            {
                FanConf fanConf = _config.FanConfs[fanIndex];
                if (fanConf == null) continue;

                bool isCPU = fanIndex == 0;
                int expectedThresholds = fanConf.FanCurveRegs?.Length ?? 7;

                fanConf.FanCurveConfs = new List<FanCurveConf>();

                for (int curveIndex = 0; curveIndex < 4; curveIndex++)
                {
                    var defaultCurve = CreateDefaultFanCurve(isCPU, curveIndex, expectedThresholds);
                    fanConf.FanCurveConfs.Add(defaultCurve);
                }
            }
        }

        private void SaveConfig()
        {
            if (_config == null) return;
            _config.Save(YAMDCC.Common.Paths.CurrentConf);
        }

        private void ButtonImport_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "XML 配置文件|*.xml|所有文件|*.*";
                openFileDialog.Title = "加载配置文件";
                openFileDialog.RestoreDirectory = true;
                openFileDialog.InitialDirectory = YAMDCC.Common.Paths.Data;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        if (File.Exists(openFileDialog.FileName))
                        {
                            _config = YAMDCC_Config.Load(openFileDialog.FileName);
                            LoadConfig();
                            ApplyConfig();
                            labelFansResult.Text = "配置加载成功";
                            labelFansResult.ForeColor = Color.Green;
                            labelFansResult.Visible = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        labelFansResult.Text = "配置加载失败: " + ex.Message;
                        labelFansResult.ForeColor = Color.Red;
                        labelFansResult.Visible = true;
                    }
                }
            }
        }

        private void SaveChartData()
        {
            if (_config == null) return;

            SaveSeriesData(seriesCPU, 0);
            SaveSeriesData(seriesGPU, 1);
        }

        private void SaveSeriesData(Series series, int fanIndex)
        {
            if (_config == null || _config.FanConfs == null) return;
            if (fanIndex >= _config.FanConfs.Count) return;

            FanConf fanConf = _config.FanConfs[fanIndex];
            if (fanConf == null || fanConf.FanCurveConfs == null || fanConf.FanCurveConfs.Count == 0) return;

            int curveIndex = _currentPerfMode;
            if (curveIndex >= fanConf.FanCurveConfs.Count)
                curveIndex = 0;

            FanCurveConf curveConf = fanConf.FanCurveConfs[curveIndex];
            if (curveConf == null || curveConf.TempThresholds == null) return;

            for (int i = 0; i < series.Points.Count && i < curveConf.TempThresholds.Count; i++)
            {
                DataPoint point = series.Points[i];
                curveConf.TempThresholds[i].UpThreshold = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(point.XValue)));
                curveConf.TempThresholds[i].FanSpeed = (byte)Math.Min(fanConf.MaxSpeed, Math.Max(fanConf.MinSpeed, (int)Math.Round(point.YValues[0])));
            }
        }

        /// <summary>
        /// 按下左键时启用鼠标捕获, 保证拖拽 90/100°C 等靠右的点时鼠标就算移出 Chart,
        /// 事件仍然送到 Chart 而不会被 Form 边框识别为 "拖边框调整窗口大小".
        /// </summary>
        private void Chart_MouseDown(object? sender, MouseEventArgs e)
        {
            if (sender is not Chart chart) return;
            if (!e.Button.HasFlag(MouseButtons.Left)) return;

            HitTestResult hit = chart.HitTest(e.X, e.Y);
            if (hit.Series is not null && hit.PointIndex >= 0)
            {
                curIndex = hit.PointIndex;
                curPoint = hit.Series.Points[curIndex];
                // 双保险 1: 捕获鼠标
                chart.Capture = true;
                // 双保险 2: 标记拖拽状态, 让 Form.WndProc 在 WM_NCHITTEST 阶段
                //           把所有"在边框上"的 hit 都改写成"在客户区内", 彻底堵死
                //           Windows 触发窗口 resize 的路径.
                _isDraggingCurvePoint = true;
            }
        }

        private void Chart_MouseMove(object? sender, MouseEventArgs e)
        {
            if (sender is null) return;
            Chart chart = (Chart)sender;

            ChartArea ca = chart.ChartAreas[0];
            Axis ax = ca.AxisX;
            Axis ay = ca.AxisY;

            bool tip = false;

            HitTestResult hit = chart.HitTest(e.X, e.Y);
            Series series = chart.Series[0];

            if (hit.Series is not null && hit.PointIndex >= 0)
            {
                curIndex = hit.PointIndex;
                curPoint = hit.Series.Points[curIndex];
                tip = true;
            }

            if (curPoint != null)
            {
                try
                {
                    double dx, dy;

                    dx = ax.PixelPositionToValue(e.X);
                    dy = ay.PixelPositionToValue(e.Y);

                    if (dx < tempMin) dx = tempMin;
                    if (dx > tempMax) dx = tempMax;

                    if (dy < 0) dy = 0;
                    if (dy > fansMax) dy = fansMax;

                    if (e.Button.HasFlag(MouseButtons.Left))
                    {
                        curPoint.XValue = dx;
                        curPoint.YValues[0] = dy;
                        tip = true;
                    }

                    labelTip.Text = Math.Floor(curPoint.XValue) + "°C, " + Math.Floor(curPoint.YValues[0]) + "%";
                    Point chartLocation = chart.PointToScreen(new Point(0, 0));
                    Point panelLocation = labelTip.Parent.PointToClient(chartLocation);
                    int tipX = e.X + panelLocation.X + 15;
                    int tipY = e.Y + panelLocation.Y + 15;
                    tipX = Math.Min(labelTip.Parent.Width - labelTip.Width - 10, tipX);
                    tipY = Math.Min(labelTip.Parent.Height - labelTip.Height - 10, tipY);
                    labelTip.Left = Math.Max(10, tipX);
                    labelTip.Top = Math.Max(10, tipY);

                }
                catch
                {
                    tip = false;
                }
            }

            labelTip.Visible = tip;
        }

        private void Chart_MouseUp(object? sender, MouseEventArgs e)
        {
            // 对称释放 MouseDown 里启用的两个保险
            if (sender is Chart chart && chart.Capture)
            {
                chart.Capture = false;
            }
            _isDraggingCurvePoint = false;

            if (curPoint != null)
            {
                SaveChartData();
                ApplyConfig();
            }
            curPoint = null;
            curIndex = -1;
            labelTip.Visible = false;
        }

        // ====================================================================
        // WndProc 拦截 WM_NCHITTEST: 拖动曲线点期间, 把所有在窗口非客户区 (边框、
        // 标题栏) 的 hit 都改写成 HTCLIENT, 使 Windows 不会启动 "拖动边框调整
        // 大小" 的系统流程. 释放拖拽后恢复默认行为.
        // ====================================================================
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 0x01;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && _isDraggingCurvePoint)
            {
                // 让整个窗口在拖拽期间看起来就是一整块客户区
                m.Result = (IntPtr)HTCLIENT;
                return;
            }
            base.WndProc(ref m);
        }

        private void Chart_MouseLeave(object? sender, EventArgs e)
        {
            curPoint = null;
            curIndex = -1;
            labelTip.Visible = false;
        }

        private void ApplyDefaultConfig()
        {
            if (_config == null || _config.FanConfs == null) return;

            for (int fanIndex = 0; fanIndex < _config.FanConfs.Count && fanIndex < 2; fanIndex++)
            {
                FanConf fanConf = _config.FanConfs[fanIndex];
                if (fanConf == null || fanConf.FanCurveConfs == null) continue;

                bool isCPU = fanIndex == 0;
                int expectedThresholds = fanConf.FanCurveRegs?.Length ?? 7;

                while (fanConf.FanCurveConfs.Count < 4)
                {
                    int profileIndex = fanConf.FanCurveConfs.Count;
                    var newCurve = CreateDefaultFanCurve(isCPU, profileIndex, expectedThresholds);
                    fanConf.FanCurveConfs.Add(newCurve);
                }

                for (int curveIndex = 0; curveIndex < fanConf.FanCurveConfs.Count; curveIndex++)
                {
                    FanCurveConf curveConf = fanConf.FanCurveConfs[curveIndex];
                    if (curveConf.TempThresholds == null || curveConf.TempThresholds.Count == 0)
                    {
                        var defaultCurve = CreateDefaultFanCurve(isCPU, curveIndex, expectedThresholds);
                        curveConf.Name = defaultCurve.Name;
                        curveConf.Desc = defaultCurve.Desc;
                        curveConf.TempThresholds = defaultCurve.TempThresholds;
                    }
                }
            }
        }

        private FanCurveConf CreateDefaultFanCurve(bool isCPU, int profileIndex, int expectedThresholds)
        {
            string[] profileNames = { "省电模式", "静音模式", "平衡模式", "增强模式" };
            string[] profileDescs = {
                "低功耗模式，风扇安静，适合办公和轻度使用",
                "静音优先，风扇转速较低，适合日常使用",
                "性能与噪音平衡，适合游戏和重度使用",
                "最大性能，风扇全速运转，适合高负载场景"
            };

            var curve = new FanCurveConf
            {
                Name = profileNames[Math.Min(profileIndex, 3)],
                Desc = profileDescs[Math.Min(profileIndex, 3)],
                TempThresholds = new List<TempThreshold>()
            };

            int[,] cpuProfiles = {
                { 0, 0, 0, 45, 0, 25, 55, 0, 40, 65, 0, 55, 75, 0, 70, 85, 0, 85, 95, 0, 100 },
                { 0, 0, 0, 50, 0, 20, 60, 0, 35, 70, 0, 50, 80, 0, 70, 90, 0, 85, 95, 0, 100 },
                { 0, 0, 0, 45, 0, 25, 55, 0, 45, 65, 0, 60, 75, 0, 80, 85, 0, 100, 95, 0, 100 },
                { 0, 0, 0, 40, 0, 30, 50, 0, 50, 60, 0, 70, 70, 0, 90, 80, 0, 110, 90, 0, 130 }
            };

            int[,] gpuProfiles = {
                { 0, 0, 0, 50, 0, 20, 60, 0, 35, 70, 0, 50, 80, 0, 70, 90, 0, 85, 95, 0, 100 },
                { 0, 0, 0, 55, 0, 25, 65, 0, 40, 75, 0, 55, 85, 0, 75, 92, 0, 90, 97, 0, 100 },
                { 0, 0, 0, 45, 0, 25, 55, 0, 45, 65, 0, 65, 75, 0, 85, 85, 0, 110, 95, 0, 130 },
                { 0, 0, 0, 40, 0, 35, 50, 0, 55, 60, 0, 75, 70, 0, 100, 80, 0, 125, 90, 0, 150 }
            };

            int[,] profile = isCPU ? cpuProfiles : gpuProfiles;
            int row = Math.Min(profileIndex, 3);

            for (int i = 0; i < expectedThresholds && i < 7; i++)
            {
                int temp = profile[row, i * 3];
                int downT = profile[row, i * 3 + 1];
                int speed = profile[row, i * 3 + 2];

                curve.TempThresholds.Add(new TempThreshold
                {
                    UpThreshold = (byte)temp,
                    DownThreshold = (byte)downT,
                    FanSpeed = (byte)speed
                });
            }

            return curve;
        }
    }
}