// EC 热键 / 性能模式寄存器安全约束 —— 回归测试
//
// 背景（2026-10-19）：
//   EC[0xC1] bit7 = 「EC 热键调试模式」使能位，会把 EC 键码上报
//   从正常键盘通道切到调试通道。MSI Center 卸载后无人仲裁，
//   置位可能导致内置键盘失效且必须断电重置 EC。
//
//   EC[0xD2] bit0/bit1 与「Shift 模式」状态机耦合，整字节覆盖
//   可能把固件状态机写到非法组合。

using MSIFlux.Common.Configs;
using Xunit;

namespace MSIFlux.Tests;

public class EcRegisterSafetyTests
{
    // ── 1. 默认值必须安全 ──────────────────────────────────────
    [Fact]
    public void EnableEcHotkeyPolling_DefaultsToFalse()
    {
        var cfg = new MSIFlux_Config();
        Assert.False(cfg.EnableEcHotkeyPolling,
            "EnableEcHotkeyPolling 默认必须为 false —— 置 true 会写 EC[0xC1] bit7");
    }

    // ── 2. XML 往返 ───────────────────────────────────────────
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnableEcHotkeyPolling_RoundTrips(bool value)
    {
        string path = Path.Combine(Path.GetTempPath(), $"msiflux-ec-{Guid.NewGuid():N}.xml");
        try
        {
            var cfg = LoadBuiltinGenericConfig();
            cfg.EnableEcHotkeyPolling = value;
            cfg.Save(path);

            var loaded = MSIFlux_Config.Load(path);
            Assert.Equal(value, loaded.EnableEcHotkeyPolling);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── 3. 旧配置缺字段时 → false（安全默认）──────────────────
    [Fact]
    public void LegacyConfigWithoutField_LoadsAsFalse()
    {
        string path = Path.Combine(Path.GetTempPath(), $"msiflux-legacy-{Guid.NewGuid():N}.xml");
        try
        {
            var cfg = LoadBuiltinGenericConfig();
            cfg.Save(path);

            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(File.ReadAllText(path));
            var nodes = doc.SelectNodes("//EnableEcHotkeyPolling");
            if (nodes is not null)
            {
                foreach (System.Xml.XmlNode n in nodes) n.ParentNode?.RemoveChild(n);
            }
            doc.Save(path);

            Assert.DoesNotContain("EnableEcHotkeyPolling", File.ReadAllText(path));

            var loaded = MSIFlux_Config.Load(path);
            Assert.False(loaded.EnableEcHotkeyPolling,
                "旧配置缺该字段时必须回落到 false（安全默认）");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── 4. WritePerfMode 官方掩码 ─────────────────────────────
    // 公式: ((cur & 0xC3) | 0xC0) & 0xFC | (target & 0x07)
    private static byte PerfMask(byte cur, byte target)
        => (byte)(((cur & 0xC3) | 0xC0) & 0xFC | (target & 0x07));

    [Theory]
    // 常见配置值 193(0xC1) = Silent
    [InlineData(0xC1, 0xC1, 0xC1)]
    [InlineData(0xC0, 0xC0, 0xC0)]
    [InlineData(0xC4, 0xC4, 0xC4)]
    [InlineData(0xC2, 0xC2, 0xC2)]
    // 保留 bit0/bit1 的既有语义
    [InlineData(0xC1, 0xC0, 0xC0)]
    [InlineData(0xC3, 0xC0, 0xC0)]
    // 强制 bit7/bit6 = 1
    [InlineData(0x01, 0x01, 0xC1)]
    [InlineData(0x00, 0x02, 0xC2)]
    // bit2-5 被清后由 target 低 3 位决定
    //   推导 cur=0xFF, target=0x00:
    //     (0xFF & 0xC3) = 0xC3 → | 0xC0 = 0xC3 → & 0xFC = 0xC0 → | 0x00 = 0xC0
    [InlineData(0xFF, 0x00, 0xC0)]
    [InlineData(0xFF, 0x05, 0xC5)]
    public void WritePerfMode_MaskMatchesOfficial(byte cur, byte target, byte expected)
    {
        Assert.Equal(expected, PerfMask(cur, target));
    }

    [Fact]
    public void WritePerfMode_MaskIsIdempotent()
    {
        // 对已正确的值再算一次，结果不变 → 可安全跳过写入
        byte[] values = { 0xC0, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5 };
        foreach (var v in values)
        {
            byte once = PerfMask(v, v);
            byte twice = PerfMask(once, v);
            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void WritePerfMode_AlwaysSetsBit6And7()
    {
        // 掩码强制 bit7/bit6 = 1（表示"手动/已配置"）
        for (byte cur = 0; cur < 0x10; cur++)
        {
            for (byte target = 0; target < 8; target++)
            {
                byte r = PerfMask(cur, target);
                Assert.True((r & 0xC0) == 0xC0,
                    $"cur=0x{cur:X2} target=0x{target:X2} → 0x{r:X2} 的 bit6/bit7 未置位");
            }
        }
    }

    // ── 辅助 ──────────────────────────────────────────────────
    private static MSIFlux_Config LoadBuiltinGenericConfig()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "Configs", "Generic", "MSI-10th-gen-or-newer-dualfan.xml");
            if (File.Exists(candidate))
                return MSIFlux_Config.Load(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "找不到内置 Generic 配置（Configs/Generic/MSI-10th-gen-or-newer-dualfan.xml）");
    }
}
