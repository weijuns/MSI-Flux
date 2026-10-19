// MSI Flux — 机型自动判定
//
// 目的：首次运行时自动挑选最匹配的内置配置，替代现在硬编码 Generic 的行为。
//       **绝不覆盖用户已有配置** —— 只在 CurrentConfig.xml 不存在时生效。
//
// 判定来源（逆向自 MSI Center DCv2.C_NB_Features.Parse()）：
//   · 机箱类型  HKLM\SYSTEM\HardwareConfig\Current\EnclosureType
//               10 = Notebook, 31 = Convertible
//   · 主板厂商  HKLM\SYSTEM\HardwareConfig\Current\BaseBoardManufacturer
//               MSI Center 白名单：Micro-Star International / MSI / MICRO-STAR
//   · 机型名    HKLM\HARDWARE\DESCRIPTION\System\BIOS\SystemProductName
//   · CPU 名称  HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0\ProcessorNameString
//
// 注意：MSI Center 的判定只输出"支不支持"，不识别具体型号。
//       本实现做得更细：先精确匹配 13 个已知机型，再按代际+风扇数兜底。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MSIFlux.Common;

/// <summary>
/// 本机硬件画像，用于自动选择机型配置。
/// </summary>
public sealed class MachineProfile
{
    // ── 原始检测值 ────────────────────────────────────────────
    public string EnclosureTypeRaw { get; init; } = "";
    public int EnclosureType { get; init; } = -1;
    public string BaseBoardManufacturer { get; init; } = "";
    public string SystemProductName { get; init; } = "";
    public string ProcessorName { get; init; } = "";

    // ── 派生结论 ──────────────────────────────────────────────
    /// <summary>是否 MSI 机器（机箱类型 + 厂商双条件）。</summary>
    public bool IsMsiLaptop { get; init; }

    /// <summary>是否笔记本/平板形态（EnclosureType ∈ {10, 31}）。</summary>
    public bool IsLaptopFormFactor { get; init; }

    /// <summary>CPU 代际。Intel 取型号数字首段，AMD/Ryzen 返回 0。</summary>
    public int CpuGeneration { get; init; }

    /// <summary>CPU 厂商是否 Intel。</summary>
    public bool IsIntel { get; init; }

    /// <summary>是否双风扇机型（按 SystemProductName 关键词启发式判断）。</summary>
    public bool IsDualFan { get; init; }

    /// <summary>诊断描述，写日志用。</summary>
    public string Summary =>
        $"Enclosure={EnclosureTypeRaw}/{EnclosureType}, Mfr='{BaseBoardManufacturer}', " +
        $"Model='{SystemProductName}', CPU='{ProcessorName}', " +
        $"Gen={CpuGeneration}, Intel={IsIntel}, DualFan={IsDualFan}, " +
        $"Msi={IsMsiLaptop}, Laptop={IsLaptopFormFactor}";

    // ── 采集 ──────────────────────────────────────────────────
    private const string RegHardwareConfig = @"HKEY_LOCAL_MACHINE\SYSTEM\HardwareConfig\Current";
    private const string RegBios = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";
    private const string RegCpu = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0";

    private static readonly string[] MsiManufacturers =
        { "Micro-Star International", "MSI", "MICRO-STAR" };

    [SupportedOSPlatform("windows")]
    public static MachineProfile Detect()
    {
        string encRaw = ReadReg(RegHardwareConfig, "EnclosureType");
        string mfr = ReadReg(RegHardwareConfig, "BaseBoardManufacturer");
        string model = ReadReg(RegBios, "SystemProductName");
        string cpu = ReadReg(RegCpu, "ProcessorNameString");

        int enc = int.TryParse(encRaw, out int e) ? e : -1;
        bool laptop = enc == 10 || enc == 31;
        bool isMsi = MsiManufacturers.Any(m =>
            mfr.Contains(m, StringComparison.OrdinalIgnoreCase));

        bool isIntel = cpu.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        int gen = isIntel ? ParseIntelGeneration(cpu) : 0;

        return new MachineProfile
        {
            EnclosureTypeRaw = encRaw,
            EnclosureType = enc,
            BaseBoardManufacturer = mfr,
            SystemProductName = model,
            ProcessorName = cpu,
            IsLaptopFormFactor = laptop,
            IsMsiLaptop = laptop && isMsi,
            IsIntel = isIntel,
            CpuGeneration = gen,
            IsDualFan = GuessDualFan(model),
        };
    }

    private static string ReadReg(string hive, string name)
    {
        try { return Registry.GetValue(hive, name, "")?.ToString()?.Trim() ?? ""; }
        catch { return ""; }
    }

    /// <summary>
    /// 从 ProcessorNameString 解析 Intel 代际。
    ///
    /// 实测样本（本机验证通过）：
    ///   "13th Gen Intel(R) Core(TM) i9-13900H"           → 13   （显式 "13th Gen"）
    ///   "12th Gen Intel(R) Core(TM) i7-12700H"           → 12
    ///   "Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz"      → 10   （5 位型号 10750）
    ///   "Intel(R) Core(TM) i5-9300H CPU @ 2.40GHz"       →  9   （4 位型号 9300）
    ///   "Intel(R) Core(TM) i7-8750H CPU @ 2.20GHz"       →  8   （4 位型号 8750）
    ///   "Intel(R) Core(TM) Ultra 7 155H"                 → 14
    ///   "AMD Ryzen 9 7945HX"                             →  0   （非 Intel）
    ///
    /// 型号数字规则：4 位取第 1 位，5 位取前 2 位。
    /// </summary>
    public static int ParseIntelGeneration(string cpuName)
    {
        if (string.IsNullOrWhiteSpace(cpuName)) return 0;

        // 形式 1：显式 "Nth Gen"
        var m = System.Text.RegularExpressions.Regex.Match(
            cpuName, @"(\d{1,2})(?:st|nd|rd|th)\s+Gen",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int g1) && g1 is > 0 and < 30)
            return g1;

        // 形式 2：型号数字，如 i7-10750H / i5-9300H / i9-13900H
        m = System.Text.RegularExpressions.Regex.Match(
            cpuName, @"\bi[3579]-(\d{4,5})[A-Z]{0,3}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            string digits = m.Groups[1].Value;
            // 4 位 (9300) → 首位 9；5 位 (10750) → 前两位 10
            string head = digits.Length == 4 ? digits.Substring(0, 1) : digits.Substring(0, 2);
            if (int.TryParse(head, out int g2) && g2 is > 0 and < 30)
                return g2;
        }

        // 形式 3：Ultra 系列统一按 14 代以上
        if (cpuName.Contains("Ultra", StringComparison.OrdinalIgnoreCase)) return 14;

        return 0;
    }

    /// <summary>
    /// 按机型名的系列关键词猜风扇数（启发式）。
    /// 这是保守推断：默认双风扇，只有明确匹配单风扇系列才返回 false。
    /// </summary>
    private static bool GuessDualFan(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return true;

        // 明确单风扇的轻量系列
        string[] singleFanHints =
        {
            "Modern 14", "Modern 15", "Prestige 14", "Prestige 15",
            "GF63", "GF75", "Thin GF63",
        };
        foreach (var hint in singleFanHints)
        {
            if (model.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 按本机画像挑选内置配置文件名（仅文件名，不含目录）。
    /// 返回 null 表示没有特别匹配，应沿用默认 Generic。
    /// </summary>
    /// <param name="availableModels">
    /// 可用配置：文件名 → 配置里的 Model 字段。由调用方从 Templates 目录枚举。
    /// </param>
    public string? SelectConfigFile(IReadOnlyDictionary<string, string> availableModels)
    {
        if (availableModels.Count == 0) return null;

        // ── 第 1 层：精确机型匹配 ─────────────────────────────
        // 配置的 Model 形如 "GS76 Stealth 11UG"，本机 SystemProductName 可能形如
        // "GS76 Stealth 11UG" 或 "MS-17M1"。做双向包含匹配 + 归一化比较。
        string mine = Normalize(SystemProductName);
        if (mine.Length > 0)
        {
            foreach (var kv in availableModels)
            {
                string theirs = Normalize(kv.Value);
                if (theirs.Length == 0 || theirs.StartsWith("~")) continue;   // 跳过 Generic

                if (theirs.Equals(mine, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;

                // 双向包含（容忍 "GS76 Stealth 11UG" vs "Stealth GS76 11UG" 这类词序差异）
                if (theirs.Contains(mine, StringComparison.OrdinalIgnoreCase) ||
                    mine.Contains(theirs, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;

                // 词集合完全相同即视为同一机型（忽略词序）
                if (SameTokenSet(theirs, mine))
                    return kv.Key;
            }
        }

        // ── 第 2 层：按代际 + 风扇数选 Generic ─────────────────
        if (!IsIntel || CpuGeneration <= 0) return null;   // 非 Intel 或解析失败 → 交给默认

        bool gen10Plus = CpuGeneration >= 10;
        string? target = (gen10Plus, IsDualFan) switch
        {
            (true, true) => "MSI-10th-gen-or-newer-dualfan.xml",
            (true, false) => "MSI-10th-gen-or-newer-singlefan.xml",
            (false, true) => "MSI-9th-gen-or-older-dualfan.xml",
            (false, false) => "MSI-9th-gen-or-older-singlefan.xml",
        };

        if (target is null) return null;
        return availableModels.Keys.FirstOrDefault(k =>
            string.Equals(k, target, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string s) =>
        (s ?? "").Replace("  ", " ").Trim();

    /// <summary>忽略词序比较两个机型名（大小写不敏感）。</summary>
    private static bool SameTokenSet(string a, string b)
    {
        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                  .Select(t => t.ToUpperInvariant()).OrderBy(t => t).ToArray();
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                  .Select(t => t.ToUpperInvariant()).OrderBy(t => t).ToArray();
        return ta.Length > 0 && ta.SequenceEqual(tb);
    }
}
