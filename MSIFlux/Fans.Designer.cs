using System.Windows.Forms.DataVisualization.Charting;

namespace MSIFlux.GUI
{
    partial class Fans
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

        private void InitializeComponent()
        {
            ChartArea chartArea1 = new ChartArea();
            Title title1 = new Title();
            ChartArea chartArea2 = new ChartArea();
            Title title2 = new Title();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Fans));

            this.panelSliders = new Panel();
            this.panelPowerModeTitle = new Panel();
            this.picturePowerMode = new PictureBox();
            this.labelPowerModeTitle = new Label();
            this.panelPowerMode = new Panel();
            this.comboPowerMode = new UI.RComboBox();
            this.panelBoostTitle = new Panel();
            this.pictureBoost = new PictureBox();
            this.labelBoostTitle = new Label();
            this.panelBoost = new Panel();
            this.comboBoost = new UI.RComboBox();
            this.panelImport = new Panel();
            this.buttonImport = new UI.RButton();
            this.panelNav = new Panel();
            this.tableNav = new TableLayoutPanel();
            this.buttonGPU = new UI.RButton();
            this.buttonCPU = new UI.RButton();

            this.panelFans = new Panel();
            this.labelTip = new Label();
            this.tableFanCharts = new TableLayoutPanel();
            this.chartGPU = new Chart();
            this.chartCPU = new Chart();
            this.panelTitleFans = new Panel();
            this.picturePerf = new PictureBox();
            this.labelFans = new Label();
            this.panelApplyFans = new Panel();
            this.labelFansResult = new Label();
            this.buttonReset = new UI.RButton();
            this.chkFullBlast = new UI.RCheckBox();

            ((System.ComponentModel.ISupportInitialize)this.chartCPU).BeginInit();
            ((System.ComponentModel.ISupportInitialize)this.chartGPU).BeginInit();
            this.panelSliders.SuspendLayout();
            this.panelPowerModeTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.picturePowerMode).BeginInit();
            this.panelPowerMode.SuspendLayout();
            this.panelBoostTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.pictureBoost).BeginInit();
            this.panelBoost.SuspendLayout();
            this.panelImport.SuspendLayout();
            this.panelNav.SuspendLayout();
            this.tableNav.SuspendLayout();
            this.panelFans.SuspendLayout();
            this.tableFanCharts.SuspendLayout();
            this.panelTitleFans.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)this.picturePerf).BeginInit();
            this.panelApplyFans.SuspendLayout();
            this.SuspendLayout();

            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            // 关键修复: 之前 AutoSize=true 会导致 Chart 里数据点坐标变化时
            //          Form 自动计算并改变自身大小, 表现为"拖 90/100°C 的点时
            //          窗口右边框被带着往外扩". 必须关掉.
            this.AutoSize = false;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.ClientSize = new Size(600, 480);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            // 顺便改成不可调整大小: 风扇曲线编辑对话框不需要 resize
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new Size(450, 380);
            this.Name = "Fans";
            this.Text = "风扇调节";

            this.panelSliders.Controls.Add(this.panelImport);
            this.panelSliders.Controls.Add(this.panelBoost);
            this.panelSliders.Controls.Add(this.panelBoostTitle);
            this.panelSliders.Controls.Add(this.panelPowerMode);
            this.panelSliders.Controls.Add(this.panelPowerModeTitle);
            this.panelSliders.Dock = DockStyle.Left;
            this.panelSliders.Location = new Point(0, 0);
            this.panelSliders.Name = "panelSliders";
            this.panelSliders.Padding = new Padding(8, 0, 8, 0);
            this.panelSliders.Size = new Size(220, 480);
            this.panelSliders.TabIndex = 0;

            this.panelPowerModeTitle.AutoSize = true;
            this.panelPowerModeTitle.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.panelPowerModeTitle.Controls.Add(this.picturePowerMode);
            this.panelPowerModeTitle.Controls.Add(this.labelPowerModeTitle);
            this.panelPowerModeTitle.Dock = DockStyle.Top;
            this.panelPowerModeTitle.Location = new Point(8, 0);
            this.panelPowerModeTitle.Name = "panelPowerModeTitle";
            this.panelPowerModeTitle.Size = new Size(204, 30);
            this.panelPowerModeTitle.TabIndex = 0;

            this.picturePowerMode.BackgroundImage = (Image)resources.GetObject("picturePowerMode.BackgroundImage");
            this.picturePowerMode.BackgroundImageLayout = ImageLayout.Zoom;
            this.picturePowerMode.Location = new Point(4, 5);
            this.picturePowerMode.Name = "picturePowerMode";
            this.picturePowerMode.Size = new Size(18, 18);
            this.picturePowerMode.TabIndex = 1;
            this.picturePowerMode.TabStop = false;

            this.labelPowerModeTitle.AutoSize = true;
            this.labelPowerModeTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.labelPowerModeTitle.Location = new Point(26, 6);
            this.labelPowerModeTitle.Name = "labelPowerModeTitle";
            this.labelPowerModeTitle.Size = new Size(80, 15);
            this.labelPowerModeTitle.TabIndex = 0;
            this.labelPowerModeTitle.Text = "性能模式";

            this.panelPowerMode.Controls.Add(this.comboPowerMode);
            this.panelPowerMode.Dock = DockStyle.Top;
            this.panelPowerMode.Location = new Point(8, 30);
            this.panelPowerMode.Name = "panelPowerMode";
            this.panelPowerMode.Size = new Size(204, 30);
            this.panelPowerMode.TabIndex = 1;

            this.comboPowerMode.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.comboPowerMode.BorderColor = Color.White;
            this.comboPowerMode.ButtonColor = Color.FromArgb(255, 255, 255);
            this.comboPowerMode.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboPowerMode.FormattingEnabled = true;
            this.comboPowerMode.Location = new Point(4, 4);
            this.comboPowerMode.Name = "comboPowerMode";
            this.comboPowerMode.Size = new Size(196, 22);
            this.comboPowerMode.TabIndex = 0;

            this.panelBoostTitle.AutoSize = true;
            this.panelBoostTitle.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.panelBoostTitle.Controls.Add(this.pictureBoost);
            this.panelBoostTitle.Controls.Add(this.labelBoostTitle);
            this.panelBoostTitle.Dock = DockStyle.Top;
            this.panelBoostTitle.Location = new Point(8, 60);
            this.panelBoostTitle.Name = "panelBoostTitle";
            this.panelBoostTitle.Size = new Size(204, 30);
            this.panelBoostTitle.TabIndex = 2;

            this.pictureBoost.BackgroundImage = (Image)resources.GetObject("pictureBoost.BackgroundImage");
            this.pictureBoost.BackgroundImageLayout = ImageLayout.Zoom;
            this.pictureBoost.Location = new Point(4, 5);
            this.pictureBoost.Name = "pictureBoost";
            this.pictureBoost.Size = new Size(18, 18);
            this.pictureBoost.TabIndex = 1;
            this.pictureBoost.TabStop = false;

            this.labelBoostTitle.AutoSize = true;
            this.labelBoostTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.labelBoostTitle.Location = new Point(26, 6);
            this.labelBoostTitle.Name = "labelBoostTitle";
            this.labelBoostTitle.Size = new Size(65, 15);
            this.labelBoostTitle.TabIndex = 0;
            this.labelBoostTitle.Text = "CPU 睿频";

            this.panelBoost.Controls.Add(this.comboBoost);
            this.panelBoost.Dock = DockStyle.Top;
            this.panelBoost.Location = new Point(8, 90);
            this.panelBoost.Name = "panelBoost";
            this.panelBoost.Size = new Size(204, 30);
            this.panelBoost.TabIndex = 3;

            this.comboBoost.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.comboBoost.BorderColor = Color.White;
            this.comboBoost.ButtonColor = Color.FromArgb(255, 255, 255);
            this.comboBoost.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBoost.FormattingEnabled = true;
            this.comboBoost.Location = new Point(4, 4);
            this.comboBoost.Name = "comboBoost";
            this.comboBoost.Size = new Size(196, 22);
            this.comboBoost.TabIndex = 0;

            this.panelImport.Controls.Add(this.buttonImport);
            this.panelImport.Dock = DockStyle.Top;
            this.panelImport.Location = new Point(8, 120);
            this.panelImport.Name = "panelImport";
            this.panelImport.Size = new Size(204, 36);
            this.panelImport.TabIndex = 4;

            this.buttonImport.Activated = false;
            this.buttonImport.BackColor = SystemColors.ControlLight;
            this.buttonImport.BorderColor = Color.Transparent;
            this.buttonImport.BorderRadius = 2;
            this.buttonImport.Dock = DockStyle.Fill;
            this.buttonImport.FlatStyle = FlatStyle.Flat;
            this.buttonImport.Location = new Point(4, 4);
            this.buttonImport.Name = "buttonImport";
            this.buttonImport.Secondary = true;
            this.buttonImport.Size = new Size(196, 28);
            this.buttonImport.TabIndex = 0;
            this.buttonImport.Text = "加载配置";
            this.buttonImport.UseVisualStyleBackColor = false;

            this.panelNav.Controls.Add(this.tableNav);
            this.panelNav.Dock = DockStyle.Bottom;
            this.panelNav.Location = new Point(8, 410);
            this.panelNav.Name = "panelNav";
            this.panelNav.Size = new Size(204, 70);
            this.panelNav.TabIndex = 5;

            this.tableNav.ColumnCount = 2;
            this.tableNav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableNav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            this.tableNav.Controls.Add(this.buttonCPU, 0, 0);
            this.tableNav.Controls.Add(this.buttonGPU, 1, 0);
            this.tableNav.Dock = DockStyle.Top;
            this.tableNav.Location = new Point(0, 0);
            this.tableNav.Name = "tableNav";
            this.tableNav.RowCount = 1;
            this.tableNav.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.tableNav.Size = new Size(204, 36);
            this.tableNav.TabIndex = 0;

            this.buttonCPU.Activated = true;
            this.buttonCPU.BackColor = SystemColors.ControlLight;
            this.buttonCPU.BorderColor = Color.Transparent;
            this.buttonCPU.BorderRadius = 2;
            this.buttonCPU.Dock = DockStyle.Fill;
            this.buttonCPU.FlatStyle = FlatStyle.Flat;
            this.buttonCPU.Location = new Point(0, 0);
            this.buttonCPU.Margin = new Padding(2);
            this.buttonCPU.Name = "buttonCPU";
            this.buttonCPU.Secondary = true;
            this.buttonCPU.Size = new Size(100, 32);
            this.buttonCPU.TabIndex = 0;
            this.buttonCPU.Text = "CPU";
            this.buttonCPU.UseVisualStyleBackColor = false;

            this.buttonGPU.Activated = false;
            this.buttonGPU.BackColor = SystemColors.ControlLight;
            this.buttonGPU.BorderColor = Color.Transparent;
            this.buttonGPU.BorderRadius = 2;
            this.buttonGPU.Dock = DockStyle.Fill;
            this.buttonGPU.FlatStyle = FlatStyle.Flat;
            this.buttonGPU.Location = new Point(104, 0);
            this.buttonGPU.Margin = new Padding(2);
            this.buttonGPU.Name = "buttonGPU";
            this.buttonGPU.Secondary = true;
            this.buttonGPU.Size = new Size(98, 32);
            this.buttonGPU.TabIndex = 1;
            this.buttonGPU.Text = "GPU";
            this.buttonGPU.UseVisualStyleBackColor = false;

            this.panelFans.AutoSize = true;
            this.panelFans.Controls.Add(this.labelTip);
            this.panelFans.Controls.Add(this.tableFanCharts);
            this.panelFans.Controls.Add(this.panelTitleFans);
            this.panelFans.Controls.Add(this.panelApplyFans);
            this.panelFans.Dock = DockStyle.Fill;
            this.panelFans.Location = new Point(220, 0);
            this.panelFans.Name = "panelFans";
            this.panelFans.Padding = new Padding(0, 0, 8, 0);
            this.panelFans.Size = new Size(380, 480);
            this.panelFans.TabIndex = 1;

            this.labelTip.AutoSize = true;
            this.labelTip.BackColor = SystemColors.ControlLightLight;
            this.labelTip.Location = new Point(280, 130);
            this.labelTip.Name = "labelTip";
            this.labelTip.Padding = new Padding(4);
            this.labelTip.Size = new Size(50, 20);
            this.labelTip.TabIndex = 40;
            this.labelTip.Text = "50, 60";
            this.labelTip.Visible = false;

            this.tableFanCharts.AutoSize = true;
            this.tableFanCharts.ColumnCount = 1;
            this.tableFanCharts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.tableFanCharts.Controls.Add(this.chartCPU, 0, 0);
            this.tableFanCharts.Controls.Add(this.chartGPU, 0, 1);
            this.tableFanCharts.Dock = DockStyle.Fill;
            this.tableFanCharts.Location = new Point(0, 30);
            this.tableFanCharts.Name = "tableFanCharts";
            this.tableFanCharts.Padding = new Padding(4, 0, 4, 4);
            this.tableFanCharts.RowCount = 2;
            this.tableFanCharts.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            this.tableFanCharts.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            this.tableFanCharts.Size = new Size(368, 370);
            this.tableFanCharts.TabIndex = 36;

            chartArea1.Name = "ChartArea1";
            this.chartCPU.ChartAreas.Add(chartArea1);
            this.chartCPU.Dock = DockStyle.Fill;
            this.chartCPU.Location = new Point(4, 0);
            this.chartCPU.Margin = new Padding(2, 4, 2, 4);
            this.chartCPU.Name = "chartCPU";
            this.chartCPU.Size = new Size(358, 177);
            this.chartCPU.TabIndex = 14;
            this.chartCPU.Text = "chartCPU";
            title1.Name = "Title1";
            title1.Text = "CPU Fan";
            this.chartCPU.Titles.Add(title1);

            chartArea2.Name = "ChartArea1";
            this.chartGPU.ChartAreas.Add(chartArea2);
            this.chartGPU.Dock = DockStyle.Fill;
            this.chartGPU.Location = new Point(4, 185);
            this.chartGPU.Margin = new Padding(2, 4, 2, 4);
            this.chartGPU.Name = "chartGPU";
            this.chartGPU.Size = new Size(358, 177);
            this.chartGPU.TabIndex = 17;
            this.chartGPU.Text = "chartGPU";
            title2.Name = "Title1";
            title2.Text = "GPU Fan";
            this.chartGPU.Titles.Add(title2);

            this.panelTitleFans.Controls.Add(this.picturePerf);
            this.panelTitleFans.Controls.Add(this.labelFans);
            this.panelTitleFans.Dock = DockStyle.Top;
            this.panelTitleFans.Location = new Point(0, 0);
            this.panelTitleFans.Name = "panelTitleFans";
            this.panelTitleFans.Size = new Size(364, 30);
            this.panelTitleFans.TabIndex = 42;

            this.picturePerf.BackgroundImage = (Image)resources.GetObject("picturePerf.BackgroundImage");
            this.picturePerf.BackgroundImageLayout = ImageLayout.Zoom;
            this.picturePerf.Location = new Point(6, 5);
            this.picturePerf.Name = "picturePerf";
            this.picturePerf.Size = new Size(18, 18);
            this.picturePerf.TabIndex = 41;
            this.picturePerf.TabStop = false;

            this.labelFans.AutoSize = true;
            this.labelFans.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.labelFans.Location = new Point(28, 6);
            this.labelFans.Name = "labelFans";
            this.labelFans.Size = new Size(65, 15);
            this.labelFans.TabIndex = 40;
            this.labelFans.Text = "风扇曲线";

            this.panelApplyFans.Controls.Add(this.labelFansResult);
            this.panelApplyFans.Controls.Add(this.buttonReset);
            this.panelApplyFans.Controls.Add(this.chkFullBlast);
            this.panelApplyFans.Dock = DockStyle.Bottom;
            this.panelApplyFans.Location = new Point(0, 400);
            this.panelApplyFans.Name = "panelApplyFans";
            this.panelApplyFans.Size = new Size(364, 60);
            this.panelApplyFans.TabIndex = 43;

            this.labelFansResult.AutoSize = true;
            this.labelFansResult.Location = new Point(6, 40);
            this.labelFansResult.Name = "labelFansResult";
            this.labelFansResult.Size = new Size(80, 15);
            this.labelFansResult.TabIndex = 47;
            this.labelFansResult.Text = "";
            this.labelFansResult.Visible = false;

            this.buttonReset.Activated = false;
            this.buttonReset.BackColor = SystemColors.ControlLight;
            this.buttonReset.BorderColor = Color.Transparent;
            this.buttonReset.BorderRadius = 2;
            this.buttonReset.FlatStyle = FlatStyle.Flat;
            this.buttonReset.Location = new Point(6, 6);
            this.buttonReset.Name = "buttonReset";
            this.buttonReset.Secondary = true;
            this.buttonReset.Size = new Size(60, 26);
            this.buttonReset.TabIndex = 44;
            this.buttonReset.Text = "默认";
            this.buttonReset.UseVisualStyleBackColor = false;

            this.chkFullBlast.AutoSize = true;
            this.chkFullBlast.Location = new Point(72, 10);
            this.chkFullBlast.Name = "chkFullBlast";
            this.chkFullBlast.Padding = new Padding(4, 0, 0, 0);
            this.chkFullBlast.Size = new Size(80, 19);
            this.chkFullBlast.TabIndex = 46;
            this.chkFullBlast.Text = "风扇全速";
            this.chkFullBlast.UseVisualStyleBackColor = false;

            this.Controls.Add(this.panelFans);
            this.Controls.Add(this.panelSliders);

            ((System.ComponentModel.ISupportInitialize)this.chartCPU).EndInit();
            ((System.ComponentModel.ISupportInitialize)this.chartGPU).EndInit();
            this.panelSliders.ResumeLayout(false);
            this.panelSliders.PerformLayout();
            this.panelPowerModeTitle.ResumeLayout(false);
            this.panelPowerModeTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.picturePowerMode).EndInit();
            this.panelPowerMode.ResumeLayout(false);
            this.panelBoostTitle.ResumeLayout(false);
            this.panelBoostTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.pictureBoost).EndInit();
            this.panelBoost.ResumeLayout(false);
            this.panelImport.ResumeLayout(false);
            this.panelNav.ResumeLayout(false);
            this.tableNav.ResumeLayout(false);
            this.panelFans.ResumeLayout(false);
            this.panelFans.PerformLayout();
            this.tableFanCharts.ResumeLayout(false);
            this.panelTitleFans.ResumeLayout(false);
            this.panelTitleFans.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)this.picturePerf).EndInit();
            this.panelApplyFans.ResumeLayout(false);
            this.panelApplyFans.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private Panel panelSliders;
        private Panel panelPowerModeTitle;
        private PictureBox picturePowerMode;
        private Label labelPowerModeTitle;
        private Panel panelPowerMode;
        private UI.RComboBox comboPowerMode;
        private Panel panelBoostTitle;
        private PictureBox pictureBoost;
        private Label labelBoostTitle;
        private Panel panelBoost;
        private UI.RComboBox comboBoost;
        private Panel panelImport;
        private UI.RButton buttonImport;
        private Panel panelNav;
        private TableLayoutPanel tableNav;
        private UI.RButton buttonGPU;
        private UI.RButton buttonCPU;

        private Panel panelFans;
        private Label labelTip;
        private TableLayoutPanel tableFanCharts;
        private Chart chartCPU;
        private Chart chartGPU;
        private Panel panelTitleFans;
        private PictureBox picturePerf;
        private Label labelFans;
        private Panel panelApplyFans;
        private Label labelFansResult;
        private UI.RButton buttonReset;
        private UI.RCheckBox chkFullBlast;
    }
}