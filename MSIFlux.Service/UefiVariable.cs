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
        byte cleared = (byte)(before & 0xFC);
        byte after = mode switch
        {
            1 => (byte)(cleared | 0x01),  // Discrete
            2 => (byte)(cleared | 0x02),  // Eco
            _ => cleared,                  // Hybrid (0)
        };
        log?.Info($"UEFI MsiDCVarData[5]: 0x{before:X2} -> 0x{after:X2} (mode={mode})");
        if (after == before)
        {
            log?.Info("UEFI byte[5] unchanged, skip write.");
            return true;
        }
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
}
