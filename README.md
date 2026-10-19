# VigilWatt

[English](README.en.md) | 简体中文

> Windows 无线设备电量一览：蓝牙设备、2.4G 键鼠手柄和可选的本机电池，都能在托盘与历史记录中查看。

![Platform](https://img.shields.io/badge/Windows-10%2F11-0078d4) ![.NET](https://img.shields.io/badge/.NET-8-512bd4) ![License](https://img.shields.io/badge/license-MIT-green) [![Release](https://img.shields.io/github/v/release/evren-oct/vigil-watt?label=release&color=blue)](https://github.com/evren-oct/vigil-watt/releases)

## 界面预览

设备列表与电量：

![设备电量](assets/screenshot-devices.png)

统计、历史和设置：

<p align="center">
  <img src="assets/screenshot-stats.png" width="31%" alt="历史统计">
  <img src="assets/screenshot-history.png" width="31%" alt="历史管理">
  <img src="assets/screenshot-settings.png" width="31%" alt="设置">
</p>
<p align="center"><sub>历史统计 · 历史管理 · 设置</sub></p>

桌面小组件：

<p align="center"><img src="assets/screenshot-widget.png" width="480" alt="桌面小组件"></p>

任务栏电量贴片：

<p align="center"><img src="assets/screenshot-taskbar.png" width="480" alt="任务栏电量贴片"></p>

## 功能

- 读取 BLE GATT 电池服务、经典蓝牙 HFP 网关节点电量和已适配 2.4G 设备协议。
- 托盘显示在线设备的最低电量，低于可配置阈值时提醒（默认 20%）。
- 可选显示桌面小组件、任务栏贴片和本机电池电量；本机电池不计入托盘最低电量。
- 保存电量历史，提供 24 小时、7 天和 30 天统计，并可按设备管理记录。
- 可设置开机启动。

**设备协议开关：** MCHOSE 与 Logitech 插件默认关闭，因为读取需要发送查询帧；可在设置中按品牌启用。其他纯被动插件不会主动向设备写入。

## 下载与使用

从 [GitHub Releases](https://github.com/evren-oct/vigil-watt/releases) 下载，或使用 [Gitee 镜像](https://gitee.com/YGWG-LX/vigil-watt/releases)：

- `VigilWattApp-win-Setup.exe`：当前用户安装版，包含 Velopack 自动更新。
- `VigilWattApp-win-Portable.zip`：免安装便携版。

安装包为 self-contained，**无需另外安装 .NET 运行时**。仓库不包含生成的 `dist/` 安装包。

1. 启动应用后，在任务栏通知区域找到 VigilWatt 图标；首次可能收在 `^` 溢出区。
2. 左键打开主窗口，右键打开托盘菜单。
3. 需要监控笔记本电池时，在设置中开启「监控本机电量」。

## 设备支持

下表区分已验证设备与已实现但尚待对应设备验证的协议：

| 设备 / 协议 | 当前状态 |
|---|---|
| 迈从 G87 · 2.4G `0x13` | 本机验证 |
| 迈从 A7 V2 Ultra+ 鼠标 · RealTek `0xFF01` | 本机验证 |
| 凌豹 K98（`3554:FA09`）· 2.4G Compx V1 | 本机验证 |
| 迈从新代 dongle · QHW（如 `3837:3033`） | 已实现，待对应设备验证 |
| 北通 T9 · 厂商接口被动流 | 真机验证 |
| 罗技 Bolt（C548 + MX Master 3S）· HID++ | 真机验证通过 |
| 雷柏 VT3 二代 · 被动 `status-report7` | 本机验证 |
| 英菲克 ZHEN · 被动 report `0x03` | 真机验证 |
| 经典蓝牙 · HFP 网关节点属性 | EDIFIER W820NB 已验证 |
| 本机电池 · `GetSystemPowerStatus` | 已接入，可选开启 |

弱水时砂炽天使 2.4G（`38FF:0001`）经主机侧与 RoseLink 检查确认没有电量通道，因此不支持显示电量。其他型号是否可用取决于其协议是否已适配。

## 构建

需要 .NET 8 SDK；在 Windows x64 上从解决方案构建：

```powershell
dotnet build VigilWatt.sln -c Debug
dotnet publish src/VigilWatt.App -c Release -p:Platform=x64 -o publish
```

构建、测试和发布说明见 [docs/dev/build.md](docs/dev/build.md)。

## 项目结构

```text
VigilWatt.sln
src/
  VigilWatt.Core/       # 设备发现、BLE/HID、协议插件、历史存储与日志
  VigilWatt.App/        # WinUI 3 界面、托盘、桌面小组件和任务栏贴片
  tests/
    VigilWatt.Core.Tests/       # xUnit 单元测试
    VigilWatt.BleScan/          # BLE 诊断工具
    VigilWatt.ProtocolCapture/  # 协议采集工具
assets/                 # README 截图与应用资源
docs/                   # 项目、开发、硬件与审查文档
scripts/                # 构建、打包及临时调试脚本
```

## 文档导航

| 想了解 | 查看 |
|---|---|
| 全部文档与问题速查 | [docs/README.md](docs/README.md) |
| 协议参考来源、实现依据和验证范围 | [docs/hardware/sources.md](docs/hardware/sources.md) |
| 协议帧、设备兼容性与已证伪路径 | [docs/hardware/protocols.md](docs/hardware/protocols.md) |
| 硬件读写安全规则 | [docs/hardware/safety.md](docs/hardware/safety.md) |
| 架构与模块职责 | [docs/project/architecture.md](docs/project/architecture.md) |
| 构建与测试说明 | [docs/dev/build.md](docs/dev/build.md) |

## 仓库与许可

[GitHub 主仓库](https://github.com/evren-oct/vigil-watt) · [Gitee 镜像](https://gitee.com/YGWG-LX/vigil-watt) · [MIT License](LICENSE) © 2026 evren
