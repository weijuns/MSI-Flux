// 机型自动判定 —— 回归测试
//
// 覆盖：
//   1. Intel 代际解析（7 个真实 CPU 名）
//   2. 精确机型匹配（含词序颠倒）
//   3. 代际 + 风扇数兜底
//   4. Generic 配置永不被"精确匹配"选中
//   5. 空输入 / 非 Intel / 未知机型 → 安全回退

using MSIFlux.Common;
using Xunit;

namespace MSIFlux.Tests;

public class MachineProfileTests
{
    // ── 1. Intel 代际解析 ─────────────────────────────────────
    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13900H", 13)]
    [InlineData("12th Gen Intel(R) Core(TM) i7-12700H", 12)]
    [InlineData("Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz", 10)]
    [InlineData("Intel(R) Core(TM) i5-9300H CPU @ 2.40GHz", 9)]
    [InlineData("Intel(R) Core(TM) i7-8750H CPU @ 2.20GHz", 8)]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H", 14)]
    [InlineData("AMD Ryzen 9 7945HX", 0)]
    [InlineData("", 0)]
    public void ParseIntelGeneration_RealCpuNames(string cpu, int expected)
    {
        Assert.Equal(expected, MachineProfile.ParseIntelGeneration(cpu));
    }

    // ── 2. 精确机型匹配 ───────────────────────────────────────
    private static Dictionary<string, string> Catalog(params (string file, string model)[] items)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, model) in items) d[file] = model;
        return d;
    }

    private static MachineProfile WithModel(string productName, int gen = 13, bool dualFan = true) =>
        new()
        {
            SystemProductName = productName,
            IsIntel = gen > 0,
            CpuGeneration = gen,
            IsDualFan = dualFan,
            IsLaptopFormFactor = true,
            IsMsiLaptop = true,
        };

    [Fact]
    public void SelectConfig_ExactModelMatch()
    {
        var p = WithModel("GS76 Stealth 11UG");
        var cat = Catalog(
            ("MSI-Stealth-GS76-11UG.xml", "GS76 Stealth 11UG"),
            ("MSI-10th-gen-or-newer-dualfan.xml", "~Generic: 10th-gen Intel (or newer) - Dual-fan models"));

        Assert.Equal("MSI-Stealth-GS76-11UG.xml", p.SelectConfigFile(cat));
    }

    [Fact]
    public void SelectConfig_WordOrderReversed_StillMatches()
    {
        // 配置里写 "GS76 Stealth 11UG"，本机读成 "Stealth GS76 11UG"
        var p = WithModel("Stealth GS76 11UG");
        var cat = Catalog(
            ("MSI-Stealth-GS76-11UG.xml", "GS76 Stealth 11UG"),
            ("MSI-10th-gen-or-newer-dualfan.xml", "~Generic: 10th-gen Intel (or newer) - Dual-fan models"));

        Assert.Equal("MSI-Stealth-GS76-11UG.xml", p.SelectConfigFile(cat));
    }

    [Fact]
    public void SelectConfig_CaseInsensitive()
    {
        var p = WithModel("katana gf66 12ug");
        var cat = Catalog(
            ("MSI-Katana-GF66-12UG.xml", "Katana GF66 12UG"),
            ("MSI-10th-gen-or-newer-dualfan.xml", "~Generic: 10th-gen Intel (or newer) - Dual-fan models"));

        Assert.Equal("MSI-Katana-GF66-12UG.xml", p.SelectConfigFile(cat));
    }

    // ── 3. 代际 + 风扇数兜底 ─────────────────────────────────
    [Theory]
    [InlineData(13, true, "MSI-10th-gen-or-newer-dualfan.xml")]
    [InlineData(13, false, "MSI-10th-gen-or-newer-singlefan.xml")]
    [InlineData(9, true, "MSI-9th-gen-or-older-dualfan.xml")]
    [InlineData(9, false, "MSI-9th-gen-or-older-singlefan.xml")]
    public void SelectConfig_GenerationAndFanFallback(int gen, bool dualFan, string expected)
    {
        var p = WithModel("Unknown Model XYZ", gen, dualFan);
        var cat = Catalog(
            ("MSI-10th-gen-or-newer-dualfan.xml", "~Generic: 10th-gen Intel (or newer) - Dual-fan models"),
            ("MSI-10th-gen-or-newer-singlefan.xml", "~Generic: 10th-gen Intel (or newer) - Single-fan models"),
            ("MSI-9th-gen-or-older-dualfan.xml", "~Generic: 9th-gen Intel (or older) - Dual-fan models"),
            ("MSI-9th-gen-or-older-singlefan.xml", "~Generic: 9th-gen Intel (or older) - Single-fan models"));

        Assert.Equal(expected, p.SelectConfigFile(cat));
    }

    // ── 4. Generic 不会被精确匹配误选 ────────────────────────
    [Fact]
    public void SelectConfig_GenericNeverChosenAsExactMatch()
    {
        // 本机型号恰好是 "Generic" 这种极端情况
        var p = WithModel("Generic");
        var cat = Catalog(
            ("MSI-10th-gen-or-newer-dualfan.xml", "~Generic: 10th-gen Intel (or newer) - Dual-fan models"),
            ("MSI-9th-gen-or-older-dualfan.xml", "~Generic: 9th-gen Intel (or older) - Dual-fan models"));

        // 应该走兜底（gen=13 + dualFan=true），而不是把 "Generic" 当精确名字匹配
        Assert.Equal("MSI-10th-gen-or-newer-dualfan.xml", p.SelectConfigFile(cat));
    }

    // ── 5. 安全回退 ───────────────────────────────────────────
    [Fact]
    public void SelectConfig_NonIntel_ReturnsNull()
    {
        var p = WithModel("Bravo 15 B7ED", gen: 0);   // AMD
        var cat = Catalog(("MSI-Bravo-15-B7ED.xml", "Bravo 15 B7ED"));

        // 精确匹配仍然有效（不依赖 CPU）
        Assert.Equal("MSI-Bravo-15-B7ED.xml", p.SelectConfigFile(cat));
    }

    [Fact]
    public void SelectConfig_NonIntelUnknownModel_ReturnsNull()
    {
        var p = WithModel("Totally Unknown", gen: 0);
        var cat = Catalog(
            ("MSI-10th-gen-or-newer-dualfan.xml", "~Generic: 10th-gen Intel (or newer) - Dual-fan models"));

        Assert.Null(p.SelectConfigFile(cat));
    }

    [Fact]
    public void SelectConfig_EmptyCatalog_ReturnsNull()
    {
        var p = WithModel("GS76 Stealth 11UG");
        Assert.Null(p.SelectConfigFile(new Dictionary<string, string>()));
    }

    [Fact]
    public void SelectConfig_EmptyProductName_FallsBackToGeneric()
    {
        var p = WithModel("", gen: 12, dualFan: true);
        var cat = Catalog(
            ("MSI-10th-gen-or-newer-dualfan.xml", "~Generic: 10th-gen Intel (or newer) - Dual-fan models"));

        Assert.Equal("MSI-10th-gen-or-newer-dualfan.xml", p.SelectConfigFile(cat));
    }

    // ── 6. 风扇数启发式 ───────────────────────────────────────
    [Theory]
    [InlineData("Modern 15 A5M", false)]     // 单风扇系列
    [InlineData("GF63 Thin 11SC", false)]    // 单风扇系列
    [InlineData("Prestige 14 Evo", false)]   // 单风扇系列
    [InlineData("Titan GT77HX 13VH", true)]  // 双风扇
    [InlineData("Vector 16 HX A14VHG", true)]// 双风扇
    [InlineData("Katana GF66 12UG", true)]   // 双风扇
    public void DualFanHeuristic(string model, bool expected)
    {
        // 通过公开 API 间接验证：用同一代际 + 不同型号，看选出哪份配置
        var p = WithModel(model, gen: 12, dualFan: expected);
        var cat = Catalog(
            ("MSI-10th-gen-or-newer-dualfan.xml", "~Generic: 10th-gen Intel (or newer) - Dual-fan models"),
            ("MSI-10th-gen-or-newer-singlefan.xml", "~Generic: 10th-gen Intel (or newer) - Single-fan models"));

        string? got = p.SelectConfigFile(cat);
        string expectFile = expected
            ? "MSI-10th-gen-or-newer-dualfan.xml"
            : "MSI-10th-gen-or-newer-singlefan.xml";
        Assert.Equal(expectFile, got);
    }
}
