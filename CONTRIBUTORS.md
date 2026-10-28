# 贡献者

VigilWatt 的设备适配大多来自社区：有人提供抓包样本，有人把协议写成插件，有人报出"这台设备读不到电量"的现象。这份名单按人记录做了什么。

> 每个 PR 具体收了什么、**没**收到什么、是否本机复测，记在 [docs/project/contributions.md](docs/project/contributions.md)。本文只回答"谁做了什么"。

| 贡献者 | 贡献内容 | 关联 |
|---|---|---|
| **endermiteDOTcn** | AULA HERO 68 Air 2.4G 电量与充电支持、AKG N9 Hybrid 接收器电量支持；协议研究、真机验证与回归测试 | [PR #44](https://github.com/evren-oct/vigil-watt/pull/44) |
| **dotisland** | HECATE G5M Pro（`35BB:D00B`）只读电量插件（被动 report `0x04`），后升级为主动查询（`04 00 00 1A`，电量不再依赖 HECATE Connect，离线改按连续 3 次无应答判）；拔出设备后把离线状态交回设备列表；BLE 端点补全缓存名、设备列表电量列右对齐 | [PR #13](https://github.com/evren-oct/vigil-watt/pull/13) · [PR #14](https://github.com/evren-oct/vigil-watt/pull/14) · [PR #47](https://github.com/evren-oct/vigil-watt/pull/47) |
| **Baron77777** | AULA F99 Pro（`3554:FA09`，`0x13` 会话/推送）与 DELUX M900 Pro（`1D57:FA65`，被动 5 字节状态帧）两个插件 | [PR #16](https://github.com/evren-oct/vigil-watt/pull/16) |
| **luze258** | 三台设备：DualSense 蓝牙电量（report `0x31` + CRC）、GANSS GS3104T-Pro（33 字节查询帧）、MCHOSE V9 Pro 耳机（64 字节查询帧）；以及 GANSS 认领判据的风险分析 | [PR #15](https://github.com/evren-oct/vigil-watt/pull/15) |
| **zi-ha** | 迈从鼠标 V2（A7 V2 Pro `3837:100A`）与 G87 V2（`3837:201B`）支持；按协议区分键鼠图标的 `ResolveKind`；用官方密码表识别型号 | [PR #17](https://github.com/evren-oct/vigil-watt/pull/17) |
| **haihuang0506** | 迈从 K7 V2 Ultra+（`3837:1018`）：`0x0900` 查询 + `0x0600` 事件协议；VGN 蜻蜓 F1 V5 / F2 大师+ 两套协议与狼蛛 F108 Pro（与 GANSS 同指纹，按产品名分品牌）；协议均取自官方前端运行时，真机验证 | [PR #33](https://github.com/evren-oct/vigil-watt/pull/33) · [PR #34](https://github.com/evren-oct/vigil-watt/pull/34) |
| **hjc3094893379** | 迈从 G3 V2（`3837:4245`）支持：Gmouse 主动查询协议（65 字节 `00 55 30 A5…` 查询帧，应答前缀 `AA 30`），真机验证 | [PR #38](https://github.com/evren-oct/vigil-watt/pull/38) |
| **KNA7022** | 黑爵 AK870（`3151:4011`，feature `F7` 状态查询）与雷神 ML903 NearLink Se（`369B:0234`，`0xA0` 设备信息 TLV）两个插件 | [PR #25](https://github.com/evren-oct/vigil-watt/pull/25) |
| **shengzhi233** | CHERRY PIXIU75（`046A:01E4`，64 字节块协议，块 `0x1A` 取电量 + 充电位）与派世 Pulsar X2 V2（`3554:F508`，17 字节 STATUS/POWER）两个插件 | [PR #36](https://github.com/evren-oct/vigil-watt/pull/36) |
| **Mutsuki233** | 罗技 PRO X 2 LIGHTSPEED 耳机（`046D:0AF7`）：提供设备信息与 2.4G / 蓝牙真机验证，Centurion 只读电量与蓝牙显示名适配 | [PR #45](https://github.com/evren-oct/vigil-watt/pull/45) |
| **imx1n** | 雷蛇毒蝰 V4 Pro（`1532:00E6` 接收器 / `1532:00E5` 本体，经典 90 字节 feature 协议）与索尼 INZONE H9 II（`054C:0FA8`，`0xFF04` 控制传输查询）两个插件 | [PR #54](https://github.com/evren-oct/vigil-watt/pull/54) |
| **k1ky0** | 寸渊影弑耳机与官方驱动抢占用修复（`OfficialAppGuard`：检测到 `DARKRIM.exe` 就不认领，官方退出后自动恢复），以及盖世小鸡解析失败诊断 | [PR #43](https://github.com/evren-oct/vigil-watt/pull/43) |
| **Delitriuz** | HECATE G3M Pro 升级版（接收器 `35BB:D3FB`，17 字节 `0xFF02` 通道，STATUS `08 03` / POWER `08 04`）插件，并给出 Kysona 内核驱动出处与 10 分钟被动监听结论 | [PR #49](https://github.com/evren-oct/vigil-watt/pull/49) |
| **Gpan666** | 历史库时间戳时区修正（`HistoryStore` 读库时把 UTC 毫秒转本地时区）；ATK A98 Pro 键盘的 VIA 电量通道、F1 Ultimate 2.0 型号识别，以及迈从 X9 耳机；ATK F1 Ultimate 2.0 两处认领缺陷修复（接收器白名单补登 `11D9`/`11E4`、两个 PID 归一 `atk:atk-f1ult`、插充电线时不再重复显示）；熔断期「半开探活」通道（设备睡醒后电量不再卡住，失败不升退避档位） | [PR #50](https://github.com/evren-oct/vigil-watt/pull/50) · [PR #28](https://github.com/evren-oct/vigil-watt/pull/28) · [PR #65](https://github.com/evren-oct/vigil-watt/pull/65) · [PR #80](https://github.com/evren-oct/vigil-watt/pull/80) |
| **mofeng816** | ATK / VXE 无线鼠标（接收器 `373B:101B` / `101A` / `1303`，本体 `1015` / `12A9`，`FF02` 口 17 字节查询帧）与 HyperX Cloud Flight 2（`03F0:0AC1`，`FF90` 口被动推送帧）两个插件，含协议分析、编解码单测与真机验证；雷柏 VT2S Air Max 有线 / 2.4G / P5 Plus 底座多状态判定修复（有线模式误断链、复合设备认成键盘、切换瞬间图标连闪），8 个物理状态实测全过 | [PR #48](https://github.com/evren-oct/vigil-watt/pull/48) · [PR #61](https://github.com/evren-oct/vigil-watt/pull/61) |
| **wx2995** | 狼蛛 G9 耳机（`38A6:0030`，被动状态帧）、L9 Ultra 鼠标（`38A6:28FD`，`0x81` 查询）与 VGN V98 Pro 键盘（`320F:5088`，`0x1A` 查询）三台适配 | [PR #31](https://github.com/evren-oct/vigil-watt/pull/31) |
| **Cecilian-Elysian** | 北通鲲鹏20 三通道适配（2.4G 杂项页广播 / 有线 `02 15` 查询 / 双通道归一一个设备位），并逐路排查确认蓝牙通道无用户态可读数据 | [PR #60](https://github.com/evren-oct/vigil-watt/pull/60) |
| **kaniu** | 迈从 A5 V2 Ultra（`3837:100C`，反码 Feature 查询）、雷蛇 Kraken V4 耳机（`1532:056C`，64 字节 `0x02` 报告）与 Haute42 Adapter 4DS（借 DualSense 号段的 USB 适配器）三台适配；另提供 11 项重连回归测试 | [PR #20](https://github.com/evren-oct/vigil-watt/pull/20) |
| **KaelenVoss10** | 凌豹 M5PRO Max（`3151:402D`，官方驱动同款 `F7` 状态查询）与迈从 G75 Pro（`3554:FA09`）适配；给出「同 VID/PID 按会话 ACK 型号码分流」的认领机制 | [PR #53](https://github.com/evren-oct/vigil-watt/pull/53) |
| **Bamlax** | 凌豹 K98（`3554:FA09`，`0x13` 会话/推送）适配；给出型号码 `0x0212` 与官方驱动 `KB.ini` 的对照 | [PR #29](https://github.com/evren-oct/vigil-watt/pull/29) |
| **Mzood** | 华硕 ROG Strix Scope II 96 Wireless（Omni 接收器 `0B05:1ACE`）、雷蛇 BlackShark V2 Pro 2023（`1532:0555`）与 Viper V3 Pro SE（`1532:00DF`）三台适配；顺带给基类加了零权限 Feature 句柄与按接口取读数下界两个钩子 | [PR #58](https://github.com/evren-oct/vigil-watt/pull/58) |
| **Leejangh** | 华硕 ROG 棱镜2 / Delta II 耳机（`0B05:1AFA`，`0xCC` 报告 + group `0x12`，`0x07` 读电量、`0x08` 读充电位）适配，并入现有 `AsusPlugin` 共用品牌开关；协议取自 MIT 许可的 pelta-control 并逐字节复核，真机验证 | [PR #75](https://github.com/evren-oct/vigil-watt/pull/75) |
| **rain1113y** | 墨将烈风2 GALE2 手柄（`E2B7:122C`，`0xA5` 帧读寄存器 `0x04`，含 USBPcap 抓包与帧解码脚本）与 ATK A9 Air 大师版+（GEM 8K 接收器 `373B:1278` / 本体 `1293`）两台适配；另修 ATK 双连接（接收器没拔就插线）整只鼠标显示离线——改为按型号组配对、由 `SkipPathsForThisPass` 摘掉接收器 | [PR #72](https://github.com/evren-oct/vigil-watt/pull/72) |

## 还能怎么帮

- **有设备**：抓一次官方软件的包（手法见 [debugging.md](docs/hardware/debugging.md) §1），或按协议文档里的验证清单复测。GANSS 与 V9 Pro 目前都只有贡献者一台机器的样本，还缺第二台设备的数据（`docs/reviews/BACKLOG.md` CR-66 / CR-67）。
- **写代码**：新设备按 [AGENTS.md](AGENTS.md) 的规则接入——插件继承 `BatteryPluginBase`、会写帧的默认关闭、只发有出处的帧。
- **报问题**：设备读不到、读数跳动、离线误判，带上型号、VID/PID 和日志（`%LOCALAPPDATA%\VigilWatt\logs`）。现象描述清楚的 issue 与代码贡献一样有价值。

> 只做兼容性适配、不涉及厂商代码复用的贡献同样计入。每个协议的来源与许可边界见 [docs/hardware/sources.md](docs/hardware/sources.md)。
