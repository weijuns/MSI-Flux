using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MSIFlux.GUI.Display
{
    class DeviceComparer : IComparer
    {
        public int Compare(object x, object y)
        {
            return 0;
        }
    }

    class ScreenComparer : IComparer
    {
        public int Compare(object x, object y)
        {
            return 0;
        }
    }

    internal class ScreenNative
    {
        [DllImport("gdi32", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateDC(string driver, string device, string port, IntPtr deviceMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;

            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;

            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        };

        [Flags()]
        public enum DisplaySettingsFlags : int
        {
            CDS_UPDATEREGISTRY = 1,
            CDS_TEST = 2,
            CDS_FULLSCREEN = 4,
            CDS_GLOBAL = 8,
            CDS_SET_PRIMARY = 0x10,
            CDS_RESET = 0x40000000,
            CDS_NORESET = 0x10000000
        }

        [DllImport("user32.dll")]
        public static extern int EnumDisplaySettingsEx(
             string lpszDeviceName,
             int iModeNum,
             ref DEVMODE lpDevMode);

        [DllImport("user32.dll")]
        public static extern int ChangeDisplaySettingsEx(
                string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd,
                DisplaySettingsFlags dwflags, IntPtr lParam);

        public static DEVMODE CreateDevmode()
        {
            DEVMODE dm = new DEVMODE();
            dm.dmDeviceName = new String(new char[32]);
            dm.dmFormName = new String(new char[32]);
            dm.dmSize = (short)Marshal.SizeOf(dm);
            return dm;
        }

        public const int ENUM_CURRENT_SETTINGS = -1;
        public const string defaultDevice = @"\\.\DISPLAY1";

        public static string? FindLaptopScreen(bool log = false)
        {
            return Screen.PrimaryScreen.DeviceName;
        }

        public static int GetMaxRefreshRate(string? laptopScreen)
        {
            if (laptopScreen is null) return -1;

            DEVMODE dm = CreateDevmode();
            int frequency = -1;

            int i = 0;
            while (0 != EnumDisplaySettingsEx(laptopScreen, i, ref dm))
            {
                if (dm.dmDisplayFrequency > frequency) frequency = dm.dmDisplayFrequency;
                i++;
            }

            return frequency;
        }

        public static int GetRefreshRate(string? laptopScreen)
        {
            if (laptopScreen is null) return -1;

            DEVMODE dm = CreateDevmode();
            int frequency = -1;

            if (0 != EnumDisplaySettingsEx(laptopScreen, ENUM_CURRENT_SETTINGS, ref dm))
            {
                frequency = dm.dmDisplayFrequency;
            }

            return frequency;
        }

        public static int SetRefreshRate(string laptopScreen, int frequency = 120)
        {
            DEVMODE dm = CreateDevmode();

            if (0 != EnumDisplaySettingsEx(laptopScreen, ENUM_CURRENT_SETTINGS, ref dm))
            {
                dm.dmDisplayFrequency = frequency;
                int iRet = ChangeDisplaySettingsEx(laptopScreen, ref dm, IntPtr.Zero, DisplaySettingsFlags.CDS_UPDATEREGISTRY, IntPtr.Zero);
                return iRet;
            }

            return 0;
        }
    }
}
