using System;
using System.Drawing;
using System.Windows.Forms;
using MSIFlux.GUI.UI;
using MSIFlux.Common.Configs;

namespace MSIFlux.GUI
{
    public partial class PowerPlanForm : RForm
    {
        private TextBox txtEco;
        private TextBox txtSilent;
        private TextBox txtBalanced;
        private TextBox txtTurbo;
        private Button btnSave;
        private Button btnCancel;
        private Button btnClear;

        public PowerPlanForm()
        {
            InitializeComponent();
            Text = "电源计划联动";
            LoadGuids();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.FromArgb(240, 240, 240);
            this.ClientSize = new Size(420, 280);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ShowIcon = false;
            this.Padding = new Padding(16);

            var lblHint = new Label
            {
                Text = "为每个性能模式指定 Windows 电源计划 GUID。\n留空则不切换。",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 110, 130),
                Location = new Point(16, 12),
                Size = new Size(388, 36),
                AutoSize = false,
            };
            this.Controls.Add(lblHint);

            int y = 54;
            txtEco = CreateGuidRow("省电模式 (Eco)", y); y += 46;
            txtSilent = CreateGuidRow("安静模式 (Silent)", y); y += 46;
            txtBalanced = CreateGuidRow("平衡模式 (Balanced)", y); y += 46;
            txtTurbo = CreateGuidRow("增强模式 (Turbo)", y); y += 20;

            int buttonWidth = 90;
            int buttonHeight = 32;
            int buttonGap = 8;
            int totalButtonsWidth = buttonWidth * 3 + buttonGap * 2;
            int startX = (420 - totalButtonsWidth) / 2;

            btnClear = new Button
            {
                Text = "全部清空",
                Location = new Point(startX, y),
                Size = new Size(buttonWidth, buttonHeight),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                BackColor = Color.FromArgb(229, 231, 235),
                Cursor = Cursors.Hand,
            };
            btnClear.Click += (s, e) =>
            {
                txtEco.Text = "";
                txtSilent.Text = "";
                txtBalanced.Text = "";
                txtTurbo.Text = "";
            };
            this.Controls.Add(btnClear);

            btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(startX + buttonWidth + buttonGap, y),
                Size = new Size(buttonWidth, buttonHeight),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                BackColor = Color.FromArgb(229, 231, 235),
                DialogResult = DialogResult.Cancel,
                Cursor = Cursors.Hand,
            };
            this.Controls.Add(btnCancel);

            btnSave = new Button
            {
                Text = "保存",
                Location = new Point(startX + (buttonWidth + buttonGap) * 2, y),
                Size = new Size(buttonWidth, buttonHeight),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
            };
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);

            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;

            this.ResumeLayout(false);
        }

        private TextBox CreateGuidRow(string label, int y)
        {
            var lbl = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(55, 65, 81),
                Location = new Point(16, y),
                Size = new Size(150, 22),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            this.Controls.Add(lbl);

            var txt = new TextBox
            {
                Font = new Font("Consolas", 9F),
                Location = new Point(168, y),
                Size = new Size(236, 22),
                PlaceholderText = "例如 a1841308-3541-4fab-bc81-f71556f20b4a",
            };
            this.Controls.Add(txt);

            return txt;
        }

        private void LoadGuids()
        {
            txtEco.Text = CommonConfig.GetPowerPlanGuid(0) ?? "";
            txtSilent.Text = CommonConfig.GetPowerPlanGuid(1) ?? "";
            txtBalanced.Text = CommonConfig.GetPowerPlanGuid(2) ?? "";
            txtTurbo.Text = CommonConfig.GetPowerPlanGuid(3) ?? "";
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (!ValidateGuid(txtEco, "省电模式") ||
                !ValidateGuid(txtSilent, "安静模式") ||
                !ValidateGuid(txtBalanced, "平衡模式") ||
                !ValidateGuid(txtTurbo, "增强模式"))
                return;

            CommonConfig.SetPowerPlanGuids(
                NullIfEmpty(txtEco.Text),
                NullIfEmpty(txtSilent.Text),
                NullIfEmpty(txtBalanced.Text),
                NullIfEmpty(txtTurbo.Text));

            DialogResult = DialogResult.OK;
            Close();
        }

        private bool ValidateGuid(TextBox txt, string name)
        {
            string text = txt.Text.Trim();
            if (string.IsNullOrEmpty(text)) return true;
            if (!Guid.TryParse(text, out _))
            {
                MessageBox.Show(
                    $"「{name}」的 GUID 格式不正确。\n请使用格式：xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx\n或留空表示不联动。",
                    "MSI Flux",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                txt.Focus();
                txt.SelectAll();
                return false;
            }
            return true;
        }

        private static string? NullIfEmpty(string? s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
    }
}
