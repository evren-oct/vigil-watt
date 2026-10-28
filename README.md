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

**设备协议开关：** 华硕、MCHOSE、Logitech、雷蛇与索尼 INZONE 插件默认关闭，因为读取需要发送查询帧；可在设置中按品牌启用。其他纯被动插件不会主动向设备写入。

## 设备诊断：给未适配的设备采证据

手里有台读不到电量的设备，不用会抓包也能帮它被支持——主窗口第 5 项「设备诊断」采一包原始证据发给维护者就行。

1. **选任务**：「未知设备调查（找新协议）」列出系统里全部 HID 设备，未适配的排前面；「已支持设备诊断（排查读数）」只列插件正在认领的设备。设备按物理设备聚合，不会把一台接收器的十个接口平铺成十行。
2. **选接口**：每个接口都标出有没有输入通道，已适配的设备直接推荐插件实际认领的那个；选中即自动预检，右栏只列需要你注意的项。
3. **按步骤采集**：页面给的是操作清单——操作设备 → 连接充电器 → 断开充电器 → 关闭设备 → 重新开启设备，每步写清怎么做，并实时显示「本步已采到 N 条报文」，哪一步没反应当场就能看出来。
4. **导出**：停止后生成离线报告 `report.html` 与 `vigilwatt-capture.zip`，可打开、另存或直接贴到 issue。全部只在本机，不上传、不需要登录。

采集句柄只申请只读权限，**不向设备写任何报文**；也不做 USB 总线抓包、不监听蓝牙空中通信、不解释报文含义。诊断包可能含设备序列号，发出去前建议自己看一眼包里的 `README.txt`。

三个常见坑：选错接口是最常见的「采不到」（同一个接收器，513 字节的大缓冲通道可能一条都不来，换 19 字节那条就正常）；0 条记录不等于没有协议（有的设备要先开会话才推送）；整场一个字节都没变，多半是配置通道而不是状态通道。

![设备诊断：选任务、选接口、按步骤采集、导出报告](assets/screenshot-capture.png)

完整流程、文件格式与已知限制见 [docs/project/capture.md](docs/project/capture.md)。

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
| 华硕 ROG Strix Scope II 96 Wireless · Omni 接收器 | 本机 95%、未充电已验证；默认关闭，见 [asus-rog.md](docs/hardware/asus-rog.md) |
| 雷蛇 BlackShark V2 Pro (2023) · 1532:0555 | 本机 79%、未充电已验证；与鼠标共用雷蛇开关，见 [razer-headset.md](docs/hardware/razer-headset.md) |
| 雷蛇 Kraken V4 耳机（接收器 `1532:056C`）· 64 字节 `0x02` 查询 | 已实现，默认关闭；会写帧，与鼠标共用雷蛇开关。贡献者声明，维护者未复测 |
| 雷蛇 Viper V3 Pro SE · 无线接收器 `1532:00DF` | 本机电量查询已验证；默认关闭，见 [razer-viper-se.md](docs/hardware/razer-viper-se.md) |
| 迈从 G87 · 2.4G `0x13` | 本机验证 |
| 迈从新代 dongle · QHW（如 `3837:3033`） | 已实现，待对应设备验证 |
| 迈从 G87 V2 / A7 V2 Pro · 鼠标 V2 被动帧 | 已实现，默认关闭；贡献者真机，本机只复测 G87 V1 不回归 |
| 迈从 K7 V2 Ultra+ · `0x0900` 查询 + `0x0600` 事件 | 贡献者真机验证；与迈从键盘共用开关 |
| 迈从 G3 V2 · Gmouse 主动查询（`3837:4245`） | 贡献者真机验证；与迈从键盘共用开关 |
| 迈从 G75 Pro · `3554:FA09`，与狼蛛 F99 Pro 同指纹，靠会话 ACK 型号分流 | 已实现，默认关闭；会写帧。贡献者真机 36%，维护者未复测，见 [mchose.md](docs/hardware/mchose.md) |
| 北通 T9 / 鲲鹏20（2.4G 广播 + 有线查询）· 厂商接口被动流 | T9 真机验证；鲲鹏20 两通道贡献者真机，维护者未复测 |
| 罗技 Bolt（C548 + MX Master 3S）· HID++ | 真机验证通过 |
| 雷蛇毒蝰 V4 Pro（接收器）· 经典 90 字节 feature 协议 | 本机验证，默认关闭；会写帧 |
| 索尼 INZONE H9 II（接收器）· `0xFF04` 控制传输查询 | 本机验证，默认关闭；会写帧 |
| 雷柏 VT3 二代 · 被动 `status-report7` | 本机验证 |
| 英菲克 ZHEN · 被动 report `0x03` | 真机验证 |
| HECATE G5M Pro · 被动 report `0x04` + 主动查询 `04 00 00 1A` | 已实现，默认关闭；会写帧。贡献者真机，维护者未复测 |
| AULA F99 Pro · `0x13` 会话/推送 | 已实现，默认关闭；贡献者真机，维护者未复测 |
| 狼蛛 G9 耳机 / L9 Ultra 鼠标 · 被动状态帧 / `0x81` 查询 | 已实现；与 F99 Pro 共用狼蛛开关（设置页一行）。贡献者声明，维护者未复测 |
| AULA HERO 68 Air · 8K 接收器 Bytech 分片查询 | 默认关闭；贡献者真机验证 10% 放电/充电，与 F99 Pro 共用狼蛛开关 |
| AKG N9 Hybrid · 接收器 RACE 电量查询 | 默认关闭；贡献者真机读取 50%，充电状态未知 |
| 凌豹 M5PRO Max（`3151:402D`）· 官方驱动同款 `F7` 状态查询 | 已实现，默认关闭；会写帧。贡献者真机 88%/95%，维护者未复测 |
| 凌豹 K98（`3554:FA09`）· `0x13` 会话/推送 | 已实现，默认关闭；与 AULA 同 VID/PID，按会话型号码 `0x0212` 区分。贡献者真机，维护者未复测 |
| VGN 蜻蜓 F1 V5 Turbo+ / F2 大师+ / V98 Pro · kc2 配置帧 / y2 查询 / `0x1A` 查询 | 已实现，默认关闭；会写帧。插线后按序列号只留一条接口。贡献者真机，维护者未复测 |
| DELUX M900 Pro · 被动 5 字节状态帧 | 已实现，默认关闭；与英菲克同 VID/PID，本机未扫到接收器 |
| DualSense · 蓝牙 HID 被动电量 | 已实现，默认开启；贡献者真机，维护者未复测 |
| Haute42 Adapter 4DS（DualSense 适配器）· 只读 feature `0xF9` | 已实现，默认关闭；零写入。适配器自报 DualSense 号，靠固件串认出来。贡献者声明，维护者未复测 |
| GANSS GS3104T-Pro / 狼蛛 F108 Pro · 33 字节查询帧 | 已实现，默认关闭；会写帧。两者同指纹共用一行开关。贡献者真机，维护者未复测 |
| MCHOSE V9 Pro / X9 耳机 · 64 字节查询帧 | 已实现，默认关闭；会写帧，与迈从键鼠共用开关。V9 Pro 贡献者真机，X9 贡献者声明，维护者均未复测 |
| MCHOSE A5 V2 Ultra · 反码 Feature `0x11` 查询 | 已实现，默认关闭；会写帧，与迈从键鼠共用开关。贡献者声明，维护者未复测 |
| CHERRY PIXIU75 · 64 字节块协议（块 `0x1A`） | 已实现，默认关闭；会写帧。贡献者真机，维护者未复测 |
| 派世 Pulsar X2 V2 · 17 字节 STATUS/POWER | 已实现，默认关闭；会写帧。贡献者真机，维护者未复测 |
| 黑爵 AK870 · feature `F7` 状态查询 | 已实现，默认关闭；会写帧。贡献者真机，维护者未复测 |
| 雷神 ML903 NearLink Se · `0xA0` 设备信息 TLV | 已实现，默认关闭；会写帧。贡献者真机，维护者未复测 |
| ATK / VXE 无线鼠标 / A98 Pro 键盘 · 17 字节查询帧 / VIA | 已实现，默认关闭；会写帧。鼠标两条型号真机验证，键盘为贡献者声明 |
| VXE R1 Nearlink · 接收器 `373B:10C9` | 本机无线与有线本体 `10C7` 充电均已验证；共用 ATK 开关，默认关闭，见 [atk.md §9](docs/hardware/atk.md) |
| HyperX Cloud Flight 2 · 2.4G 被动推送帧 | 已实现，默认开启；零写入。CF2 飓风炫彩版真机验证 |
| 墨将 烈风2 GALE2（2.4G 接收器 / 有线，同为 `E2B7:122C`）· 厂商通道 `A5` 读帧 | 已实现，默认关闭；会写帧。2.4G 与有线两种形态均已真机验证，充放电标志经插拔 A/B 对照 |
| 经典蓝牙 · HFP 网关节点属性 | EDIFIER W820NB 已验证 |
| 本机电池 · `GetSystemPowerStatus` | 已接入，可选开启 |

弱水时砂炽天使 2.4G（`38FF:0001`）经主机侧与 RoseLink 检查确认没有电量通道，因此不支持显示电量。其他型号是否可用取决于其协议是否已适配。

上表里标着「贡献者真机」的适配，都来自社区贡献者的抓包与真机验证。谁做了什么见 **[CONTRIBUTORS.md](CONTRIBUTORS.md)**。

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
