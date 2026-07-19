using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MSIFlux.GUI.UI;

namespace MSIFlux.GUI
{
    partial class Extra
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.panelHeader = new Panel();
            this.labelTitle = new Label();

            this.cardGeneral = new ModernCard();
            this.labelGeneralTitle = new Label();
            this.checkWinFnSwap = new CheckBox();
            this.checkAutoEcoOnBattery = new CheckBox();
            this.check05msTimer = new CheckBox();
            this.checkUsbPowerShare = new CheckBox();
            this.panelLangBox = new Panel();
            this.labelLanguage = new Label();
            this.comboLanguage = new ComboBox();

            this.cardConfig = new ModernCard();
            this.labelConfigTitle = new Label();
            this.tableButtonsConfig = new TableLayoutPanel();
            this.buttonExportConfig = new ModernButton(Color.FromArgb(59, 130, 246));
            this.buttonImportConfig = new ModernButton(Color.FromArgb(139, 92, 246));
            this.buttonPowerPlan = new ModernButton(Color.FromArgb(234, 179, 8));

            this.cardMSI = new ModernCard();
            this.labelMSITitle = new Label();
            this.labelMSIServiceStatus = new Label();
            this.tableButtonsMSI = new TableLayoutPanel();
            this.buttonStartMSIService = new ModernButton(Color.FromArgb(34, 197, 94));
            this.buttonStopMSIService = new ModernButton(Color.FromArgb(239, 68, 68));

            this.cardService = new ModernCard();
            this.labelServiceTitle = new Label();
            this.labelServiceStatus = new Label();
            this.tableButtonsFan = new TableLayoutPanel();
            this.buttonStartFanControl = new ModernButton(Color.FromArgb(34, 197, 94));
            this.buttonStopFanControl = new ModernButton(Color.FromArgb(239, 68, 68));

            this.SuspendLayout();

            // ===== 窗口属性 (现代双列宽大气布局: 730 x 425) =====
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.ClientSize = new Size(730, 425);
            this.Font = new Font("Segoe UI", 9F);
            this.Margin = new Padding(3, 4, 3, 4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Extra";
            this.ShowIcon = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Text = "更多设置";

            // ===== 头部 =====
            this.panelHeader = new Panel();
            this.panelHeader.Location = new Point(16, 10);
            this.panelHeader.Size = new Size(698, 36);
            this.Controls.Add(this.panelHeader);

            this.labelTitle = new Label();
            this.labelTitle.AutoSize = false;
            this.labelTitle.Dock = DockStyle.Fill;
            this.labelTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            this.labelTitle.ForeColor = Color.FromArgb(15, 23, 42);
            this.labelTitle.Text = "更多设置与高级参数";
            this.labelTitle.TextAlign = ContentAlignment.MiddleLeft;
            this.panelHeader.Controls.Add(this.labelTitle);

            // ============================================================
            // 左列 1: 通用与扩展设置卡片 (Width: 342, Height: 195)
            // ============================================================
            this.cardGeneral = new ModernCard();
            this.cardGeneral.Location = new Point(16, 50);
            this.cardGeneral.Size = new Size(342, 195);
            this.Controls.Add(this.cardGeneral);

            this.labelGeneralTitle = new Label();
            this.labelGeneralTitle.AutoSize = false;
            this.labelGeneralTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            this.labelGeneralTitle.ForeColor = Color.FromArgb(30, 41, 59);
            this.labelGeneralTitle.Location = new Point(14, 10);
            this.labelGeneralTitle.Size = new Size(314, 20);
            this.labelGeneralTitle.Text = Properties.Strings.GeneralSettings;
            this.cardGeneral.Controls.Add(this.labelGeneralTitle);

            // Win/Fn 键交换
            this.checkWinFnSwap = new CheckBox();
            this.checkWinFnSwap.AutoSize = true;
            this.checkWinFnSwap.Font = new Font("Microsoft YaHei UI", 9F);
            this.checkWinFnSwap.ForeColor = Color.FromArgb(55, 65, 81);
            this.checkWinFnSwap.Location = new Point(14, 34);
            this.checkWinFnSwap.Size = new Size(314, 22);
            this.checkWinFnSwap.TabIndex = 1;
            this.checkWinFnSwap.Text = Properties.Strings.WinFnSwap;
            this.checkWinFnSwap.UseVisualStyleBackColor = true;
            this.cardGeneral.Controls.Add(this.checkWinFnSwap);

            // 拔电自动省电
            this.checkAutoEcoOnBattery = new CheckBox();
            this.checkAutoEcoOnBattery.AutoSize = true;
            this.checkAutoEcoOnBattery.Font = new Font("Microsoft YaHei UI", 9F);
            this.checkAutoEcoOnBattery.ForeColor = Color.FromArgb(55, 65, 81);
            this.checkAutoEcoOnBattery.Location = new Point(14, 58);
            this.checkAutoEcoOnBattery.Size = new Size(314, 22);
            this.checkAutoEcoOnBattery.TabIndex = 2;
            this.checkAutoEcoOnBattery.Text = "拔电自动切换省电模式";
            this.checkAutoEcoOnBattery.UseVisualStyleBackColor = true;
            this.cardGeneral.Controls.Add(this.checkAutoEcoOnBattery);

            // 高精度定时器 (0.5ms)
            this.check05msTimer = new CheckBox();
            this.check05msTimer.AutoSize = true;
            this.check05msTimer.Font = new Font("Microsoft YaHei UI", 9F);
            this.check05msTimer.ForeColor = Color.FromArgb(55, 65, 81);
            this.check05msTimer.Location = new Point(14, 82);
            this.check05msTimer.Size = new Size(314, 22);
            this.check05msTimer.TabIndex = 3;
            this.check05msTimer.Text = "高精度定时器 (0.5ms 降低游戏延迟)";
            this.check05msTimer.UseVisualStyleBackColor = true;
            this.cardGeneral.Controls.Add(this.check05msTimer);

            // USB 关机充电
            this.checkUsbPowerShare = new CheckBox();
            this.checkUsbPowerShare.AutoSize = true;
            this.checkUsbPowerShare.Font = new Font("Microsoft YaHei UI", 9F);
            this.checkUsbPowerShare.ForeColor = Color.FromArgb(55, 65, 81);
            this.checkUsbPowerShare.Location = new Point(14, 106);
            this.checkUsbPowerShare.Size = new Size(314, 22);
            this.checkUsbPowerShare.TabIndex = 4;
            this.checkUsbPowerShare.Text = "USB 关机/睡眠对外充电";
            this.checkUsbPowerShare.UseVisualStyleBackColor = true;
            this.cardGeneral.Controls.Add(this.checkUsbPowerShare);

            // 语言选择行
            this.panelLangBox = new Panel();
            this.panelLangBox.Location = new Point(14, 142);
            this.panelLangBox.Size = new Size(314, 38);
            this.panelLangBox.BackColor = Color.FromArgb(238, 242, 246);
            this.panelLangBox.Padding = new Padding(8, 4, 8, 4);
            this.cardGeneral.Controls.Add(this.panelLangBox);

            this.labelLanguage = new Label();
            this.labelLanguage.AutoSize = false;
            this.labelLanguage.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.labelLanguage.ForeColor = Color.FromArgb(71, 85, 105);
            this.labelLanguage.Location = new Point(8, 7);
            this.labelLanguage.Size = new Size(60, 24);
            this.labelLanguage.Text = Properties.Strings.Language;
            this.labelLanguage.TextAlign = ContentAlignment.MiddleLeft;
            this.panelLangBox.Controls.Add(this.labelLanguage);

            this.comboLanguage = new ComboBox();
            this.comboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboLanguage.Font = new Font("Microsoft YaHei UI", 9F);
            this.comboLanguage.Location = new Point(72, 6);
            this.comboLanguage.Size = new Size(232, 25);
            this.comboLanguage.FlatStyle = FlatStyle.Flat;
            this.panelLangBox.Controls.Add(this.comboLanguage);

            // ============================================================
            // 左列 2: 配置文件管理卡片 (Width: 342, Height: 148)
            // ============================================================
            this.cardConfig = new ModernCard();
            this.cardConfig.Location = new Point(16, 257);
            this.cardConfig.Size = new Size(342, 148);
            this.Controls.Add(this.cardConfig);

            this.labelConfigTitle = new Label();
            this.labelConfigTitle.AutoSize = false;
            this.labelConfigTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            this.labelConfigTitle.ForeColor = Color.FromArgb(30, 41, 59);
            this.labelConfigTitle.Location = new Point(14, 10);
            this.labelConfigTitle.Size = new Size(314, 20);
            this.labelConfigTitle.Text = "配置文件与电源方案";
            this.cardConfig.Controls.Add(this.labelConfigTitle);

            this.tableButtonsConfig = new TableLayoutPanel();
            this.tableButtonsConfig.Location = new Point(14, 40);
            this.tableButtonsConfig.Size = new Size(314, 90);
            this.tableButtonsConfig.ColumnCount = 2;
            this.tableButtonsConfig.RowCount = 2;
            this.tableButtonsConfig.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsConfig.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsConfig.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            this.tableButtonsConfig.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            this.tableButtonsConfig.Margin = new Padding(0);
            this.cardConfig.Controls.Add(this.tableButtonsConfig);

            this.buttonExportConfig = new ModernButton(Color.FromArgb(59, 130, 246));
            this.buttonExportConfig.Dock = DockStyle.Fill;
            this.buttonExportConfig.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.buttonExportConfig.Name = "buttonExportConfig";
            this.buttonExportConfig.TabIndex = 7;
            this.buttonExportConfig.Text = Properties.Strings.Export;
            this.buttonExportConfig.UseVisualStyleBackColor = false;
            this.buttonExportConfig.Cursor = Cursors.Hand;
            this.tableButtonsConfig.Controls.Add(this.buttonExportConfig, 0, 0);

            this.buttonImportConfig = new ModernButton(Color.FromArgb(139, 92, 246));
            this.buttonImportConfig.Dock = DockStyle.Fill;
            this.buttonImportConfig.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.buttonImportConfig.Name = "buttonImportConfig";
            this.buttonImportConfig.TabIndex = 8;
            this.buttonImportConfig.Text = Properties.Strings.Import;
            this.buttonImportConfig.UseVisualStyleBackColor = false;
            this.buttonImportConfig.Cursor = Cursors.Hand;
            this.tableButtonsConfig.Controls.Add(this.buttonImportConfig, 1, 0);

            this.buttonPowerPlan = new ModernButton(Color.FromArgb(234, 179, 8));
            this.buttonPowerPlan.Dock = DockStyle.Fill;
            this.buttonPowerPlan.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.buttonPowerPlan.Name = "buttonPowerPlan";
            this.buttonPowerPlan.TabIndex = 9;
            this.buttonPowerPlan.Text = "电源计划关联";
            this.buttonPowerPlan.UseVisualStyleBackColor = false;
            this.buttonPowerPlan.Cursor = Cursors.Hand;
            this.tableButtonsConfig.SetColumnSpan(this.buttonPowerPlan, 2);
            this.tableButtonsConfig.Controls.Add(this.buttonPowerPlan, 0, 1);

            // ============================================================
            // 右列 1: MSI Service 管理卡片 (Width: 342, Height: 165)
            // ============================================================
            this.cardMSI = new ModernCard();
            this.cardMSI.Location = new Point(372, 50);
            this.cardMSI.Size = new Size(342, 165);
            this.Controls.Add(this.cardMSI);

            this.labelMSITitle = new Label();
            this.labelMSITitle.AutoSize = false;
            this.labelMSITitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            this.labelMSITitle.ForeColor = Color.FromArgb(30, 41, 59);
            this.labelMSITitle.Location = new Point(14, 10);
            this.labelMSITitle.Size = new Size(314, 20);
            this.labelMSITitle.Text = Properties.Strings.MSIServiceManagement;
            this.cardMSI.Controls.Add(this.labelMSITitle);

            this.labelMSIServiceStatus = new Label();
            this.labelMSIServiceStatus.AutoSize = false;
            this.labelMSIServiceStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            this.labelMSIServiceStatus.ForeColor = Color.FromArgb(100, 110, 130);
            this.labelMSIServiceStatus.Location = new Point(14, 34);
            this.labelMSIServiceStatus.Size = new Size(314, 65);
            this.labelMSIServiceStatus.Text = Properties.Strings.MSIServiceNotRunning;
            this.cardMSI.Controls.Add(this.labelMSIServiceStatus);

            this.tableButtonsMSI = new TableLayoutPanel();
            this.tableButtonsMSI.Location = new Point(14, 108);
            this.tableButtonsMSI.Size = new Size(314, 42);
            this.tableButtonsMSI.ColumnCount = 2;
            this.tableButtonsMSI.RowCount = 1;
            this.tableButtonsMSI.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsMSI.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsMSI.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            this.tableButtonsMSI.Margin = new Padding(0);
            this.cardMSI.Controls.Add(this.tableButtonsMSI);

            this.buttonStartMSIService = new ModernButton(Color.FromArgb(34, 197, 94));
            this.buttonStartMSIService.Dock = DockStyle.Fill;
            this.buttonStartMSIService.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.buttonStartMSIService.Name = "buttonStartMSIService";
            this.buttonStartMSIService.TabIndex = 3;
            this.buttonStartMSIService.Text = Properties.Strings.StartAllMSIServices;
            this.buttonStartMSIService.UseVisualStyleBackColor = false;
            this.buttonStartMSIService.Cursor = Cursors.Hand;
            this.tableButtonsMSI.Controls.Add(this.buttonStartMSIService, 0, 0);

            this.buttonStopMSIService = new ModernButton(Color.FromArgb(239, 68, 68));
            this.buttonStopMSIService.Dock = DockStyle.Fill;
            this.buttonStopMSIService.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.buttonStopMSIService.Name = "buttonStopMSIService";
            this.buttonStopMSIService.TabIndex = 4;
            this.buttonStopMSIService.Text = Properties.Strings.StopAllMSIServices;
            this.buttonStopMSIService.UseVisualStyleBackColor = false;
            this.buttonStopMSIService.Cursor = Cursors.Hand;
            this.tableButtonsMSI.Controls.Add(this.buttonStopMSIService, 1, 0);

            // ============================================================
            // 右列 2: Fan Control 服务管理卡片 (Width: 342, Height: 175)
            // ============================================================
            this.cardService = new ModernCard();
            this.cardService.Location = new Point(372, 230);
            this.cardService.Size = new Size(342, 175);
            this.Controls.Add(this.cardService);

            this.labelServiceTitle = new Label();
            this.labelServiceTitle.AutoSize = false;
            this.labelServiceTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            this.labelServiceTitle.ForeColor = Color.FromArgb(30, 41, 59);
            this.labelServiceTitle.Location = new Point(14, 10);
            this.labelServiceTitle.Size = new Size(314, 20);
            this.labelServiceTitle.Text = Properties.Strings.FanControlManagement;
            this.cardService.Controls.Add(this.labelServiceTitle);

            this.labelServiceStatus = new Label();
            this.labelServiceStatus.AutoSize = false;
            this.labelServiceStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            this.labelServiceStatus.ForeColor = Color.FromArgb(220, 60, 60);
            this.labelServiceStatus.Location = new Point(14, 34);
            this.labelServiceStatus.Size = new Size(314, 75);
            this.labelServiceStatus.Text = Properties.Strings.FanControlNotRunning;
            this.cardService.Controls.Add(this.labelServiceStatus);

            this.tableButtonsFan = new TableLayoutPanel();
            this.tableButtonsFan.Location = new Point(14, 118);
            this.tableButtonsFan.Size = new Size(314, 42);
            this.tableButtonsFan.ColumnCount = 2;
            this.tableButtonsFan.RowCount = 1;
            this.tableButtonsFan.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsFan.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsFan.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            this.tableButtonsFan.Margin = new Padding(0);
            this.cardService.Controls.Add(this.tableButtonsFan);

            this.buttonStartFanControl = new ModernButton(Color.FromArgb(34, 197, 94));
            this.buttonStartFanControl.Dock = DockStyle.Fill;
            this.buttonStartFanControl.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.buttonStartFanControl.Name = "buttonStartFanControl";
            this.buttonStartFanControl.TabIndex = 5;
            this.buttonStartFanControl.Text = Properties.Strings.StartFanControl;
            this.buttonStartFanControl.UseVisualStyleBackColor = false;
            this.buttonStartFanControl.Cursor = Cursors.Hand;
            this.tableButtonsFan.Controls.Add(this.buttonStartFanControl, 0, 0);

            this.buttonStopFanControl = new ModernButton(Color.FromArgb(239, 68, 68));
            this.buttonStopFanControl.Dock = DockStyle.Fill;
            this.buttonStopFanControl.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.buttonStopFanControl.Name = "buttonStopFanControl";
            this.buttonStopFanControl.TabIndex = 6;
            this.buttonStopFanControl.Text = Properties.Strings.StopFanControl;
            this.buttonStopFanControl.UseVisualStyleBackColor = false;
            this.buttonStopFanControl.Cursor = Cursors.Hand;
            this.tableButtonsFan.Controls.Add(this.buttonStopFanControl, 1, 0);

            this.ResumeLayout(false);
        }

        #endregion

        // ===== 控件字段声明 =====
        private Panel panelHeader;
        private Label labelTitle;

        private ModernCard cardGeneral;
        private ModernCard cardConfig;
        private ModernCard cardMSI;
        private ModernCard cardService;

        private Label labelGeneralTitle;
        private Label labelConfigTitle;
        private Label labelMSITitle;
        private Label labelServiceTitle;

        private Label labelMSIServiceStatus;
        private Label labelServiceStatus;

        private CheckBox checkWinFnSwap;
        private CheckBox checkAutoEcoOnBattery;
        private CheckBox check05msTimer;
        private CheckBox checkUsbPowerShare;

        private Panel panelLangBox;
        private Label labelLanguage;
        private ComboBox comboLanguage;

        private TableLayoutPanel tableButtonsConfig;
        private ModernButton buttonExportConfig;
        private ModernButton buttonImportConfig;
        private ModernButton buttonPowerPlan;

        private TableLayoutPanel tableButtonsMSI;
        private ModernButton buttonStartMSIService;
        private ModernButton buttonStopMSIService;

        private TableLayoutPanel tableButtonsFan;
        private ModernButton buttonStartFanControl;
        private ModernButton buttonStopFanControl;

        // ============================================================
        // 现代卡片与按钮控件
        // ============================================================
        public class ModernCard : Panel
        {
            public ModernCard()
            {
                BackColor = Color.White;
                Padding = new Padding(12);

                SetStyle(ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint |
                         ControlStyles.SupportsTransparentBackColor, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle rect = ClientRectangle;

                using (GraphicsPath path = CreateRoundedRectangle(rect, 8))
                using (SolidBrush bgBrush = new SolidBrush(BackColor))
                {
                    g.FillPath(bgBrush, path);
                }

                using (GraphicsPath path = CreateRoundedRectangle(new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), 8))
                using (Pen borderPen = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    g.DrawPath(borderPen, path);
                }
            }
        }

        public class ModernButton : Button
        {
            private Color _baseColor;
            private bool _isHovered = false;

            public ModernButton(Color baseColor)
            {
                _baseColor = baseColor;

                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                FlatAppearance.MouseOverBackColor = Color.Transparent;
                FlatAppearance.MouseDownBackColor = Color.Transparent;
                Cursor = Cursors.Hand;
                ForeColor = Color.White;
                TextAlign = ContentAlignment.MiddleCenter;

                SetStyle(ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint |
                         ControlStyles.SupportsTransparentBackColor, true);

                MouseEnter += (s, ea) => { _isHovered = true; Invalidate(); };
                MouseLeave += (s, ea) => { _isHovered = false; Invalidate(); };
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                Graphics g = pevent.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle rect = ClientRectangle;

                Color bgColor = _isHovered ? LightenColor(_baseColor, 18) : _baseColor;

                using (SolidBrush bgBrush = new SolidBrush(bgColor))
                using (GraphicsPath path = CreateRoundedRectangle(rect, 6))
                {
                    g.FillPath(bgBrush, path);
                }

                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.None
                };

                using (SolidBrush textBrush = new SolidBrush(ForeColor))
                {
                    g.DrawString(Text, Font, textBrush, rect, sf);
                }
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle r, int d)
        {
            GraphicsPath path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int diameter = Math.Min(d * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, diameter, diameter, 180, 90);
            path.AddArc(r.Right - diameter, r.Y, diameter, diameter, 270, 90);
            path.AddArc(r.Right - diameter, r.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(r.X, r.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color LightenColor(Color c, int amount)
        {
            return Color.FromArgb(
                Math.Min(255, c.R + amount),
                Math.Min(255, c.G + amount),
                Math.Min(255, c.B + amount));
        }
    }
}
