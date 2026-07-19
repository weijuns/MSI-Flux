using MSIFlux.Common.Configs;
using Xunit;

namespace MSIFlux.Tests;

public class ConfigSaveLoadTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"msiflux_test_{Guid.NewGuid():N}.xml");

    [Fact]
    public void RoundTrip_SaveAndLoad_PreservesData()
    {
        string path = TempPath();
        try
        {
            var original = CreateMinimalConfig();
            original.Save(path);

            var loaded = MSIFlux_Config.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal("TestManufacturer", loaded.Manufacturer);
            Assert.Equal("TestModel", loaded.Model);
            Assert.Single(loaded.FanConfs);
            Assert.Equal("CPU Fan", loaded.FanConfs[0].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }

    [Fact]
    public void Save_CreatesBackupFile_OnOverwrite()
    {
        string path = TempPath();
        try
        {
            var v1 = CreateMinimalConfig();
            v1.Manufacturer = "V1";
            v1.Save(path);
            Assert.True(File.Exists(path));

            var v2 = CreateMinimalConfig();
            v2.Manufacturer = "V2";
            v2.Save(path);

            Assert.True(File.Exists(path + ".bak"));
            var bak = MSIFlux_Config.Load(path + ".bak");
            Assert.Equal("V1", bak.Manufacturer);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }

    [Fact]
    public void Load_CorruptedXml_FallsBackToBak()
    {
        string path = TempPath();
        try
        {
            var valid = CreateMinimalConfig();
            valid.Manufacturer = "Backup";
            valid.Save(path);
            // Move main to .bak, write garbage to main
            File.Move(path, path + ".bak");
            File.WriteAllText(path, "<garbage>not xml<<<");

            var loaded = MSIFlux_Config.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal("Backup", loaded.Manufacturer);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }

    private static MSIFlux_Config CreateMinimalConfig()
    {
        return new MSIFlux_Config
        {
            Ver = 1,
            Manufacturer = "TestManufacturer",
            Model = "TestModel",
            Author = "TestAuthor",
            FanConfs = new()
            {
                new FanConf
                {
                    Name = "CPU Fan",
                    MinSpeed = 0,
                    MaxSpeed = 150,
                    SpeedReadReg = 0xD0,
                    TempReadReg = 0xC8,
                    FanCurveRegs = new byte[] { 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86 },
                    UpThresholdRegs = new byte[] { 0x90, 0x91, 0x92, 0x93, 0x94, 0x95 },
                    DownThresholdRegs = new byte[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 },
                    FanCurveConfs = new()
                    {
                        new FanCurveConf
                        {
                            Name = "Eco",
                            Desc = "Eco mode fan curve",
                            TempThresholds = Enumerable.Range(0, 7)
                                .Select(_ => new TempThreshold { FanSpeed = 30, UpThreshold = 60, DownThreshold = 50 })
                                .ToList(),
                        },
                    },
                },
            },
            PerfModeConf = new PerfModeConf
            {
                Reg = 0xD2,
                PerfModes = new()
                {
                    new PerfMode { Name = "Eco", Desc = "Eco", Value = 0x20 },
                    new PerfMode { Name = "Silent", Desc = "Silent", Value = 0x30 },
                    new PerfMode { Name = "Balanced", Desc = "Balanced", Value = 0x40 },
                    new PerfMode { Name = "Turbo", Desc = "Turbo", Value = 0x50 },
                },
            },
        };
    }
}
