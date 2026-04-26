// This file is part of MSIFlux, based on YAMDCC.
// Original Copyright © 2023-2025 Sparronator9999
// Modifications Copyright © 2026 weijuns.
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
// more details.
//
// You should have received a copy of the GNU General Public License along with
// This program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MSIFlux.Common;

public static class Paths
{
    public static readonly string CodebergUrl = "https://codeberg.org";
    public static readonly string ProjectRepo = "MSIFlux_Config/MSIFlux";
    public static readonly string CodebergPage = $"{CodebergUrl}/{ProjectRepo}";

    private static readonly string _executableDirectory = GetExecutableDirectory();

    private static string GetExecutableDirectory()
    {
        // PublishSingleFile 模式下:
        //   - Process.GetCurrentProcess().MainModule.FileName 指向临时解压目录 (C:\..\Temp\.net\...)
        //   - Environment.ProcessPath (.NET 6+) 才指向用户实际双击的那个 exe
        //   - AppDomain.CurrentDomain.BaseDirectory 也指向解压目录
        // 我们需要 "exe 所在实际目录", 才能找到同目录下的 Configs\ 内置模板.
        try
        {
            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath))
            {
                return Path.GetDirectoryName(procPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            }
        }
        catch { }

        // Fallback: MainModule (非 singlefile 场景 ok)
        try
        {
            var mainModulePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(mainModulePath))
            {
                return Path.GetDirectoryName(mainModulePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            }
        }
        catch { }

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private static string GetDataPath()
    {
        // 使用 CommonApplicationData (C:\ProgramData) 作为存储根.
        // 理由: MSIFlux 是 "SYSTEM 服务 + 用户 GUI" 架构, 两边必须读写同一份配置.
        // 若放在 LocalAppData, SYSTEM 和当前用户会各看到一份独立的 CurrentConfig.xml,
        // 导致 GUI 改了配置, 服务看不到. 放 ProgramData 下配合放宽 ACL 即可共享.
        string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string appDataPath = Path.Combine(commonData, "MSI Flux", "Config");

        // 迁移 1: 老版本把配置放在 exe 同级目录的 Config 子文件夹
        string legacyExeDir = Path.Combine(_executableDirectory, "Config");
        if (Directory.Exists(legacyExeDir) && !Directory.Exists(appDataPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(appDataPath)!);
                Directory.Move(legacyExeDir, appDataPath);
            }
            catch
            {
                return legacyExeDir;
            }
        }

        // 迁移 2: MSIFlux v1.x/2.x/3.0-beta 里放在 LocalAppData 的配置 ->
        //         复制 (非移动, 避免多用户场景互相踩) 到 ProgramData.
        //         以 "目标文件不存在" 而非 "目标目录不存在" 作为判定条件,
        //         因为 Service (SYSTEM) 可能先跑并创建了空目录.
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string legacyLocal = Path.Combine(localAppData, "MSI Flux", "Config");
            string legacyCurrentConf = Path.Combine(legacyLocal, "CurrentConfig.xml");
            string newCurrentConf = Path.Combine(appDataPath, "CurrentConfig.xml");

            if (File.Exists(legacyCurrentConf) && !File.Exists(newCurrentConf))
            {
                Directory.CreateDirectory(appDataPath);
                CopyDirectoryShallow(legacyLocal, appDataPath);
            }
        }
        catch { /* 迁移失败不致命 */ }

        return appDataPath;
    }

    private static void CopyDirectoryShallow(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
        {
            try
            {
                string target = Path.Combine(dst, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target, overwrite: false);
                }
            }
            catch { }
        }
    }

    public static readonly string Data = GetDataPath();
    public static readonly string Logs = Path.Combine(Data, "Logs");
    public static readonly string GlobalConf = Path.Combine(Data, "GlobalConfig.xml");
    public static readonly string CurrentConf = Path.Combine(Data, "CurrentConfig.xml");
    public static readonly string HotkeyConf = Path.Combine(Data, "HotkeyConfig.xml");

    /// <summary>
    /// 机型模板目录. 单 exe 分发模式下, 主程序集把 Configs\*.xml 以
    /// EmbeddedResource 形式打入, 第一次访问时释放到这里供 GUI 导入选择.
    /// </summary>
    public static readonly string Templates =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MSI Flux", "Templates");

    public static string? DefaultConfigPath
    {
        get
        {
            EnsureTemplatesExtracted();

            // 优先顺序: ProgramData 提取目录 -> exe 同级目录 (旧版布局兼容)
            string[] possiblePaths = new string[]
            {
                Path.Combine(Templates, "Generic", "MSI-10th-gen-or-newer-dualfan.xml"),
                Path.Combine(_executableDirectory, "Configs", "Generic", "MSI-10th-gen-or-newer-dualfan.xml"),
                Path.Combine(_executableDirectory, "MSI-10th-gen-or-newer-dualfan.xml"),
            };
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }
    }

    private static int _templatesExtracted = 0;

    /// <summary>
    /// 把主程序集里嵌入的 Configs/*.xml 资源释放到 <see cref="Templates"/> 目录.
    /// 首次调用时执行, 后续调用是 no-op (原子 Interlocked 标记).
    /// 单个模板文件已存在时跳过, 用户手工改过的文件不会被覆盖.
    /// </summary>
    public static void EnsureTemplatesExtracted()
    {
        if (Interlocked.CompareExchange(ref _templatesExtracted, 1, 0) != 0) return;

        try
        {
            // 主 exe 程序集 (Paths 位于 MSIFlux.Common, 但 Common.cs 在单 exe 里
            // 被编入主程序集, 所以 typeof(Paths).Assembly 即是 MSI Flux.exe).
            var asm = typeof(Paths).Assembly;
            string[] names = asm.GetManifestResourceNames();

            Directory.CreateDirectory(Templates);

            foreach (string resName in names)
            {
                // 只处理 "Configs/..." 开头的资源, 保留相对路径
                const string prefix = "Configs/";
                if (!resName.StartsWith(prefix, StringComparison.Ordinal)) continue;

                string relative = resName.Substring(prefix.Length);
                string destPath = Path.Combine(Templates, relative.Replace('/', Path.DirectorySeparatorChar));

                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (File.Exists(destPath)) continue;  // 不覆盖已存在模板

                using Stream? src = asm.GetManifestResourceStream(resName);
                if (src is null) continue;

                using var dst = File.Create(destPath);
                src.CopyTo(dst);
            }
        }
        catch
        {
            // 释放失败不致命: DefaultConfigPath 会回退到 exe 同级目录路径.
        }
    }

    public static void EnsureCurrentConfigExists()
    {
        try
        {
            if (!Directory.Exists(Data))
            {
                Directory.CreateDirectory(Data);
            }

            // 放宽 Data 目录 ACL, 保证 SYSTEM 和普通用户都能读写 (关键: GUI 以 asInvoker
            // 身份运行, 默认对 SYSTEM 创建的目录/文件只读).
            TryGrantUsersFullControl(Data);

            if (!File.Exists(CurrentConf))
            {
                string defaultConfig = DefaultConfigPath;
                if (!string.IsNullOrEmpty(defaultConfig) && File.Exists(defaultConfig))
                {
                    File.Copy(defaultConfig, CurrentConf, true);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// 给目录添加 "Authenticated Users - Modify" 权限, 保证服务 (SYSTEM)
    /// 与 GUI (当前用户) 都能读写同一份配置. 调用者至少需要是该目录的
    /// Owner 或具有 WRITE_DAC 权限 (安装服务时以 admin 提权一次即可完成).
    /// 调用失败不抛异常, 日常写配置若失败会在更上层暴露.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void TryGrantUsersFullControl(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var sec = di.GetAccessControl();

            // S-1-5-11 = Authenticated Users (包含 SYSTEM, 管理员, 普通交互用户)
            var sid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            var rule = new FileSystemAccessRule(
                sid,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            sec.AddAccessRule(rule);
            di.SetAccessControl(sec);
        }
        catch
        {
            // 没权限改 ACL 时静默忽略. 首次安装服务时以 admin 提权, 能一次性把 ACL 放开;
            // 之后普通用户就不用再碰 ACL 了.
        }
    }
}