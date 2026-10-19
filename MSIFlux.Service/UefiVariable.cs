// MSI GPU 切换的真正提交点: UEFI 变量 MsiDCVarData.
// 反编译 Feature Manager Service.exe 的 Set_BIOS_Flag_Of_New_GPU_Switch 发现:
//   GUID = {DD96BAAF-145E-4F56-B1CF-193256298E99}
//   Name = "MsiDCVarData"  (4096 字节)
//   byte[5] 的 bit0/bit1 编码 GPU 模式:
//     Hybrid   (mode=0): byte[5] &= 0xFC          (bit0=0, bit1=0)
//     Discrete (mode=1): byte[5] = (byte[5] & 0xFC) | 0x01  (bit0=1)
//     Eco/UMA  (mode=2): byte[5] = (byte[5] & 0xFC) | 0x02  (bit1=1)
// BIOS 在 POST 阶段读取此变量决定 GPU MUX 路由,
// 这是 EC 寄存器 0xD1/0xBE 之外的另一个独立提交渠道.
// 没有这一步, 即使 EC 写入成功, BIOS 也不会真正切换 MUX.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MSIFlux.Common.Logs;

namespace MSIFlux.Service;

internal static class UefiVariable
{
    private const string MsiGpuVarName = "MsiDCVarData";
    private const string MsiGpuVarGuid = "{DD96BAAF-145E-4F56-B1CF-193256298E99}";
    private const int BufferSize = 4096;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFirmwareEnvironmentVariableExW(
        string lpName, string lpGuid, byte[] pBuffer, uint nSize, out uint pdwAttribubutes);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFirmwareEnvironmentVariableExW(
        string lpName, string lpGuid, byte[] pValue, uint nSize, uint dwAttributes);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privilege; }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const string SE_SYSTEM_ENVIRONMENT_NAME = "SeSystemEnvironmentPrivilege";

    /// <summary>启用本进程的 SeSystemEnvironmentPrivilege 特权 (调用 firmware API 必需).</summary>
    public static bool EnableFirmwarePrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            return false;
        try
        {
            if (!LookupPrivilegeValueW(null, SE_SYSTEM_ENVIRONMENT_NAME, out LUID luid)) return false;
            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privilege = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
            };
            return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(hToken); }
    }

    /// <summary>读取 MsiDCVarData UEFI 变量. 失败返回 null.</summary>
    public static (byte[]? data, uint size, uint attributes) ReadMsiDCVarData(Logger? log = null)
    {
        EnableFirmwarePrivilege();
        var buf = new byte[BufferSize];
        uint size = GetFirmwareEnvironmentVariableExW(
            MsiGpuVarName, MsiGpuVarGuid, buf, (uint)buf.Length, out uint attrs);
        if (size == 0)
        {
            int err = Marshal.GetLastWin32Error();
            log?.Warn($"GetFirmwareEnvironmentVariableExW failed, Win32Error={err} ({new Win32Exception(err).Message})");
            return (null, 0, 0);
        }
        return (buf, size, attrs);
    }

    /// <summary>把修改后的 4096 字节写回 MsiDCVarData.</summary>
    public static bool WriteMsiDCVarData(byte[] data, uint size, uint attributes, Logger? log = null)
    {
        EnableFirmwarePrivilege();
        bool ok = SetFirmwareEnvironmentVariableExW(MsiGpuVarName, MsiGpuVarGuid, data, size, attributes);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            log?.Error($"SetFirmwareEnvironmentVariableExW failed, Win32Error={err} ({new Win32Exception(err).Message})");
        }
        return ok;
    }

    /// <summary>
    /// 提交 GPU 模式切换到 UEFI 变量 (BIOS POST 时读取).
    /// mode: 0=Hybrid, 1=Discrete, 2=Eco/iGPU
    /// </summary>
    public static bool CommitGpuMode(int mode, Logger? log = null)
    {
        var (data, size, attrs) = ReadMsiDCVarData(log);
        if (data is null || size == 0) return false;

        log?.Info($"UEFI MsiDCVarData read: size={size}, attrs=0x{attrs:X}");
        byte before = data[5];
        // Clear bits 0-3 (requested mode in 0-1, actual mode in 2-3).
        // Clearing bits 2-3 ensures the value always differs from before,
        // so the BIOS will re-process the MUX request on POST.
        //
        // ⚠️ 掩码必须是 0xFC 而不是 0xF0. MSI 官方 Feature Manager、MControl、
        //    BabaConsole 三个独立实现全部用 & 0xFC:
        //      case 0: array[5] &= 0xFC;                    // Hybrid
        //      case 1: array[5] = (array[5] & 0xFC) | 1;    // Discrete
        //      case 2: array[5] = (array[5] & 0xFC) | 2;    // Eco/UMA
        //    bit4 是 "NewGPU 支持位"（BIOS 探测后置位，已稳定锁定）。
        //    用 0xF0 会把该位一并清掉，等于抹掉 BIOS 的确认状态。
        byte cleared = (byte)(before & 0xFC);
        byte after = mode switch
        {
            1 => (byte)(cleared | 0x01),  // Discrete
            2 => (byte)(cleared | 0x02),  // Eco
            _ => cleared,                  // Hybrid (0)
        };
        log?.Info($"UEFI MsiDCVarData[5]: 0x{before:X2} -> 0x{after:X2} (mode={mode})");
        data[5] = after;
        bool ok = WriteMsiDCVarData(data, size, attrs, log);
        if (ok) log?.Info("UEFI MsiDCVarData written successfully.");
        return ok;
    }

    /// <summary>读 byte[5] 当前值. 用于诊断/校验.</summary>
    public static int? ReadCurrentMode(Logger? log = null)
    {
        var (data, _, _) = ReadMsiDCVarData(log);
        if (data is null) return null;
        return data[5] & 0x03;
    }

    // ==================================================================
    // 机型 GPU 切换能力位（只读，不写任何 EC/UEFI）
    //
    // 数据来源: MsiDCVarData byte[5]
    //   来源交叉验证:
    //   Feature Manager Service/API_Dynamic/WinAndFnChange.cs:92-112
    //   MControl.Core/MsiGpuModeController.cs:56-60
    //   BabaConsole.Core.MsiGpuModeController.cs:70-82
    // ==================================================================

    /// <summary>机型 GPU 切换能力位。</summary>
    public readonly record struct GpuCapabilities(
        bool Available,            // 是否成功读到变量
        int RequestedMode,         // bit0-1
        int CurrentMode,           // bit2-3
        bool SupportsNewGpuSwitch, // bit4
        bool SupportsUma,          // bit5
        bool SupportsDiscrete,     // bit6 == 0 时为 true
        byte RawByte)
    {
        /// <summary>读失败时的占位值: 不声明任何能力.</summary>
        public static GpuCapabilities Unavailable => new(false, -1, -1, false, false, false, 0);
    }

    /// <summary>
    /// 读取 MsiDCVarData byte[5] 的能力位。
    ///
    /// 关键语义 (易错点, 与 MControl/BabaConsole 逐位一致):
    ///   bit4 置位 → 支持新 EC 显卡切换
    ///   bit5 置位 → 支持 UMA(核显) 切换
    ///   bit6 置位 → 【不支持】独显切换 (即 SupportsDiscrete = (bit6 == 0))
    ///               官方源码: if ((num &amp; 0x40) == 0) isSupport_Discrete_Switch = true;
    /// </summary>
    public static GpuCapabilities ReadGpuCapabilities(Logger? log = null)
    {
        var (data, size, _) = ReadMsiDCVarData(log);
        if (data is null || size <= 5)
        {
            log?.Debug($"MsiDCVarData 不可读 (size={size}), 无法获取机型能力位");
            return GpuCapabilities.Unavailable;
        }

        byte b = data[5];
        var caps = new GpuCapabilities(
            Available: true,
            RequestedMode: b & 0x03,
            CurrentMode: (b & 0x0C) >> 2,
            SupportsNewGpuSwitch: (b & 0x10) != 0,
            SupportsUma: (b & 0x20) != 0,
            SupportsDiscrete: (b & 0x40) == 0,
            RawByte: b);

        log?.Info($"MsiDCVarData[5]=0x{b:X2} → 能力: NewGpuSwitch={caps.SupportsNewGpuSwitch}, " +
                  $"UMA={caps.SupportsUma}, Discrete={caps.SupportsDiscrete}, " +
                  $"请求模式={caps.RequestedMode}, 当前模式={caps.CurrentMode}");
        return caps;
    }

    /// <summary>
    /// 把能力位打包成 int, 便于经 IPC 传输:
    ///   bit0-1 = RequestedMode, bit2-3 = CurrentMode,
    ///   bit4   = Available, bit5 = SupportsNewGpuSwitch,
    ///   bit6   = SupportsUma, bit7 = SupportsDiscrete,
    ///   bit8-15 = RawByte
    /// </summary>
    public static int PackCapabilities(in GpuCapabilities c)
    {
        int v = 0;
        v |= c.RequestedMode & 0x03;
        v |= (c.CurrentMode & 0x03) << 2;
        if (c.Available) v |= 1 << 4;
        if (c.SupportsNewGpuSwitch) v |= 1 << 5;
        if (c.SupportsUma) v |= 1 << 6;
        if (c.SupportsDiscrete) v |= 1 << 7;
        v |= (c.RawByte & 0xFF) << 8;
        return v;
    }

    /// <summary>解包 <see cref="PackCapabilities"/>.</summary>
    public static GpuCapabilities UnpackCapabilities(int packed) => new(
        Available: (packed & (1 << 4)) != 0,
        RequestedMode: packed & 0x03,
        CurrentMode: (packed >> 2) & 0x03,
        SupportsNewGpuSwitch: (packed & (1 << 5)) != 0,
        SupportsUma: (packed & (1 << 6)) != 0,
        SupportsDiscrete: (packed & (1 << 7)) != 0,
        RawByte: (byte)((packed >> 8) & 0xFF));
}
