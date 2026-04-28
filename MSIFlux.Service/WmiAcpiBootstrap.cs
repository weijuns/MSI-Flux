// 完全摆脱 Feature Manager 的关键引导器.
//
// 工作原理:
//   Windows 内置的 wmiacpi.sys 驱动加载时, 会读取注册表
//   HKLM\SYSTEM\CurrentControlSet\Services\WmiAcpi\MofImagePath
//   指向的 PE 文件, 提取里面的 BMF (Binary MOF) 资源,
//   并把 MSI_ACPI/Package_32 等 ACPI WMI 类绑定到 BIOS 的 _WMI 方法.
//
// 因此只要我们:
//   1. 把 msiapcfg.dll (16KB 的 BMF-in-PE) 放到 C:\Windows\SysWOW64\
//   2. 设置 MofImagePath 指向它
//   3. 重启
//
// 就能让 wmiacpi.sys 加载 MSI 的 ACPI 方法绑定,
// 之后即使没有 Feature Manager, WMI ACPI 调用也能正常工作.

using System;
using System.IO;
using Microsoft.Win32;
using MSIFlux.Common.Logs;

namespace MSIFlux.Service;

internal static class WmiAcpiBootstrap
{
    private const string WmiAcpiServiceKey =
        @"SYSTEM\CurrentControlSet\Services\WmiAcpi";
    private const string MofImagePathValue = "MofImagePath";
    private const string ExpectedMofImagePath = @"%windir%\sysWOW64\msiapcfg.dll";
    private const string MsiApCfgFileName = "msiapcfg.dll";

    public sealed record Status(bool DllInPlace, bool RegistryConfigured, string? CurrentMofImagePath)
    {
        public bool IsFullyConfigured => DllInPlace && RegistryConfigured;
    }

    public static Status Check()
    {
        string sysWow64 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        string dst = Path.Combine(sysWow64, MsiApCfgFileName);
        bool dllInPlace = File.Exists(dst);

        string? current = null;
        try
        {
            using RegistryKey? k = Registry.LocalMachine.OpenSubKey(WmiAcpiServiceKey, writable: false);
            current = k?.GetValue(MofImagePathValue) as string;
        }
        catch { /* permission, ignore */ }

        bool regOk = !string.IsNullOrEmpty(current) &&
            current.IndexOf("msiapcfg.dll", StringComparison.OrdinalIgnoreCase) >= 0;

        return new Status(dllInPlace, regOk, current);
    }

    /// <summary>
    /// 一次性引导: 复制 msiapcfg.dll 到 SysWOW64, 设置 MofImagePath 注册表.
    /// 重启后即使卸载 Feature Manager, WMI ACPI 仍可工作.
    /// </summary>
    public static bool EnsureInstalled(string featureManagerDir, Logger log)
    {
        var st = Check();
        if (st.IsFullyConfigured)
        {
            log.Info("WMI ACPI bootstrap already configured (msiapcfg.dll + MofImagePath OK).");
            return true;
        }

        log.Info($"WMI ACPI bootstrap status: DllInPlace={st.DllInPlace}, RegistryConfigured={st.RegistryConfigured}");

        // 1. 复制 msiapcfg.dll
        string sysWow64 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        string dst = Path.Combine(sysWow64, MsiApCfgFileName);
        if (!File.Exists(dst))
        {
            string? src = FindSourceDll(featureManagerDir);
            if (src is null)
            {
                log.Error($"msiapcfg.dll not found in {featureManagerDir} or fallback locations.");
                return false;
            }
            try
            {
                File.Copy(src, dst, overwrite: false);
                log.Info($"Copied {src} -> {dst}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to copy msiapcfg.dll: {ex.Message}");
                return false;
            }
        }

        // 2. 设置 MofImagePath 注册表
        try
        {
            using RegistryKey? k = Registry.LocalMachine.OpenSubKey(WmiAcpiServiceKey, writable: true);
            if (k is null)
            {
                log.Error($"Cannot open HKLM\\{WmiAcpiServiceKey}");
                return false;
            }
            string? current = k.GetValue(MofImagePathValue) as string;
            if (!string.Equals(current, ExpectedMofImagePath, StringComparison.OrdinalIgnoreCase))
            {
                k.SetValue(MofImagePathValue, ExpectedMofImagePath, RegistryValueKind.String);
                log.Info($"Set HKLM\\...\\WmiAcpi\\{MofImagePathValue} = {ExpectedMofImagePath}");
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set MofImagePath registry: {ex.Message}");
            return false;
        }

        log.Warn("WMI ACPI bootstrap installed. A reboot is required to activate it.");
        return true;
    }

    private static string? FindSourceDll(string featureManagerDir)
    {
        string[] candidates =
        {
            Path.Combine(featureManagerDir, MsiApCfgFileName),
            Path.Combine(AppContext.BaseDirectory, MsiApCfgFileName),
            Path.Combine(AppContext.BaseDirectory, "FeatureManager", MsiApCfgFileName),
            @"C:\Program Files (x86)\Feature Manager\msiapcfg.dll",
        };
        foreach (var p in candidates)
        {
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
