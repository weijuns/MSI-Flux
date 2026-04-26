# MSI Flux — Lightweight MSI Laptop Control Tool

**English** | **[中文](README.md)**

> A deeply customized MSI laptop control tool based on [YAMDCC](https://codeberg.org/Sparronator9999/YAMDCC),
> featuring **GPU triple-mode switching**, a modern UI, and bundled MSI Feature Manager service dependencies.

---

## ✨ Key Features

- **🎮 GPU Triple-Mode Switching** — Supports Hybrid / Discrete / Eco (iGPU) GPU mode switching. **This feature was never implemented in the original YAMDCC** (marked as "Research stalled" in their Roadmap).
- **🌀 Fan Control** — Custom CPU/GPU fan speed curves, temperature threshold adjustment, Cooler Boost full-speed mode
- **🚀 Performance Modes** — Eco / Silent / Balanced / Turbo four-tier performance mode switching
- **🔋 Battery Charge Threshold** — 0-100% custom charge limit to preserve battery health
- **📦 Out of the Box** — Bundled minimal Feature Manager service (only 4 files, ~2.8MB), no MSI Center installation required
- **🎨 Modern UI** — Clean interface design inspired by [G-Helper](https://github.com/seerge/g-helper)

---

## 🆚 Comparison with Original YAMDCC

| Feature | YAMDCC Original | MSI Flux |
|---|---|---|
| GPU Mode Switching | ❌ Research stalled (Roadmap v2.1) | ✔ Triple-mode (Hybrid/Discrete/Eco) |
| GPU Switch Backend | None | WMI ACPI Set_Data + Registry + MSI Services |
| Fan Control | ✔ | ✔ |
| Temperature Threshold Control | ✔ | ✔ |
| Performance Modes | ✔ | ✔ |
| Battery Charge Threshold | ✔ | ✔ |
| Win/Fn Key Swap | ✔ | ✔ |
| UI Interface | WinForms traditional UI | Modern card-style UI (inspired by G-Helper) |
| Target Framework | .NET Framework 4.8 | .NET 8.0-windows |

---

## 🔧 GPU Switching — Under the Hood

GPU mode switching is the most significant new feature in this project, fully reverse-engineered from MSI Center's switching sequence:

### Registry Operations

```
Path: HKLM\SOFTWARE\WOW6432Node\MSI\Feature Manager\Component\Base Module\User Scenario

FW_GPU_CH        — Target GPU mode (0=Hybrid, 1=Discrete, 2=Eco)
FW_CurrentNewGPU — Must differ from FW_GPU_CH to trigger the switch
FW_SupportNewGPU — Whether discrete-only mode is supported
FW_SupportUMA    — Whether Eco/iGPU mode is supported
FW_SupportDiscrete — Whether discrete GPU is supported
```

### WMI ACPI Call Sequence

```
1. Get_AP(0x00)           → Read current ACPI state
2. Set_Data(0xD1, mod)    → Write GPU mode persistent bit (mod = byte[1] & ~0x03 | 0x01)
3. Wait 2 seconds          → BIOS processing
4. Get_AP(0x00)           → Re-read, check byte[2] bit1 == 1 (BIOS acknowledgment)
5. Set_Data(0xBE, 0x02)   → Send confirmation command, complete the switch
```

### Required Services

GPU switching requires two MSI background services:

| Service | Description |
|---|---|
| MSI Foundation Service (MSIAPService.exe) | Windows service, must be installed and started first |
| Feature Manager Service.exe | Depends on MSIAPService, exits immediately if run alone |

MSI Flux bundles a minimal FeatureManager directory (only 4 essential files). On first GPU switch, it automatically installs MSI Foundation Service via `InstallUtil`.

> For detailed reverse engineering process and API documentation, see the [MSI GPUSwitch](https://github.com/weijuns/MSI-GPUSwitch) project.

---

## 🌀 Fan Control — Under the Hood

Fan control is based on direct EC (Embedded Controller) register read/write via the WinRing0 kernel driver:

- **EC Access Method**: ACPI spec EC commands (RD_EC=0x80 / WR_EC=0x81), dispatched through WinRing0 driver
- **Driver**: WinRing0x64.sys (kernel-level driver, requires administrator privileges)

### Fan Curve Writing

Each fan (CPU/GPU) is independently configured with 7 temperature points for fan speed and thresholds:

```
FanCurveRegs[0..6]    — Fan speed for 7 temperature points (0-150)
UpThresholdRegs[0..5] — Up-shift thresholds: temperature triggers for fan speed increase
DownThresholdRegs[0..5] — Down-shift thresholds: temperature triggers for fan speed decrease
SpeedReadReg          — Current fan speed read register
TempReadReg           — Current temperature read register
RPM ReadReg           — Fan RPM read register
```

### Fan Modes

Controlled via `FanModeConf` register writes, different EC values correspond to different modes:

| Mode | EC Value | Description |
|---|---|---|
| Automatic | 0x0D (13) | Uses BIOS default fan strategy |
| Silent | 0x1D (29) | Limits maximum fan speed |
| Basic | 0x4D (77) | Single offset adjustment to default curve |
| Advanced | 0x8D (141) | Fully custom temperature-speed curve |

### Configuration Files

Per-laptop XML configs defining register addresses and default fan curves, located in `Configs/` directory.

> This software uses `MSI-10th-gen-or-newer-dualfan.xml` as the default config, suitable for 10th-gen and newer dual-fan MSI laptops.

---

## 🚀 Performance Modes — Under the Hood

Performance modes are set via EC register writes, with register addresses and values defined by per-laptop XML configs:

```xml
<PerfModeConf>
    <Reg>210</Reg>  <!-- EC register address -->
    <PerfModes>
        <PerfMode Name="Eco" Value="194" />     <!-- 0xC2 -->
        <PerfMode Name="Silent" Value="193" />   <!-- 0xC1 -->
        <PerfMode Name="Balanced" Value="192" />  <!-- 0xC0 -->
        <PerfMode Name="Turbo" Value="196" />     <!-- 0xC4 -->
    </PerfModes>
</PerfModeConf>
```

| Mode | EC Value (example) | Windows Power Plan |
|---|---|---|
| Eco | 0xC2 | Best power efficiency |
| Silent | 0xC1 | Power saver |
| Balanced | 0xC0 | Balanced |
| Turbo | 0xC4 | Best performance |

> EC register addresses and values may differ across laptop models. See XML configs in `Configs/` for specifics.

Write flow: `EC.WriteByte(Reg, Value)` → WinRing0 driver → EC hardware → BIOS takes effect

---

## 📥 Download & Installation

### Prerequisites

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- MSI laptop
- Default config file: `MSI-10th-gen-or-newer-dualfan.xml` (for 10th-gen and newer dual-fan models)

### Build

```powershell
dotnet build YAMDCC.sln -c Release
```

### Install Service

```powershell
# Run as Administrator
sc.exe create yamdccsvc binPath= "<build output path>\yamdccsvc.exe" start= auto DisplayName= "YAMDCC Service"
sc.exe start yamdccsvc
```

Then launch `MSI Flux.exe` to start using the application.

---

## 🙏 Acknowledgements

- **[YAMDCC](https://codeberg.org/Sparronator9999/YAMDCC)** by Sparronator9999 — The foundation of this project, providing core fan control, performance modes, battery threshold and other features
- **[G-Helper](https://github.com/seerge/g-helper)** by seerge — UI design inspiration, a clean and elegant ASUS laptop control tool
- **[MSI GPUSwitch](https://github.com/weijuns/MSI-GPUSwitch)** — GPU switching reverse engineering成果, providing the complete WMI ACPI switching interface

---

## ⚠️ Disclaimer

- This program requires low-level hardware access to apply settings. While no issues should arise, **the author shall not be held responsible for any hardware damage caused by using this tool**.
- If you do something silly like turn off all your fans while running under full load, **the author will not be held responsible for any damage you cause to your own hardware**.
- This project, repository and its authors are not affiliated with Micro-Star International Co., Ltd. in any way.
- The `FeatureManager/` directory contains binaries that are property of Micro-Star International (MSI). They are included solely for interoperability and research purposes. No ownership or license is claimed.

See [DISCLAIMER](DISCLAIMER) for details.

---

## 📄 License

This project is licensed under the [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html), inherited from the original YAMDCC project's GPL-3.0 license.

Copyright © 2023-2026 Sparronator9999 (YAMDCC original author)
Copyright © 2026 weijuns (MSI Flux improvements)
