// This file is part of MSIFlux.
// AudioStateController: 基于 Windows CoreAudio API 控制系统主音量静音 (F1) 与麦克风禁用 (F5)

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MSIFlux.GUI.Helpers;

public static class AudioStateController
{
    [DllImport("ole32.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int CoInitializeEx([In, Optional] IntPtr pvReserved, [In] uint dwCoInit);

    [DllImport("ole32.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
    private static extern void CoUninitialize();

    private const uint COINIT_MULTITHREADED = 0x0;
    private const uint COINIT_APARTMENTTHREADED = 0x2;

    #region COM Interfaces
    [ComImport]
    [Guid("BCDE0385-4926-40E9-87C5-69A31D764516")]
    private class MMDeviceEnumerator
    {
    }

    private enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-41E6-4A7E-9767-93735A423557"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlNotificationBack(IntPtr pNotify);
        [PreserveSig] int UnregisterControlNotificationBack(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
    #endregion

    private static IAudioEndpointVolume? GetEndpointVolume(EDataFlow flow)
    {
        try
        {
            CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            int hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.eConsole, out IMMDevice device);
            if (hr != 0 || device == null)
            {
                Debug.WriteLine($"GetDefaultAudioEndpoint failed hr=0x{hr:X8}");
                return null;
            }

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            hr = device.Activate(ref iid, 23, IntPtr.Zero, out object volumeObj);
            if (hr != 0 || volumeObj == null)
            {
                Debug.WriteLine($"Activate IAudioEndpointVolume failed hr=0x{hr:X8}");
                return null;
            }

            return (IAudioEndpointVolume)volumeObj;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetEndpointVolume Exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取/切换系统主音量静音状态 (F1)
    /// </summary>
    public static bool GetSpeakerMute()
    {
        try
        {
            var vol = GetEndpointVolume(EDataFlow.eRender);
            if (vol != null && vol.GetMute(out bool isMuted) == 0)
            {
                return isMuted;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetSpeakerMute ex: {ex.Message}");
        }
        return false;
    }

    public static bool ToggleSpeakerMute()
    {
        try
        {
            var vol = GetEndpointVolume(EDataFlow.eRender);
            if (vol != null && vol.GetMute(out bool current) == 0)
            {
                bool next = !current;
                Guid ctx = Guid.Empty;
                vol.SetMute(next, ref ctx);
                return next;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ToggleSpeakerMute ex: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// 获取/切换麦克风禁用状态 (F5)
    /// </summary>
    public static bool GetMicMute()
    {
        try
        {
            var vol = GetEndpointVolume(EDataFlow.eCapture);
            if (vol != null && vol.GetMute(out bool isMuted) == 0)
            {
                return isMuted;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetMicMute ex: {ex.Message}");
        }
        return false;
    }

    public static bool ToggleMicMute()
    {
        try
        {
            var vol = GetEndpointVolume(EDataFlow.eCapture);
            if (vol != null && vol.GetMute(out bool current) == 0)
            {
                bool next = !current;
                Guid ctx = Guid.Empty;
                vol.SetMute(next, ref ctx);
                return next;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ToggleMicMute ex: {ex.Message}");
        }
        return false;
    }
}
