// MSI Flux — 集中常量定义
// EC 寄存器地址、热键码映射、LED 寄存器、默认值等硬编码常量集中管理,
// 方便不同机型适配和维护.

using System.Collections.Generic;

namespace MSIFlux.Common;

/// <summary>EC 嵌入式控制器寄存器地址常量</summary>
public static class EcRegs
{
    /// <summary>BIOS 写入的热键扫描码 (调试模式)</summary>
    public const byte HotkeyDebug = 0xC0;

    /// <summary>热键调试模式控制: bit7=1 开启</summary>
    public const byte HotkeyCtrl  = 0xC1;

    /// <summary>WMI ACPI / Direct EC LED 控制寄存器 (来自 MSI Feature Manager 逆向)
    /// Mic Mute LED (F5): 逻辑地址 44, bit 1
    /// Audio Mute LED (F1): 逻辑地址 45, bit 1</summary>
    public const byte LedRegMic   = 44;   // 0x2C
    public const byte LedRegAudio = 45;   // 0x2D
    public static readonly byte[] LedRegs = { LedRegMic, LedRegAudio };
}

/// <summary>Fn 热键 EC 编码 → 名称映射 (来自 BabaConsole 真机逆向 + MSI 官方热键表)</summary>
public static class HotkeyCodes
{
    public static readonly Dictionary<int, string> Map = new()
    {
        [118] = "Fn+F7",  // 情景模式
        [87]  = "Fn+F6",  // 摄像头开关
        [38]  = "Fn+↑",   // Cooler Boost 强冷
        [119] = "Fn+F8",  // 键盘背光
        [27]  = "Fn+Esc", // Fn Lock
        [112] = "Fn+F1",  // 静音
        [113] = "Fn+F2",  // 音量-
        [114] = "Fn+F3",  // 音量+
    };
}

/// <summary>可修改的默认值, 用户未自定义时使用</summary>
public static class Defaults
{
    /// <summary>性能模式 → CPU Boost 模式 的默认映射 (0=禁用, 2=高效, 4=激进)</summary>
    public static readonly Dictionary<int, int> PerfModeCpuBoost = new()
    {
        { 0, 0 },  // Eco → 禁用
        { 1, 2 },  // Silent → 高效
        { 2, 2 },  // Balanced → 高效
        { 3, 2 },  // Turbo → 高效
    };
}
