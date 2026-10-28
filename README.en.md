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

**Device plugin switches:** ASUS, MCHOSE, Logitech, Razer and Sony INZONE plugins are off by default because reading requires sending query reports. Enable them by brand in Settings. Other strictly passive plugins do not write to devices.

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
| ASUS ROG Strix Scope II 96 Wireless / Omni (`0B05:1ACE`) | Contributor-verified at 95%, discharging; off by default. See [protocol notes](docs/hardware/asus-rog.md) |
| Razer BlackShark V2 Pro (2023) (`1532:0555`) | Contributor-verified at 79%, discharging; shares the Razer switch. See [protocol notes](docs/hardware/razer-headset.md) |
| Razer Viper V3 Pro SE wireless (`1532:00DF`) | Contributor-verified battery/charging queries; off by default. See [protocol notes](docs/hardware/razer-viper-se.md) |
| Razer Kraken V4 headset (receiver `1532:056C`) · 64-byte `0x02` query | Implemented, off by default; writes frames, shares the Razer switch. Contributor claim only. See [protocol notes](docs/hardware/razer-kraken.md) |
| MCHOSE G87 · 2.4 GHz `0x13` | Verified on the developer's device |
| New-generation MCHOSE dongles · QHW (e.g. `3837:3033`) | Implemented; awaiting matching hardware validation |
| MCHOSE G87 V2 / A7 V2 Pro · mouse V2 passive frames | Implemented, off by default; contributor hardware only, only G87 V1 retested here |
| MCHOSE K7 V2 Ultra+ · `0x0900` query + `0x0600` events | Contributor-verified on real hardware; shares the MCHOSE plugin switch |
| MCHOSE G3 V2 · Gmouse active query (`3837:4245`) | Contributor-verified on real hardware; shares the MCHOSE plugin switch |
| MCHOSE G75 Pro (`3554:FA09`) · same fingerprint as AULA F99 Pro, split by session ACK model code | Implemented, off by default; writes frames. Contributor verified 36%, not retested here. See [protocol notes](docs/hardware/mchose.md) |
| Betop T9 / Kunpeng 20 (2.4G broadcast + wired query) · passive vendor-interface stream | T9 verified on hardware; Kunpeng 20 contributor-verified only |
| Logitech Bolt receiver (C548 + MX Master 3S) · HID++ | Verified on hardware |
| Razer Viper V4 Pro (receiver) · classic 90-byte feature protocol | Verified on the developer's device; off by default, writes reports |
| Sony INZONE H9 II (receiver) · `0xFF04` control-transfer query | Verified on the developer's device; off by default, writes reports |
| Rapoo VT3 Gen 2 · passive `status-report7` | Verified on the developer's device |
| Inphic ZHEN · passive report `0x03` | Verified on hardware |
| HECATE G5M Pro · passive report `0x04` + active query `04 00 00 1A` | Implemented, off by default; writes frames. Contributor hardware only |
| AULA F99 Pro · `0x13` session/push | Implemented, off by default; contributor hardware only |
| AULA G9 headset / L9 Ultra mouse · passive status frame / `0x81` query | Implemented; shares the AULA switch (one settings row). Contributor claim only, not retested here |
| AULA HERO 68 Air · Bytech queries over 8K receiver | Off by default; contributor verified 10% discharging/charging; shares the AULA switch |
| AKG N9 Hybrid · RACE battery query over USB receiver | Off by default; contributor verified 50%; charging state unknown |
| LINGBAO M5PRO Max (`3151:402D`) · vendor-driver `F7` status query | Implemented, off by default; writes frames. Contributor verified 88%/95%, not retested here |
| LINGBAO K98 (`3554:FA09`) · `0x13` session/push | Implemented, off by default; shares VID/PID with AULA, split by session model code `0x0212`. Contributor hardware only |
| VGN Dragonfly F1 V5 Turbo+ / F2 Master+ / V98 Pro · kc2 config frame / y2 query / `0x1A` query | Implemented, off by default; writes frames. Wired duplicates dropped by serial number. Contributor hardware only |
| DELUX M900 Pro · passive 5-byte status | Implemented, off by default; same VID/PID as Inphic, receiver not seen here |
| DualSense · passive Bluetooth HID | Implemented, on by default; contributor hardware only |
| Haute42 Adapter 4DS (DualSense adapter) · read-only feature `0xF9` | Implemented, off by default; zero writes. Presents itself as a DualSense, identified by its firmware string. Contributor claim only |
| GANSS GS3104T-Pro / AULA F108 Pro · 33-byte query frame | Implemented, off by default; writes frames. Same HID fingerprint, one shared switch. Contributor hardware only |
| MCHOSE V9 Pro / X9 headset · 64-byte query frame | Implemented, off by default; writes frames, shares the MCHOSE switch. V9 Pro verified by contributor, X9 contributor claim only |
| MCHOSE A5 V2 Ultra · inverted Feature `0x11` query | Implemented, off by default; writes frames, shares the MCHOSE switch. Contributor claim only |
| CHERRY PIXIU75 · 64-byte block protocol (block `0x1A`) | Implemented, off by default; writes frames. Contributor hardware only |
| Pulsar X2 V2 · 17-byte STATUS/POWER | Implemented, off by default; writes frames. Contributor hardware only |
| AJAZZ AK870 · feature `F7` status query | Implemented, off by default; writes frames. Contributor hardware only |
| Thunderobot ML903 NearLink Se · `0xA0` device-info TLV | Implemented, off by default; writes frames. Contributor hardware only |
| ATK / VXE wireless mouse / A98 Pro keyboard · 17-byte query frame / VIA | Implemented, off by default; writes frames. Verified on F1 Pro Max and F1 V3 ULTIMATE+ |
| HyperX Cloud Flight 2 · passive 2.4 GHz push frames | Implemented, on by default; zero writes. Verified on a CF2 |
| MOJHON GALE2 (2.4G dongle / wired, both `E2B7:122C`) · vendor-channel `A5` read frame | Implemented, off by default; writes frames. Verified on hardware in both dongle and wired modes; charging flag confirmed by an A/B plug test |
| Classic Bluetooth · HFP gateway device-node property | EDIFIER W820NB verified |
| System battery · `GetSystemPowerStatus` | Implemented; can be enabled optionally |

Adapters marked "contributor hardware only" come from community captures and on-device verification. See **[CONTRIBUTORS.md](CONTRIBUTORS.md)** for who contributed what.

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
