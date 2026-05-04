# MSI Flux 交接文档

**日期**: 2026-04-27  
**作者**: Cascade (AI 辅助开发)

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

### 2. Feature Manager 依赖处理

**关键发现**: Feature Manager **必须保留安装**，卸载后 WMI ACPI 方法调用会永久挂起。

已验证的失败方案:
- `mofcomp` 注册完整 MOF schema（15 个 WMI 类）→ 方法调用仍挂起
- WMI 仓库重建 (`winmgmt /salvagerepository`) → 无效
- 系统重启 → 无效
- PNP0C14 设备禁用/启用 → 无效
- 只有重新安装 FM 才能恢复

**当前方案**:
- FM 安装但服务不自启（MSI Foundation Service 设为 Manual，Micro Star SCM 设为 Disabled）
- MSI Flux 首次 GPU 切换时自动配置服务
- 项目内置 `Feature Manager_1.0.2312.2201.exe` 安装包供用户安装

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

- 中英文 README 均已更新，说明 FM 依赖和安装步骤
- 添加前置要求: Feature Manager (MSI Center 组件)
- 移除 "无需安装 MSI Center" 的错误声明

---

## 二、仍然存在的问题和不足

### 🔴 严重问题

1. **Feature Manager 必须安装** — 这是最大的限制。用户必须先安装 MSI Center（含 Feature Manager 组件），GPU 切换才能工作。无法实现"完全脱离 MSI Center"的初衷。原因未知，可能与 FM 安装时注册的内核级组件有关，但具体机制未查明。

2. **KernCoreLib64.Sys 作用不明** — 此文件被嵌入为资源但实际作用未确认。它不是注册为 Windows 驱动服务的内核驱动（Subsystem=0，非标准 PE），可能是被 MSIAPService.exe 加载的数据文件或辅助库。当前代码不会主动安装/加载它。

3. **WMI ACPI 方法调用挂起问题未根治** — FM 卸载后 WMI 方法调用挂起的根本原因未查明。可能的方向:
   - FM 安装时通过 `ServiceInstall.exe` 注册了某个内核驱动或 ACPI 过滤器
   - FM 安装时修改了 ACPI BIOS 的 WMI 数据块配置
   - `KernCoreLib64.Sys` 可能是关键组件但安装方式未知

### 🟡 中等问题

4. **Feature Manager Service.exe 无法独立运行** — 它是 WPF 应用，启动时在 `MainWindow..ctor()` 中因缺少 MSI Center 组件而抛出 `FileNotFoundException` 崩溃。当前方案跳过 FM Service，只依赖 MSI Foundation Service + 注册表写入。但 FM Service 可能在某些机型上是必要的（使 EC 写入 + 注册表修改在重启后生效）。

5. **EnsureMsiRegistryKeys() 可能不完整** — 当前只创建 `FW_GPU_CH` 和 `FW_CurrentNewGPU` 两个注册表值。MSI Center 可能还需要 `FW_SupportNewGPU`, `FW_SupportUMA`, `FW_SupportDiscrete` 等值才能正确触发切换。在 FM 已安装的机器上这些值已存在，但在全新安装的机器上可能缺失。

6. **GPU 切换需要重启** — 当前所有 GPU 模式切换都需要重启才能生效（BIOS 需要在 POST 阶段读取 EC 寄存器配置 MUX）。这是硬件限制，无法绕过。

7. **IPC 超时风险** — WMI ACPI 调用如果挂起，会导致 IPC 超时（15 秒），GUI 显示切换失败。当前没有对 WMI 调用设置独立的超时机制。

### 🟢 轻微问题

8. **EnsureFeatureManagerExtracted() 不会覆盖已有文件** — 如果 `C:\ProgramData\MSI Flux\FeatureManager\` 已存在旧文件，新增的嵌入资源（如 MSI_ACPI.mof）不会被提取。需要手动删除该目录才能触发完整提取。

9. **MOF 文件路径依赖** — `mofcomp` 的 `#PRAGMA AUTORECOVER` 会将 MOF 文件路径写入注册表。如果用户删除了 `C:\ProgramData\MSI Flux\FeatureManager\MSI_ACPI.mof`，WMI 仓库重建时会失败。

10. **仅测试了绝影 14 (Stealth 14)** — 所有测试均在 MSI 绝影 14 上进行，其他机型的 EC 寄存器地址和 WMI 行为可能不同。

---

## 三、关键文件索引

| 文件 | 说明 |
|---|---|
| `MSIFlux.Service/FanControlService.cs` | GPU 切换核心逻辑 (`SetGpuMode`, `WmiCallGet`, `WmiCallSet`, `EnsureMsiRegistryKeys`) |
| `MSIFlux.Service/UefiVariable.cs` | UEFI 变量读写 (`MsiDCVarData`, `CommitGpuMode`) |
| `MSIFlux/NativeInterop.cs` | GUI 侧 `EnumDisplayDevices` P/Invoke 声明 |
| `MSIFlux/Program.cs` | GUI 侧 GPU 模式本地检测 (`DetectGpuModeLocal`) + IPC 报告 |
| `MSIFlux/MSIFlux.csproj` | 嵌入资源配置 |
| `MSIFlux.Common/Paths.cs` | `EnsureFeatureManagerExtracted()` 资源提取 |
| `MSIFlux.Common/Utils.cs` | `IsMSIServiceRunning()` 冲突检测（已排除 MSI Foundation Service） |
| `MSIFlux/SettingsForm.cs` | GUI 端 GPU 切换按钮和 `EnsureFeatureManagerServiceRunning()` |
| `MSIFlux/Helpers/ServiceIpcProxy.cs` | IPC 代理（含 `ReportGpuMode`） |
| `MSIFlux.IPC/ServiceCommand.cs` | IPC 命令定义（含 `ReportGpuMode`） |
| `FeatureManager/MSI_ACPI.mof` | 完整 WMI ACPI schema（15 个类） |
| `FeatureManager/KernCoreLib64.Sys` | MSI 内核组件（作用不明） |
| `Feature Manager_1.0.2312.2201.exe` | FM 安装包 |

---

## 四、后续工作建议

1. **研究 FM 安装时注册的内核组件** — 用 Procmon 监控 FM 安装过程，找出它注册了什么驱动/过滤器。如果能找到并独立安装，就能摆脱 FM 依赖。

2. **研究 ServiceInstall.exe** — FM 安装目录中的 `ServiceInstall.exe` 可能负责注册内核驱动。反编译它可能找到关键信息。

3. **添加 WMI 调用超时机制** — 用 `Thread.Start` + `Join(timeout)` 包装 WMI 调用，避免 IPC 超时。

4. **多机型测试** — 在不同 MSI 笔记本型号上测试 GPU 切换。

5. **实现 FM Service 的替代方案** — 如果某些机型需要 FM Service 才能切换，需要找到它执行的关键动作并用代码替代。
