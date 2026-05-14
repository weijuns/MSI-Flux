using System;
using System.Management;
using System.Runtime.InteropServices;

namespace MSIFlux.GUI
{
    internal class PowerNative
    {
        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerWriteDCValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
            int AcValueIndex);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerWriteACValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
            int AcValueIndex);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerReadACValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
            out IntPtr AcValueIndex
            );

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerSetActiveScheme(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerGetActiveScheme(IntPtr UserPowerKey, out IntPtr ActivePolicyGuid);

        static readonly Guid GUID_CPU = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        static readonly Guid GUID_BOOST = new Guid("be337238-0d82-4146-a960-4f3749d470c7");

        // Video subgroup and brightness setting GUIDs for display brightness
        static readonly Guid GUID_VIDEO_SUBGROUP = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
        static readonly Guid GUID_VIDEO_BRIGHTNESS = new Guid("aded5e82-b909-4619-9949-f5d71dac0bcb");

        static Guid GetActiveScheme()
        {
            IntPtr pActiveSchemeGuid;
            var hr = PowerGetActiveScheme(IntPtr.Zero, out pActiveSchemeGuid);
            Guid activeSchemeGuid = (Guid)Marshal.PtrToStructure(pActiveSchemeGuid, typeof(Guid));
            return activeSchemeGuid;
        }

        public static int GetCPUBoost()
        {
            IntPtr AcValueIndex;
            Guid activeSchemeGuid = GetActiveScheme();

            UInt32 value = PowerReadACValueIndex(IntPtr.Zero,
                 activeSchemeGuid,
                 GUID_CPU,
                 GUID_BOOST, out AcValueIndex);

            return AcValueIndex.ToInt32();

        }

        public static void SetCPUBoost(int boost = 0)
        {
            Guid activeSchemeGuid = GetActiveScheme();

            var hrAC = PowerWriteACValueIndex(
                 IntPtr.Zero,
                 activeSchemeGuid,
                 GUID_CPU,
                 GUID_BOOST,
                 boost);

            PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);

            var hrDC = PowerWriteDCValueIndex(
                 IntPtr.Zero,
                 activeSchemeGuid,
                 GUID_CPU,
                 GUID_BOOST,
                 boost);

            PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);
        }

        public static bool SetPowerPlan(Guid planGuid)
        {
            var hr = PowerSetActiveScheme(IntPtr.Zero, planGuid);
            return hr == 0;
        }

        /// <summary>
        /// Pre-sets the display brightness values (AC + DC) on a specific power
        /// scheme without activating it.  This way, when <see cref="SetPowerPlan"/>
        /// activates the scheme, the OS applies the desired brightness from the
        /// start — no race condition.
        /// </summary>
        public static void SetSchemeBrightness(Guid schemeGuid, int brightness)
        {
            try
            {
                PowerWriteACValueIndex(IntPtr.Zero, schemeGuid,
                    GUID_VIDEO_SUBGROUP, GUID_VIDEO_BRIGHTNESS, brightness);
                PowerWriteDCValueIndex(IntPtr.Zero, schemeGuid,
                    GUID_VIDEO_SUBGROUP, GUID_VIDEO_BRIGHTNESS, brightness);
            }
            catch { }
        }

        public static bool SetPowerPlan(string? planGuid)
        {
            if (string.IsNullOrWhiteSpace(planGuid)) return false;
            if (Guid.TryParse(planGuid, out Guid guid))
            {
                return SetPowerPlan(guid);
            }
            return false;
        }

        /// <summary>
        /// Gets the current screen brightness (0-100) via WMI.
        /// Returns -1 if unable to read.
        /// </summary>
        public static int GetBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\wmi",
                    "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return Convert.ToInt32(obj["CurrentBrightness"]);
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Sets the screen brightness (0-100) via WMI.
        /// </summary>
        public static void SetBrightness(int brightness)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\wmi",
                    "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject obj in searcher.Get())
                {
                    obj.InvokeMethod("WmiSetBrightness", new object[] { (uint)brightness });
                    return;
                }
            }
            catch { }
        }
    }
}