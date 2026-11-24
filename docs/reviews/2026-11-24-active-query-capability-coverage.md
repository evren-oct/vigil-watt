# 主动查询能力覆盖与证据盘点（2026-11-24）

> 状态：M2 阶段 2 只读审计完成；未改代码、协议 JSON、设备目录、测试或运行行为。
> 基线：`dev` 分支 `447def5e776912ce06a9d938a0a36de2030be564`；工作区审计开始时干净。
> 本文回答“哪些设备实际依赖主动查询、需要什么能力、哪些型号有足够证据”，不实施门控。

## 1. 审计基线与范围

### 1.1 基线

| 项 | 值 |
|---|---|
| 分支 | `dev` |
| HEAD | `447def5e776912ce06a9d938a0a36de2030be564` |
| 提交标题 | `docs: 建立协议引擎治理文档体系并完成 M2 阶段 1 门控设计` |
| 工作区 | 审计开始时干净 |
| 历史审计基线 | `d117b59` |
| `d117b59..HEAD` 差异 | 仅 18 个文档文件；没有 `.cs`、协议 JSON、catalog 或测试变更 |

**已确认事实**：历史报告写在 `d117b59`，当前 HEAD 只在其上增加了治理文档。当前代码与
[主动查询写入点审计](2026-11-23-active-query-write-audit.md) 的代码结论没有行为漂移。

### 1.2 依据文档

- [协议引擎治理路线图](../architecture/protocol-engine-roadmap.md)
- [主动查询安全规范](../architecture/active-query-safety.md)
- [设备身份与能力声明规范](../architecture/device-identity-and-capabilities.md)
- [主动查询写入点审计](2026-11-23-active-query-write-audit.md)
- [主动查询门控方案](2026-11-23-active-query-gate-design.md)
- [设备支持矩阵](../hardware/support-matrix.md)
- [硬件安全规则](../hardware/safety.md)
- GitHub issue/PR 的协议、抓包、补丁与真机记录（2026-11-24 只读检索）

### 1.3 审计范围

只统计“主机向设备发送指令、主动获取设备状态”的路径，包括：

- Output Report 写入；
- Feature Report 写入 / `SetFeature`；
- 固定查询帧、动态模板、会话帧、触发帧；
- 为读取状态而必须发送的身份/型号查询。

不计入：

- 纯被动 `ReadInput` / `GetFeature`；
- BLE GATT、经典蓝牙 HFP、本机电源；
- 诊断采集中的手动发帧；
- 设备配置写入（DPI 写、回报率写、灯光、宏、固件）。

### 1.4 方法与证据等级

本次用 CodeGraph 与 `rg` 定位写入点，再逐文件读当前实现。未运行应用、未打开设备、未发送任何报文，也未运行测试。

证据等级：

| 等级 | 含义 |
|---|---|
| **S1** | 维护者本机真机读数，且文档记录了原始值或应用日志 |
| **S2** | 贡献者真机读数或真实录制帧，文档可追溯 |
| **S3** | 单测合成帧、官方静态分析、公开逆向资料 |
| **S4** | 只有代码实现，没有对应设备的读数或录制帧 |

`supported` 只接受 S1 / S2；S3 / S4 不足以把能力升为 `supported`。这与
[设备身份与能力声明规范](../architecture/device-identity-and-capabilities.md) §6 一致。

### 1.5 与历史报告的差异

**没有发现代码结论与历史报告冲突**。`d117b59..HEAD` 只有文档变更，历史报告中的以下结论在当前代码里仍成立：

- 迈从 0x4D 调用点没有传 `activeAllowed`；
- 罗技没有 `CapabilitiesOf` 覆写，HID++ 只靠 `WritesAllowed`；
- 雷柏 `WritesEnabled => false`，纯被动；
- 北通按 `BatteryReadVerified` 门控，测试覆盖完整。

本次相对历史报告新增的是“路径拆分”，不是行为差异：历史报告把迈从 0x4D 的 DPI / 回报率归在
“Mchose 直接 HID”下，本次拆成 M1 / M2 / M3；罗技的 DPI / 回报率也拆成 L3 / L4。这样能力矩阵才能
按查询类型逐项判断。

## 2. 当前主动查询路径汇总

### 2.1 计数口径

本文把“同一插件、同一协议族、同一条状态查询链路”计为一条路径；探测与轮询共用同一条查询帧时只计一次。
例如迈从 `0x4D 0x0900` 的 `Try0x4D` 与 `Poll0x4D` 算一条，DPI 的 X/Y 两次请求也算一条 `DpiRead` 路径。

按此口径：

| 厂商 | 状态查询路径数 | 备注 |
|---|---:|---|
| 迈从 MCHOSE | 10 | 0x4D 设备信息、回报率、DPI、键盘 V1、鼠标 V2 触发、gmouse、V9、X9、CZ、A5 |
| 罗技 Logitech | 4 | Centurion 电量、HID++ 电量级联、HID++ DPI、HID++ 回报率 |
| 北通 BETOP | 1 | `betop-status15` 主动查询 |
| 雷柏 RAPOO | 0 | 纯被动 |
| 其他主动查询插件 | 33 | 见 §7 |
| **合计** | **48** | 不含纯被动插件与诊断采集 |

另有 6 类“支持性写入”不计入 48 条状态查询：迈从 V1 会话帧、AULA/Lingbao 的 V1 会话帧、
罗技 HID++ 的 `Root.Ping` / 槽位容量 / 配对与名称读取、华硕 Omni 配对型号核实、墨将型号寄存器读取。
它们仍是写入，门控实施时也必须受同一授权链约束。

### 2.2 现有能力声明覆盖

当前代码中的能力声明只有：

| 来源 | 设备条目 | 声明 |
|---|---:|---|
| `betop-catalog.json` | 3 台 | `5073` 电量、充电不支持；`5158` / `5159` 电量 + 充电 |
| `rapoo-catalog.json` | 3 台 | `1412` VT3 电量 / DPI / 档位、回报率 unverified；`1B13` / `4B13` 电量 |
| `mchose-catalog.json` | 1 条传输通道 | `3837:1018` 电量 / 连接 / 充电，**不是设备型号能力** |

合计 15 条声明：13 条 `supported`、1 条 `unsupported`、1 条 `unverified`。

**关键结论**：

- 48 条状态查询路径中，只有北通 `betop-status15` 一条按设备能力门控；
- 迈从 `3837:1018` 有传输级声明，但不能据此授权该 dongle 下的所有设备本体；
- 罗技和其余 33 条其他厂商路径没有任何设备级能力声明；
- 因此“全面开门控”会让大量当前可读设备停查，M2 阶段 2 仍不能直接进入全面实施。

### 2.3 逐路径清单

表中“当前结论”是证据结论，不是当前代码是否已经声明。`U` = `unverified`，`S` = `supported`，`X` = `unsupported`。

| ID | 厂商 / 插件 | 设备与身份 | 查询入口 | 查询类型 | 当前授权 | 能力声明位置 | 证据 | 当前结论 | 缺失 / 最小建议 |
|---|---|---|---|---|---|---|---|---|---|
| M1 | 迈从 / `MchosePlugin` | `3837:1018` 等 0x4D dongle；型号由 `0x0900` 自报 | `Try0x4D` / `Poll0x4D` | BatteryRead、ChargingRead、ConnectionRead | 插件开关 + `CanWrite`；`Engine` 调用未传 `activeAllowed` | `transports[1018].capabilities`（传输级） | S2：1018 真机 100% / charge=0 / connect=2；其他通道 S4 | 通道三项 S；本体型号 U | 本体 PID 未闭环（`0x4026` / `0x4028` 冲突）；需 `devices[]` 逐型号能力，不能继承传输声明 |
| M2 | 迈从 / `MchosePlugin` | 0x4D 鼠标，型号同上 | `TryReadPerformance` | RateRead | 插件开关；无设备能力 | 无 | S3：官方静态表 + 合成单测 | U | 无真机 `0x0002` 样本；型号→回报率表未验证 |
| M3 | 迈从 / `MchosePlugin` | 0x4D 鼠标，型号同上 | `TryReadDpi` | DpiRead | 插件开关；无设备能力 | 无 | S3：官方静态偏移 + 合成单测 | U | 无真机 `0x0003` 样本；活动档与 DPI 关系未闭环 |
| M4 | 迈从 / `MchosePlugin` | G87 `41E4:2001`、G75 Pro `3554:FA09`、G87 V2 `3837:201B` | `TryKeyboardV1` / `PollKeyboardV1` | BatteryRead | 插件开关 + `CanWrite` | 无 | S1 G87 / G75 Pro；S2 G87 V2 | B=S；C=U | 充电标志语义未全验；需按型号/ACK 型号码补 `devices[]` |
| M5 | 迈从 / `MchosePlugin` | A7 V2 Pro `3837:100A`、G87 V2 `3837:201B` | `TryMouseV2` | BatteryRead（触发后读 E2 被动帧） | 插件开关 | 无 | S2 | B=S；C=U（只区分充电/未充） | 触发帧与被动帧同属一条链路；需设备目录 |
| M6 | 迈从 / `MchosePlugin` | G3 V2 `3837:4245` | `TrySpec` → `Engine.ProbeAsync/Poll` | BatteryRead、ChargingRead | 插件开关；`Engine` 未传 `activeAllowed` | 无 | S2 电量；S3 充电 | B=S；C=U | 充电态未在真机充电时抓包；需 catalog |
| M7 | 迈从 / `MchosePlugin` | V9 Pro `291D:385D` | `TrySpec` → `Engine` | BatteryRead | 插件开关；`Engine` 未传 `activeAllowed` | 无 | S2：100% 单点 | B=S（单点）；C=U | 只有一个电量点；`[3]` 未校准；需 catalog |
| M8 | 迈从 / `MchosePlugin` | X9 `3837:6045` | `TrySpec` → `Engine` | BatteryRead、ChargingRead | 插件开关；`Engine` 未传 `activeAllowed` | 无 | S2：80% / 90%；充电只见过 `0x02` | B=S；C=U | `0x03` / `0x04` / `0x1A` 只有官方代码；需 catalog |
| M9 | 迈从 / `MchosePlugin` | `3837:3033` CZ 分块（G87 V2 / G75 V2 / K87S / K99 V3） | `TrySpec` → `Engine` | BatteryRead、ChargingRead | 插件开关；`Engine` 未传 `activeAllowed` | 无 | S1：电量与充电位真机 | B=S；C=S | 共享 dongle，需按型号/本体身份绑定；不能给所有 CZ 设备泛化 |
| M10 | 迈从 / `MchoseA5Plugin` | A5 V2 Ultra `3837:100C` | `MchoseMouseProtocol.QueryAsync` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | 贡献者声明，支持矩阵记为“仅实现待验” | U | 维护者未复测；需补真机读数后再声明 |
| L1 | 罗技 / `LogitechPlugin` | PRO X 2 LIGHTSPEED `046D:0AF7` | `TryReadCenturionBattery` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S1：45% 放电、43% 充电 | B=S；C=S | 需按直连 PID 建最小设备目录 |
| L2 | 罗技 / `LogitechPlugin` | Bolt `046D:C548` 槽位 + MX Master 3S；其他接收器/槽位 | `RefreshBattery` | BatteryRead、ChargingRead | 插件开关 + `HidppIO.WritesAllowed` | 无 | S1 MX Master 3S：95% 放电；其他槽位 S4 | MX B=S；MX C=S（仅见放电态）；其他 U | 接收器 PID 不是设备身份；需按 wireless PID / 名称 / 槽位建目录 |
| L3 | 罗技 / `LogitechPlugin` | HID++ 2.0 鼠标 | `RefreshPointer` / `TryReadDpi` | DpiRead | 插件开关 + `HidppIO.WritesAllowed` | 无 | S1 MX Master 3S：DPI=1000；其他 U | MX Dpi=S；其他 U | 需按设备型号绑定，不能给所有 HID++ 鼠标授权 |
| L4 | 罗技 / `LogitechPlugin` | HID++ 2.0 鼠标 | `TryReadReportRate` | RateRead | 插件开关 + `HidppIO.WritesAllowed` | 无 | S1 MX Master 3S 无应答；G304 日志有 1000Hz 候选 | MX U；G304 候选未正式登记 | 需 per-model 证据与目录；不能按 MX 系列规格补 125Hz |
| B1 | 北通 / `BetopPlugin` | `20BC:5158` / `5159`、`5191` 及其他 `betop-status15` | `ProbeAsync` / `PollDevice` → `Engine` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` + `BatteryReadVerified` | `betop-catalog.json devices[].capabilities` | S2 5158/5159；S3 5191；其他 S4 | 5158/5159 B=S、C=S；5191 U；其他 U | 5191 需维护者复测；其他型号不要批量升级 |
| O1 | 黑爵 / `AjazzPlugin` | AK870 `3151:4011` | `Query` → `SetFeature` | BatteryRead | 插件开关 + `CanWrite` | 无 | S2 | B=S；C=U | 需最小设备目录 |
| O2 | AKG / `AkgPlugin` | N9 Hybrid `0ECB:20D9` | `Query` → RACE `0CD6` | BatteryRead | 插件开关 + `CanWrite` | 无 | S2 | B=S；C=U | 充电未验证；需目录 |
| O3 | 华硕 / `AsusPlugin` | Scope II 96 `0B05:1ACE` | `QueryBattery` | BatteryRead、ChargingRead | 插件开关 + `CanWrite`；另有配对型号核实 | 无 | S1 电量；充电只见过放电 | B=S；C=U | 需把“已核实配对型号”纳入能力身份 |
| O4 | 华硕 / `AsusPlugin` | ROG 耳机 `0B05:1AFA` | `ReadHeadsetPower` + `ReadHeadsetCharging` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S1：154 帧覆盖充电/非充电 | B=S；C=S | 需目录；`12 08` 是可缺省能力 |
| O5 | ATK / `AtkPlugin` | FF02 族：F1 Pro Max、VXE R1、F1 V3、F1 Ultimate、A9 Air、A9 Ultra Max 等 | `WriteQuery` → `FF02` | BatteryRead、ChargingRead | 插件开关 + `CanWrite`；后备 `HidD_SetOutputReport` 自身不查开关 | 无 | F1 Pro Max / VXE R1 S1；F1 V3 / A9 Air S2；A9 Ultra Max 仅社区帧；F1 Ultimate 充电 U | 多数 B/C=S；A9 Ultra Max U；F1 Ultimate C=U | 需按“接收器 + 本体配对组”建目录；后备写入必须单列门控 |
| O6 | ATK / `AtkPlugin` | FF05 新固件 `373B:1216` | `AtkFf05Codec.BuildQuery` | BatteryRead | 插件开关 + `CanWrite` | 无 | S4 | U | 没有支持矩阵条目；先补证据 |
| O7 | ATK / `AtkPlugin` | A98 Pro `373B:1152` | `AtkViaCodec.CreateQuery` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2 电量；充电位样本未正式登记 | B=S；C=U | 需目录与充电证据 |
| O8 | 狼蛛 / `AulaPlugin` | F99 / F99 Pro `3554:FA09` | V1 会话帧 + 0A 推送 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：10% 放电 / 充电；F99 采集包 80% 充电 | B=S；C=S | 与迈从 G75、凌豹 K98 共用 PID；必须按 ACK 型号码绑定 |
| O9 | 狼蛛 / `AulaPlugin` | L9 Ultra `38A6:28FD` | `0x81` 查询 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2（PR #27：真机读数与官方驱动一致；支持矩阵仍写“仅实现待验”） | B=S2（待矩阵复核）；C=U | 建议先把矩阵升级为“贡献者真机”，再补目录 |
| O10 | 狼蛛 / `AulaPlugin` | HERO 68 Air `372E:106C` | UUID + 分片查询 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：10% 放电 / 充电 | B=S；C=S | 同 PID 还有其它 HERO 型号，需 UUID/型号身份 |
| O11 | CHERRY / `CherryPlugin` | PIXIU75 `046A:01E4` / `01E2` | 块 `0x1A` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：电量 + 插线充电位 | B=S；C=S | 需目录；有线/无线两 PID 可显式配对 |
| O12 | 寸渊影弑 / `DarkrimPlugin` | SHADOW `3654:6901` | `GetBasicInfo` | BatteryRead、ChargingRead | 插件开关 + `CanWrite`；官方客户端让位 | 无 | S2：90% / 充电位校准 | B=S；C=S | 需目录；官方客户端运行时的让位规则应保留 |
| O13 | 前行者 / `EweadnPlugin` | X87 `0C45:FEFE` | 33 字节查询 | BatteryRead | 插件开关 + `CanWrite` | 无 | S2 | B=S；C=U | 官方不解析充电位；需目录 |
| O14 | 前行者 / `EweadnPlugin` | S9 `372E:1026` | Feature `0x03` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2 电量；充电位未单独验证 | B=S；C=U | 多型号共用 PID；需 UUID/型号身份 |
| O15 | 飞智 / `FlydigiPlugin` | APEX5 `37D7:2501` | NewXInput 心跳 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：充电位有实证 | B=S；C=S | 需目录；蓝牙 PID 不接入 |
| O16 | 盖世小鸡 / `GameSirPlugin` | G7 Pro CE `3537`（`10B9` / `0575`） | 心跳 + `0x12` 状态流 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：电量；充电只见过放电 | B=S；C=U | 不按 PID 白名单，需按 VID + 接口 + 型号目录 |
| O17 | 高斯 / `GanssPlugin` | GS3104T-Pro / 狼蛛 F108 Pro `05AC:024F` | 33 字节查询 | BatteryRead | 插件开关 + `CanWrite` | 无 | S2 | B=S；C=U | 两品牌同指纹；能力必须按产品串/型号区分 |
| O18 | HECATE / `HecatePlugin` | G5M Pro `35BB:D00B` | 主动查询 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：充电三态真机 | B=S；C=S | 需目录；型号可能不止 G5M Pro |
| O19 | HECATE / `HecateWirelessPlugin` | G3M Pro 升级版 `35BB:D3FB` | STATUS / POWER | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S1 电量；充电位未验证 | B=S；C=U | 型号写死；需目录与充电证据 |
| O20 | 英菲克 / `InphicIn9Plugin` | IN9 `248A:5B2F` | 刷新帧 + `GetFeature` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S1：电量 + 充电位 | B=S；C=S | 需目录；充电中只报状态、不报电量 |
| O21 | 凌豹 / `LingbaoPlugin` | M5PRO Max `3151:402D` | `F7` Feature 查询 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S1：充电 90% | B=S；C=S | 需目录；有线 4026 不认领 |
| O22 | 凌豹 / `LingbaoPlugin` | K98 `3554:FA09` | V1 会话帧 + 0A 推送 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：100% | B=S；C=U | 与 AULA/迈从共用 PID；必须按 ACK 型号码 `0x0212` 绑定 |
| O23 | 墨将 / `MojhonPlugin` | GALE2 `E2B7:122C` | `A5 04 04` 查询 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S1：充电/放电双态 | B=S；C=S | 需目录；型号自报 `C4_SERVER` / `C4_DONGLE` 可作身份 |
| O24 | 派世 / `PulsarPlugin` | X2 V2 `3554:F508` | STATUS + POWER | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：20% 未充电 | B=S；C=U | 充电切换未验证；需目录 |
| O25 | 雷蛇 / `RazerPlugin` | Viper V4 Pro `1532:00E6` / `00E5` | `0x80` / `0x84` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2 电量；充电 Unknown | B=S；C=U | 部分型号不报充电；需 per-model 目录 |
| O26 | 雷蛇 / `RazerPlugin` | BlackShark V2 Pro `1532:0555` | PA `0x21` / `0x2A` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：77% / Discharging | B=S；C=U | 实际充电切换未测 |
| O27 | 雷蛇 / `RazerPlugin` | Viper V3 Pro SE `1532:00DF` | Feature `0x80` / `0x84` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：61% / Discharging | B=S；C=U | 插线充电未测 |
| O28 | 雷蛇 / `RazerKrakenPlugin` | Kraken V4 `1532:056C` | `0x21` / `0x2A` | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | 支持矩阵“仅实现待验” | U | 需真机读数 |
| O29 | 索尼 / `InzoneH9Plugin` | INZONE H9 II `054C:0FA8` | `0xFF04` SET_REPORT | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2：78% / 充电位插线实测 | B=S；C=S | 需目录；只接这一个 PID |
| O30 | 雷神 / `ThunderobotPlugin` | ML903 NearLink Se `369B:0234` | `0xA0` TLV | BatteryRead | 插件开关 + `CanWrite` | 无 | S2 | B=S；C=U | 充电状态未知；需目录 |
| O31 | VGN / `VgnPlugin` | F1 V5 `391D:1A23` / `1026` | `0x06` 配置帧 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S1：电量 + 充电位 | B=S；C=S | 需目录；插线换 PID 需归一 |
| O32 | VGN / `VgnPlugin` | F2 大师+ `3554:FB56` / `FB57` | `BatteryLevel` 查询 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S1 电量；S2 充电（PR #34：插线 85% / Charging / Full） | B=S；C=S | 有线 PID 未复测；需目录 |
| O33 | VGN / `VgnPlugin` | V98 Pro `320F:5088` | `0x1A` 查询 | BatteryRead、ChargingRead | 插件开关 + `CanWrite` | 无 | S2（issue #125：作者真机验证 V98 PRO；PR #27 同结论） | B=S2（待矩阵复核）；C=U | 建议先升级矩阵并补目录；不要批量声明同族 |

## 3. 迈从能力覆盖

### 3.1 实际依赖主动查询的设备

迈从不是“一个协议”，而是至少十条不同链路：

| 链路 | 设备 | 主动行为 | 证据边界 |
|---|---|---|---|
| `keyboard-v1` | G87 `41E4:2001`、G75 Pro `3554:FA09`、G87 V2 `3837:201B` | 先发会话帧 `13 05 01`，再发 `13 4A 01` 电量查询 | G87 / G75 Pro 本机；G87 V2 贡献者 |
| `mouse-v2` | A7 V2 Pro `3837:100A`、G87 V2 `3837:201B` | 等 E2 被动帧，等不到发 `0x11/0x06` 触发帧 | 贡献者真机 |
| `mchose-0x4d` | `3837:1018` 等 dongle 后的多型号 | `0x0900` 设备信息、`0x0002` 性能、`0x0003` DPI | 仅 `0x1018` 有 0x0900 真机；DPI/回报率无真机 |
| `gmouse` | G3 V2 `3837:4245` | 65 字节 `AA 30` 查询 | 贡献者真机电量；充电未验 |
| `headset-v9` | V9 Pro `291D:385D` | 64 字节 `55 65 01` 查询 | 贡献者单点 100% |
| `headset-x9` | X9 `3837:6045` | 64 字节 `55 65 01` 查询 | 贡献者 80% / 90%；充电只见过放电 |
| `cz-block` | `3837:3033` 后的 G87 V2 / G75 V2 / K87S / K99 V3 | 65 字节 CZ 分块查询 | 本机真机电量 + 充电位 |
| `mouse-a5` | A5 V2 Ultra `3837:100C` | Feature `0x11` 反码查询 | 贡献者声明；支持矩阵仍为“仅实现待验” |

### 3.2 0x4D 的能力声明问题

**已确认事实**：

- `MchosePlugin.CapabilitiesOf` 先读 `MchoseDeviceCatalog.CapabilitiesOf(info)`；
- 后者只按 **USB 枚举的 VID/PID** 找 `transports[]`，并要求当前接口能跑 0x4D；
- 当前只有 `3837:1018` 在 `transports[]` 里声明 `batteryRead / connectionRead / chargingRead = supported`；
- `devices[]` 里的本体 PID 没有 `capabilities`；
- `MchoseDeviceCatalog.CapabilitiesOf` 的注释明确写着“传输通道的能力声明”，不是型号能力。

**代码推断**：即使 `3837:1018` 的 0x4D 通道声明了电量，也不能据此给该 dongle 下所有型号授权。
`0x1018` 是通用 8K dongle，K7 V2、A5 V3、A7 V3、K5、R7 等共用；型号只由 `0x0900` 自报的
配对 VID/PID 决定。`0x0900` 也可能在鼠标睡着时只回 `FF`，此时本体身份未确认。

**待真机**：当前唯一 0x0900 真机样本自报 `0x4026`，官方表是 A5 V3 Ultra+，而历史记录曾写 K7 V2 Ultra+
（`0x4028`）。这个身份冲突未关闭，不能把某个具体型号的 DPI/回报率/电量能力直接落库。

### 3.3 迈从的逐型号结论

| 型号 / 通道 | BatteryRead | ChargingRead | ConnectionRead | DpiRead | DpiLevelRead | RateRead | 说明 |
|---|---|---|---|---|---|---|---|
| G87 `41E4:2001` | S | U | U（仅 Inferred 2.4G） | — | — | — | 本机电量；充电标志只部分验证 |
| G75 Pro `3554:FA09` | S | U | U | — | — | — | 本机电量；与 AULA / 凌豹共用 PID |
| G87 V2 `3837:201B` | S | U | U | — | — | — | 贡献者 E2 / V1；充电只区分充/未充 |
| A7 V2 Pro `3837:100A` | S | U | U | — | — | — | 贡献者 E2；充电只区分充/未充 |
| 0x4D 通道 `3837:1018` | S（通道） | S（通道） | S（通道） | U | U | U | **不能直接当本体型号能力** |
| 0x4D 本体 `0x4026` | U（身份冲突） | U | U | U | U | U | 唯一 0x0900 样本；型号名未闭环 |
| 0x4D 其他 dongle `1014/1016/101C/101D` | U | U | U | U | U | U | 无真机能力证据 |
| G3 V2 `3837:4245` | S | U | U | — | — | — | 贡献者电量；充电未验 |
| V9 Pro `291D:385D` | S（单点） | U | U | — | — | — | 100% 单点；`[3]` 未校准 |
| X9 `3837:6045` | S | U | U | — | — | — | 80/90 两点；充电码未全验 |
| CZ `3837:3033` | S | S | U | — | — | — | 本机电量 + 充电位；型号仍需目录 |
| A5 V2 Ultra `3837:100C` | U | U | U | — | — | — | 支持矩阵“仅实现待验” |

**结论**：迈从当前只有“通道证据”和“少量单型号电量证据”，没有一份可用的“本体型号 → 能力”数据源。
不能因为 `3837:1018` 有声明，就给所有 0x4D 设备授权；也不能因为 G87 键盘有电量，就给同品牌鼠标授权。

## 4. 罗技能力覆盖

### 4.1 两条完全不同的路径

| 路径 | 身份 | 主动行为 | 当前授权 |
|---|---|---|---|
| Centurion | `046D:0AF7`，`0xFFA0/0x0001`，64 字节 | 固定 `51 08 00 03 1A 00 03 00 04 0A` 查询 | `WritesEnabled` + `CanWrite` |
| HID++ | `046D` 接收器或直连设备 | `Root.Ping`、`GetFeature`、电量级联、DPI、回报率 | `WritesEnabled` + `HidppIO.WritesAllowed` |

**已确认事实**：

- `LogitechPlugin` 没有覆写 `CapabilitiesOf`，基类返回 `CapabilitySet.Empty`；
- HID++ 会话在 `WritesEnabled` 为 false 时根本不创建；
- `HidppIO.WritesAllowed` 是 `Func<bool>?`；`null` 时 `Request` 不会拦截写入；
- 当前会话创建处会给它赋值，但这是“初始化正确才安全”的隐式前提，不是设备级能力门控；
- `AddDevice` 的 `DeviceStatus.Usb` 记录的是接收器 VID/PID；`Receiver` 记录接收器族；`SlotId` 才是逻辑设备身份。

### 4.2 能力应该绑在哪

罗技不能用“接收器 PID”授权。正确绑定层级至少是：

1. **接收器**：`046D:C548` 只说明这是 Bolt 通道；
2. **接收器槽位**：`SlotId = logitech:<接收器稳定路径哈希>:<slot>` 是逻辑设备；
3. **逻辑设备本体**：型号来自 `0x0005` 名称/类型或 `0xB5` 配对 WPID；C548 的 `0xB5` 实测无回包；
4. **查询会话**：`HidppIO` 的 report ID / 长短报文 / feature 下标是会话能力，不是设备能力。

因此，`046D:C548` + 槽位 1 的 MX Master 3S 证据，不能授权同一接收器上的槽位 2，也不能授权
Unifying / LIGHTSPEED / Nano 接收器下的任意设备。

### 4.3 罗技逐设备结论

| 设备 | 身份 | BatteryRead | ChargingRead | DpiRead | RateRead | 证据 |
|---|---|---|---|---|---|---|
| MX Master 3S（Bolt C548 槽位 1） | `046D:C548` + 槽位 | S | S（仅见放电态） | S（1000） | U（0x8060/0x8061 无应答） | S1 |
| PRO X 2 LIGHTSPEED | `046D:0AF7` | S | S | — | — | S1：45% / 43% 充电 |
| G304（C53F 槽位 1） | `046D:C53F` | 文档日志 90% | 未登记 | 文档日志 1600 | 文档日志 1000Hz | 文档日志，支持矩阵未列；按候选处理 |
| 其他 HID++ 设备 | 接收器/槽位/直连 | U | U | U | U | 无型号目录 |

**结论**：罗技需要一份“逻辑设备型号 → 能力”的目录，而不是继续按接收器 PID 推断。MX Master 3S
和 PRO X 2 已有足够证据，但当前没有数据源承载；G304 的日志证据需要正式登记后才能升级。

## 5. 北通现状复核

**已确认事实**：

- `BetopPlugin.ProbeAsync` 调用 `_engine.ProbeAsync(handle, WritesEnabled, BatteryReadVerified(handle.Info))`；
- `BetopPlugin.PollDevice` 调用 `_engine.Poll(..., BatteryReadVerified(dev.Info))`；
- `BatteryReadVerified` 只接受 `CapabilitiesOf(info).Of(BatteryRead) == Supported`；
- `CapabilitiesOf` 来自 `betop-catalog.json` 的 `devices[].capabilities`；
- `BetopActiveGateTests` 覆盖未授权探测零写入、未授权轮询 `NotAttempted`、未授权被动帧仍可读、
  已授权查询、未登记接口、`WritesEnabled=false`、T9 被动路径和目录三态。

**当前结论**：北通门控完整有效，是当前唯一可直接作为参考实现的插件。

边界：

- `20BC:5191` 仍只有上游实现证据，支持矩阵为“仅实现待验”，目录未声明 `batteryRead`，保持 `unverified`；
- `20DD:5128/5130` 只回 `02 CD`，不是电量帧，不能声明能力；
- `20BC:5158/5159` 的 `batteryRead` / `chargingRead` 有贡献者真机证据，现有声明与证据一致；
- T9 `20BC:5073` 是纯被动，`chargingRead=unsupported` 有明确“无充电位”证据。

## 6. 雷柏现状复核

**已确认事实**：

- `RapooPlugin.WritesEnabled => false`；
- `RapooPlugin.ProbeWrites => false`；
- `ProbeAsync` 只调用 `_engine.ProbeAsync(handle, WritesEnabled)`；
- `PollDevice` 只调用 `_engine.Poll(..., WritesEnabled)`，且 `status-report7` 等协议 `pollable=false`；
- 没有任何 `WriteOutput` / `SetFeature` / `SendOutputReport` 调用。

**当前结论**：雷柏仍是纯被动协议，没有主动查询路径。本任务不增加能力声明、不实施修改。

现有能力声明只描述被动解析：

- VT3 `24AE:1412`：`batteryRead` / `dpiRead` / `dpiLevelRead = supported`，`rateRead = unverified`；
- VT2S Air MAX V2 `1B13` / `4B13`：`batteryRead = supported`。

这些声明与主动查询门控无关；不要为了“让矩阵看起来完整”给雷柏补主动能力。

## 7. 其他主动查询插件的能力缺口

其他 33 条路径全部没有设备级能力声明，`CapabilitiesOf` 走基类 `CapabilitySet.Empty`。共同问题不是
“代码没有查询方法”，而是“查询方法存在，但没有一个可被门控读取的型号能力源”。

### 7.1 有较强电量证据、但缺目录的插件

这些设备已有 S1 / S2 电量证据，适合作为下一批“只补数据、不改行为”的候选：

- CHERRY PIXIU75（电量 + 充电位）
- 寸渊影弑 SHADOW（电量 + 充电位）
- 墨将 GALE2（电量 + 充电位）
- 索尼 INZONE H9 II（电量 + 充电位）
- 凌豹 M5PRO Max（电量 + 充电位）
- HECATE G3M Pro 升级版（电量）
- 英菲克 IN9（电量 + 充电位）
- 派世 X2 V2（电量）
- 黑爵 AK870（电量）
- 雷神 ML903（电量）
- AKG N9 Hybrid（电量）
- 前行者 X87 / S9（电量）
- 高斯 GS3104T-Pro / 狼蛛 F108 Pro（电量）

### 7.2 共享指纹、必须先解决身份再谈能力的插件

| 指纹 | 共用方 | 风险 |
|---|---|---|
| `3554:FA09` | 迈从 G75 Pro、AULA F99 / F99 Pro、凌豹 K98 | HID 层完全同型；必须靠会话 ACK 型号码分流 |
| `05AC:024F` | 高斯 GS3104T-Pro、狼蛛 F108 Pro | 同硅方案；必须靠产品串/型号区分 |
| `372E:1026` | 前行者 S9 Pro / Max / Ultra | 多型号共用 PID；必须靠 UUID/型号 |
| `248A:5B2F` / `5B2E` | 英菲克 IN9 的接收器 / 有线本体 | 充电时可能切换身份，不能把接收器能力给本体 |
| `373B` ATK 系列 | 多个接收器 PID + 本体 PID | 必须按实测配对组归一，不能按 dongle PID 泛化 |
| `046D` 罗技接收器 | Bolt / Unifying / LIGHTSPEED / Nano | 接收器 PID 不是设备型号；必须绑定槽位 + 本体身份 |
| `0B05:1ACE` 华硕 Omni | 多台 ROG 设备共用接收器 | 必须保留 `VerifyPairedModel` 的配对型号判据 |

### 7.3 当前声明仍为 unverified 的路径

以下路径当前没有 `supported` 声明，不要因为“代码能发帧”或“官方软件有功能”升级：

- 迈从 A5 V2 Ultra `3837:100C`（PR #20 有贡献者声明，支持矩阵仍“仅实现待验”）
- 狼蛛 L9 Ultra `38A6:28FD`（† PR #27 有 S2 真机证据，待矩阵复核）
- 雷蛇 Kraken V4 `1532:056C`（PR #20 有声明，支持矩阵仍“仅实现待验”）
- VGN V98 Pro `320F:5088`（† issue #125 / PR #27 有 S2 真机证据，待矩阵复核）
- ATK FF05 `373B:1216`
- ATK A9 Ultra Max `373B:11FE`（† issue #71 有 S2 帧，支持矩阵仍“仅实现待验”）
- 北通 `20BC:5191`（S3 上游实现）
- 北通 `20DD:5128/5130`
- 罗技未知槽位 / 未知型号

`†` 只表示“有足够证据可以进入复核”，不是已经写入能力声明；升级前仍按 `unverified` 处理。

## 8. 按设备和查询类型整理的能力矩阵

图例：`S` = `supported`，`U` = `unverified`，`X` = `unsupported`，`—` = 该路径不提供/不适用。

| 设备 | 身份 | BatteryRead | ChargingRead | ConnectionRead | DpiRead | DpiLevelRead | RateRead | 主动路径 |
|---|---|---|---|---|---|---|---|---|
| 迈从 G87 | `41E4:2001` | S | U | U | — | — | — | M4 |
| 迈从 G75 Pro | `3554:FA09` | S | U | U | — | — | — | M4 |
| 迈从 G87 V2 | `3837:201B` | S | U | U | — | — | — | M4 / M5 |
| 迈从 A7 V2 Pro | `3837:100A` | S | U | U | — | — | — | M5 |
| 迈从 0x4D 通道 | `3837:1018` | S（通道） | S（通道） | S（通道） | U | U | U | M1 / M2 / M3 |
| 迈从 0x4D 本体 | `0x4026`（身份待闭环） | U | U | U | U | U | U | M1 / M2 / M3 |
| 迈从 0x4D 其他 dongle | `1014/1016/101C/101D` | U | U | U | U | U | U | M1 / M2 / M3 |
| 迈从 G3 V2 | `3837:4245` | S | U | U | — | — | — | M6 |
| 迈从 V9 Pro | `291D:385D` | S（单点） | U | U | — | — | — | M7 |
| 迈从 X9 | `3837:6045` | S | U | U | — | — | — | M8 |
| 迈从 CZ 键盘 | `3837:3033` | S | S | U | — | — | — | M9 |
| 迈从 A5 V2 Ultra | `3837:100C` | U | U | U | — | — | — | M10 |
| 罗技 MX Master 3S | Bolt `C548` + 槽位 | S | S（仅见放电态） | U（Inferred） | S | — | U | L2 / L3 / L4 |
| 罗技 PRO X 2 | `046D:0AF7` | S | S | U | — | — | — | L1 |
| 罗技 G304 | `046D:C53F` + 槽位 | 文档日志 90% | U | U | 文档日志 1600 | — | 文档日志 1000Hz | L2 / L3 / L4 |
| 北通 T9 | `20BC:5073` | S | X | U | — | — | — | 纯被动，不计入 48 |
| 北通 鲲鹏20 有线 | `20BC:5158` | S | S | U | — | — | — | B1 |
| 北通 鲲鹏20 2.4G | `20BC:5159` | S | S | U | — | — | — | B1 |
| 北通 鲲鹏20 精英版 | `20BC:5191` | U | U | U | — | — | — | B1 |
| 北通 20DD 新批次 | `20DD:5128/5130` | U | U | U | — | — | — | 无电量帧 |
| 雷柏 VT3 | `24AE:1412` | S | S（被动） | S（被动） | S（被动） | S（被动） | U | 纯被动，不计入 48 |
| 雷柏 VT2S Air MAX V2 | `1B13/4B13` | S | U | U | — | — | — | 纯被动，不计入 48 |
| 黑爵 AK870 | `3151:4011` | S | U | U | — | — | — | O1 |
| AKG N9 Hybrid | `0ECB:20D9` | S | U | U | — | — | — | O2 |
| 华硕 Scope II 96 | `0B05:1ACE` | S | U | U | — | — | — | O3 |
| 华硕 ROG 耳机 | `0B05:1AFA` | S | S | U | — | — | — | O4 |
| ATK F1 Pro Max | `373B:101B/101A`（本体 `1015`） | S | S | U | — | — | — | O5 |
| ATK VXE R1 | `373B:10C9`（本体 `10C7`） | S | S | U | — | — | — | O5 |
| ATK F1 V3 ULTIMATE+ | `373B:1303`（本体 `12A9`） | S | S | U | — | — | — | O5 |
| ATK F1 Ultimate 2.0 | `373B:11D9/11E4` | S | U | U | — | — | — | O5 |
| ATK A9 Air 大师版+ | `373B:1278`（本体 `1293`） | S | S（充电读数偏高） | U | — | — | — | O5 |
| ATK A9 Ultra Max 2.0 | `373B:11FE` | U | U | U | — | — | — | O5 |
| ATK A98 Pro | `373B:1152` | S | U | U | — | — | — | O7 |
| ATK FF05 新固件 | `373B:1216` | U | U | U | — | — | — | O6 |
| 狼蛛 F99 / F99 Pro | `3554:FA09` | S | S | U | — | — | — | O8 |
| 狼蛛 L9 Ultra | `38A6:28FD` | S† | U | U | — | — | — | O9 |
| 狼蛛 HERO 68 Air | `372E:106C` | S | S | U | — | — | — | O10 |
| CHERRY PIXIU75 | `046A:01E4/01E2` | S | S | U | — | — | — | O11 |
| 寸渊影弑 SHADOW | `3654:6901` | S | S | U | — | — | — | O12 |
| 前行者 X87 | `0C45:FEFE` | S | U | U | — | — | — | O13 |
| 前行者 S9 | `372E:1026` | S | U | U | — | — | — | O14 |
| 飞智 APEX5 | `37D7:2501` | S | S | U | — | — | — | O15 |
| 盖世小鸡 G7 Pro CE | `3537` | S | U | U | — | — | — | O16 |
| 高斯 GS3104T-Pro | `05AC:024F` | S | U | U | — | — | — | O17 |
| 狼蛛 F108 Pro | `05AC:024F` | S | U | U | — | — | — | O17 |
| HECATE G5M Pro | `35BB:D00B` | S | S | U | — | — | — | O18 |
| HECATE G3M Pro 升级版 | `35BB:D3FB` | S | U | U | — | — | — | O19 |
| 英菲克 IN9 | `248A:5B2F` | S | S | U | — | — | — | O20 |
| 凌豹 M5PRO Max | `3151:402D` | S | S | U | — | — | — | O21 |
| 凌豹 K98 | `3554:FA09` | S | U | U | — | — | — | O22 |
| 墨将 GALE2 | `E2B7:122C` | S | S | U | — | — | — | O23 |
| 派世 X2 V2 | `3554:F508` | S | U | U | — | — | — | O24 |
| 雷蛇 Viper V4 Pro | `1532:00E6/00E5` | S | U | U | — | — | — | O25 |
| 雷蛇 BlackShark V2 Pro | `1532:0555` | S | U | U | — | — | — | O26 |
| 雷蛇 Viper V3 Pro SE | `1532:00DF` | S | U | U | — | — | — | O27 |
| 雷蛇 Kraken V4 | `1532:056C` | U | U | U | — | — | — | O28 |
| 索尼 INZONE H9 II | `054C:0FA8` | S | S | U | — | — | — | O29 |
| 雷神 ML903 | `369B:0234` | S | U | U | — | — | — | O30 |
| VGN F1 V5 | `391D:1A23/1026` | S | S | U | — | — | — | O31 |
| VGN F2 大师+ | `3554:FB56/FB57` | S | S | U | — | — | — | O32 |
| VGN V98 Pro | `320F:5088` | S† | U | U | — | — | — | O33 |

**注意**：表中 `S` 表示“该型号、该连接方式下，当前适配有足够证据”，不等于“同系列所有型号都支持”。
`—` 表示当前没有该主动查询路径，不表示设备没有该功能。

`†`：S2 证据来自 issue/PR 的真机记录，但支持矩阵仍保守写“仅实现待验”；这是数据复核候选，
不是当前代码已经声明的能力。见 §9.5。

## 9. 每项能力的证据来源

### 9.1 迈从

| 能力 / 设备 | 证据 | 等级 | 边界 |
|---|---|---|---|
| G87 电量 | `mchose.md` §1.3 真机 35% 与键盘指示一致 | S1 | 充电标志语义未全验 |
| G75 Pro 电量 | `mchose.md` §5.3 真机 36% | S1 | 充电只见过 `0x01` |
| G87 V2 / A7 V2 Pro E2 电量 | `mchose.md` §1.6 贡献者 E2 帧 68% | S2 | 充电只有布尔位，无 Full |
| 0x4D 0x0900 电量/充电/连接 | `mchose.md` §3.3 真机 100% / charge=0 / connect=2 | S2 | 只有一条样本，本体 PID 冲突 |
| 0x4D 0x0002 回报率 | 官方 JS + `Mchose0x4DRateTests` 合成帧 | S3 | 无真机 `0x0002` 样本 |
| 0x4D 0x0003 DPI | 官方 JS + `Mchose0x4DDpiTests` 合成帧 | S3 | 无真机 `0x0003` 样本 |
| G3 V2 电量 | `mchose.md` §4.3 真机 89% | S2 | 充电未在真机充电时抓包 |
| V9 Pro 电量 | `mchose-v9-pro.md` §3 贡献者 100% | S2 | 单点；`[3]` 不解释为充电 |
| X9 电量 | `mchose-x9.md` §3 贡献者 90% / 80% | S2 | 充电只见过 `0x02` |
| CZ 电量/充电 | `mchose.md` §2.4 本机 90/98/100 与 bit7 | S1 | 型号仍靠目录/产品串 |
| A5 V2 Ultra | 贡献者声明，支持矩阵“仅实现待验” | S4 | 维护者未复测 |

### 9.2 罗技

| 能力 / 设备 | 证据 | 等级 | 边界 |
|---|---|---|---|
| MX Master 3S 电量 | `logitech.md` §1.1 真机 95% | S1 | 仅 Bolt C548 槽位 1 |
| MX Master 3S DPI | `logitech.md` §4 真机 1000 | S1 | 只取 X 轴 |
| MX Master 3S 充电 | 同一日志 Discharging | S1 | 未做充电切换 |
| MX Master 3S 回报率 | `0x8060` / `0x8061` 均无应答 | S1 负结果 | 保持 Unknown，不能按规格补 125Hz |
| PRO X 2 电量/充电 | `logitech.md` §2 真机 45% / 43% 充电 | S1 | 只覆盖 2.4G USB，蓝牙走 HFP |
| G304 电量/DPI/回报率 | `logitech.md` §1.2 日志 90% / 1600 / 1000Hz | S2 候选 | 未在支持矩阵正式登记 |

### 9.3 北通与雷柏

| 能力 / 设备 | 证据 | 等级 |
|---|---|---|
| 北通 T9 电量 | `betop.md` §1.4 本机 248 条样本 | S1 |
| 北通 5158 电量/充电 | `betop.md` §1.8.1 贡献者 78% 充电中 | S2 |
| 北通 5159 电量/充电 | `betop.md` §1.8.2 贡献者 39% 广播 | S2 |
| 北通 5191 | 上游实现，支持矩阵“仅实现待验” | S3 |
| 雷柏 VT3 电量/DPI/档位 | `rapoo.md` §1.1 本机 | S1 |
| 雷柏 VT2S 电量 | `rapoo.md` §5 本机 | S1 |

### 9.4 其他厂商

证据来源以 [设备支持矩阵](../hardware/support-matrix.md) 的状态为准，逐项对应品牌文档：

- 黑爵 / 雷神：`ak870-ml903.md`；
- AKG：`akg.md`；
- 华硕：`asus-rog.md`；
- ATK：`atk.md`；
- 狼蛛：`aula.md`；
- CHERRY：`cherry.md`；
- 寸渊影弑：`darkrim.md`；
- 前行者：`eweadn.md`；
- 飞智：`flydigi.md`；
- 盖世小鸡：`gamesir.md`；
- 高斯：`ganss.md`；
- HECATE：`hecate.md`；
- 英菲克：`inphic.md`；
- 凌豹：`lingbao.md`；
- 墨将：`mojhon.md`；
- 派世：`pulsar.md`；
- 雷蛇：`razer.md`、`razer-headset.md`、`razer-viper-se.md`、`razer-kraken.md`；
- 索尼：`inzone-h9.md`；
- VGN：`vgn.md`；issue #125 的 18 类协议 / 155 个 VID-PID 映射，其中 F1 PRO MAX、V98 PRO 为作者真机验证。

### 9.5 issue / PR 外部贡献证据索引

以下是 2026-11-24 重新检索 issue/PR 后确认“有协议或真实数据、但未全部进入当前声明”的条目。
它们可以作为补数据/复核的证据，不等于可以直接合并代码，也不等于同系列自动 `supported`。

| 来源 | 设备 / 身份 | 可验证能力 | 证据 | 当前代码 / 声明 | 建议 |
|---|---|---|---|---|---|
| [Issue #128](https://github.com/evren-oct/vigil-watt/issues/128) | Logitech M590，`046D:C52B` Unifying，长接口 `0xFF00/0x0002` | BatteryRead | S2：同一设备短接口全超时，改 long 后 `90% Discharging` | 当前目录 `C52B` 仍标 `short`；M590 无型号目录 | 先修接口回退策略，再把 M590 作为“逻辑设备 + 槽位”证据；不能按 C52B 授权所有设备 |
| [Issue #127](https://github.com/evren-oct/vigil-watt/issues/127) + [补丁](https://github.com/user-attachments/files/35132351/incott-g23.patch) | Incott G23，`093A:522C`（有线 `622C`） | BatteryRead；ChargingRead 仅位图推断 | S2：本机 67% / 68%，`0x89` 应答 bit7 充电、低 7 位电量；充电未插线对照 | 当前无 Incott 插件 | 新品牌插件候选；`093A:522C` 是 PixArt 公版号段，必须用 `0x8F` 型号码只接 G23（2），不能按 PID 泛化 |
| [Issue #125](https://github.com/evren-oct/vigil-watt/issues/125) | VGN VHUB 全量协议包：18 类协议 / 155 个 VID-PID | BatteryRead / ChargingRead（按协议族） | S2：作者真机验证 F1 PRO MAX、V98 PRO；其余为官方前端静态分析 S3 | 当前 VGN 只实现 F1 V5、F2、V98 Pro 三条；无 VGN 设备目录 | 这是 VGN 目录的主数据源；先只给 F1 PRO MAX / V98 PRO 写 `supported`，其余保持 `unverified`，不要导入 155 条全声明 |
| [Issue #71](https://github.com/evren-oct/vigil-watt/issues/71) | ATK A9 Ultra Max 2.0，`373B:11FE` | BatteryRead、ChargingRead | S2：无线 50%、插线 50%、充电 65/70/75、满电 100%、拔线 65%；`[7]=1` 表示充电 | 当前 AtkPlugin 有 FF02 路径；支持矩阵仍“仅实现待验” | 复核后补 `11FE` 的型号能力；本体 PID 未知，不要按接收器 PID 给其它 ATK 型号 |
| [Issue #123](https://github.com/evren-oct/vigil-watt/issues/123) | 狼途 T98，`1A2C:8FFF`，65 字节 `00 AA 55 CC 33 0E ...` | BatteryRead；ChargingRead 候选 | S2：电量 71→74；`[8]` 随插线 0→1、拔线 1→0 | 当前 `LangtuT98Codec` 只读电量，明确把 `[7]` 当常量并返回 `Unknown`；未读 `[8]` | 先用新抓包复核 `[8]` 是否稳定充电位；这是被动插件，不进入主动门控，但可补 `chargingRead` 数据 |
| [Issue #115](https://github.com/evren-oct/vigil-watt/issues/115) | 狼蛛 SC580，`089D:062F`，33 字节帧 `00 D1 ...` | BatteryRead；ChargingRead 候选 | S2：电量 91→92；`[2]` 随充电 0→1 | 当前无该型号/插件 | 新 AULA 型号候选；先确认 PID 是否多型号共用，再建目录 |
| [Issue #114](https://github.com/evren-oct/vigil-watt/issues/114) + [PR #120](https://github.com/evren-oct/vigil-watt/pull/120) | GANSS GS3108T-PRO，`05AC:024F` | BatteryRead | Issue 采集包只有 4 字节 `05 A6 FF 01`，不含电量；PR #120 应用内读到 100% | 当前 Ganss 协议可复用；但 `LooksLikeGanss` 未含 GS3108 字符串（PR #120 补） | 以 PR #120 的应用内读数为准，不把 4 字节采集包当电量证据 |
| [Issue #107](https://github.com/evren-oct/vigil-watt/issues/107) + [PR #108](https://github.com/evren-oct/vigil-watt/pull/108) | `A8A5:2255`：Issue 产品串 `MCHOSE G7`；PR 产品串 `USB Receiver`，称 K-snake X11 | 电量尚未定位；PR 称 K-snake 81% / 充电 0→1 | Issue 抓包 65 字节 `00 AA FA AE ...`，无 `batteryReferences`；PR 是另一台设备/另一条 Gmouse 帧 | 当前无该型号；PR 未合并 | **同一 PID 至少出现两个型号/产品串**；先按产品串 + 帧头区分，绝不能只按 `A8A5:2255` 授权 |
| [Issue #94](https://github.com/evren-oct/vigil-watt/issues/94) | 西伯利亚 X7 Ultra Max，`36F9:AA20`，65 字节 `00 55 12 1A ...` | BatteryRead；ChargingRead 候选 | S2：采集分析给出 23% / 31%，电量在 offset 6；offset 5 随充电 0→1 | 当前无该品牌/插件 | 新品牌候选；先用更多电量点确认 offset 6 的线性与充电位 |
| [Issue #92](https://github.com/evren-oct/vigil-watt/issues/92) | 迈从 G75 V2，`3837:3033`，MI_01 标准页 `0x0001/0x0000`，65 字节 `55/AA` 帧 | BatteryRead；ConnectionRead 候选 | S2：`cmd=0x05`，`data[32]=0x58` = 88%，连接模式 `data[34]=2`；充电未测 | 当前 G75 V2 走 CZ `0x55 05` 路径；未实现这条标准页命令 | 作为 G75 V2 的交叉验证/备用路径；报告明确说协议无型号回执，不能凭 3033 区分 G75 V2 / G87 V2 / K87S / K99 V3 |
| [Issue #87](https://github.com/evren-oct/vigil-watt/issues/87) | IQUNIX Q101，`369B:0184`，64 字节 report `0xA1` | BatteryRead | S2：采集分析给出 36%，电量在 offset 8；充电未定位 | 当前无该型号 | 新品牌候选；先补充电/低电量样本 |
| [Issue #77](https://github.com/evren-oct/vigil-watt/issues/77) | Keychron Z6 Ultra 8K，`3434:D028` | BatteryRead 在 2.4G **不支持** | S2 负证据：六条 2.4G 路径全部证伪；蓝牙 GATT `0x180F` 可读 | 当前无 2.4G 插件 | 记录为 `unsupported`（2.4G 无电量通道），不要为它写主动查询 |
| [PR #126](https://github.com/evren-oct/vigil-watt/pull/126) | 雷蛇 Viper V3 Pro，`1532:00C1` | BatteryRead；ChargingRead 仅 0 | S2：真机 `C5` → 77%，`0x84` 返回 0；充电切换未测 | 当前只支持 Viper V3 Pro SE `00DF` | 扩展现有 Razer profile；00C1 / 00DF 分开声明，不能按 00DF 继承 |
| [PR #124](https://github.com/evren-oct/vigil-watt/pull/124) | 狼途 T87 `0C45:FEFE`；飞智 APEX6 `37D7:2502` | BatteryRead；ChargingRead | S2：T87 27% + 插线 0→1→0；APEX6 100% / Full | 当前无 T87 / APEX6 | 两个型号分别补目录；T87 与前行者 X87 同 `0C45:FEFE`，必须按产品串排除 |
| [PR #121](https://github.com/evren-oct/vigil-watt/pull/121) | 迈从 G98 V3 / K87 V3，`3837:1015`，0x4D 键盘通道 `0xFF80/0x0001` | BatteryRead、ChargingRead、ConnectionRead | S2：0x0900 真机 92% / charge=0 / connect=2；`0x4D` 键盘路径 | 当前分类器会把 64/64 吞成 MouseV2；无 KeyboardCz | 作为迈从 0x4D 键盘能力数据；**不要发 AA55 握手**，报告记录它会让 1015 重枚举成 1017 |
| [PR #120](https://github.com/evren-oct/vigil-watt/pull/120) | VXE R1SE+ `3554:F58E`；GANSS GS3108T-PRO `05AC:024F` | BatteryRead | S2：R1SE+ 20% / 3569 mV；GS3108T 100% | 当前 Atk/Ganss 无这两个精确型号 | 补精确指纹；`3554:F58E` 在 Compx 共用段，不能按 VID 泛化 |
| [PR #108](https://github.com/evren-oct/vigil-watt/pull/108) | K-snake X11，`A8A5:2255` | BatteryRead、ChargingRead | S2：81%，拔线 80% / 插线 81% 充电；Gmouse `AA 30` | 当前无 K-snake；与 #107 的 MCHOSE G7 同 PID | 先解决同 PID 多型号；不要把 K-snake 能力给 MCHOSE G7 |
| [PR #103](https://github.com/evren-oct/vigil-watt/pull/103) | VGN F1 Pro，`3554:F503` | BatteryRead | S2：95% / 未充电 / 4101 mV | 当前 `VgnY2Codec` 只认 FB56/FB57 | 扩展现有 VGN y2 匹配；充电未测，保持 `ChargingRead=unverified` |
| [PR #93](https://github.com/evren-oct/vigil-watt/pull/93) | AUSDOM G04，`363C:FF9D` | BatteryRead、ChargingRead（被动） | S2：90→91→95，充电位方向真机修正 | 当前无 Ausdom | 纯被动，不需要主动门控；如要接入，只做被动解析 |
| [PR #97](https://github.com/evren-oct/vigil-watt/pull/97) | VGN F2 `3554:FB56/FB57` | BatteryRead / 电压字段 | S2：同一帧 95% / 4076mV，官方显示 94%；电压曲线仅 F2 | 当前代码保留设备百分比，不采用电压平滑；PR 已关闭 | 只把它当“电压字段与官方显示差异”的证据，不引入估算覆盖设备上报值 |
| [PR #34](https://github.com/evren-oct/vigil-watt/pull/34) / [PR #33](https://github.com/evren-oct/vigil-watt/pull/33) | VGN F2、MCHOSE K7 V2 Ultra+、狼蛛 F108 Pro | BatteryRead / ChargingRead | S2：F2 85% 充电、Full；K7 100% / Full / connect=2；F108 充电字段不存在 | 代码已合入；F108 充电仍应为 `unsupported` | 用它们更新 VGN F2 充电结论；不要把 F108 的 `96→97` 当充电 |
| [PR #27](https://github.com/evren-oct/vigil-watt/pull/27) / [PR #31](https://github.com/evren-oct/vigil-watt/pull/31) | 狼蛛 L9 Ultra、VGN V98 Pro | BatteryRead | S2：三台与官方驱动显示一致；L9/V98 电量真机 | 代码已合入，但支持矩阵仍写“仅实现待验” | 建议复核后把 L9 / V98 升级为“贡献者真机”，再补目录 |
| [PR #20](https://github.com/evren-oct/vigil-watt/pull/20) | 迈从 A5 V2 Ultra `3837:100C`；Razer Kraken V4；Haute42 | BatteryRead | S2：A5 / Kraken 有真机声明；Haute 只读 feature | A5 支持矩阵仍“仅实现待验”；Kraken 仍“仅实现待验” | 先复核 A5/Kraken 的维护者可复现读数，再升级；不要因 PR 合并自动升级 |
| [PR #19](https://github.com/evren-oct/vigil-watt/pull/19) | 迈从 A7 V2 Ultra+ `3837:100B` | BatteryRead；RateRead 候选 | S2：真机 98% / 1000Hz；Feature `0x11` + E2 | 当前无 `100B` 型号；A7 V2 Pro `100A` 走 MouseV2 | 新迈从 feature 路径候选；需确认 100B 与 100A 的型号/协议关系 |
| 已合并且已进文档的贡献 | #15/#16/#17/#18/#21/#22/#23/#25/#29/#35/#36/#38/#40/#44/#45/#47/#48/#49/#53/#54/#58/#61/#65/#72/#75/#79 | 已有品牌文档中的 S2 证据 | 这些是当前文档和测试的主要来源 | 已合入/已记录 | 不重复采集；需要时只补 `devices[]` 能力与证据指针 |

## 10. 不能直接授权的设备及原因

### 10.1 迈从

| 设备 / 通道 | 为什么不能直接授权 |
|---|---|
| 所有 `3837:1014/1016/1018/101C/101D` 后的 0x4D 本体 | dongle PID 被多型号共用；本体型号只由 `0x0900` 自报，睡着时可能没有身份 |
| `0x4026` / `0x4028` | 唯一真机自报与历史记录冲突；身份未闭环前不能把样本能力落到具体型号 |
| G87 V2 / A7 V2 Pro 的 `3837:100A/201B` | 这是接收器/通道；E2 链路没有 0x0900 本体身份，不能把通道能力给所有设备 |
| A5 V2 Ultra `3837:100C` | 支持矩阵仍是“仅实现待验”，没有维护者可复现的读数 |
| G3 V2 / V9 Pro / X9 | 有电量证据，但当前没有设备目录承载；不能靠 C# 品牌级硬编码“全部支持” |

### 10.2 罗技

| 设备 / 通道 | 为什么不能直接授权 |
|---|---|
| Bolt / Unifying / LIGHTSPEED / Nano 接收器 PID | 接收器是传输通道，不是设备本体；槽位与型号必须另行确认 |
| C548 槽位 1 以外的槽位 | 没有对应型号/能力证据；不能从 MX Master 3S 继承 |
| 未知型号的 HID++ 设备 | 没有 wireless PID / 名称匹配；不能按 `0x046D` 或接收器 PID 泛化 |
| `HidppIO.WritesAllowed == null` 的会话 | 当前代码不会拦截写入；门控前应把 null 视为拒绝或强制赋值 |

### 10.3 北通与其他厂商

| 设备 | 为什么不能直接授权 |
|---|---|
| `20BC:5191` | 只有上游实现，支持矩阵“仅实现待验” |
| `20DD:5128/5130` | 只回 `02 CD`，不是电量帧；电量格式未知 |
| `3554:FA09` 三家共用 | HID 层同指纹；必须先按会话 ACK 型号码确认品牌/型号 |
| `05AC:024F` 两家共用 | 同硅方案；必须按产品串/型号确认 |
| `372E:1026` 多型号 | 同 PID 多型号；必须靠 UUID/型号 |
| `3837:3033` 迈从通用 dongle | G75 V2 / G87 V2 / K87S / K99 V3 共用；0x55/AA 路径无型号回执，必须靠产品串或其它型号命令 |
| `A8A5:2255` | issue #107 是 MCHOSE G7，PR #108 是 K-snake X11；同 PID、不同产品串/帧头，不能按 PID 授权 |
| `093A:522C` | PixArt 公版号段，G23/G24/G29 共用；必须用 `0x8F` 型号码只接 G23 |
| `3554:F503` / `3554:F58E` | VGN F1 Pro / VXE R1SE+ 落在 Compx 共用段；必须带精确 PID + usage + 长度 |
| `0C45:FEFE` | 狼途 T87 与前行者 X87 同号段；PR #124 按产品串排除前行者 |
| VGN 155 个 VID-PID | 18 类协议，只有 F1 PRO MAX / V98 PRO 有作者真机证据；其余不能批量声明 |
| `373B` ATK 接收器族 | 接收器/本体配对关系因型号而异；不能按 dongle PID 授权 |
| `0B05:1ACE` 华硕 Omni | 接收器下可能有多台设备；必须保留配对型号核实 |
| 雷蛇 Kraken V4、狼蛛 L9、VGN V98 Pro、ATK FF05、ATK A9 Ultra Max | 已有实现或社区数据，但支持矩阵尚未升级；先复核再声明 |

## 11. 缺少能力数据源的插件及建议

### 11.1 已有目录，但结构不够

| 插件 | 现有数据源 | 问题 | 最小可行方案 |
|---|---|---|---|
| 迈从 | `mchose-catalog.json` 的 `transports[]` + `devices[]` | 能力挂在传输通道；`devices[]` 无能力；本体身份来自运行时 | 保留 `transports[]` 只描述通道；在 `devices[]` 增加逐本体 PID 的 `capabilities`；认领后按 `0x0900` 自报 VID/PID 绑定；不要把 transport capabilities 直接返回给 `DeviceStatus` |
| 罗技 | `logitech-hidpp.json` 只有接收器/曲线 | 没有逻辑设备型号与能力 | 在同一文件增加 `devices[]`：`wirelessPid` / 归一化名称 / 直连 PID / 连接方式 / `capabilities`；槽位身份用 `SlotId`；匹配不到一律 `unverified` |
| 北通 | `betop-catalog.json devices[].capabilities` | 结构已满足；个别型号缺证据 | 只补有真机证据的条目；`5191` 未复测前不加 |
| 雷柏 | `rapoo-catalog.json devices[].capabilities` | 纯被动，不需要主动门控 | 不为本任务新增字段 |

### 11.2 没有目录的主动查询插件

这些插件目前只有 C# 查询代码，没有可读的型号能力源：

- 黑爵、AKG、华硕、ATK、狼蛛、CHERRY、寸渊影弑、前行者、飞智、盖世小鸡、高斯、HECATE、
  英菲克 IN9、凌豹、墨将、派世、雷蛇、索尼 INZONE、雷神、VGN。

**建议的最小方案**：

1. 优先复用现有 `devices[]` 结构：`vid`、`pid`、`model`、`kind`、`protocol`、`group`、`pairedPid`、`capabilities`；
2. 每个品牌一个 `<brand>-catalog.json`，不要建全局能力 JSON；
3. 能走 `ProtocolEngine` 的插件直接复用 `ProtocolSpecLoader` 的设备 DTO；不能走的插件只增加一个品牌内小加载器；
4. 能力只按“型号 + 连接方式 + 已确认接口”声明，不按品牌或 dongle PID 泛化；
5. 共享 PID 的型号必须把 ACK 型号码、UUID、自报型号或产品串作为匹配键；
6. 没有证据的型号不写 `supported`，省略即 `unverified`。

### 11.3 不需要新增主动能力源的插件

纯被动插件不需要为 M2 主动门控增加能力：Delux、Haute42、Ilovbee、Langtu T98、Aula G9、
HyperX Cloud Flight 2、DualSense、Inphic ZHEN、Rapoo。它们的能力若将来要用于 UI，另按
`DeviceCapability` 的只读语义单独评估。

### 11.4 可直接复用的 issue/PR 数据源

| 数据源 | 能补什么 | 不能直接做什么 |
|---|---|---|
| Issue #125 VGN 协议包 | 18 类协议、155 个 VID-PID；F1 PRO MAX / V98 PRO 真机 | 不能把 155 条全部写成 `supported`；先建 VGN 型号目录，只给两个已验证型号 |
| Issue #127 Incott 补丁 | G23 的 `0x8F` / `0x89` feature 协议、67/68% 真机 | 不能按 `093A:522C` 给 G24/G29；必须校验型号码 |
| Issue #123 / #115 / #94 / #87 | Langtu T98 充电位候选、AULA SC580、Xiberia X7、IQUNIX Q101 的真实采集 | 采集包不是能力声明；先确认字节语义和型号判别键 |
| Issue #92 / PR #121 / PR #19 | 迈从 G75 V2、G98 V3/K87 V3、A7 V2 Ultra+ 的真实电量/连接数据 | 不能解决 `0x4026` / `0x4028` 冲突；0x4D 仍要按自报身份绑定 |
| PR #126 / #124 / #120 / #108 / #103 | Razer 00C1、狼途 T87、飞智 APEX6、VXE R1SE+、GANSS GS3108T、K-snake X11、VGN F1 Pro 的真机数据 | 不能直接合并代码；先按当前架构重写/补目录 |
| PR #34 / #33 / #27 / #31 | VGN F2 充电、Mchose K7、AULA L9、VGN V98 Pro 的真机证据 | 不能把同系列其它型号一起升级 |
| Issue #77 Keychron | 2.4G 无电量通道的负证据 | 不要写插件；需要电量走蓝牙 |

## 12. 建议的下一批数据补齐顺序

### 12.1 第一批：先解身份，不碰行为

1. **迈从 0x4D 本体身份闭环**：用真机 `0x0900` 复核 `0x4026` / `0x4028`，确认型号名与配对关系；
2. 在 `mchose-catalog.json devices[]` 给已确认本体型号补 `batteryRead` / `chargingRead` / `connectionRead`；
3. 把 `transports[1018]` 从“设备能力来源”降为“通道事实”，不直接返回给 `DeviceStatus`；
4. 用 PR #121 的 G98 V3 / K87 V3 数据补 0x4D 键盘通道，用 issue #92 的 G75 V2 数据做 CZ 路径交叉验证；
5. 迈从 0x0002 / 0x0003 仍保持 `unverified`，直到拿到真机 DPI / 回报率样本。

### 12.2 第二批：罗技逻辑设备目录

1. 为 Bolt C548 槽位 + MX Master 3S 建一条逻辑设备能力；
2. 为 `046D:0AF7` PRO X 2 建直连设备能力；
3. 用 issue #128 的 C52B + M590 数据补“长接口回退 + 逻辑设备”证据；
4. 把 `HidppIO.WritesAllowed == null` 明确为拒绝，或保证会话创建时必定赋值；
5. G304 的日志证据先登记为候选，不直接升 `supported`。

### 12.3 第三批：单型号、专用 PID 的电量插件

优先选择身份明确、无共享 PID 的设备：

- CHERRY PIXIU75；
- 寸渊影弑 SHADOW；
- 墨将 GALE2；
- 索尼 INZONE H9 II；
- 凌豹 M5PRO Max；
- HECATE G3M Pro 升级版；
- 英菲克 IN9；
- 派世 X2 V2；
- 黑爵 AK870；
- 雷神 ML903；
- AKG N9 Hybrid；
- 前行者 X87 / S9。

可直接从 issue/PR 取数的精确型号候选（仍必须带型号判别键，不能按共享段泛化）：

- VGN F1 Pro `3554:F503`（PR #103）；
- VXE R1SE+ `3554:F58E`（PR #120）；
- GANSS GS3108T-PRO `05AC:024F`（PR #120）；
- Razer Viper V3 Pro `1532:00C1`（PR #126）；
- 狼途 T87 `0C45:FEFE`、飞智 APEX6 `37D7:2502`（PR #124）；
- Incott G23 `093A:522C`（issue #127）；
- Xiberia X7 `36F9:AA20`（issue #94）；
- IQUNIX Q101 `369B:0184`（issue #87）；
- AULA SC580 `089D:062F`（issue #115）；
- AUSDOM G04 `363C:FF9D`（PR #93，纯被动）。

### 12.4 第四批：共享指纹与接收器族

最后处理：

- `3554:FA09`（迈从 G75 / AULA / 凌豹 K98）；
- `05AC:024F`（高斯 / 狼蛛 F108）；
- `372E:1026`（前行者多型号）；
- `3837:3033`（迈从 G75 V2 / G87 V2 / K87S / K99 V3）；
- `A8A5:2255`（MCHOSE G7 / K-snake X11）；
- `093A:522C`（Incott G23 / PixArt 同号段）；
- `3554:F503` / `3554:F58E`（VGN F1 Pro / VXE R1SE+）；
- `0C45:FEFE`（狼途 T87 / 前行者 X87）；
- `373B` ATK 接收器/本体配对组；
- `0B05:1ACE` 华硕 Omni；
- 罗技多接收器族；
- VGN 的 155 个 VID-PID（先建目录，再逐型号声明）。

这些必须先有“型号判别键”，否则能力数据一定会被错误继承。

## 13. 当前是否具备逐插件实施门控的条件

### 13.1 结论

**不具备全面实施条件。**

| 插件 | 是否可立即实施 | 原因 |
|---|---|---|
| 北通 | 已完成 | `BatteryReadVerified` + 目录 + 测试；保持参考实现 |
| 雷柏 | 不适用 | 纯被动，无主动查询 |
| 迈从 | 不可以 | 只有传输通道能力；本体型号身份未闭环；DPI/回报率无真机 |
| 罗技 | 不可以 | 没有逻辑设备型号目录；接收器 PID 不能授权；`WritesAllowed` 可空 |
| 其他主动查询插件 | 不可以 | 全部没有设备级能力源 |

issue/PR 复核补充了若干 S2 证据（VGN F2 充电、L9 / V98 Pro、Razer 00C1、狼蛛 T87、飞智 APEX6、
VXE R1SE+、GANSS GS3108T、K-snake X11、VGN F1 Pro、Incott G23 等），但它们仍缺“当前插件可读取的
型号能力源”。**没有新增一个可以立即全面开门控的插件。**

### 13.2 可以立即做的事

只做数据与文档，不改变查询行为：

1. 关闭迈从 0x4D 本体身份冲突；
2. 给迈从、罗技建立“逻辑设备型号 → 能力”的最小目录；
3. 给专用 PID、已有 S1/S2 电量证据的插件补 `devices[]`；
4. 先把 issue/PR 中已确认的 S2 候选（VGN F2 充电、L9 / V98 Pro、Razer 00C1、狼途 T87、
   飞智 APEX6、VXE R1SE+、GANSS GS3108T、K-snake X11、VGN F1 Pro、Incott G23）登记为候选；
5. 为共享指纹设备补型号判别键；
6. 为每条能力写证据指针，禁止把 S3/S4 升为 `supported`。

### 13.3 不应立即做的事

- 不把 `3837:1018` 的传输能力复制给所有 0x4D 设备；
- 不按罗技接收器 PID 给所有连接设备授权；
- 不把 `3554:FA09` / `05AC:024F` 的某个型号能力继承给同 PID 的其它品牌；
- 不为纯被动插件新增主动查询能力；
- 不引入全局能力 JSON 或通用能力框架；
- 不在数据补齐前切换门控行为。

## 14. 边界声明

- 本文没有修改任何 `.cs`、协议 JSON、设备 catalog、测试或运行行为；
- 本文没有执行真机写入、没有打开设备、没有发送报文；
- 本文只读检索了 issue/PR；附件下载到工作区临时目录核对后已删除，未进入仓库；
- 本文没有把 M2 阶段 2 标为完成；它只完成能力覆盖与证据盘点；
- 所有“当前结论”都区分了 S1/S2 真机证据与 S3/S4 代码/静态证据；
- 下一步应等待审查，不自行进入门控实施。
