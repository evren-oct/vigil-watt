# 贡献记录

> 社区改动收进本仓库时记一笔：谁、哪个 PR、收了什么、没收什么。协议细节留在专题文档。
> **面向用户的名单在根目录 [CONTRIBUTORS.md](../../CONTRIBUTORS.md)**（只写"谁做了什么"）；本文只记收了什么、没收到什么、是否本机复测，两处不重复。

## 2026-10-20

- **Gpan666**（[#28](https://github.com/evren-oct/vigil-watt/pull/28)）：纳入三台设备——ATK F1 Ultimate 2.0 鼠标（`373B:11D9` / `11E4`，`AtkCodec`，16 字节 op 帧 + `(0x4D - sum)` 校验和）、ATK A98 Pro 键盘（`373B:1152`，`AtkViaCodec`，VIA `0x02/0x96/0x24`）、MCHOSE X9 耳机（`3837:6045`，`MchoseX9Codec`，64 字节查询帧）。ATK 两条链路并入新插件 `AtkPlugin`，共用一个品牌级开关 `PluginEnabled["Atk"]`（默认关，会写帧）；X9 并入现有 `MchosePlugin`，协议名 `headset-x9`，共用 `PluginEnabled["Mchose"]`。**代码由 AI（WorkBuddy 智能体）编写**，协议抓包、真机验证与审阅由贡献者完成；本仓库维护者未复测。未纳入任何 UI/设置页改动——设置页开关由反射自动出现，`Description` 一类本地 hack 不带入上游（`AGENTS.md` §9）。帧样本与验证范围见 [atk.md](../hardware/atk.md) / [mchose-x9.md](../hardware/mchose-x9.md)。

## 2026-10-19

- **dotisland**（[#13](https://github.com/evren-oct/vigil-watt/pull/13)）：纳入 HECATE G5M Pro（`35BB:D00B`）只读电量插件，以及拔出设备后上报离线。认领收窄到 VID、PID 和 usage；迈从插件排除 `0x35BB`；探测窗口内没有任何报文则不认领。未纳入 [#14](https://github.com/evren-oct/vigil-watt/pull/14) 的按显示名去重。电量帧为本贡献者真机声明，本机未复测。
- **Baron77777**（[#16](https://github.com/evren-oct/vigil-watt/pull/16)）：纳入 AULA F99 Pro 与 DELUX M900 Pro 插件。AULA 只保留会话帧 `13 05 01`，不进 15 秒发现，默认关。DELUX 保持只读，但本机未扫到 `1D57`，默认关。未纳入删除离线、只显示在线、同源合并和电量条对齐。帧样本为本贡献者真机声明，本机未复测。
- **luze258**（[#15](https://github.com/evren-oct/vigil-watt/pull/15)）：这一笔只纳入 DualSense 蓝牙只读电量。未纳入 GANSS、MCHOSE V9 Pro，也未纳入 BLE 大改和按名字去重。电量档位为本贡献者声明，本机未复测。
- **luze258 / #15（第二轮）**：只纳入 GANSS GS3104T-Pro 2.4G 插件（`GanssCodec` + `GanssPlugin` + 单测）。未纳入同提交的 MCHOSE V9 Pro、BLE 大改（`BleBatteryWatcher` / `WatcherProps` / `DeviceKindGuesser` / `DeviceSourceInfo` / `SettingsPage`）与 DualSense（已在前一轮另行纳入）。认领硬绑 `05AC:024F` + `&mi_03#` + `0xFF60/0x0061` + 33 字节，会写查询帧，默认关。帧样本为本贡献者真机声明，本机无该设备，未复测。
- **zi-ha**（[#17](https://github.com/evren-oct/vigil-watt/pull/17)）：纳入鼠标 V2（A7 V2 Pro `3837:100A`）与 G87 V2（`3837:201B`）支持，以及按协议区分键盘/鼠标图标的 `ResolveKind`。认领加了 `0x3837` / `0x41E4` 白名单，≥60 字节接口只走鼠标通道。型号识别用官方密码表。键盘链路本机（G87 V1）复测通过；鼠标链路为本贡献者真机验证，本机未复测。
- **luze258 / #15（第三轮）**：只纳入 MCHOSE V9 Pro 耳机（`MchoseV9ProCodec` + 单测）。**没有按贡献者的做法新开 `MchoseV9Pro` 插件键**——设置页是品牌级开关（`AGENTS.md` §9），同品牌必须一行，故并入现有 `MchosePlugin`，协议名 `headset-v9`，共用 `PluginEnabled["Mchose"]`。未沿用贡献者的 `MinValidPercent => 1`（基类已无该成员；口径同 GANSS：0 由协议层拒、1 由公共下界拒）。另加一条回显保护：整帧等于查询帧时丢弃，避免把主机写入的回显读成 1%。仍未纳入同提交的 BLE 大改与 DualSense（已在前两轮处理）。帧样本为本贡献者真机声明，本机无该设备，未复测。

## 2026-10-21

- **shengzhi233**（[#36](https://github.com/evren-oct/vigil-watt/pull/36)）：纳入 CHERRY PIXIU75 与派世 Pulsar X2 V2 两个插件（`CherryCodec` / `CherryPlugin`、`PulsarCodec` / `PulsarPlugin`，21 条 codec 用例，帧字面量取自真机），协议文档 §13 / §14 与 BACKLOG 两条一并收下。两个品牌都是「发查询帧才出读数」，按 R1 默认关，走 `SupportsDiscoveryProbe` + `ProbeCooldown = 10s`。本机无这两台设备，未复测；作者真机日志与官方软件读数一致（48% / 55% 充电、20% + 3566 mV）。**收口改动**：BACKLOG 的 CR-73 / CR-74 已被 dev 占用，本次条目改为 CR-81 / CR-82，并按代码实情重写 CR-81（3 轮无应答即转离线 + 熔断，不是「不判离线」）；changelog 里同一句一并改正。
- **KNA7022**（[#25](https://github.com/evren-oct/vigil-watt/pull/25)）：纳入黑爵 AK870（`3151:4011`，feature `F7` 状态查询）与雷神 ML903 NearLink Se（`369B:0234`，`0xA0` 设备信息 TLV `0x0225`）两个插件 + 29 条用例，默认关；附「迈从不得误认这两款」的回归用例。本机无该设备，未复测；作者实机确认过 AK870 休眠/唤醒与 ML903 关机后无响应、重开恢复。充电状态保持 `Unknown`（作者未验证）。该 PR 只含设备适配，无未纳入项。
- **haihuang0506**（[#33](https://github.com/evren-oct/vigil-watt/pull/33)）：纳入迈从 K7 V2 Ultra+（`3837:1018`）的 `mouse-k7` 协议（`MchoseK7Codec` + 分类器 `MouseK7` + 16 条真机帧用例），并入现有 `MchosePlugin`、共用 `Mchose` 开关，默认关。作者真机连跑 6.5 小时，读数与官方 M HUB 一致。**收口改动**：K7 协议小节从 §13 顺延为 §15（§13 / §14 已给 CHERRY / PULSAR），内部小节与代码指针同步；`MchoseK7Codec` 类头 7 行压到 3 行（配额 ≤5）。**未纳入**同作者 [#34](https://github.com/evren-oct/vigil-watt/pull/34) 的其余内容（VGN 蜻蜓 F1/F2、狼蛛 F108 Pro、安装位置/MSI、展示层去重、贴片设备筛选、`tools/battcheck`）——K7 部分与 #33 **逐字节相同**，只差类头署名（#34 写 `Mightling`），以先提的 #33 为准。

## 2026-10-23

- **Mutsuki233**（[#45](https://github.com/evren-oct/vigil-watt/pull/45)）：纳入罗技 PRO X 2 LIGHTSPEED 耳机（`046D:0AF7`）的 Centurion 只读电量通道、经典蓝牙显示名归一、失败容错与 2.4G 查询帧解析 / 名称两条单测。认领硬绑 `046D:0AF7` + `0xFFA0:0x0001` + 输入输出各 ≥64 字节，沿用「罗技」插件开关；Centurion 连续 4 次无应答才熔断转离线（比基类默认多容忍 1 次，拔线瞬间的短时超时不该造成离线闪烁）。**未纳入** HID++ 路径的改动与 USB 有线模式认领——后者耳机支持有线，但本次未验证。真机由贡献者本人验证：2.4G 放电 / 充电 / 拔线恢复 / 休眠唤醒，蓝牙电量与 Windows 设置一致，关闭罗技开关后插件卸载且不再查询（未做 USB 总线级抓包）。测试项目编译通过；单测在贡献者环境里因测试宿主起不来未能运行。**收口改动（维护者）**：本机复跑全量单测 **662 通过 / 1 跳过（CR-83）/ 0 失败**，用例本身没问题；`WritesEnabled` 的注释仍写「闸门是唯一写点内的 `HidppIO.WritesAllowed`」，与 Centurion 新增的第二个写点不符，已改写为两个写点同一道闸门；补回被误删的 `IsHidppInterface` 摘要并整理空行；`review-standard.md` 的 R2 例外条款按「混合型插件」重写；`logitech.md` 补 `## 1.` 标题（原文只有 §2，编号从 2 开始）；handoff §4 的 `#24` 改为 `#45`（#24 是设备适配指南 issue）；CR-91~93 记入 BACKLOG。
