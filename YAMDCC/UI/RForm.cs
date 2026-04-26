using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace YAMDCC.GUI.UI
{
    public class RForm : Form
    {

        public static Color colorEco = Color.FromArgb(255, 6, 180, 138);
        public static Color colorStandard = Color.FromArgb(255, 58, 174, 239);
        public static Color colorTurbo = Color.FromArgb(255, 255, 32, 32);
        public static Color colorCustom = Color.FromArgb(255, 255, 128, 0);
        public static Color colorGray = Color.FromArgb(255, 168, 168, 168);


        public static Color buttonMain;
        public static Color buttonSecond;

        public static Color formBack;
        public static Color foreMain;
        public static Color borderMain;
        public static Color chartMain;
        public static Color chartGrid;

        [DllImport("UXTheme.dll", SetLastError = true, EntryPoint = "#138")]
        public static extern bool CheckSystemDarkModeStatus();

        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, int[] attrValue, int attrSize);

        public bool darkTheme = false;
        protected override CreateParams CreateParams
        {
            get
            {
                var parms = base.CreateParams;
                parms.Style &= ~0x02000000;
                parms.ClassStyle &= ~0x00020000;
                return parms;
            }
        }
        public static void InitColors(bool darkTheme)
        {
            if (darkTheme)
            {
                buttonMain = Color.FromArgb(255, 55, 55, 55);
                buttonSecond = Color.FromArgb(255, 38, 38, 38);

                formBack = Color.FromArgb(255, 28, 28, 28);
                foreMain = Color.FromArgb(255, 240, 240, 240);
                borderMain = Color.FromArgb(255, 50, 50, 50);

                chartMain = Color.FromArgb(255, 35, 35, 35);
                chartGrid = Color.FromArgb(255, 70, 70, 70);
            }
            else
            {
                buttonMain = SystemColors.ControlLightLight;
                buttonSecond = SystemColors.ControlLight;

                formBack = SystemColors.Control;
                foreMain = SystemColors.ControlText;
                borderMain = Color.LightGray;

                chartMain = SystemColors.ControlLightLight;
                chartGrid = Color.LightGray;
            }
        }

        public static bool IsDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var registryValueObject = key?.GetValue("AppsUseLightTheme");

                if (registryValueObject == null) return false;
                return (int)registryValueObject <= 0;
            }
            catch
            {
                return false;
            }
        }

        public bool InitTheme(bool setDPI = false)
        {
            bool newDarkTheme = IsDarkTheme();
            bool changed = darkTheme != newDarkTheme;
            darkTheme = newDarkTheme;

            InitColors(darkTheme);

            if (setDPI)
                ControlHelper.Resize(this);

            if (changed)
            {
                DwmSetWindowAttribute(Handle, 20, new[] { darkTheme ? 1 : 0 }, 4);
                ControlHelper.Adjust(this, changed);
                this.Invalidate();
            }


            return changed;

        }

    }
}
