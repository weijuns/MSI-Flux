// MSI Flux GUI — Fn 热键检测 (键盘钩子 → IPC → Service / WMI ACPI LED)
// 摄像头状态监听 (Fn+F6 由 BIOS 处理, 我们只监控状态弹 OSD)

using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MSIFlux.GUI.Helpers;

internal sealed class HotkeyHook : IDisposable
{
    #region 键盘钩子

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _proc;
    private IntPtr _hookId;
    private readonly FanControlRunner _runner;
    private readonly MSIFlux.Common.Logs.Logger? _log;
    private readonly HashSet<uint> _dedup = new();
    private bool _disposed;

    private static bool _audioMutedState = false;
    private static bool _micMutedState = false;

    // 扫描码 → 动作
    // 注意: Fn+F7 不走键盘钩子, 由服务端 EC[0xC0] 轮询处理 (避免与 W 键共享 scanCode 0x0011 的冲突)
    private static readonly Dictionary<uint, (string, Action<FanControlRunner>)> ScanMap = new()
    {
        [0x0071] = ("Fn+F5 麦克风静音", r => {
            _micMutedState = !_micMutedState;
            try { ToggleMicAction(); } catch { }
            r.SetMicMuteLed(_micMutedState);
            OsdToastForm.ShowToast(_micMutedState ? "麦克风已禁用" : "麦克风已启用");
        }),
    };
    // VK 码 → 动作
    private static readonly Dictionary<uint, (string, Action<FanControlRunner>)> VkMap = new()
    {
        [0xAD] = ("Fn+F1 静音", r => {
            _audioMutedState = !_audioMutedState;
            try { AudioStateController.ToggleSpeakerMute(); } catch { }
            r.SetAudioMuteLed(_audioMutedState);
            OsdToastForm.ShowToast(_audioMutedState ? "静音" : "取消静音");
        }),
    };
    #endregion

    #region Win32

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static void SendMuteKey()
    {
        keybd_event(0xAD, 0, 0, UIntPtr.Zero);
        keybd_event(0xAD, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    private static void ToggleMicAction()
    {
        try { SendMessageW(GetForegroundWindow(), 0x0319, IntPtr.Zero, (IntPtr)0x180000); } catch { }
    }

    // 摄像头状态: 通过 WMI MSI_Event 监听 Fn+F6 (事件码 87)
    private ManagementEventWatcher? _camWatcher;
    private bool _camDisabled;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint tid);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }
    #endregion

    public HotkeyHook(FanControlRunner runner, MSIFlux.Common.Logs.Logger? logger = null)
    { _runner = runner; _log = logger; }

    public void Start()
    {
        _proc = HookProc;
        using var cp = Process.GetCurrentProcess();
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc!,
            cp.MainModule != null ? GetModuleHandle(cp.MainModule.ModuleName) : IntPtr.Zero, 0);
        SafeLog(_hookId != IntPtr.Zero ? "Fn 热键已安装 (IPC LED)" : "Fn 热键失败");

        // 启动 WMI 事件监听 (Fn+F6 摄像头等)
        StartWmiEventWatcher();

        // 启动时自动同步一次 F1 与 F5 的白色指示灯状态
        _ = Task.Run(() =>
        {
            try
            {
                bool spkMuted = AudioStateController.GetSpeakerMute();
                _audioMutedState = spkMuted;
                _runner.SetAudioMuteLed(spkMuted);

                bool micMuted = AudioStateController.GetMicMute();
                _micMutedState = micMuted;
                _runner.SetMicMuteLed(micMuted);
            }
            catch { }
        });
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)0x0104)) // WM_KEYDOWN or WM_SYSKEYDOWN
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // 专门检测 Fn+F7 (scanCode 0x0011)
            // 注意: 必须过滤掉标准键盘的 'W' 键 (vkCode == 0x57)
            if (kb.scanCode == 0x0011 && kb.vkCode != 0x57)
            {
                uint keyId = (kb.vkCode << 16) | (kb.scanCode & 0xFFFF);
                if (_dedup.Add(keyId))
                {
                    Task.Delay(400).ContinueWith(_ => _dedup.Remove(keyId));
                    SafeLog($"HOOK: 捕获到 Fn+F7 (scan=0x{kb.scanCode:X4}, vk=0x{kb.vkCode:X2}, flags=0x{kb.flags:X2}) → 切换性能模式");
                    _ = Task.Run(() => { try { _runner.NextPerfMode(); } catch { } });
                }
            }
            else if (ScanMap.TryGetValue(kb.scanCode, out var sc))
            {
                uint keyId = (kb.vkCode << 16) | (kb.scanCode & 0xFFFF);
                if (_dedup.Add(keyId))
                {
                    Task.Delay(300).ContinueWith(_ => _dedup.Remove(keyId));
                    SafeLog($"HOOK: scan=0x{kb.scanCode:X4} vk=0x{kb.vkCode:X2} → {sc.Item1}");
                    _ = Task.Run(() => { try { sc.Item2(_runner); } catch { } });
                }
            }
            else if (VkMap.TryGetValue(kb.vkCode, out var vk))
            {
                uint keyId = (kb.vkCode << 16) | (kb.scanCode & 0xFFFF);
                if (_dedup.Add(keyId))
                {
                    Task.Delay(300).ContinueWith(_ => _dedup.Remove(keyId));
                    SafeLog($"HOOK: scan=0x{kb.scanCode:X4} vk=0x{kb.vkCode:X2} → {vk.Item1}");
                    _ = Task.Run(() => { try { vk.Item2(_runner); } catch { } });
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // WMI MSI_Event 事件监听 (MSI 官方方式: Fn+F6 摄像头=87, 麦克风=25, 性能=29 等)
    private void StartWmiEventWatcher()
    {
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            scope.Connect();
            var query = new WqlEventQuery("SELECT * FROM MSI_Event");
            _camWatcher = new ManagementEventWatcher(scope, query);
            _camWatcher.EventArrived += (_, e) =>
            {
                try
                {
                    int code = 0;
                    foreach (PropertyData p in e.NewEvent.Properties)
                    {
                        if (p.Name == "MSIEvt" && p.Value != null && int.TryParse(p.Value.ToString(), out int v))
                        { code = v & 0xFF; break; }  // MSI 用 & 0xFF 提取事件码
                    }

                    const int Webcam = 87;       // WMIEventCode.Webcam
                    if (code == Webcam)
                    {
                        _camDisabled = !_camDisabled;
                        OsdToastForm.ShowToast(_camDisabled ? "摄像头已禁用" : "摄像头已启用");
                    }
                }
                catch { }
            };
            _camWatcher.Start();
            SafeLog("WMI MSI_Event 监听已启动 (摄像头 OSD)");
        }
        catch (Exception ex) { SafeLog($"WMI MSI_Event 监听失败: {ex.Message}"); }
    }

    private void SafeLog(string msg) { try { _log?.Info(msg); } catch { Debug.WriteLine(msg); } }
    public void Dispose()
    { if (_disposed) return; _disposed = true; _camWatcher?.Stop(); _camWatcher?.Dispose(); if (_hookId != IntPtr.Zero) { UnhookWindowsHookEx(_hookId); } }
}
