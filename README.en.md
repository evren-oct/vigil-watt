# VigilWatt

English | [简体中文](README.md)

> A quick view of wireless device batteries on Windows: Bluetooth devices, supported 2.4 GHz peripherals, and an optional system battery in the tray and history.

![Platform](https://img.shields.io/badge/Windows-10%2F11-0078d4) ![.NET](https://img.shields.io/badge/.NET-8-512bd4) ![License](https://img.shields.io/badge/license-MIT-green) [![Release](https://img.shields.io/github/v/release/evren-oct/vigil-watt?label=release&color=blue)](https://github.com/evren-oct/vigil-watt/releases)

## Screenshots

Device list and battery levels:

![Device battery levels](assets/screenshot-devices.png)

Statistics, history, and settings:

<p align="center">
  <img src="assets/screenshot-stats.png" width="31%" alt="History statistics">
  <img src="assets/screenshot-history.png" width="31%" alt="History management">
  <img src="assets/screenshot-settings.png" width="31%" alt="Settings">
</p>
<p align="center"><sub>History statistics · History management · Settings</sub></p>

Desktop widget:

<p align="center"><img src="assets/screenshot-widget.png" width="480" alt="Desktop widget"></p>

Taskbar battery pill:

<p align="center"><img src="assets/screenshot-taskbar.png" width="480" alt="Taskbar battery pill"></p>

## Features

- Reads BLE GATT battery services, Classic Bluetooth HFP gateway battery properties, and supported 2.4 GHz device protocols.
- Shows the lowest battery level of online devices in the tray and alerts below a configurable threshold (20% by default).
- Optional desktop widget, taskbar pill, and system battery monitoring; the system battery is excluded from the tray's lowest level.
- Stores battery history with 24-hour, 7-day, and 30-day statistics; records can be managed by device.
- Can launch at startup.

**Device plugin switches:** Query-based brands are off by default (ASUS, MCHOSE, Logitech, Razer, Sony INZONE, ATK, CHERRY, etc.); strictly passive plugins are on. Enable brands in Settings.

## Device diagnostics: capture evidence for unsupported devices

If a device shows no battery level, you do not need packet-capture skills to get it supported — the fifth item in the main window, **Device diagnostics**, collects a raw evidence package you can attach to an issue.

1. **Pick a task.** "Unknown device investigation (find a new protocol)" lists every HID device, unsupported ones first; "Supported device diagnostics (troubleshoot readings)" lists only devices a plugin is claiming. Devices are grouped by physical device, so one receiver is not flattened into ten interface rows.
2. **Pick an interface.** Each interface is labelled with whether it has an input channel, and for supported devices the one the plugin actually claims is recommended. Selecting an interface runs the capability pre-check automatically; the right column only lists items that need your attention.
3. **Collect step by step.** Instead of an empty log window you get a checklist — operate the device → plug in the charger → unplug the charger → power off → power on. Each step says what to do and shows how many reports arrived so far, so a silent step is obvious immediately.
4. **Export.** Stopping produces an offline `report.html` plus `vigilwatt-capture.zip` that you can open, save, or attach to an issue. Everything stays local: nothing is uploaded and no account is needed.

The capture handle is opened read-only and **never writes to the device**; there is no USB bus-level capture, no over-the-air Bluetooth sniffing, and no interpretation of what the bytes mean. A package may contain the device serial number, so check the bundled `README.txt` before sharing it.

Three common pitfalls: picking the wrong interface is the usual reason for "nothing captured" (on one receiver a 513-byte buffered channel may stay silent while the 19-byte one works); zero records does not mean there is no protocol (some devices only push after a session is opened); and a channel that never changes a single byte is usually a configuration channel, not a status channel.

![Device diagnostics: pick a task, pick an interface, collect step by step, export a report](assets/screenshot-capture.png)

Full workflow, file format, and known limits: [docs/project/capture.md](docs/project/capture.md).

## Download and quick start

Download from [GitHub Releases](https://github.com/evren-oct/vigil-watt/releases); the [Gitee Releases](https://gitee.com/YGWG-LX/vigil-watt/releases) page is a mirror backup:

- `VigilWattApp-win-Setup.exe`: per-user installer with Velopack auto-updates.
- `VigilWattApp-win-Portable.zip`: portable build; no installation required.

The packages are self-contained and **do not require a separate .NET runtime installation**. Generated installers in `dist/` are not included in the repository.

1. Launch the app and find the VigilWatt icon in the notification area; it may initially be under the `^` overflow menu.
2. Left-click the icon to open the main window; right-click for the tray menu.
3. To monitor a laptop battery, enable **Monitor system battery** in Settings.

## Device support

Only devices verified on the maintainer's machine are listed here. The full matrix (contributor-verified, implemented but unverified, unsupported) is in **[docs/hardware/support-matrix.md](docs/hardware/support-matrix.md)**.

| Device | Status |
|---|---|
| MCHOSE G87 · 2.4 GHz `0x13` | Verified on the maintainer's device |
| Logitech Bolt (C548 + MX Master 3S) · HID++ | Verified on the maintainer's device |
| Logitech PRO X 2 LIGHTSPEED | Verified on the maintainer's device (USB wired untested) |
| Rapoo VT3 Gen 2 · passive `status-report7` | Verified on the maintainer's device |
| Betop T9 · passive stream | Verified on the maintainer's device |
| Inphic ZHEN / IN9 | Verified on the maintainer's device |
| HECATE G3M Pro upgraded | Verified on the maintainer's device (charging bit untested) |
| ATK F1 Pro Max | Verified on the maintainer's device |
| EDIFIER W820NB · HFP gateway | Verified on the maintainer's device |
| System battery | Verified on the maintainer's device (optional) |

The 弱水时砂炽天使 2.4 GHz device (`38FF:0001`) has no battery reporting channel, confirmed through host-side inspection and RoseLink analysis. Other models depend on whether their protocols have been implemented.

Rows marked "contributor-verified" in the full matrix come from community captures and on-device verification. See **[CONTRIBUTORS.md](CONTRIBUTORS.md)** for who contributed what.

## Build

Requires the .NET 8 SDK. Build on Windows x64 from the solution:

```powershell
dotnet build VigilWatt.sln -c Debug
dotnet publish src/VigilWatt.App -c Release -p:Platform=x64 -o publish
```

See [docs/dev/build.md](docs/dev/build.md) for build, test, and publishing instructions.

## Project structure

```text
VigilWatt.sln
src/
  VigilWatt.Core/       # Device discovery, BLE/HID, protocol plugins, history, and logging
  VigilWatt.App/        # WinUI 3 UI, tray, desktop widget, and taskbar pill
  tests/
    VigilWatt.Core.Tests/       # xUnit unit tests
    VigilWatt.BleScan/          # BLE diagnostic utility
    VigilWatt.ProtocolCapture/  # Protocol capture utility
assets/                 # README screenshots and app assets
docs/                   # Project, development, hardware, and review documentation
scripts/                # Build, packaging, and temporary debugging scripts
```

## Documentation

| Topic | Guide |
|---|---|
| Documentation index and symptom lookup | [docs/README.md](docs/README.md) |
| Protocol references, implementation rationale, and validation status | [docs/hardware/sources.md](docs/hardware/sources.md) |
| Protocol frames, device compatibility, and disproven approaches | [docs/hardware/protocols.md](docs/hardware/protocols.md) |
| Hardware read/write safety rules | [docs/hardware/safety.md](docs/hardware/safety.md) |
| Architecture and module responsibilities | [docs/project/architecture.md](docs/project/architecture.md) |
| Build and test notes | [docs/dev/build.md](docs/dev/build.md) |

Detailed project documentation is primarily in Chinese.

## Repository and license

[GitHub repository](https://github.com/evren-oct/vigil-watt) · [Gitee mirror](https://gitee.com/YGWG-LX/vigil-watt) · [MIT License](LICENSE) © 2026 evren
