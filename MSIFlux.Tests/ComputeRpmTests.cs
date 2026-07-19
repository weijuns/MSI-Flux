using MSIFlux.Common.Configs;
using MSIFlux.Service;
using Xunit;

namespace MSIFlux.Tests;

public class ComputeRpmTests
{
    [Fact]
    public void ZeroInput_ReturnsZero()
    {
        var cfg = new FanRPMConf { RPMMult = 100, DivideByMult = false, Invert = false };
        int result = FanControlService.ComputeRpm(cfg, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void MultiplyMode_NormalValue()
    {
        var cfg = new FanRPMConf { RPMMult = 100, DivideByMult = false, Invert = false };
        int result = FanControlService.ComputeRpm(cfg, 50);
        Assert.Equal(5000, result); // 50 * 100
    }

    [Fact]
    public void DivideMode_NormalValue()
    {
        var cfg = new FanRPMConf { RPMMult = 100, DivideByMult = true, Invert = false };
        int result = FanControlService.ComputeRpm(cfg, 5000);
        Assert.Equal(50, result); // 5000 / 100
    }

    [Fact]
    public void DivideByZero_ReturnsZero()
    {
        var cfg = new FanRPMConf { RPMMult = 0, DivideByMult = true, Invert = false };
        int result = FanControlService.ComputeRpm(cfg, 100);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Invert_NormalValue()
    {
        var cfg = new FanRPMConf { RPMMult = 1, DivideByMult = false, Invert = true };
        int result = FanControlService.ComputeRpm(cfg, 2);
        Assert.Equal(0, result); // 1/2 = 0.5 → (int) → 0
    }

    [Fact]
    public void Invert_LargeEnoughValue()
    {
        // 1 / (1/200) = 200, but with mult=1 and RPM=200, rpm = 200, invert = 1/200 ~ 0.005 → (int)0
        // Let's use a case where invert gives a meaningful value:
        // rpmValue=1, mult=1, invert → rpm=1, 1/1=1 → (int)1
        var cfg = new FanRPMConf { RPMMult = 1, DivideByMult = false, Invert = true };
        int result = FanControlService.ComputeRpm(cfg, 1);
        Assert.Equal(1, result); // 1*1=1, invert=1/1=1
    }

    [Fact]
    public void Invert_ZeroRpm_ReturnsZero()
    {
        var cfg = new FanRPMConf { RPMMult = 1, DivideByMult = false, Invert = true };
        // rpm = 0*1 = 0, invert: rpm <= 0 → return 0
        int result = FanControlService.ComputeRpm(cfg, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void MultiplyByIdentity_ReturnsSame()
    {
        var cfg = new FanRPMConf { RPMMult = 1, DivideByMult = false, Invert = false };
        int result = FanControlService.ComputeRpm(cfg, 42);
        Assert.Equal(42, result);
    }

    [Fact]
    public void TypicalMSIRpmCalculation()
    {
        // MSI common pattern: raw * 100 = RPM (e.g. 30 → 3000 RPM)
        var cfg = new FanRPMConf { RPMMult = 100, DivideByMult = false, Invert = false };
        int result = FanControlService.ComputeRpm(cfg, 30);
        Assert.Equal(3000, result);
    }
}
