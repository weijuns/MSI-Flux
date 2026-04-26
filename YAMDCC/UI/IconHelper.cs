using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YAMDCC.GUI.UI
{
    public class IconHelper
    {

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_SETICON = 0x80u;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;


        public static void SetIcon(Form form, Bitmap icon)
        {
            try
            {
                SendMessage(form.Handle, WM_SETICON, (IntPtr)ICON_BIG, Icon.ExtractAssociatedIcon(Application.ExecutablePath)!.Handle);
                SendMessage(form.Handle, WM_SETICON, (IntPtr)ICON_SMALL, icon.GetHicon());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting icon {ex.Message}");
            }
        }

    }
}
