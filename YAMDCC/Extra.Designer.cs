﻿using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using YAMDCC.GUI.UI;
using YAMDCC.GUI.Helpers;

namespace YAMDCC.GUI
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
            this.components = new System.ComponentModel.Container();
            this.SuspendLayout();

            // 
            // Extra - 主窗口
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.FromArgb(240, 240, 240);
            this.ClientSize = new Size(380, 420);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Extra";
            this.StartPosition = FormStartPosition.Manual;
            this.Text = Properties.Strings.ExtraSettings;
            this.ShowIcon = false;
            this.Padding = new Padding(16);

            // ===== 顶部标题栏区域 =====
            this.panelHeader = new Panel();
            this.panelHeader.Location = new Point(16, 12);
            this.panelHeader.Size = new Size(348, 36);
            this.panelHeader.BackColor = Color.Transparent;
            this.Controls.Add(this.panelHeader);

            this.labelTitle = new Label();
            this.labelTitle.AutoSize = false;
            this.labelTitle.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            this.labelTitle.ForeColor = Color.FromArgb(30, 41, 59);
            this.labelTitle.Location = new Point(0, 6);
            this.labelTitle.Size = new Size(348, 26);
            this.labelTitle.Text = Properties.Strings.ExtraSettings;
            this.labelTitle.TextAlign = ContentAlignment.MiddleLeft;
            this.panelHeader.Controls.Add(this.labelTitle);

            // ===== 通用设置卡片 =====
            this.cardGeneral = new ModernCard();
            this.cardGeneral.Location = new Point(16, 52);
            this.cardGeneral.Size = new Size(348, 88);
            this.Controls.Add(this.cardGeneral);

            this.labelGeneralTitle = new Label();
            this.labelGeneralTitle.AutoSize = false;
            this.labelGeneralTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            this.labelGeneralTitle.ForeColor = Color.FromArgb(30, 41, 59);
            this.labelGeneralTitle.Location = new Point(14, 10);
            this.labelGeneralTitle.Size = new Size(320, 20);
            this.labelGeneralTitle.Text = Properties.Strings.GeneralSettings;
            this.cardGeneral.Controls.Add(this.labelGeneralTitle);

            // Win/Fn 键交换
            this.checkWinFnSwap = new CheckBox();
            this.checkWinFnSwap.AutoSize = true;
            this.checkWinFnSwap.Font = new Font("Segoe UI", 9F);
            this.checkWinFnSwap.ForeColor = Color.FromArgb(55, 65, 81);
            this.checkWinFnSwap.Location = new Point(14, 30);
            this.checkWinFnSwap.Size = new Size(320, 22);
            this.checkWinFnSwap.TabIndex = 1;
            this.checkWinFnSwap.Text = Properties.Strings.WinFnSwap;
            this.checkWinFnSwap.UseVisualStyleBackColor = true;
            this.cardGeneral.Controls.Add(this.checkWinFnSwap);

            // 语言选择行（独立框）
            this.panelLangBox = new Panel();
            this.panelLangBox.Location = new Point(14, 52);
            this.panelLangBox.Size = new Size(320, 32);
            this.panelLangBox.BackColor = Color.FromArgb(227, 227, 227);
            this.panelLangBox.Padding = new Padding(6, 4, 6, 0);
            this.cardGeneral.Controls.Add(this.panelLangBox);

            this.labelLanguage = new Label();
            this.labelLanguage.AutoSize = false;
            this.labelLanguage.Font = new Font("Segoe UI", 9F);
            this.labelLanguage.ForeColor = Color.FromArgb(100, 110, 130);
            this.labelLanguage.Location = new Point(6, 4);
            this.labelLanguage.Size = new Size(56, 22);
            this.labelLanguage.Text = Properties.Strings.Language;
            this.labelLanguage.TextAlign = ContentAlignment.MiddleLeft;
            this.panelLangBox.Controls.Add(this.labelLanguage);

            this.comboLanguage = new ComboBox();
            this.comboLanguage.Font = new Font("Segoe UI", 9F);
            this.comboLanguage.Location = new Point(68, 3);
            this.comboLanguage.Size = new Size(244, 26);
            this.comboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboLanguage.TabIndex = 2;
            this.comboLanguage.FlatStyle = FlatStyle.Flat;
            this.panelLangBox.Controls.Add(this.comboLanguage);

            // ===== MSI Service 管理卡片 =====
            this.cardMSI = new ModernCard();
            this.cardMSI.Location = new Point(16, 148);
            this.cardMSI.Size = new Size(348, 110);
            this.Controls.Add(this.cardMSI);

            this.labelMSITitle = new Label();
            this.labelMSITitle.AutoSize = false;
            this.labelMSITitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            this.labelMSITitle.ForeColor = Color.FromArgb(30, 41, 59);
            this.labelMSITitle.Location = new Point(14, 10);
            this.labelMSITitle.Size = new Size(320, 20);
            this.labelMSITitle.Text = Properties.Strings.MSIServiceManagement;
            this.cardMSI.Controls.Add(this.labelMSITitle);

            this.labelMSIServiceStatus = new Label();
            this.labelMSIServiceStatus.AutoSize = false;
            this.labelMSIServiceStatus.Font = new Font("Segoe UI", 8.5F);
            this.labelMSIServiceStatus.ForeColor = Color.FromArgb(100, 110, 130);
            this.labelMSIServiceStatus.Location = new Point(14, 34);
            this.labelMSIServiceStatus.Size = new Size(320, 18);
            this.labelMSIServiceStatus.Text = Properties.Strings.MSIServiceNotRunning;
            this.cardMSI.Controls.Add(this.labelMSIServiceStatus);

            // 按钮容器
            this.tableButtonsMSI = new TableLayoutPanel();
            this.tableButtonsMSI.Location = new Point(14, 58);
            this.tableButtonsMSI.Size = new Size(320, 38);
            this.tableButtonsMSI.ColumnCount = 2;
            this.tableButtonsMSI.RowCount = 1;
            this.tableButtonsMSI.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsMSI.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsMSI.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            this.tableButtonsMSI.Margin = new Padding(0);
            this.tableButtonsMSI.Padding = new Padding(4, 0, 4, 0);
            this.cardMSI.Controls.Add(this.tableButtonsMSI);

            this.buttonStartMSIService = new ModernButton(Color.FromArgb(34, 197, 94));
            this.buttonStartMSIService.Dock = DockStyle.Fill;
            this.buttonStartMSIService.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.buttonStartMSIService.Name = "buttonStartMSIService";
            this.buttonStartMSIService.TabIndex = 3;
            this.buttonStartMSIService.Text = Properties.Strings.StartAllMSIServices;
            this.buttonStartMSIService.UseVisualStyleBackColor = false;
            this.buttonStartMSIService.Cursor = Cursors.Hand;
            this.tableButtonsMSI.Controls.Add(this.buttonStartMSIService, 0, 0);

            this.buttonStopMSIService = new ModernButton(Color.FromArgb(239, 68, 68));
            this.buttonStopMSIService.Dock = DockStyle.Fill;
            this.buttonStopMSIService.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.buttonStopMSIService.Name = "buttonStopMSIService";
            this.buttonStopMSIService.TabIndex = 4;
            this.buttonStopMSIService.Text = Properties.Strings.StopAllMSIServices;
            this.buttonStopMSIService.UseVisualStyleBackColor = false;
            this.buttonStopMSIService.Cursor = Cursors.Hand;
            this.tableButtonsMSI.Controls.Add(this.buttonStopMSIService, 1, 0);

            // ===== Fan Control 管理卡片 =====
            this.cardService = new ModernCard();
            this.cardService.Location = new Point(16, 268);
            this.cardService.Size = new Size(348, 110);
            this.Controls.Add(this.cardService);

            this.labelServiceTitle = new Label();
            this.labelServiceTitle.AutoSize = false;
            this.labelServiceTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            this.labelServiceTitle.ForeColor = Color.FromArgb(30, 41, 59);
            this.labelServiceTitle.Location = new Point(14, 10);
            this.labelServiceTitle.Size = new Size(320, 20);
            this.labelServiceTitle.Text = Properties.Strings.FanControlManagement;
            this.cardService.Controls.Add(this.labelServiceTitle);

            this.labelServiceStatus = new Label();
            this.labelServiceStatus.AutoSize = false;
            this.labelServiceStatus.Font = new Font("Segoe UI", 8.5F);
            this.labelServiceStatus.ForeColor = Color.FromArgb(220, 60, 60);
            this.labelServiceStatus.Location = new Point(14, 34);
            this.labelServiceStatus.Size = new Size(320, 18);
            this.labelServiceStatus.Text = Properties.Strings.FanControlNotRunning;
            this.cardService.Controls.Add(this.labelServiceStatus);

            // 按钮容器
            this.tableButtonsFan = new TableLayoutPanel();
            this.tableButtonsFan.Location = new Point(14, 58);
            this.tableButtonsFan.Size = new Size(320, 38);
            this.tableButtonsFan.ColumnCount = 2;
            this.tableButtonsFan.RowCount = 1;
            this.tableButtonsFan.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsFan.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableButtonsFan.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            this.tableButtonsFan.Margin = new Padding(0);
            this.tableButtonsFan.Padding = new Padding(4, 0, 4, 0);
            this.cardService.Controls.Add(this.tableButtonsFan);

            this.buttonStartFanControl = new ModernButton(Color.FromArgb(34, 197, 94));
            this.buttonStartFanControl.Dock = DockStyle.Fill;
            this.buttonStartFanControl.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.buttonStartFanControl.Name = "buttonStartFanControl";
            this.buttonStartFanControl.TabIndex = 5;
            this.buttonStartFanControl.Text = Properties.Strings.StartFanControl;
            this.buttonStartFanControl.UseVisualStyleBackColor = false;
            this.buttonStartFanControl.Cursor = Cursors.Hand;
            this.tableButtonsFan.Controls.Add(this.buttonStartFanControl, 0, 0);

            this.buttonStopFanControl = new ModernButton(Color.FromArgb(239, 68, 68));
            this.buttonStopFanControl.Dock = DockStyle.Fill;
            this.buttonStopFanControl.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
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

        // 头部
        private Panel panelHeader;
        private Label labelTitle;

        // 卡片（无彩色边条）
        private ModernCard cardGeneral;
        private ModernCard cardMSI;
        private ModernCard cardService;

        // 标题标签
        private Label labelGeneralTitle;
        private Label labelMSITitle;
        private Label labelServiceTitle;

        // 状态标签
        private Label labelMSIServiceStatus;
        private Label labelServiceStatus;

        // 复选框
        private CheckBox checkWinFnSwap;

        // 语言选择
        private Panel panelLangBox;
        private Label labelLanguage;
        private ComboBox comboLanguage;

        // 按钮（纯色，无渐变）
        private ModernButton buttonStartMSIService;
        private ModernButton buttonStopMSIService;
        private ModernButton buttonStartFanControl;
        private ModernButton buttonStopFanControl;

        // 按钮容器
        private TableLayoutPanel tableButtonsMSI;
        private TableLayoutPanel tableButtonsFan;


        // ============================================================
        // 卡片控件 - 白色背景 + 圆角边框（无左侧彩色边条）
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
                {
                    using (SolidBrush bgBrush = new SolidBrush(BackColor))
                    {
                        g.FillPath(bgBrush, path);
                    }

                    using (Pen borderPen = new Pen(Color.FromArgb(228, 230, 235)))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }
            }
        }


        // ============================================================
        // 纯色按钮 - 无渐变，hover 变亮
        // ============================================================
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

                // hover 时变亮，否则用原始颜色（无渐变）
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


        // ============================================================
        // 辅助方法
        // ============================================================
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
