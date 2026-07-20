// This file is part of MSIFlux.
// AudioStateController: 基于 Windows CoreAudio API 控制系统主音量静音 (F1) 与麦克风禁用 (F5)

using System;
using System.Runtime.InteropServices;

namespace MSIFlux.GUI.Helpers;

public static class AudioStateController
{
    #region COM Interfaces
    [ComImport]
    [Guid("BCDE0385-4926-40E9-87C5-69A31D764516")]
    private class MMDeviceEnumerator
    {
    }

    private enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-41E6-4A7E-9767-93735A423557"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlNotificationBack(IntPtr pNotify);
        int UnregisterControlNotificationBack(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
    #endregion

    private static IAudioEndpointVolume? GetEndpointVolume(EDataFlow flow)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            int hr = enumerator.GetDefaultAudioEndpoint(flow, ERole.eConsole, out IMMDevice device);
            if (hr != 0 || device == null) return null;

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            hr = device.Activate(ref iid, 23, IntPtr.Zero, out object volumeObj);
            if (hr != 0 || volumeObj == null) return null;

            return (IAudioEndpointVolume)volumeObj;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取/切换系统主音量静音状态 (F1)
    /// </summary>
    public static bool GetSpeakerMute()
    {
        var vol = GetEndpointVolume(EDataFlow.eRender);
        if (vol != null && vol.GetMute(out bool isMuted) == 0)
        {
            return isMuted;
        }
        return false;
    }

    public static bool ToggleSpeakerMute()
    {
        var vol = GetEndpointVolume(EDataFlow.eRender);
        if (vol != null && vol.GetMute(out bool current) == 0)
        {
            bool next = !current;
            Guid ctx = Guid.Empty;
            vol.SetMute(next, ref ctx);
            return next;
        }
        return false;
    }

    /// <summary>
    /// 获取/切换麦克风禁用状态 (F5)
    /// </summary>
    public static bool GetMicMute()
    {
        var vol = GetEndpointVolume(EDataFlow.eCapture);
        if (vol != null && vol.GetMute(out bool isMuted) == 0)
        {
            return isMuted;
        }
        return false;
    }

    public static bool ToggleMicMute()
    {
        var vol = GetEndpointVolume(EDataFlow.eCapture);
        if (vol != null && vol.GetMute(out bool current) == 0)
        {
            bool next = !current;
            Guid ctx = Guid.Empty;
            vol.SetMute(next, ref ctx);
            return next;
        }
        return false;
    }
}
