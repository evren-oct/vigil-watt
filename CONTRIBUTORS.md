# 贡献者

VigilWatt 的设备适配大多来自社区：有人提供抓包样本，有人把协议写成插件，有人报出"这台设备读不到电量"的现象。这份名单按人记录做了什么。

> 每个 PR 具体收了什么、**没**收到什么、是否本机复测，记在 [docs/project/contributions.md](docs/project/contributions.md)。本文只回答"谁做了什么"。

| 贡献者 | 贡献内容 | 关联 |
|---|---|---|
| **dotisland** | HECATE G5M Pro 只读电量插件（`35BB:D00B` 被动 report `0x04`）；拔出设备后把离线状态交回设备列表 | [PR #13](https://github.com/evren-oct/vigil-watt/pull/13) |
| **Baron77777** | AULA F99 Pro（`3554:FA09`，`0x13` 会话/推送）与 DELUX M900 Pro（`1D57:FA65`，被动 5 字节状态帧）两个插件 | [PR #16](https://github.com/evren-oct/vigil-watt/pull/16) |
| **luze258** | 三台设备：DualSense 蓝牙电量（report `0x31` + CRC）、GANSS GS3104T-Pro（33 字节查询帧）、MCHOSE V9 Pro 耳机（64 字节查询帧）；以及 GANSS 认领判据的风险分析 | [PR #15](https://github.com/evren-oct/vigil-watt/pull/15) |
| **zi-ha** | 迈从鼠标 V2（A7 V2 Pro `3837:100A`）与 G87 V2（`3837:201B`）支持；按协议区分键鼠图标的 `ResolveKind`；用官方密码表识别型号 | [PR #17](https://github.com/evren-oct/vigil-watt/pull/17) |
| **haihuang0506** | 迈从 K7 V2 Ultra+（`3837:1018`）支持：`0x0900` 查询 + `0x0600` 事件协议；从官方 M HUB 运行时 JS 取出帧构造与解析，真机验证 | [PR #33](https://github.com/evren-oct/vigil-watt/pull/33) |
| **KNA7022** | 黑爵 AK870（`3151:4011`，feature `F7` 状态查询）与雷神 ML903 NearLink Se（`369B:0234`，`0xA0` 设备信息 TLV）两个插件 | [PR #25](https://github.com/evren-oct/vigil-watt/pull/25) |
| **shengzhi233** | CHERRY PIXIU75（`046A:01E4`，64 字节块协议，块 `0x1A` 取电量 + 充电位）与派世 Pulsar X2 V2（`3554:F508`，17 字节 STATUS/POWER）两个插件 | [PR #36](https://github.com/evren-oct/vigil-watt/pull/36) |
| **Gpan666** | 三台设备：ATK F1 Ultimate 2.0 鼠标（16 字节 op 帧）、ATK A98 Pro 键盘（VIA `0x02/0x96/0x24`）、MCHOSE X9 耳机（64 字节查询帧）。协议抓包与真机验证由本人完成；**代码由 AI（WorkBuddy 智能体）编写**，提交前经本人审阅 | [PR #28](https://github.com/evren-oct/vigil-watt/pull/28) |
| **Mutsuki233** | 罗技 PRO X 2 LIGHTSPEED 耳机（`046D:0AF7`）：提供设备信息与 2.4G / 蓝牙真机验证，Centurion 只读电量与蓝牙显示名适配 | [PR #45](https://github.com/evren-oct/vigil-watt/pull/45) |

## 还能怎么帮

- **有设备**：抓一次官方软件的包（手法见 [debugging.md](docs/hardware/debugging.md) §1），或按协议文档里的验证清单复测。GANSS 与 V9 Pro 目前都只有贡献者一台机器的样本，还缺第二台设备的数据（`docs/reviews/BACKLOG.md` CR-66 / CR-67）。
- **写代码**：新设备按 [AGENTS.md](AGENTS.md) 的规则接入——插件继承 `BatteryPluginBase`、会写帧的默认关闭、只发有出处的帧。
- **报问题**：设备读不到、读数跳动、离线误判，带上型号、VID/PID 和日志（`%LOCALAPPDATA%\VigilWatt\logs`）。现象描述清楚的 issue 与代码贡献一样有价值。

> 只做兼容性适配、不涉及厂商代码复用的贡献同样计入。每个协议的来源与许可边界见 [docs/hardware/sources.md](docs/hardware/sources.md)。
