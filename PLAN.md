# MSI Flux 功能扩展计划

**日期**: 2026-05-02
**基于**: 当前代码库全面分析

---

## 一、现状总结

### 项目架构

| 模块 | 项目 | 职责 |
|---|---|---|
| `MSIFlux.Common` | 类库 | 共享配置、路径、工具函数、日志 |
| `MSIFlux.ECAccess` | 类库 | EC 读写 + WinRing0 驱动管理 |
| `MSIFlux.IPC` | 类库 | 命名管道 IPC 协议（命令/响应/枚举） |
| `MSIFlux.Service` | Worker Service | 后台服务：EC 读写、风扇控制、GPU 切换、IPC 服务器 |
| `MSIFlux` (GUI) | WinForms | 前端：SettingsForm 主界面 + Extra 设置 + Fans 风扇配置 |
| `MSIFlux.sln` | 解决方案 | 仅含 MSIFlux + MSIFlux.IPC（Service/Common/ECAccess 通过 ProjectReference 引入） |

### 已实现功能

- GPU 三模式切换（Hybrid/Discrete/Eco）
- 风扇曲线控制（7 点温度-转速曲线、4 种模式）
- 性能模式（省电/安静/均衡/加速）
- 电池充电阈值
- Win/Fn 键互换
- 键盘背光亮度
- 全速模式（Cooler Boost）
- 多语言支持（25 种语言）
- 系统托盘最小化（P0 ✅）
- 开机自启动（Task Scheduler，P0 ✅）
- 电池电量实时显示（P1-2 ✅）
- 拔电自动省电 + 插电恢复上次模式（P1-1 ✅）
- 电源计划联动（用户 GUI 填写 GUID，P2-1 ✅）
- 配置导入导出（P2-2 ✅）

### 外部依赖

- WinRing0x64.sys（EC 访问内核驱动）
- FeatureManager 组件（WMI ACPI 基础设施）
- XML 机型配置（`Configs/` 目录）

---

## 二、功能需求全景

按优先级和依赖关系分为 **P0/P1/P2** 三级：

| 优先级 | 功能 | 涉及模块 | 复杂度 | 状态 |
|---|---|---|---|---|
| **P0** | 自动更新系统 | GUI + Common + 新增 Update 模块 | 中 | ❌ 未实现 |
| **P0** | 系统托盘最小化 | GUI | 低 | ✅ 已实现 |
| **P0** | 开机自启动 | GUI + Common | 低 | ✅ 已实现 |
| **P1** | Windows 事件热响应（拔电省电） | GUI + Common | 中 | ✅ 已实现 |
| **P1** | 电池电量实时监控 | GUI | 低 | ✅ 已实现 |
| **P1** | 温度/功耗仪表盘 | Service + IPC + GUI | 高 | ❌ 未实现 |
| **P2** | 电源计划联动 | GUI + Common | 低 | ✅ 已实现 |
| **P2** | 配置导入导出 | GUI | 低 | ✅ 已实现 |
| **P2** | 键盘宏重映射 | Service（需 HID 驱动研究） | 高 | ❌ 暂缓 |

---

## 三、P0 功能详细设计

### 3.1 自动更新系统

**目标**：用户可在 GUI 内检查新版本，一键下载安装，无需手动去 GitHub。

**技术选型**：GitHub Releases API + 语义化版本比较 + 增量下载 zip 包

**架构**：

```
┌──────────┐   CheckForUpdates()   ┌──────────────┐   GET /releases   ┌─────────────┐
│  GUI 按钮 │ ──────────────────→  │ UpdateService │ ──────────────→  │ GitHub API  │
│  定时器   │ ←──────────────────  │  (Common)     │ ←──────────────  │ Releases    │
└──────────┘   UpdateInfo? null    └──────┬───────┘   JSON response   └─────────────┘
                                          │
                                   Download & Verify
                                          │
                                          ▼
                                   ┌──────────────┐
                                   │ 临时目录下载  │
                                   │ 解压覆盖重启  │
                                   └──────────────┘
```

**新增文件**：

| 文件 | 位置 | 说明 |
|---|---|---|
| `UpdateService.cs` | `MSIFlux.Common/` | 版本检查、下载、安装核心逻辑 |
| `UpdateInfo.cs` | `MSIFlux.Common/` | 版本信息数据类 |
| `GitHubRelease.cs` | `MSIFlux.Common/` | GitHub API JSON 反序列化模型 |

**核心接口**：

```csharp
// MSIFlux.Common/UpdateService.cs
public static class UpdateService
{
    public static async Task<UpdateInfo?> CheckForUpdatesAsync(
        string repoOwner, string repoName, Version currentVersion);

    public static async Task<string> DownloadUpdateAsync(
        UpdateInfo update, IProgress<double>? progress = null);

    public static void ApplyUpdate(string zipPath);
}

// MSIFlux.Common/UpdateInfo.cs
public record UpdateInfo(
    Version Version,
    string ReleaseNotes,
    string DownloadUrl,
    long FileSize,
    DateTimeOffset PublishedAt
);
```

**版本比较**：使用 `System.Version` 的四段式比较（Major.Minor.Build.Revision），从程序集元数据读取当前版本。

**GUI 集成**：

- SettingsForm 顶栏添加"检查更新"按钮（齿轮图标旁）
- 托盘菜单添加"检查更新"选项
- 检查到新版本时弹出更新对话框：版本号、更新日志、下载进度条
- 下载完成后提示"立即重启应用以完成更新"

**安全考虑**：

- 使用 HTTPS 下载，验证 ZIP 文件完整性（SHA256）
- 更新前备份当前版本到 `Backup/` 目录，失败时可回滚
- 需要管理员权限覆盖文件（如果安装目录在 Program Files）

---

### 3.2 系统托盘最小化

**目标**：关闭窗口时最小化到系统托盘而非退出，双击托盘图标恢复窗口。

**修改文件**：`MSIFlux/Program.cs`、`MSIFlux/SettingsForm.cs`、`MSIFlux/SettingsForm.Designer.cs`

**核心逻辑**：

```
用户点击 X 关闭窗口
  → 拦截 FormClosing 事件
  → e.Cancel = true
  → this.Hide()
  → NotifyIcon.Visible = true
  → 显示气泡通知 "MSI Flux 已最小化到托盘"

用户双击 NotifyIcon
  → this.Show()
  → this.WindowState = FormWindowState.Normal
  → this.Activate()
```

**托盘菜单（右键）**：

```
┌──────────────────────────┐
│ 显示主窗口               │
├──────────────────────────┤
│ GPU 模式  →  混合/独显/核显 │
│ 性能模式  →  省电/安静/均衡/加速 │
│ 风扇模式  →  自动/安静/基础/高级 │
├──────────────────────────┤
│ 检查更新...              │
├──────────────────────────┤
│ 退出                     │
└──────────────────────────┘
```

**不需要新增 IPC 命令**：托盘菜单直接调用 `Program.FanRunner` 的现有方法（`SetFanProf`、`SetPerfMode`、`SetGpuMode` 均已存在）。

**图标**：使用已有的 `MSIFlux.ico`，可选根据当前 GPU 模式切换不同图标。

---

### 3.3 开机自启动

**目标**：用户可在设置中启用/禁用开机自动启动 MSIFlux GUI。

**修改文件**：`MSIFlux.Common/Startup.cs`（已有引用但未实现）、`MSIFlux/Extra.Designer.cs`、`MSIFlux/Extra.cs`

**核心接口**：

```csharp
// MSIFlux.Common/Startup.cs
public static class Startup
{
    private const string AppName = "MSIFlux";

    public static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(AppName) != null;
    }

    public static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        if (enable)
            key?.SetValue(AppName, $"\"{Application.ExecutablePath}\" --minimized");
        else
            key?.DeleteValue(AppName, false);
    }
}
```

**使用 HKCU**：不需要管理员权限，且只影响当前用户。

**`--minimized` 参数**：启动时直接最小化到托盘，不显示主窗口。`Program.cs` 的入口需要解析此参数。

**GUI 集成**：Extra 设置页面的"通用设置"卡片中添加"开机自启动"复选框，与 Win/Fn 交换和语言选择并列。

---

## 四、P1 功能详细设计

### 4.1 Windows 事件热响应

**✅ 已实现**

**实际方案**：在 GUI 端（`SettingsForm.cs`）监听 `SystemEvents.PowerModeChanged`，拔电时自动切换到 Eco 模式并保存当前 AC 模式，插电时恢复上次的 AC 性能模式。

**实现细节**：
- `CommonConfig` 新增 `AutoEcoOnBattery` 开关 + `LastAcPerfMode` 持久化字段
- `HandleAutoEcoOnBattery()` 方法：拔电 → 保存当前模式到 `LastAcPerfMode` → 切到 Eco(0)；插电 → 恢复到 `LastAcPerfMode`
- Extra 设置页新增"拔电自动省电模式"复选框
- 使用 `_switchingPerfModeForBattery` 标志防止自动切换与手动操作冲突
- 不需要新增 IPC 命令，纯 GUI 端实现

**目标**：AC 电源插拔、屏幕锁定/解锁、系统休眠/唤醒等事件触发自动行为。

**现状**：Service 的 `OnPowerEvent` 已处理 `ResumeSuspend`（唤醒后重新应用风扇配置），但缺少 AC 事件处理。

**修改文件**：`MSIFlux.Service/FanControlService.cs`、`MSIFlux.IPC/ServiceCommand.cs`、`MSIFlux.IPC/ServiceResponse.cs`

**AC 电源事件处理**：

```
SystemEvents.PowerModeChanged → PowerStatus change
  → 读取 SystemInformation.PowerStatus.PowerLineStatus
  → AC Online:  可切换到性能模式（如 Balanced→Turbo）
  → AC Offline: 可切换到省电模式（如 Turbo→Eco）
```

**新增 IPC 命令**：

| 命令 | 方向 | 参数 | 说明 |
|---|---|---|---|
| `Command.SetPowerPolicy` | GUI→Service | `int acMode, int dcMode` | 设置 AC/DC 各自的性能模式 |

**配置扩展**（XML）：

```xml
<PowerPolicy>
    <AcPerfMode>2</AcPerfMode>    <!-- AC 供电时使用 Turbo -->
    <DcPerfMode>0</DcPerfMode>    <!-- DC 供电时使用 Eco -->
    <Enabled>false</Enabled>      <!-- 默认关闭，用户手动开启 -->
</PowerPolicy>
```

在 `MSIFlux.Common/Configs/` 中新增 `PowerPolicyConf`，或扩展现有的 `MSIFlux_Config`。

---

### 4.2 电池电量实时监控

**✅ 已实现**

**实际方案**：在 GUI 端使用 .NET 内置的 `SystemInformation.PowerStatus`，在 `TempRefreshTimer_Tick`（1 秒）中同步刷新。

**实现细节**：
- `SettingsForm.cs` 新增 `UpdateBatteryDisplay()` 方法
- 使用 `SystemInformation.PowerStatus.BatteryLifePercent` 获取电量百分比
- 使用 `PowerLineStatus` 判断充电状态
- 显示在 `labelBattery` 标签上，格式：`85%  充电中`
- 无电池台式机或数据无效时自动隐藏
- 不需要新增 IPC 命令，纯 GUI 端实现，零额外开销

---

### 4.3 温度/功耗仪表盘

**目标**：主界面添加实时温度曲线图和功耗显示。

**技术选型**：

| 方案 | 优点 | 缺点 |
|---|---|---|
| 自绘（GDI+） | 无外部依赖，轻量 | 开发工作量大 |
| ScottPlot | 功能丰富，MIT 许可 | 增加依赖 ~2MB |
| LiveCharts2 | 现代化，支持动画 | 依赖较重 |

**推荐**：自绘 GDI+ 温度曲线，保持轻量。功耗数据用数字显示即可。

**数据来源**：

| 数据 | 来源 | IPC 命令 |
|---|---|---|
| CPU 温度 | EC `TempReadReg` | `Command.GetTemp` (已有) |
| GPU 温度 | EC `TempReadReg` (GPU) | `Command.GetTemp` (已有) |
| CPU 风扇转速 | EC `SpeedReadReg` / `RPM ReadReg` | `Command.GetFanSpeed` / `Command.GetFanRPM` (已有) |
| GPU 风扇转速 | EC `SpeedReadReg` / `RPM ReadReg` | `Command.GetFanSpeed` / `Command.GetFanRPM` (已有) |
| CPU 功耗 | WMI `Win32_PerfFormattedData_Counters_ProcessorInformation` | 新增 `Command.GetCpuPower` |

**GUI 布局**：SettingsForm 顶部区域（GPU 按钮下方）添加温度/转速/功耗实时显示。

**轮询机制**：GUI 侧使用 `System.Windows.Forms.Timer`（500ms 间隔）调用已有 IPC 命令。

**新增 IPC 命令**：

| 命令 | 参数 | 说明 |
|---|---|---|
| `Command.GetCpuPower` | — | 返回 CPU Package Power (W) |

---

## 五、P2 功能详细设计

### 5.1 电源计划联动

**✅ 已实现**

**实际方案**：`PowerNative.cs` 新增 `SetPowerPlan()` P/Invoke 方法（调用 `PowerSetActiveScheme`），切换性能模式时自动联动对应的 Windows 电源计划。

**实现细节**：
- `CommonConfig` 新增 4 个 GUID 字段：`PowerPlanGuidEco` / `PowerPlanGuidSilent` / `PowerPlanGuidBalanced` / `PowerPlanGuidTurbo`
- 新建 `PowerPlanForm` 对话框：用户在 GUI 中填写 4 种模式对应的电源计划 GUID，无需手动编辑 XML
- Extra 设置页"配置"卡片新增"电源计划"按钮，点击打开 GUID 编辑对话框
- `SettingsForm.ApplyPowerPlanForPerfMode()` 在 `SetPerformanceMode()` 中被调用
- 支持 GUID 格式校验，留空的模式不触发电源计划切换

---

### 5.2 配置导入导出

**✅ 已实现**

**实现细节**：
- Extra 设置页"配置"卡片新增"导出"和"导入"两个按钮
- 导出：`SaveFileDialog` → `Config.Save()`，文件名自动带机型名和日期
- 导入：`OpenFileDialog` → `Config.Load()` → 校验机型匹配（不匹配弹警告可选继续）→ 复制到 `Paths.CurrentConf` → 提示重启

---

### 5.3 键盘宏重映射

**目标**：允许用户自定义键盘快捷键（如 Fn+F5 切换风扇模式）。

**技术难点**：

- MSI 笔记本的 Fn 组合键通过 EC 寄存器或 ACPI 事件传递
- 需要逆向 MSI Center 的键盘事件处理机制
- 可能需要 HID 驱动拦截或 ACPI 事件监听

**研究方向**：

1. 使用 `Procmon` 监控 MSI Center 按下 Fn+F5 时的行为
2. 检查 WMI ACPI 的 `MSI_Event` 类（已知的 15 个 WMI 类之一）
3. 检查 EC 寄存器中的键盘事件缓冲区

**建议**：此功能技术风险高，建议先在 MSI GPUSwitch 项目中用命令行工具研究，确认可行后再集成到 MSI Flux。

---

## 六、分阶段实现路线图

```
Phase 1 ✅ (已完成)
├── 系统托盘最小化                    ✅
├── 开机自启动                        ✅
└── 测试 + Bug 修复                   ✅

Phase 2 (自动更新)
├── UpdateService 核心逻辑            [2天]
├── GitHub API 集成                   [1天]
├── GUI 更新对话框                    [1天]
└── 测试（下载/安装/回滚）            [1天]

Phase 3 ✅ (已完成)
├── AC/DC 电源事件响应                ✅ (拔电省电 + 插电恢复)
├── 电池电量实时显示                  ✅ (SystemInformation.PowerStatus)
├── 温度/转速/功耗仪表盘              [未实现，暂不做功耗显示]
└── 托盘菜单快捷控制                  [未实现]

Phase 4 ✅ (已完成)
├── 配置导入导出                      ✅
├── 电源计划联动                      ✅ (PowerPlanForm GUI 对话框)
└── 多配置方案切换                    [未实现]

Phase 5 (高级功能，研究优先)
├── 键盘宏重映射（逆向研究）          [不确定]
└── 更多机型适配测试                  [持续]
```

---

## 七、跨模块约束与集成点

### 7.1 IPC 命令扩展规则

所有新功能的 Service↔GUI 通信必须遵循现有模式：

1. 在 `ServiceCommand.cs` 中添加 `Command` 枚举值
2. 在 `ServiceResponse.cs` 中添加 `Response` 枚举值（如果需要新响应类型）
3. 在 `FanControlService.cs` 的 `IPCClientMessage` switch 中添加 case
4. 在 `NamedPipeClient.cs`（GUI 侧）中添加对应的 `SendCommand` 重载

**版本兼容**：IPC 协议没有版本协商机制。如果 GUI 版本高于 Service 版本，发送新命令会导致 Service 返回 `Response.Error`。建议在 `Command.GetServiceVer` 中增加版本检查，版本不匹配时提示用户更新 Service。

### 7.2 配置类扩展规则

`MSIFlux_Config` 使用 `System.Xml.Serialization` 序列化。新增配置节时：

1. 在 `MSIFlux.Common/Configs/` 中添加新的配置类（参考 `FanConf`、`PerfModeConf`）
2. 在 `MSIFlux_Config` 中添加属性（`[XmlElement]` 标注）
3. 确保向后兼容：新属性必须有默认值，旧配置文件缺少新字段时不会崩溃

### 7.3 机型配置扩展

当前 XML 机型配置（`Configs/*.xml`）覆盖了风扇和性能模式的寄存器定义。新增功能可能需要扩展 XML schema：

```xml
<!-- 新增：电池寄存器 -->
<BatteryConf>
    <BatteryLevelReg>...</BatteryLevelReg>
    <ChargingStatusReg>...</ChargingStatusReg>
    <ChargingStatusBit>...</ChargingStatusBit>
</BatteryConf>

<!-- 新增：功耗寄存器（如果 EC 暴露了的话） -->
<PowerConf>
    <CpuPowerReg>...</CpuPowerReg>
    <GpuPowerReg>...</GpuPowerReg>
</PowerConf>
```

这些寄存器地址因机型而异，需要逐机型逆向确认。在没有确认之前，可以先用 WMI 作为 fallback。

### 7.4 管理员权限边界

| 操作 | 需要管理员？ | 说明 |
|---|---|---|
| EC 读写 | ✅ | WinRing0 内核驱动 |
| 注册表 HKLM 写入 | ✅ | GPU 切换注册表 |
| 注册表 HKCU 写入 | ❌ | 开机自启动 |
| 文件覆盖（更新） | ✅ 如果在 Program Files | 自动更新 |
| WMI ACPI 调用 | ✅ | 需要 SYSTEM 权限 |
| HTTP 请求 | ❌ | 检查更新、下载 |

**建议**：GUI 默认以普通用户权限运行，需要管理员权限的操作通过 IPC 委托给 Service（已是 SYSTEM 权限）。自动更新的文件覆盖可以通过 UAC 提权实现。

---

## 八、关键风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| GitHub API 限流（未认证 60次/小时） | 更新检查失败 | 使用条件请求 `If-None-Match`，缓存 ETag |
| WinRing0 驱动签名问题（Win11 24H2+） | EC 访问失败 | 关注 WinRing0 更新，备选方案：RWEverything 内核驱动 |
| IPC 版本不匹配 | 新 GUI 配旧 Service 崩溃 | 添加版本检查，版本不匹配时提示 |
| EC 寄存器地址跨机型不同 | 功能在其他机型失效 | 维护多机型 XML 配置，提供"机型适配"社区流程 |
| 自动更新文件覆盖失败 | 更新中断 | 更新前备份，提供回滚机制 |
| 键盘宏逆向不确定性 | 功能无法实现 | 先在 GPUSwitch 项目中命令行验证 |

---

## 九、开发环境与构建约束

- **目标框架**：.NET 8.0-windows（WinForms + Worker Service）
- **解决方案**：`MSIFlux.sln` 仅含 GUI + IPC 项目，其余通过 ProjectReference
- **构建命令**：`dotnet build MSIFlux.sln -c Release`
- **测试方式**：需要 MSI 硬件（EC 寄存器），无单元测试框架
- **发布**：`dotnet publish -c Release -r win-x64 --self-contained` 生成单文件 exe

**建议补充**：添加 `dotnet test` 项目，至少覆盖配置序列化/反序列化、版本比较、更新逻辑等不需要硬件的部分。
