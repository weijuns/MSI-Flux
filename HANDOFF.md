# MSI Flux 交接文档

**日期**: 2026-10-20 (更新)
**原作者**: Cascade + Claude (AI 辅助开发)
**更新说明**: 新增 v1.6.5 / v1.6.6 / v1.6.7 增量移植记录（含 EC 寄存器安全加固）

---

## 零、v1.6.5 → v1.6.7 增量移植（2026-10-19/20）

### 背景

v1.7.0 ~ v1.7.5 引入了一批「贴近官方 MSI Center 实现」的改动，但在本机
（Stealth 14Studio A13VF）上出现过内置键盘失效问题。排查后确认：
**多数 `Enable*` 开关是 v1.7.0 自己引入问题后又加开关关掉的空壳。**

因此决定以 v1.6.4 为基线，逐项移植有明确收益、低风险的改动，每项都实测键盘。

### 移植清单

| # | 项目 | 版本 | 说明 |
|---|---|---|---|
| 1 | UEFI 掩码 `0xF0` → `0xFC` | v1.6.5 | `MsiDCVarData[5]` 清位时保留 bit4（NewGPU 支持位） |
| 2 | `ApplyConf` 重入锁 | v1.6.5 | `Monitor.TryEnter` 防止并发重复写 EC |
| 3 | 机型自动判定 | v1.6.6 | `MachineProfile.cs`，三层匹配 |
| 4 | `0xE8` 读-比较-写 | v1.6.6 | 值相同不写；不同则只改 bit4 |
| 5 | GPU 能力读取 | v1.6.6 | `UefiVariable.GpuCapabilities`，纯只读 |
| 6 | EC 热键安全默认值 | v1.6.7 | `EnableEcHotkeyPolling` 默认 false |
| 7 | `WritePerfMode` 掩码 | v1.6.7 | `0xD2` 用官方掩码 + 读比较写 + 回读确认 |

### 刻意未移植

| 项目 | 原因 |
|---|---|
| `SetManualFanCurveEnable`（写 `0xD4` bit7） | 现有 `ApplyConf` 第 5 步已整字节写 `0x8D`，效果等效；多做反而多一次 WMI 调用 |
| `WmiAcpi` 统一封装 | 本机 `MSI_ACPI` 有实例，硬编码 `SELECT * FROM MSI_ACPI` 已够用；`GpuSwitchService` 本来就有正确 flag 校验 |

### 关键实测数据（供后续参考）

**EC 寄存器基线（键盘正常时，服务运行中）**

```
EC[0xC1] = 0x80   (EC 热键调试模式已开启)
EC[0xD2] = 0xC1   (Silent 性能模式)
EC[0xD4] = 0x8D   (Advanced 风扇曲线，bit7=1)
EC[0xD7] = 0xDA   (充电阈值 128+90)
EC[0xE8] = 0x00   (Win/Fn 不交换)
```

> ⚠️ **重要更正**：早期分析曾认为 `0xD4` bit7 或 `0xC1` bit7 会导致键盘失效。
> **实测证伪** —— v1.6.4 / v1.6.5 / v1.6.6 三次运行均在 `0xD4=0x8D`、
> `0xC1=0x80` 状态下键盘完全正常（2294 / 223 / 353 个按键样本）。
> 之前那次键盘失效的真实原因是**部署了 `publish_single` 里 `FileVersion 0.0.0.0`
> 且缺失 SDDL 的坏构建**，导致 IPC 管道 ACL 错误、GUI 连不上服务。

**WMI ACPI 实测布局（`Package_32`，含 flag）**

```
Get_AP(1)          = 01 8D 00 05 ...     [1] = 0xD4 当前值
Get_AP(0)          = 01 00 00 C1 80 DA   [3]=0xD2  [4]=0xC1  [5]=0xD7
Get_Fan(1)         = 01 3C 00 0A 0F 14 23 3C 55
                     [1]=controlByte  [3..9]=曲线速度
Get_Temperature(1) = 01 00 61 64 26 34 46 54 5C
                     [1]=T0  [2]=T6  [3]=thermalLimit(0x64=100)
```

> `Get_AP(0)` 一次可读回 3 个寄存器，比逐字节 `ReadByte` 快，
> 将来优化读取性能时可用。

### 发布验证清单（每次发布必查）

上次事故的教训：**部署前必须验证产物**，不能部署后才发现问题。

```powershell
# 1. 版本号
(Get-Item "publish_c\MSI Flux.exe").VersionInfo.FileVersion   # 必须匹配 csproj

# 2. SDDL（IPC 管道 ACL，缺失会导致 GUI 连不上服务）
$u = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes("publish_c\MSI Flux.exe"))
$u.Contains('O:BAG:SYD')                                      # 必须 True

# 3. 部署后立即测管道
$p = [System.IO.Pipes.NamedPipeClientStream]::new(".", "MSIFlux-Server", 'InOut')
$p.Connect(3000)                                              # 不抛异常
```

**校验和（v1.6.7 最终产物）**

```
大小   : 9,908,133 字节 (9.45 MB)
版本   : 1.6.7
MD5    : A4C1F60281EFDC90253A86943BBC96CA
```

---

## 一、已完成的工作

### 1. GPU 三模式切换功能

完整实现了 Hybrid / Discrete / Eco(iGPU) 三种 GPU 模式切换，逆向工程自 MSI Center 的 Feature Manager。

**核心代码**: `MSIFlux.Service/FanControlService.cs` → `SetGpuMode(int mode)`

**切换流程**:
1. 自动禁用 Micro Star SCM 服务（避免冲突）
2. 设置 MSI Foundation Service 为手动启动（不自启）
3. 启动 MSI Foundation Service（如未运行则通过 InstallUtil 安装）
4. 写注册表 `FW_GPU_CH` / `FW_CurrentNewGPU`
5. 写 UEFI 变量 `MsiDCVarData` byte[5]（提前到 EC 序列之前，匹配 GPUSwitch 工具顺序）
6. WMI ACPI 调用序列（带重试，最多 3 次）: `Get_AP(0)` → `Set_Data(0xD1)` → 等待 3s → `Set_Data(0xBE)`
7. 提示用户冷启动生效

### 2. WMI ACPI 自动引导 (2026-05-02 突破 — 无需安装 Feature Manager)

**关键发现**: 通过逆向 `wmiacpi.sys` 驱动的加载机制, 发现只需两个条件即可让 WMI ACPI 方法工作:

1. `msiapcfg.dll` (16KB 的 BMF-in-PE 文件) 放到 `C:\Windows\SysWOW64\`
2. `MofImagePath` 注册表值指向上述 dll

**当前方案**: 完全不依赖 Feature Manager 安装。
- `msiapcfg.dll` 内置为嵌入资源, 首次 GPU 切换时由 `WmiAcpiBootstrap.EnsureInstalled()` 自动提取并复制到 SysWOW64
- 自动设置 `HKLM\SYSTEM\CurrentControlSet\Services\WmiAcpi\MofImagePath`
- 首次引导后需重启一次以激活 WMI 绑定
- WMI 仓库损坏时, `SetGpuMode` 会自动用内置的 `MSI_ACPI.mof` 修复

**旧方案 (已废弃)**: 曾尝试要求用户安装 Feature Manager, 但逆向后发现只需 `msiapcfg.dll` + 注册表即可。`Feature Manager_1.0.2312.2201.exe` 安装包仍保留在项目中作为备用, 但正常使用不需要。

### 3. WMI ACPI MOF Schema

**文件**: `FeatureManager/MSI_ACPI.mof`

包含完整的 15 个 WMI 类定义（带 `#PRAGMA AUTORECOVER`）:
- `Package` (16 bytes), `Package_1` (1 byte), `Package_10` (10 bytes), `Package_32` (32 bytes)
- `MSI_ACPI` — 主方法类，29 个 Get_*/Set_* 方法
- `MSI_AP`, `MSI_CPU`, `MSI_Device`, `MSI_Event`, `MSI_Master_Battery`, `MSI_Power`, `MSI_Slave_Battery`, `MSI_Software`, `MSI_System`, `MSI_VGA` — 数据块类

**用途**: 当 WMI 仓库损坏时，`SetGpuMode` 会自动用 `mofcomp` 注册此 MOF 修复。

### 4. 嵌入资源更新

**文件**: `MSIFlux/MSIFlux.csproj`

新增嵌入资源:
- `MSI_ACPI.mof` — WMI schema 自动修复
- `KernCoreLib64.Sys` — MSI 内核组件（.Sys 后缀，Subsystem=0）
- `KernCoreLib64.dll` — MSI 用户态库
- `Feature Manager Service.exe.config` — FM Service 配置
- `Microsoft.Expression.Drawing.dll`, `MsiHid.dll`, `NamedPipeClientLib.dll`, `Newtonsoft.Json.dll`, `UEFIVaribleDll.dll` — FM 依赖 DLL

所有资源通过 `EnsureFeatureManagerExtracted()` 提取到 `C:\ProgramData\MSI Flux\FeatureManager\`。

### 5. GPU 模式检测 (GUI 侧)

**问题**: Windows 服务运行在 Session 0，`EnumDisplayDevices` 无法检测显示适配器。

**方案**: GUI 侧（用户会话）通过 `EnumDisplayDevices` 检测实际 GPU 模式，通过 IPC `ReportGpuMode` 命令报告给服务端缓存。

**检测逻辑**:
- NVIDIA 驱动桌面输出 → Discrete (1)
- Intel 驱动桌面输出 + NVIDIA 状态 OK → Hybrid (0)
- Intel 驱动桌面输出 + NVIDIA 不可用 → Eco (2)

**关键文件**:
- `MSIFlux/NativeInterop.cs` — EnumDisplayDevices P/Invoke
- `MSIFlux/Program.cs` — DetectGpuModeLocal() + ReportGpuMode IPC
- `MSIFlux.IPC/ServiceCommand.cs` — ReportGpuMode 命令
- `MSIFlux/Helpers/ServiceIpcProxy.cs` — ReportGpuMode 代理方法
- `MSIFlux.Service/FanControlService.cs` — SetCachedGpuMode + GetGpuMode 缓存逻辑

### 6. UEFI 变量修复

**问题**: `UefiVariable.CommitGpuMode()` 使用 `0xFC` 掩码只清除 bit 0-1，当 byte[5] 的 bit 2-3（BIOS 回写的实际模式）与目标不一致时，写入被跳过。

**修复**: 掩码改为 `0xF0`，清除 bit 0-3（请求模式 + 实际模式），确保每次切换都写入 UEFI 变量。

### 7. README 更新

- 中英文 README 均已更新
- ~~添加前置要求: Feature Manager (MSI Center 组件)~~ — 已在 2026-05-02 突破后撤回, 当前 README 说明"完全不依赖 Feature Manager 安装"
- 移除 "无需安装 MSI Center" 的错误声明 → 后因 WMI ACPI 引导器实现, README 恢复为"无需安装"

### 8. v1.1.0 代码质量优化 (2026-05-06)

按优先级完成了以下优化:

| # | 改动 | 文件 |
|---|---|---|
| #4 | SetGpuMode 返回值修正: UEFI 写入失败返回 false, EC 失败仅警告 (UEFI 是真正提交点) | `FanControlService.cs` |
| #3 | EnsureFeatureManagerExtracted 比较文件大小, 不同时覆盖旧文件 | `Paths.cs` |
| #11 | 日志文件超 10MB 自动轮转 (复用已有的 gzip 归档机制) | `Logger.cs` |
| #7 | DetectGpuModeLocal 结果缓存 10 秒, GPU 切换后自动失效 | `Program.cs`, `SettingsForm.cs` |
| #2 | WmiCallGet/WmiCallSet 加 15 秒超时保护 (Task.Run + Wait) | `FanControlService.cs` |
| #5 | sc.exe 子进程调用替换为 P/Invoke ChangeServiceConfigW | `FanControlService.cs` |
| — | 电源计划联动: 留空自动回退到平衡模式 GUID, 平衡也留空则不生效 | `SettingsForm.cs`, `PowerPlanForm.cs` |

### 9. 分发方案 (2026-05-06)

- 版本号: v1.1.0
- 框架依赖构建: 主 exe 约 9.4MB (需要 .NET 8 Desktop Runtime)
- 启动器: 自包含裁剪版 21MB, 内嵌主 exe, 启动时检测 .NET 运行时
- 未安装 .NET 8 时弹窗引导用户到下载页面
- 构建命令: `dotnet publish Launcher/Launcher.csproj -c Release -o publish-launcher`

### 10. v1.2.0 代码质量优化 + 退出清理 (2026-05-17)

| # | 改动 | 文件 |
|---|---|---|
| 1 | Launcher 临时目录泄漏修复: 启动前清理旧 `MSI_Flux_*` 临时目录 | `Launcher/Program.cs` |
| 2 | FanControlService.cs 拆分: GPU 切换逻辑 (~1000 行) 抽到 `GpuSwitchService.cs` (partial class) | `GpuSwitchService.cs`, `FanControlService.cs` |
| 3 | InstallUtil 重复代码提取: 3 处重复块合并为 `EnsureMsiFoundationServiceRunning` + `InstallAndStartMsiFoundationService` | `GpuSwitchService.cs` |
| 4 | 替换 schtasks 为 `CreateProcessAsUserW` P/Invoke: 直接在用户 Session 启动进程, 不再依赖临时任务计划 | `GpuSwitchService.cs` |
| 5 | `StartServiceWithRetry` sleep 修复: 用 `WaitForStatus` + `TimeoutException` 替代无条件 `Sleep(2000)` | `GpuSwitchService.cs` |
| 6 | SetGpuMode 120 秒超时保护: IPC handler 用 `Task.Run + Wait(120s)` 包装 | `FanControlService.cs` |
| 7 | 干净退出: 托盘退出时停止 MSIFluxService (WinRing0 驱动卸载) + 新增 `--stop-service` CLI 参数 | `SettingsForm.cs`, `Program.cs` |
| 8 | 修正过时注释: "请先安装 Feature Manager" 等旧描述 | `SettingsForm.cs`, `GpuSwitchService.cs` |

### 11. v1.6.3 品牌清理 + 版本管理重构 (2026-08-14)

| # | 改动 | 文件 |
|---|---|---|
| 1 | 品牌去 YAMDCC 化: 移除代码/文档中所有 YAMDCC、Sparronator9999 标识 (41 个源文件版权头, README 对比章节, DISCLAIMER), 仅保留 README 末尾致谢 | 41 个 .cs + `README.md` + `README.en.md` + `DISCLAIMER` |
| 2 | 版本号单一来源: 删除 `GenerateAssemblyInfo=false`, 版本统一由 `MSIFlux.csproj` 的 `<Version>` 管理, SDK 自动派生 AssemblyVersion/FileVersion/InformationalVersion | `MSIFlux.csproj` |
| 3 | 主页版本动态读取: `labelVersion.Text = "v" + Utils.GetVerString()`, 不再硬编码; 修复 exe 文件属性/About 对话框版本显示 0.0.0.0 | `SettingsForm.cs`, `SettingsForm.Designer.cs` |
| 4 | 禁用 InformationalVersion 附加 git hash: `IncludeSourceRevisionInInformationalVersion=false` | `MSIFlux.csproj` |

> **版本发布约定**: 改版本号只需修改 `MSIFlux.csproj` 的 `<Version>` 一处 (FileVersion 若需去掉 .0 尾巴则同步修改相邻的 `<FileVersion>`). 发布流程: `dotnet publish MSIFlux/MSIFlux.csproj -c Release -o publish-r2r -r win-x64 /p:PublishSingleFile=true /p:DebugType=none /p:PublishReadyToRun=true` → `git tag` → `gh release create`。
>
> **产物复制约定**: 每次编译完成后, 将产物 `MSI Flux.exe` 复制一份到 `C:\Users\wjs\Desktop\MSI Flux\` (桌面独立目录, 供直接使用).

---

## 二、仍然存在的问题和不足

### 🔴 严重问题

1. ~~**Feature Manager 必须安装**~~ — ✅ 已解决 (2026-05-02): 通过逆向 `wmiacpi.sys` 发现只需 `msiapcfg.dll` + `MofImagePath` 注册表即可。`WmiAcpiBootstrap.EnsureInstalled()` 首次运行自动完成引导, 无需安装 FM。

2. **KernCoreLib64.Sys 作用不明** — 此文件被嵌入为资源但实际作用未确认。它不是注册为 Windows 驱动服务的内核驱动（Subsystem=0，非标准 PE），可能是被 MSIAPService.exe 加载的数据文件或辅助库。当前代码不会主动安装/加载它。

3. ~~**WMI ACPI 方法调用挂起问题未根治**~~ — ✅ 已绕过 (2026-05-02): FM 卸载后 WMI 挂起的根本原因仍未查明, 但 WMI ACPI 引导器绕过了这个问题 — 直接让 `wmiacpi.sys` 加载 MSI ACPI 绑定, 不依赖 FM 安装状态。

### 🟡 中等问题

4. **Feature Manager Service.exe 无法独立运行** — 它是 WPF 应用，启动时在 `MainWindow..ctor()` 中因缺少 MSI Center 组件而抛出 `FileNotFoundException` 崩溃。当前方案跳过 FM Service，只依赖 MSI Foundation Service + 注册表写入。由于已实现 WMI ACPI 引导器绕过 FM 依赖, 此问题影响范围有限, 仅在部分机型可能需要 FM Service 来确保 EC 写入在重启后生效。

5. **EnsureMsiRegistryKeys() 可能不完整** — 当前只创建 `FW_GPU_CH` 和 `FW_CurrentNewGPU` 两个注册表值。MSI Center 可能还需要 `FW_SupportNewGPU`, `FW_SupportUMA`, `FW_SupportDiscrete` 等值才能正确触发切换。在 FM 已安装的机器上这些值已存在，但在全新安装的机器上可能缺失。

6. **GPU 切换需要重启** — 当前所有 GPU 模式切换都需要重启才能生效（BIOS 需要在 POST 阶段读取 EC 寄存器配置 MUX）。这是硬件限制，无法绕过。

7. ~~**IPC 超时风险**~~ — ✅ 已修复 (v1.1.0): WmiCallGet/WmiCallSet 已添加 15 秒超时保护 (Task.Run + Wait)。

### 🟢 轻微问题

8. ~~**EnsureFeatureManagerExtracted() 不会覆盖已有文件**~~ — ✅ 已修复 (v1.1.0): 现在比较嵌入资源和磁盘文件的大小, 不同时自动覆盖。

9. **MOF 文件路径依赖** — `mofcomp` 的 `#PRAGMA AUTORECOVER` 会将 MOF 文件路径写入注册表。如果用户删除了 `C:\ProgramData\MSI Flux\FeatureManager\MSI_ACPI.mof`，WMI 仓库重建时会失败。

10. **仅测试了绝影 14 (Stealth 14)** — 所有测试均在 MSI 绝影 14 上进行，其他机型的 EC 寄存器地址和 WMI 行为可能不同。

---

## 三、关键文件索引

| 文件 | 说明 |
|---|---|
| `MSIFlux.Service/FanControlService.cs` | 服务主体: EC 读写、风扇控制、IPC 消息处理、配置加载 |
| `MSIFlux.Service/GpuSwitchService.cs` | GPU 切换逻辑: `SetGpuMode`, WMI ACPI 调用, 服务管理, `CreateProcessAsUserW` |
| `MSIFlux.Service/WmiAcpiBootstrap.cs` | WMI ACPI 引导器: 复制 msiapcfg.dll + 设置 MofImagePath, 摆脱 FM 依赖 |
| `MSIFlux.Service/UefiVariable.cs` | UEFI 变量读写 (`MsiDCVarData`, `CommitGpuMode`) |
| `MSIFlux/NativeInterop.cs` | GUI 侧 `EnumDisplayDevices` P/Invoke 声明 |
| `MSIFlux/Program.cs` | 入口: GUI / --service / --install-service / --stop-service 分派; GPU 模式本地检测 |
| `MSIFlux/MSIFlux.csproj` | 嵌入资源配置 |
| `MSIFlux.Common/Paths.cs` | `EnsureFeatureManagerExtracted()` 资源提取 |
| `MSIFlux.Common/Utils.cs` | `IsMSIServiceRunning()` 冲突检测（已排除 MSI Foundation Service） |
| `MSIFlux/SettingsForm.cs` | GUI 端 GPU 切换按钮和 `EnsureFeatureManagerServiceRunning()` |
| `MSIFlux/Helpers/ServiceIpcProxy.cs` | IPC 代理（含 `ReportGpuMode`） |
| `MSIFlux.IPC/ServiceCommand.cs` | IPC 命令定义（含 `ReportGpuMode`） |
| `FeatureManager/MSI_ACPI.mof` | 完整 WMI ACPI schema（15 个类），WMI 仓库损坏时自动修复用 |
| `FeatureManager/KernCoreLib64.Sys` | MSI 内核组件（作用不明，当前未使用） |
| `Launcher/Program.cs` | .NET 运行时检测启动器 (自包含, 内嵌主 exe) |
| `Launcher/Launcher.csproj` | 启动器项目配置 (裁剪优化) |
| `MSIFlux/PowerPlanForm.cs` | 电源计划 GUID 配置窗口 |
| `MSIFlux/PowerNative.cs` | Windows 电源计划 P/Invoke |

---

## 四、后续工作建议

1. ~~**研究 FM 安装时注册的内核组件**~~ — ✅ 已不再需要 (2026-05-02): WMI ACPI 引导器已绕过 FM 依赖。仅 `msiapcfg.dll` + `MofImagePath` 注册表即可工作。

2. ~~**研究 ServiceInstall.exe**~~ — ✅ 已不再需要: 同上。

3. ~~**添加 WMI 调用超时机制**~~ — ✅ 已完成 (v1.1.0): WmiCallGet/WmiCallSet 已用 Task.Run + Wait(15s) 包装。

4. **多机型测试** — 在不同 MSI 笔记本型号上测试 GPU 切换。当前仅在绝影 14 (Stealth 14) 上验证。

5. **实现 FM Service 的替代方案** — 如果某些机型需要 FM Service 才能切换，需要找到它执行的关键动作并用代码替代。优先级较低，因 WMI ACPI 引导器已解决大部分场景。
