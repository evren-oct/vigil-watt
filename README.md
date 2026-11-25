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

**设备协议开关：** 需要发查询帧的品牌默认关闭（华硕、迈从、罗技、雷蛇、索尼 INZONE、ATK、CHERRY 等）；纯被动插件默认开启。可在设置中按品牌启用。

## 设备诊断：给未适配的设备采证据

手里有台读不到电量的设备，不用会抓包也能帮它被支持——主窗口第 4 项「设备诊断」采一包原始证据发给维护者就行。

1. **选任务**：「未知设备调查（找新协议）」列出系统里全部 HID 设备，未适配的排前面；「已支持设备诊断（排查读数）」只列插件正在认领的设备。设备按物理设备聚合，不会把一台接收器的十个接口平铺成十行。
2. **选接口**：每个接口都标出有没有输入通道，已适配的设备直接推荐插件实际认领的那个；选中即自动预检，右栏只列需要你注意的项。
3. **按步骤采集**：页面给的是操作清单——操作设备 → 连接充电器 → 断开充电器 → 关闭设备 → 重新开启设备，每步写清怎么做，并实时显示「本步已采到 N 条报文」，哪一步没反应当场就能看出来。
4. **导出**：停止后生成离线报告 `report.html` 与 `vigilwatt-capture.zip`，可打开、另存或直接贴到 issue。全部只在本机，不上传、不需要登录。

采集句柄只申请只读权限，**不向设备写任何报文**；也不做 USB 总线抓包、不监听蓝牙空中通信、不解释报文含义。诊断包可能含设备序列号，发出去前建议自己看一眼包里的 `README.txt`。

三个常见坑：选错接口是最常见的「采不到」（同一个接收器，513 字节的大缓冲通道可能一条都不来，换 19 字节那条就正常）；0 条记录不等于没有协议（有的设备要先开会话才推送）；整场一个字节都没变，多半是配置通道而不是状态通道。

![设备诊断：选任务、选接口、按步骤采集、导出报告](assets/screenshot-capture.png)

完整流程、文件格式与已知限制见 [docs/project/capture.md](docs/project/capture.md)。

## 下载与使用

从 [GitHub Releases](https://github.com/evren-oct/vigil-watt/releases) 下载；[Gitee Releases](https://gitee.com/YGWG-LX/vigil-watt/releases) 是镜像备份：

- `VigilWattApp-win-Setup.exe`：当前用户安装版，包含 Velopack 自动更新。
- `VigilWattApp-win-Portable.zip`：免安装便携版。

安装包为 self-contained，**无需另外安装 .NET 运行时**。仓库不包含生成的 `dist/` 安装包。

1. 启动应用后，在任务栏通知区域找到 VigilWatt 图标；首次可能收在 `^` 溢出区。
2. 左键打开主窗口，右键打开托盘菜单。
3. 需要监控笔记本电池时，在设置中开启「监控本机电量」。

## 设备支持

下表只列维护者本机已验证的设备；完整清单（贡献者真机、仅实现待验、不支持）见 **[docs/hardware/support-matrix.md](docs/hardware/support-matrix.md)**。

| 设备 | 状态 |
|---|---|
| 迈从 G87 · 2.4G `0x13` | 本机已验证 |
| 罗技 Bolt（C548 + MX Master 3S）· HID++ | 本机已验证 |
| 罗技 PRO X 2 LIGHTSPEED | 本机已验证（USB 有线未验） |
| 雷柏 VT3 二代 · 被动 `status-report7` | 本机已验证 |
| 北通 T9 · 被动流 | 本机已验证 |
| 英菲克 ZHEN / IN9 | 本机已验证 |
| HECATE G3M Pro 升级版 | 本机已验证（充电位未验） |
| ATK F1 Pro Max | 本机已验证 |
| EDIFIER W820NB · HFP 网关 | 本机已验证 |
| 本机电池 | 本机已验证（可选） |

弱水时砂炽天使 2.4G（`38FF:0001`）经主机侧与 RoseLink 检查确认没有电量通道，因此不支持显示电量。其他型号是否可用取决于其协议是否已适配。

完整清单里标着「贡献者真机」的适配，都来自社区贡献者的抓包与真机验证。谁做了什么见 **[CONTRIBUTORS.md](CONTRIBUTORS.md)**。

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
| 全部文档清单（自动生成） | [docs/INDEX.md](docs/INDEX.md) |
| 协议参考来源、实现依据和验证范围 | [docs/hardware/sources.md](docs/hardware/sources.md) |
| 协议帧、设备兼容性与已证伪路径 | [docs/hardware/protocols.md](docs/hardware/protocols.md) |
| 硬件读写安全规则 | [docs/hardware/safety.md](docs/hardware/safety.md) |
| 架构与模块职责 | [docs/project/architecture.md](docs/project/architecture.md) |
| 构建与测试说明 | [docs/dev/build.md](docs/dev/build.md) |

## 仓库与许可

[GitHub 主仓库](https://github.com/evren-oct/vigil-watt) · [Gitee 镜像](https://gitee.com/YGWG-LX/vigil-watt) · [MIT License](LICENSE) © 2026 evren
