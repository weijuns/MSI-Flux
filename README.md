# MSI Flux — 轻量级 MSI 笔记本控制工具

**[English](README.en.md)** | **中文**

> 基于 [YAMDCC](https://codeberg.org/Sparronator9999/YAMDCC) 深度定制的 MSI 笔记本控制工具,
> 新增 **GPU 三模式切换**、现代化 UI, 并内置 WMI ACPI 引导器和 Feature Manager 精简文件。
>
> 🎯 **单体 exe, 无需安装** — 双击即用, 实测运行时后台内存仅占用 ~10MB, 非常清爽。

---

## ✨ 核心特色

- **🎮 GPU 三模式切换** — 支持 Hybrid (混合输出) / Discrete (独显直连) / Eco (核显模式) 三种 GPU 模式切换, **这是 YAMDCC 原版未实现的功能** (原版 Roadmap 中标注为 "Research stalled")
- **🌀 风扇控制** — 自定义 CPU/GPU 风扇转速曲线, 温度阈值调节, Cooler Boost 全速模式
- **🚀 性能模式** — 省电 / 安静 / 均衡 / 加速 四档性能模式切换
- **🔋 电池充电阈值** — 0-100% 自定义充电上限, 保护电池健康
- **📊 电池电量显示** — 实时显示电池电量百分比和充电状态
- **⚡ 拔电自动省电** — 拔掉电源自动切换省电模式, 插电后恢复上次的性能模式
- **🔌 电源计划联动** — 切换性能模式时自动切换 Windows 电源计划 (支持用户自定义 GUID)
- **💾 配置导入导出** — 支持导出当前配置为文件, 导入时自动校验机型匹配
- **📦 单体 exe, 开箱即用** — 双击即用; 内置精简版 Feature Manager 自动提取到 `C:\ProgramData\MSI Flux\FeatureManager\`
- **🪶 极致轻量** — 实测运行时后台内存仅占用 ~10MB, 对比 MSI Center ~950MB
- **🎨 现代化 UI** — 参考 [G-Helper](https://github.com/seerge/g-helper) 设计的简洁界面

---

## 🆚 与原版 YAMDCC 的对比

| 功能 | YAMDCC 原版 | MSI Flux |
|---|---|---|
| GPU 模式切换 | ❌ 研究停滞 (Roadmap v2.1) | ✔ 三模式 (Hybrid/Discrete/Eco) |
| GPU 切换底层 | 无 | WMI ACPI + 注册表 + UEFI 变量 + 自动服务管理 |
| 风扇控制 | ✔ | ✔ |
| 温度阈值控制 | ✔ | ✔ |
| 性能模式 | ✔ | ✔ |
| 电池充电阈值 | ✔ | ✔ |
| 电池电量显示 | ❌ | ✔ (SystemInformation.PowerStatus) |
| 拔电自动省电 | ❌ | ✔ (拔电切 Eco, 插电恢复上次模式) |
| 电源计划联动 | ❌ | ✔ (GUI 填写 GUID, 自动联动) |
| 配置导入导出 | ❌ | ✔ (含机型匹配校验) |
| Win/Fn 键互换 | ✔ | ✔ |
| 系统托盘最小化 | ❌ | ✔ |
| 开机自启动 | ❌ | ✔ (Task Scheduler) |
| UI 界面 | WinForms 传统界面 | 现代化卡片式 UI (参考 G-Helper) |
| 目标框架 | .NET Framework 4.8 | .NET 8.0-windows |

---

## 🔧 GPU 切换底层接口

GPU 模式切换是本项目最核心的新增功能, 其实现完全基于逆向工程 MSI Center 的切换流程。

### 核心原理: 完全不依赖 Feature Manager 安装 (2026-05-02 突破)

通过逆向 `wmiacpi.sys` 驱动的加载机制, 发现只需两个条件即可让 WMI ACPI 方法工作:

```
msiapcfg.dll (16KB 的 BMF-in-PE 文件, 放到 C:\Windows\SysWOW64\)  ←  内置在程序中, 首次运行自动提取并复制
MofImagePath 注册表值 (指向上述 dll)                                  ←  首次运行自动设置
```

**MSI Flux 首次 GPU 切换时自动完成引导** (`WmiAcpiBootstrap.EnsureInstalled`), 之后 WMI ACPI 调用即可正常工作。首次引导后需重启一次以激活 WMI 绑定。

### 完整切换公式

```
EC 寄存器 0xD1/0xBE  →  写入 GPU 模式位 + BIOS 确认
+ 注册表 FW_GPU_CH / FW_CurrentNewGPU  →  MSI 服务协作 (Discrete→Hybrid 需要 MSIAPService)
+ UEFI 变量 MsiDCVarData byte[5]  →  BIOS POST 读取的真正 MUX 路由
= 关机再开机 (冷启动, S5→S0)  →  BIOS POST 配置 MUX, GPU 切换完成
```

### 切换流程 (10 步)

```
Step 0.   禁用 Micro Star SCM 服务 (避免冲突); MSI Foundation Service 设为手动启动
Step 0.5  WMI ACPI 引导: 复制 msiapcfg.dll + 设置 MofImagePath (首次, 之后自动跳过)
Step 1.   按需启动 MSI Foundation Service (MSIAPService.exe)
Step 2.   写注册表: FW_GPU_CH=target, FW_CurrentNewGPU=cur (必须不同才能触发)
Step 3.   Get_AP(0x00) → 读取当前 ACPI 状态
Step 4.   Set_Data(0xD1, mod) → 写入 GPU 模式持久化位 (mod = byte[1] & ~0x03 | 0x01)
Step 5.   等待 2 秒 → BIOS 处理
Step 6.   Get_AP(0x00) → 重新读取, 检查 byte[2] bit1 是否为 1 (BIOS 确认)
Step 7.   Set_Data(0xBE, 0x02) → 发送确认命令, 完成 EC 层切换
Step 8.   写 UEFI 变量 MsiDCVarData byte[5] → BIOS POST 读取的真正提交点
Step 9.   清理: Kill FM Service 进程 + Stop MSIAPService (防止关机 0xe0434352 崩溃)
```

> **必须冷启动**: 切换完成后需 **关机再开机** (S5→S0), 热重启 (S4→S0) 无效。
> 因为 EC 在重启期间不断电, BIOS 跳过 MUX 重配置。

### 依赖服务 (运行时自动管理)

| 组件 | 说明 |
|---|---|
| WMI ACPI 引导器 | 内置 `msiapcfg.dll`, 首次切换时自动复制到 SysWOW64 + 设置 `MofImagePath` |
| MSI Foundation Service (MSIAPService.exe) | Windows 服务, 首次切换时自动安装并启动, 切换完成后自动停止 |
| Micro Star SCM | MSI Center 主服务, MSI Flux 会自动禁用此服务以避免冲突 |
| Feature Manager | **无需安装**; 内置精简版文件, WMI 仓库损坏时自动用 MOF schema 修复 |

> 详细的逆向工程过程和接口文档, 请参考 [MSI GPUSwitch](https://github.com/weijuns/MSI-GPUSwitch) 项目。

---

## 🌀 风扇控制底层接口

风扇控制基于 EC (Embedded Controller) 寄存器直接读写, 通过 WinRing0 内核驱动访问:

- **EC 访问方式**: ACPI 规范 EC 命令 (RD_EC=0x80 / WR_EC=0x81), 经 WinRing0 驱动下发
- **驱动**: WinRing0x64.sys (内核级驱动, 需管理员权限)

### 风扇曲线写入

每个风扇 (CPU/GPU) 独立配置, 写入 7 个温度点的风扇转速和温度阈值:

```
FanCurveRegs[0..6]    — 7 个温度点对应的风扇转速 (0-150)
UpThresholdRegs[0..5] — 升温阈值: 风扇加速的温度触发点
DownThresholdRegs[0..5] — 降温阈值: 风扇减速的温度触发点
SpeedReadReg          — 当前风扇转速读取寄存器
TempReadReg           — 当前温度读取寄存器
RPM ReadReg           — 风扇 RPM 读取寄存器
```

### 风扇模式

通过 `FanModeConf` 寄存器写入控制, 不同 EC 值对应不同模式:

| 模式 | EC 值 | 说明 |
|---|---|---|
| 自动 (Automatic) | 0x0D (13) | 使用 BIOS 默认风扇策略 |
| 安静 (Silent) | 0x1D (29) | 限制最大风扇转速 |
| 基本 (Basic) | 0x4D (77) | 单偏移量调整默认曲线 |
| 高级 (Advanced) | 0x8D (141) | 完全自定义温度-转速曲线 |

### 配置文件

每个机型独立 XML 配置, 定义寄存器地址和默认风扇曲线, 位于 `Configs/` 目录。

> 本软件默认使用 `MSI-10th-gen-or-newer-dualfan.xml` 配置文件, 适用于 10 代及更新的双风扇 MSI 笔记本。

---

## 🚀 性能模式底层接口

性能模式通过 EC 寄存器写入实现, 寄存器地址和值由机型 XML 配置定义:

```xml
<PerfModeConf>
    <Reg>210</Reg>  <!-- EC 寄存器地址 -->
    <PerfModes>
        <PerfMode Name="Eco" Value="194" />     <!-- 0xC2 -->
        <PerfMode Name="Silent" Value="193" />   <!-- 0xC1 -->
        <PerfMode Name="Balanced" Value="192" />  <!-- 0xC0 -->
        <PerfMode Name="Turbo" Value="196" />     <!-- 0xC4 -->
    </PerfModes>
</PerfModeConf>
```

| 模式 | EC 值 (示例) | Windows 电源计划 |
|---|---|---|
| 省电 (Eco) | 0xC2 | 最佳能效 |
| 安静 (Silent) | 0xC1 | 节能 |
| 均衡 (Balanced) | 0xC0 | 均衡 |
| 加速 (Turbo) | 0xC4 | 最佳性能 |

> 不同机型的 EC 寄存器地址和值可能不同, 具体以 `Configs/` 下的 XML 配置为准。

写入流程: `EC.WriteByte(Reg, Value)` → WinRing0 驱动 → EC 硬件 → BIOS 生效

---

## 📥 下载与安装

### 前置要求

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- MSI 笔记本
- 首次使用需运行一次 GPU 切换 (自动完成 WMI ACPI 引导), 然后重启一次以激活
- 默认配置文件: `MSI-10th-gen-or-newer-dualfan.xml` (适用于 10 代及更新双风扇机型)

### 构建

```powershell
dotnet build YAMDCC.sln -c Release
```

### 安装服务

```powershell
# 以管理员身份运行
sc.exe create yamdccsvc binPath= "<构建输出路径>\yamdccsvc.exe" start= auto DisplayName= "YAMDCC Service"
sc.exe start yamdccsvc
```

然后启动 `MSI Flux.exe` 即可使用。

---

## 🙏 致谢

- **[YAMDCC](https://codeberg.org/Sparronator9999/YAMDCC)** by Sparronator9999 — 本项目的底层基础, 提供了风扇控制、性能模式、电池阈值等核心功能
- **[G-Helper](https://github.com/seerge/g-helper)** by seerge — UI 设计灵感来源, 简洁优雅的 ASUS 笔记本控制工具
- **[MSI GPUSwitch](https://github.com/weijuns/MSI-GPUSwitch)** — GPU 切换逆向工程成果, 提供了完整的 WMI ACPI 切换接口

---

## ⚠️ 免责声明

- 本工具需要底层硬件访问权限来应用设置。虽然不应出现问题, 但 **作者不对因使用本工具导致的任何硬件损坏负责**。
- 如果你做了愚蠢的操作 (比如满载时关闭所有风扇), **作者不对你造成的任何硬件损坏负责**。
- 本项目、仓库及其作者与 Micro-Star International Co., Ltd. 没有任何形式的关联。
- `FeatureManager/` 目录中包含的文件为 Micro-Star International (MSI) 所有, 仅出于互操作性和研究目的包含于此, 不主张任何所有权或许可。

详见 [DISCLAIMER](DISCLAIMER)。

---

## 📄 许可证

本项目基于 [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html) 开源, 继承自 YAMDCC 原项目的 GPL-3.0 许可证。

Copyright © 2023-2026 Sparronator9999 (YAMDCC 原作者)
Copyright © 2026 weijuns (MSI Flux 改进部分)
