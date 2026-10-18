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
- Delete offline entries together with their name/icon and history, or show online devices only; merge history between same-source 2.4 GHz entries and remove the duplicate.
- Can launch at startup.

**Device plugin switches:** MCHOSE, AULA and Logitech plugins are off by default because reading requires sending query reports. Enable them by brand in Settings. Other strictly passive plugins do not write to devices.

## Download and quick start

Download from [GitHub Releases](https://github.com/evren-oct/vigil-watt/releases) or the [Gitee mirror](https://gitee.com/YGWG-LX/vigil-watt/releases):

- `VigilWattApp-win-Setup.exe`: per-user installer with Velopack auto-updates.
- `VigilWattApp-win-Portable.zip`: portable build; no installation required.

The packages are self-contained and **do not require a separate .NET runtime installation**. Generated installers in `dist/` are not included in the repository.

1. Launch the app and find the VigilWatt icon in the notification area; it may initially be under the `^` overflow menu.
2. Left-click the icon to open the main window; right-click for the tray menu.
3. To monitor a laptop battery, enable **Monitor system battery** in Settings.

## Device support

The table distinguishes devices verified on hardware from protocols implemented but still awaiting a matching device for validation:

| Device / protocol | Status |
|---|---|
| MCHOSE G87 · 2.4 GHz `0x13` | Verified on the developer's device |
| New-generation MCHOSE dongles · QHW (e.g. `3837:3033`) | Implemented; awaiting matching hardware validation |
| Betop T9 · passive vendor-interface stream | Verified on hardware |
| Logitech Bolt receiver (C548 + MX Master 3S) · HID++ | Verified on hardware |
| Rapoo VT3 Gen 2 · passive `status-report7` | Verified on the developer's device |
| Inphic ZHEN · passive report `0x03` | Verified on hardware |
| AULA F99 Pro · `0x13` session/push | Verified on hardware (plugin off by default) |
| DELUX M900 Pro · passive 5-byte status frame | Verified on hardware |
| Classic Bluetooth · HFP gateway device-node property | EDIFIER W820NB verified |
| System battery · `GetSystemPowerStatus` | Implemented; can be enabled optionally |

The 弱水时砂炽天使 2.4 GHz device (`38FF:0001`) has no battery reporting channel, confirmed through host-side inspection and RoseLink analysis. Other models depend on whether their protocols have been implemented.

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
