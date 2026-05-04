using System;
using System.Runtime.InteropServices;

class Program
{
    [DllImport("user32.dll")]
    static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    [DllImport("user32.dll")]
    static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct DEVMODE
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
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    static void Main()
    {
        Console.WriteLine("=== Display Device Enumeration ===\n");

        // Enumerate display adapters (GPU)
        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref adapter, EDD_GET_DEVICE_INTERFACE_NAME))
                break;

            Console.WriteLine($"Adapter {i}: {adapter.DeviceString}");
            Console.WriteLine($"  Name: {adapter.DeviceName}");
            Console.WriteLine($"  ID: {adapter.DeviceID}");
            Console.WriteLine($"  Flags: 0x{adapter.StateFlags:X8}");

            // Enumerate monitors for this adapter
            for (uint j = 0; ; j++)
            {
                var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(adapter.DeviceName, j, ref monitor, EDD_GET_DEVICE_INTERFACE_NAME))
                    break;

                Console.WriteLine($"  Monitor {j}: {monitor.DeviceString}");
                Console.WriteLine($"    Name: {monitor.DeviceName}");
                Console.WriteLine($"    ID: {monitor.DeviceID}");
                Console.WriteLine($"    Flags: 0x{monitor.StateFlags:X8}");
            }
        }

        Console.WriteLine("\n=== Display Settings ===\n");

        // Enumerate display settings for each adapter
        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0))
                break;

            if ((adapter.StateFlags & 0x1) != 0) // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettings(adapter.DeviceName, -1, ref dm)) // ENUM_CURRENT_SETTINGS
                {
                    Console.WriteLine($"{adapter.DeviceName} ({adapter.DeviceString}):");
                    Console.WriteLine($"  Resolution: {dm.dmPelsWidth}x{dm.dmPelsHeight}");
                    Console.WriteLine($"  Frequency: {dm.dmDisplayFrequency}Hz");
                }
            }
        }
    }
}
