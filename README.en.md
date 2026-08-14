# MSI Flux — Lightweight MSI Laptop Control Tool

**English** | **[中文](README.md)**

> A lightweight MSI laptop control tool,
> featuring **GPU triple-mode switching**, a modern UI, and bundled WMI ACPI bootstrap and minimal Feature Manager files.
>
> 🎯 **Single exe, no installation** — Just double-click to run. Runtime memory usage is only ~10MB, incredibly lightweight.
> Built-in .NET runtime detection: if .NET 8 is not installed, a dialog guides the user to download it.

---

## ✨ Key Features

- **🎮 GPU Triple-Mode Switching** — Supports Hybrid / Discrete / Eco (iGPU) GPU mode switching.
- **🌀 Fan Control** — Custom CPU/GPU fan speed curves, temperature threshold adjustment, Cooler Boost full-speed mode
- **🚀 Performance Modes** — Eco / Silent / Balanced / Turbo four-tier performance mode switching
- **🔋 Battery Charge Threshold** — 0-100% custom charge limit to preserve battery health
- **📊 Battery Level Display** — Real-time battery percentage and charging status
- **⚡ Auto Eco on Battery** — Automatically switch to Eco mode when unplugged, restore last AC mode when plugged back in
- **🔌 Power Plan Linking** — Automatically switch Windows power plans when changing performance modes (user-configurable GUIDs, falls back to Balanced if left empty)
- **💾 Config Import/Export** — Export current config to file, import with automatic model mismatch validation
- **📦 Single exe, out of the box** — Just double-click. Bundled minimal Feature Manager auto-extracts to `C:\ProgramData\MSI Flux\FeatureManager\`
- **🪶 Ultra lightweight** — Runtime memory usage only ~10MB, compared to MSI Center's ~950MB
- **🎨 Modern UI** — Clean interface design inspired by [G-Helper](https://github.com/seerge/g-helper)
- **🚪 Clean Exit** — Quit from tray auto-stops the background service (WinRing0 driver unload), no leftover processes

---

---

## 🔧 GPU Switching — Under the Hood

GPU mode switching is the most significant new feature in this project, fully reverse-engineered from MSI Center's switching sequence.

### Core Principle: No Feature Manager Installation Required (2026-05-02 Breakthrough)

By reverse-engineering the `wmiacpi.sys` driver loading mechanism, we discovered that only two conditions are needed for WMI ACPI methods to work:

```
msiapcfg.dll (16KB BMF-in-PE file, placed in C:\Windows\SysWOW64\)  ←  Bundled in the app, auto-copied on first run
MofImagePath registry value (pointing to the above dll)               ←  Auto-set on first run
```

**MSI Flux automatically completes the bootstrap on first GPU switch** (`WmiAcpiBootstrap.EnsureInstalled`). A single reboot is required after the first bootstrap to activate WMI bindings.

### Complete Switching Formula

```
EC registers 0xD1/0xBE  →  Write GPU mode bit + BIOS acknowledgment
+ Registry FW_GPU_CH / FW_CurrentNewGPU  →  MSI service cooperation (Discrete→Hybrid needs MSIAPService)
+ UEFI variable MsiDCVarData byte[5]  →  BIOS POST reads this for MUX routing
= Shutdown + power on (cold boot, S5→S0)  →  BIOS POST configures MUX, switch complete
```

### Switching Flow (10 Steps)

```
Step 0.   Disable Micro Star SCM service (conflict avoidance); set MSI Foundation Service to Manual
Step 0.5  WMI ACPI bootstrap: copy msiapcfg.dll + set MofImagePath (first time only, auto-skipped after)
Step 1.   Start MSI Foundation Service (MSIAPService.exe) on demand
Step 2.   Write registry: FW_GPU_CH=target, FW_CurrentNewGPU=cur (must differ to trigger switch)
Step 3.   Write UEFI variable MsiDCVarData byte[5] → True commit point read by BIOS POST (written early so BIOS sees it when processing EC commands)
Step 4.   Get_AP(0x00) → Read current ACPI state
Step 5.   Set_Data(0xD1, mod) → Write GPU mode persistent bit (mod = byte[1] & ~0x03 | 0x01)
Step 6.   Wait 3 seconds → BIOS processing (3s on first attempt, 2s on retries)
Step 7.   Get_AP(0x00) → Re-read, check byte[2] bit1 == 1 (BIOS acknowledgment)
Step 8.   Set_Data(0xBE, 0x02) → Send confirmation command, complete EC-level switch (always sent, never skipped)
Step 9.   Cleanup: Kill FM Service process + Stop MSIAPService (prevent 0xe0434352 shutdown crash)
```

> **Retry mechanism**: EC write sequence (Step 4-8) retries up to 3 times. Some transitions (e.g. Discrete→Hybrid) require the BIOS to receive the command multiple times before acknowledging.

> **Cold boot required**: After switching, you must **shutdown then power on** (S5→S0). Warm reboot (S4→S0) will NOT work.
> This is because the EC stays powered during reboot and BIOS skips MUX reconfiguration.

### Runtime Services (Automatically Managed)

| Component | Description |
|---|---|
| WMI ACPI Bootstrap | Bundled `msiapcfg.dll`, auto-copied to SysWOW64 + sets `MofImagePath` on first switch |
| MSI Foundation Service (MSIAPService.exe) | Windows service, auto-installed and started on first switch, auto-stopped after switch |
| Micro Star SCM | MSI Center main service; MSI Flux automatically disables it to avoid conflicts |
| Feature Manager | **Not required**; bundled minimal files, WMI repository auto-repaired with MOF schema if needed |

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

## 📅 Changelog

### v1.6.3 (2026-08-14)
- 🚀 **Branding cleanup** — All YAMDCC / Sparronator9999 references removed from code and docs (41 source file headers, README comparison section, DISCLAIMER), keeping only the acknowledgement at the end of the README.
- 📦 **Single-source versioning** — The version is now managed solely by `<Version>` in `MSIFlux.csproj`; the homepage and About dialog read the version dynamically from the assembly instead of hardcoding. Fixed exe file properties and About dialog showing `0.0.0.0` (now correctly shows v1.6.3).

### v1.6.1 (2026-08-14)
- 🚫 **Fixed UAC popup on double-click / auto-start at boot**: Root cause was the Settings page initializing the "Auto-start" checkbox state in its constructor — assigning `Checked` accidentally fired the `CheckedChanged` event → re-triggered `Startup.Schedule()` → every launch prompted a UAC elevation. The initial assignment is now moved *before* event subscription so startup is completely prompt-free.
- 🌀 **Fan overdrive fix (WMI path)**: Following the official Feature Manager, the WMI ACPI path now **skips `Set_Thermal`** (only `Set_Temperature` + `Set_Fan`). Previously the software-written thermal offset was misinterpreted by the BIOS as an absolute temperature, causing 5000+ RPM fan overdrive at 53°C. The Direct EC path re-adds **DownThresholdRegs** writes with a safety check (`Down < Up`, fallback `UpThreshold - 4`) to prevent inverted hysteresis triggering hardware thermal shutdown.
- 🛡️ **Official MSI service coexistence guard**: When MSI Center / Feature Manager services are detected running, EC hotkey polling is skipped — `EC[0xC1]` is no longer force-written every 400 ms (only enabled once on first run or if bit7 is unexpectedly cleared), and the EC hardware sync timer was relaxed from 500 ms to **2000 ms**, avoiding register write conflicts that caused forced system power-off.
- ⚙️ **One-click auto-start toggle via elevation**: The "Auto-start" switch now performs both Task Scheduler (`RunLevel=LUA`) registration and service start type (auto/manual) switching through a single elevated child process, without exiting the app; the checkbox rolls back if UAC is cancelled.
- 🔧 **More tolerant service startup**: Service start timeout raised 10s → 15s plus an additional 15s grace period; on failure the app **no longer prompts UAC** and continues running in degraded mode.
- 📷 **F6 camera OSD stability**: Restored `CamGuid` device-GUID filtering, removed `DEVICE_NOTIFY_ALL_INTERFACE_CLASSES` and the redundant dedup counters, fixing occasional missing/duplicate toasts.

### v1.6.0 (2026-08-07)
- 🛡️ **Service SDDL Authorization & Seamless Double-click Self-healing**: Resolved the issue where double-clicking under standard user rights prompted "Service Not Running". Granted full `CCLCSWRPWPDTLOCRRC` start and control permissions to `Authenticated Users` (`AU`) via `sc.exe sdset` with quotes validation and retry routines.
- 💡 **Physical Key LED Direct EC Fallback**: Refactored F1 (Audio Mute) and F5 (Mic Mute) LED controls. In addition to `MSI_ACPI` / `MSI_ACPI2` WMI polling, introduced direct bit manipulation on EC physical registers `0x2C` (Mic) and `0x2D` (Audio) so physical LEDs light up cleanly even without official WMI drivers.
- 📷 **Zero-CPU Native F6 Camera OSD**: Replaced CPU polling and unreliable WMI `MSI_Event` with Windows kernel-level device interface notification (`RegisterDeviceNotification` + `WM_DEVICECHANGE` for `KSCATEGORY_VIDEO_CAMERA`). Toggling the physical camera triggers instant OS device notifications, powering 0% CPU Toast animations.
- ⚙️ **Auto Directory Initialization & Graceful Degradation**: Auto-creates `C:\ProgramData\MSI Flux\Config` on early startup to ensure zero-crash clean installs on fresh machines; degraded `WinRing0` driver loading errors to soft warnings that gracefully fall back to WMI ACPI mode if blocked by antivirus software.

### v1.5.0 (2026-08-06)
- 🌀 **Refactored Fan Temp Cooldown & Dropdown Mechanism**: Fixed the hardware cooling bug on Stealth 14 / newer MSI models where fans wouldn't drop speeds when temps cooled down. Defaulted `<OffsetDT>` to `false` (meaning absolute temperature registers), locking the WMI and Direct EC write payloads to absolute temperatures.
- ⚖️ **Multi-Profile Fan Speed Gradient**: Re-designed default curve curves for Battery, Silent, and Extreme modes. Wind noise levels are now strictly categorized in a monotonic order: Battery < Silent < Balanced (MSI Factory) < Extreme.
- 🔒 **Interactive Coordinate Drag Lock**: Implemented an Active Drag Target lock inside the fan curve chart to prevent mouse moves from accidentally switching active drag targets due to HitTest crossovers. Added adjacent bounds checks to both X and Y axes to enforce absolute monotonicity and prevent point overlapping.
- 🎯 **Fine-grained Reset Control**: Rewrote the "Default" reset button behavior to only restore factory defaults for the currently active performance mode profile, preserving custom configurations in all other modes.
- 📦 **Version Bump**: Bumped version to v1.5.0 across the projects and packed a standalone ~20MB trimmed self-contained executable.

---

## 📥 Download & Installation

### Prerequisites

- Windows 10/11 (64-bit)
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows x64)
- MSI laptop
- First use requires one GPU switch (auto-completes WMI ACPI bootstrap), then a reboot to activate
- Default config file: `MSI-10th-gen-or-newer-dualfan.xml` (for 10th-gen and newer dual-fan models)

> Just double-click `MSI Flux.exe` to run. If .NET 8 is not installed, a dialog will guide you to the download page.

### Build

```powershell
# Build framework-dependent
dotnet publish MSIFlux/MSIFlux.csproj -c Release -r win-x64 --self-contained false -o publish

# Build launcher with .NET runtime detection (embeds main exe, self-contained)
dotnet publish Launcher/Launcher.csproj -c Release -o publish-launcher
```

Final artifact: `publish-launcher/MSI Flux.exe` (~21MB), double-click to run.

---

## 🙏 Acknowledgements

- **[YAMDCC](https://codeberg.org/Sparronator9999/YAMDCC)** by Sparronator9999 — The foundation of this project, providing core fan control, performance modes, battery threshold and other features
- **[G-Helper](https://github.com/seerge/g-helper)** by seerge — UI design inspiration, a clean and elegant ASUS laptop control tool
- **[MSI GPUSwitch](https://github.com/weijuns/MSI-GPUSwitch)** — GPU switching reverse engineering project, providing the complete WMI ACPI switching interface

---

## ⚠️ Disclaimer

- This program requires low-level hardware access to apply settings. While no issues should arise, **the author shall not be held responsible for any hardware damage caused by using this tool**.
- If you do something silly like turn off all your fans while running under full load, **the author will not be held responsible for any damage you cause to your own hardware**.
- This project, repository and its authors are not affiliated with Micro-Star International Co., Ltd. in any way.
- The `FeatureManager/` directory contains binaries that are property of Micro-Star International (MSI). They are included solely for interoperability and research purposes. No ownership or license is claimed.

See [DISCLAIMER](DISCLAIMER) for details.

---

## 📄 License

This project is licensed under the [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html).

Copyright © 2026 weijuns
