// This file is part of MSIFlux.
// UsbPowerShare: 微星笔记本 USB 关机充电控制器 (基于 UEFI NVRAM MsiDCVarData)

using System;
using System.Runtime.InteropServices;

namespace MSIFlux.GUI.Helpers;

public static class UsbPowerShare
{
    private const string VarName = "MsiDCVarData";
    private const string VarGuid = "{DD96BAAF-145E-4F56-B1CF-193256298E99}";

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFirmwareEnvironmentVariableW(string lpName, string lpGuid, byte[] pBuffer, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetFirmwareEnvironmentVariableW(string lpName, string lpGuid, byte[] pBuffer, uint nSize);

    /// <summary>
    /// 读取当前 USB 关机充电状态
    /// </summary>
    public static bool GetState()
    {
        try
        {
            byte[] buf = new byte[16];
            uint read = GetFirmwareEnvironmentVariableW(VarName, VarGuid, buf, (uint)buf.Length);
            if (read > 3)
            {
                return (buf[3] & 1) != 0;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 设置 USB 关机充电状态
    /// </summary>
    public static bool SetState(bool enabled)
    {
        try
        {
            byte[] buf = new byte[16];
            uint read = GetFirmwareEnvironmentVariableW(VarName, VarGuid, buf, (uint)buf.Length);
            if (read > 3)
            {
                if (enabled)
                    buf[3] = (byte)(buf[3] | 1);
                else
                    buf[3] = (byte)(buf[3] & ~1);

                return SetFirmwareEnvironmentVariableW(VarName, VarGuid, buf, read);
            }
        }
        catch { }
        return false;
    }
}
