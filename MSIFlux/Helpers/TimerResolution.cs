// This file is part of MSIFlux.
// TimerResolution: 高精度系统定时器控制器 (锁定 0.5ms 系统中断频率，降低游戏抖动与延迟)

using System;
using System.Runtime.InteropServices;

namespace MSIFlux.GUI.Helpers;

public static class TimerResolution
{
    private static bool _isEnabled;
    private static uint _currentResolution;

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    /// <summary>
    /// 设置高精度定时器状态 (0.5ms = 5000 * 100ns)
    /// </summary>
    public static bool SetState(bool enable)
    {
        try
        {
            if (enable)
            {
                // 1. 请求 Windows Multimedia 1ms 保护
                TimeBeginPeriod(1);

                // 2. 请求 Native NT 5000 (0.5ms = 5000 * 100ns) 高精度中断
                int result = NtSetTimerResolution(5000, true, out _currentResolution);
                _isEnabled = (result == 0);
                return _isEnabled;
            }
            else
            {
                // 恢复默认中断频率
                NtSetTimerResolution(5000, false, out _currentResolution);
                TimeEndPeriod(1);
                _isEnabled = false;
                return true;
            }
        }
        catch
        {
            _isEnabled = false;
            return false;
        }
    }

    public static bool IsEnabled => _isEnabled;
    public static double CurrentResolutionMs => _currentResolution / 10000.0;
}
