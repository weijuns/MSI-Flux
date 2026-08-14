# MSI Flux 项目核心架构与历史调试记录 (规则与记忆)

## 📌 项目基本信息
- **项目名称**: MSI Flux (位于 `c:\Users\wjs\Desktop\YAMDCC\MSI Flux`)
- **适用机型示例**: 微星绝影 14 (Stealth 14, EC 固件版本 `14K1EMS1.108`) 及微星全系笔记本
- **最新发布版本**: `v1.6.1` (独立单文件 `MSI Flux.exe` 约 15MB)

---

## 🔑 核心硬件与驱动控制链路

### 1. Fn 键 LED 指示灯与原生 OSD
- **硬件依赖**: 物理 LED 硬件必须保持 `MSIService.exe` 在线维持驱动 Active 句柄。
- **WMI ACPI 配置**: 系统注册表 `HKLM\...\Services\WmiAcpi\MofImagePath` 指向 `%windir%\sysWOW64\msiapcfg.dll`，首次配置后需重启电脑使 `WmiAcpi.sys` 挂载 `MSI_ACPI` 类。
- **指示灯控制**:
  - **F5 (麦克风禁用灯)**: WMI Package `44`，Bit 1 (`0x02` 亮，`0x00` 灭)。使用系统级 `WM_APPCOMMAND` (`MIC_VOLUME_MUTE`) 切换状态。
  - **F1 (主音量静音灯)**: WMI Package `45`，Bit 1 (`0x02` 亮，`0x00` 灭)。
  - **状态追踪**: `HotkeyHook.cs` 中自维护 `_audioMutedState` 与 `_micMutedState` 变量，避免依赖 Release 模式下易失效的 CoreAudio COM 返回值。
- **OSD 实现 (`OsdToast.cs`)**:
  - 风格媲美原生，淡入淡出 Toast 动画（20ms 间隔，2秒消失）。
  - 扩展样式: `WS_EX_NOACTIVATE` (不抢焦点), `WS_EX_TRANSPARENT` (鼠标穿透), `WS_EX_TOOLWINDOW` (任务栏无图标)。
  - F6 (摄像头) OSD 实现: 放弃 CPU 轮询与不可靠的 WMI `MSI_Event`，采用 `RegisterDeviceNotification` 注册 `KSCATEGORY_VIDEO_CAMERA` 设备接口监听 (`NativeWindow` 子类 `CamDeviceWatcher`)。当收到 `WM_DEVICECHANGE` (`0x0219`) 的 `DBT_DEVICEARRIVAL` (`0x8000`) 弹“摄像头已启用”，`DBT_DEVICEREMOVECOMPLETE` (`0x8004`) 弹“摄像头已禁用”。纯事件驱动、0% CPU 占用、毫秒级原生系统通知！

---

## 🌡️ 风扇曲线控制与退档逻辑

- **退档锁死 Bug 根由与修复**:
  - 绝影 14 的 EC 硬件降温退档 (DownThreshold) 需要**绝对温度值**（如 46°C），而非相对温差（如 4°C）。
  - 全局配置 `OffsetDT = false`，禁止计算温差 offset，直接下发绝对退档温度，彻底解决降温时风扇狂转不退档问题。
- **通信路径**:
  - 优先通过 WMI ACPI Payload 写入：`Set_Temperature` (14)、`Set_Fan` (18)。**不再写 `Set_Thermal`** (官方 Feature Manager 只写前两者，软件层写入热偏移会导致退档偏置错误 → 风扇暴转 5000+ RPM)。
  - 若无 WMI 驱动则自动平滑降级至 Direct EC 改写，Direct EC 路径必须写 **DownThresholdRegs** (Down < Up 校验，越界回退 `UpThreshold - 4`)。
- **阶梯式默认风扇曲线矩阵**:
  - 同等温度下风量与转速大小严格保持：`省电` < `静音` < `平衡 (微星原厂)` < `增强 (极速)`。
  - **平衡模式原厂参数**:
    - CPU: `30°C/15%` ➔ `48°C/21%` ➔ `69°C/25%` ➔ `84°C/50%` ➔ `92°C/79%` ➔ `97°C/100%`
    - GPU: `38°C/10%` ➔ `65°C/18%` ➔ `78°C/29%` ➔ `88°C/40%` ➔ `93°C/75%` ➔ `98°C/90%`
  - 点击“默认”按钮仅重置当前选中的性能模式曲线 (`_currentPerfMode`)。
- **150% 物理极限**: 强冷 (Cooler Boost) 模式向 EC 写入十进制 `150` (`0x96`) 超频供电。

---

## 🎨 UI 拖拽锁与点位保护

- **拖拽锁机制 (`_isDraggingCurvePoint`)**:
  - 在鼠标左键处于拖拽状态时跳过 `HitTest` 二次碰撞检测，锁定当前拖动目标点。
  - 强制限制控制点 X/Y 轴不可越界重叠，维持严格单调递增性。

---

## 💡 物理硬件指示灯 (电源键白灯/黄灯)

- **白色 LED**: 集成显卡 (iGPU) 工作，独显处于 `D3Cold` 彻底断电休眠。
- **黄/橙色 LED**: 独立显卡 (dGPU) 通电唤醒 (`D0` 待机/工作状态)。
- **占用 0% 亮黄灯原理**: 监控软件 (如 Task Manager, MSI Flux, HWiNFO) 通过 NVAPI 轮询温度/转速时，轮询动作本身会触发独显 PCIe 通电 (D0)，此时功耗极低、3D 占用为 0%，但电源灯依然物理变黄。

---

## 📦 编译与发布流程

- **单 EXE 编译**: 先发布轻量主程序嵌入 Launcher，再对 Launcher 强剪裁发布为单文件 EXE。
- **GitHub Release 发布**: 需使用 Python API 脚本显式指定 `utf-8` 编码提交 UTF-8 格式 JSON Payload，避免 PowerShell GBK 编码导致的 Release Notes 乱码。
