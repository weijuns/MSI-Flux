using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MSIFlux.GUI.UI;

namespace MSIFlux.GUI.UI
{
    public static class ControlHelper
    {

        static bool _invert = false;
        static float _scale = 1;

        public static float Scale => _scale;

        public static void Adjust(RForm container, bool invert = false)
        {

            container.BackColor = RForm.formBack;
            container.ForeColor = RForm.foreMain;

            _invert = invert;
            AdjustControls(container.Controls);
            _invert = false;

        }

        public static void Resize(RForm container, float baseScale = 2)
        {
            _scale = GetDpiScale(container) / baseScale;
            if (Math.Abs(_scale - 1) > 0.2) ResizeControls(container.Controls);

        }

        private static void ResizeControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                var button = control as RButton;
                if (button != null && button.Image is not null)
                    button.Image = ResizeImage(button.Image);

                ResizeControls(control.Controls);
            }
        }


        private static void AdjustControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {

                AdjustControls(control.Controls);

                var button = control as RButton;
                if (button != null)
                {
                    button.BackColor = button.Secondary ? RForm.buttonSecond : RForm.buttonMain;
                    button.ForeColor = RForm.foreMain;

                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = RForm.borderMain;

                    if (button.Image is not null)
                        button.Image = AdjustImage(button.Image);
                }

                var pictureBox = control as PictureBox;
                if (pictureBox != null && pictureBox.BackgroundImage is not null)
                    pictureBox.BackgroundImage = AdjustImage(pictureBox.BackgroundImage);


                var combo = control as RComboBox;
                if (combo != null)
                {
                    combo.BackColor = RForm.buttonMain;
                    combo.ForeColor = RForm.foreMain;
                    combo.BorderColor = RForm.buttonMain;
                    combo.ButtonColor = RForm.buttonMain;
                    combo.ArrowColor = RForm.foreMain;
                }
                var numbericUpDown = control as NumericUpDown;
                if (numbericUpDown is not null)
                {
                    numbericUpDown.ForeColor = RForm.foreMain;
                    numbericUpDown.BackColor = RForm.buttonMain;
                }

                var gb = control as GroupBox;
                if (gb != null)
                {
                    gb.ForeColor = RForm.foreMain;
                }

                var pn = control as Panel;
                if (pn != null && pn.Name.Contains("Header"))
                {
                    pn.BackColor = RForm.buttonSecond;
                }

                var sl = control as Slider;
                if (sl != null)
                {
                    sl.borderColor = RForm.buttonMain;
                }

                var chk = control as CheckBox;
                if (chk != null && chk.BackColor != RForm.formBack)
                {
                    chk.BackColor = RForm.buttonSecond;
                }

            }
        }

        public static float GetDpiScale(Control control)
        {
            using (var graphics = control.CreateGraphics())
                return graphics.DpiX / 96.0f;
        }

        private static Image ResizeImage(Image image)
        {
            return ResizeImage(image, _scale);
        }

        public static Image ResizeImage(Image image, float scale)
        {
            if (Math.Abs(scale - 1) < 0.1) return image;

            var newSize = new Size((int)(image.Width * scale), (int)(image.Height * scale));
            var pic = new Bitmap(newSize.Width, newSize.Height);

            using (var g = Graphics.FromImage(pic))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, new Rectangle(new Point(), newSize));
            }
            return pic;
        }

        private static Image AdjustImage(Image image)
        {
            var pic = new Bitmap(image);

            if (_invert)
            {
                for (int y = 0; (y <= (pic.Height - 1)); y++)
                {
                    for (int x = 0; (x <= (pic.Width - 1)); x++)
                    {
                        Color col = pic.GetPixel(x, y);
                        pic.SetPixel(x, y, Color.FromArgb(col.A, (255 - col.R), (255 - col.G), (255 - col.B)));
                    }
                }
            }

            return pic;

        }

    }
}
