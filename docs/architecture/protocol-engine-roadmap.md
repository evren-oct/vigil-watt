# 通用协议引擎治理路线图

> 状态：M1 文档收尾完成；M2 阶段 1 方案定稿，受能力覆盖阻塞，待实施。
> 基线：2026-11-23，`dev` 分支 `d117b59`。严格 JSON 加载代码提交为 `6315988`；本文档体系本轮改动尚未提交。
> 本文只定方向、阶段和完成标准；字段词表、插件契约、硬件安全与品牌事实继续由现有专题文档承载。

## 1. 文档状态与阅读方式

这套文档回答“通用协议引擎长期往哪走、先做什么、什么算完成”。具体到某一套协议怎么写字、某一个插件怎么实现、某一台设备怎么验证，仍看对应专题：

- 新会话先读 [`docs/project/handoff.md`](../project/handoff.md) 和本文；
- 改插件或新增品牌，再读 [`docs/dev/plugin-standard.md`](../dev/plugin-standard.md) 与 [`docs/dev/vendor-onboarding-process.md`](../dev/vendor-onboarding-process.md)；
- 改 JSON，再读 [`docs/dev/protocol-json.md`](../dev/protocol-json.md)；
- 涉及写入，先读 [`docs/hardware/safety.md`](../hardware/safety.md) 和本文的 [`active-query-safety.md`](active-query-safety.md)。

状态词沿用插件标准：

| 标记 | 含义 |
|---|---|
| **已实现** | 当前代码里存在对应类型、方法或配置 |
| **已验证** | 有真机读数、抓包或单测钉住的证据 |
| **已提交** | 以 `git log` / `git status` 为准，不靠文档记忆 |
| **规划 / 待开展** | 尚未落地，不得写成已有能力 |
| **待审计** | 当前无法从代码或证据确认，先标记，不猜 |
| **禁止** | 违反只读原则、安全规则或本路线图 |

**命名说明**：本文的“治理阶段 M1–M6”与 `docs/hardware/brands/mchose.md`、`docs/project/changelog.md` 中既有的“M2-1～M2-5（迈从协议里程碑）”不是同一套编号。历史文档中的 M2-x 不改名；以后讨论协议引擎治理时，统一说“治理阶段 M2”或“治理阶段 M4”。

## 2. 背景与治理目标

VigilWatt 的 2.4G 设备支持已经从“每个品牌一套 C# 编解码”发展成“协议 JSON + 解释器 + 插件编排”的混合形态。这个形态解决了固定帧协议的数据化问题，但也留下了新的边界问题：

- 哪些事实必须进 JSON，哪些判断必须留 C#，目前分散在多份文档和代码注释里；
- 主动查询的授权只看插件开关，设备级能力门控尚未覆盖所有路径；
- 测试有大量真实帧，但没有统一的证据索引和覆盖矩阵；
- 设备身份、能力声明、统一遥测各自有模型，但衔接规则没有一份总规范。

治理目标不是把所有协议都塞进一个通用 DSL，而是让后续新厂商、新型号、新能力接入时，能直接回答四个问题：

1. 这项事实应该放在 JSON、插件还是 Core？
2. 这个主动查询是否被授权，失败时会不会伤害设备？
3. 这项能力有没有证据，测试能否复现？
4. 这条读数进入统一状态后，`Unknown`、`Unsupported`、`Unverified` 分别是什么意思？

## 3. 通用协议引擎的架构定位

`ProtocolEngine` 是**声明式数据协议解释器**，不是通用设备驱动，也不是状态机执行器。

它负责：

- 按目录和接口指纹认领纯数据协议；
- 先被动监听，再按 `active` 规格查询；
- 按 JSON 偏移、掩码、码表解析电量、充电、链路、连接方式和指针遥测；
- 提供纯配置的能力查询。

它不负责：

- 握手、会话、密码门、型号 ACK；
- 校验和、CRC、加密；
- 多帧组合、跨报告状态机；
- 电压到百分比的换算；
- 离线判定、读数过滤、显示名启发式、身份归一；
- 设备配置写入。

四层职责如下：

| 层 | 负责 | 不负责 |
|---|---|---|
| **Core** | HID 枚举、认领、插件生命周期、轮询节拍、状态汇聚、历史、设置、日志 | 品牌帧偏移、VID/PID、命令码、显示名规则 |
| **插件** | 品牌候选过滤、探测编排、协议分派、身份归一、显示名、离线参数、能力绑定 | 跨品牌通用机制；把品牌事实写进 Core |
| **协议 JSON** | 接口指纹、帧布局、字段偏移、充电码表、查询模板、设备目录、能力声明 | 握手顺序、会话状态机、校验和算法、电压换算 |
| **App** | 展示、托盘、小组件、设置页 | 解析协议、决定设备是否在线 |

依赖方向只有 `VigilWatt.App → VigilWatt.Core`。插件不依赖 App，Core 不依赖 UI。

## 4. JSON 与 C# 的分工

判断口径只有一句：**“这个值是什么意思”优先进 JSON，“知道意思之后做什么”留 C#。**

| 优先放 JSON | 留 C# |
|---|---|
| VID/PID、型号、`kind`、`role`、`group`、`pairedPid`、`wired`、`capabilities` | 握手、会话、密码门 |
| 接口指纹、报告长度、`pathContains`、`report ID` | 校验和、CRC、加密 |
| 查询帧模板、窗口、重试次数 | 多帧组合、跨报告状态机 |
| 字段偏移、类型、位宽、`mask`、`xor` | 电压换算、线性换算、跨字节位域 |
| 充电码表、链路码表、`fullAt`、`chargeWhenMissing` | 离线策略、轮询节拍、稳定窗口、满电口径 |

现有 JSON 能表达的数据，不应为了“代码方便”重新硬编码；现有 JSON 表达不了的流程，也不应为了“统一”硬塞进 JSON。详细词表与校验规则见 [`docs/dev/protocol-json.md`](../dev/protocol-json.md)。

## 5. 当前主要架构问题

以下问题按“影响长期维护”排序，不按修起来是否容易：

| 问题 | 现状 | 影响 | 归属阶段 |
|---|---|---|---|
| 主动查询授权未全路径统一 | `ProtocolEngine` 已有 `activeAllowed` 接缝；北通按 `batteryRead=supported` 门控；其它引擎调用点和直接 HID 插件仍主要依赖插件开关 | 未验证设备可能被主动查询 | M2 |
| `codeFamily` 只有声明 | `ProtocolSpecLoader` 只校验非空；没有运行时执行器 | 容易被误读为“已支持通用有状态协议” | M4 |
| 目录加载器不统一 | `ProtocolSpecLoader`、`MchoseDeviceCatalog`、`HidppCatalog` 三套 | 严格校验、错误定位和扩展规则不一致 | M4 |
| 插件仍有品牌常量 | 例如 VID 常量、共享指纹分流 | JSON 边界不彻底，品牌事实可能回流 Core | M4 |
| 能力枚举含写入项 | `DeviceCapability` 仍有 `DpiWrite` / `RateWrite` | 与只读产品原则冲突，目录可能误声明 | M4 |
| Golden Frame 分散 | 真实帧写在多个测试类常量里 | 证据来源、覆盖范围和缺口不直观 | M3 |
| 遥测只统一了一部分 | `PointerTelemetry`、`Connection` 有统一模型；电量是 `int?`、充电是 `ChargingState` | 新厂商接入时容易各自解释 `Unknown` | M5 |
| 严格加载范围有限 | 只有 `ProtocolSpecLoader` 严格拒绝未知字段；`schemaVersion` 只在北通目录出现；重复 `VID:PID` 当前后写覆盖前写 | 配置错误可能绕过校验 | M1 已知边界，后续治理 |

## 6. 六个治理阶段

| 阶段 | 目标 | 主要工作 | 前置依赖 | 完成标准 | 当前状态 |
|---|---|---|---|---|---|
| **M1 协议定义与加载基础治理** | 让 JSON 定义可验证、可定位、可维护 | DTO 未知字段拒绝、错误定位、目录清理、文档收尾 | 无 | 加载失败可定位；字段边界有测试；高层标准与字段参考分工清楚 | **主要实施已完成**；代码提交 `6315988`；本轮文档收尾完成但未提交 |
| **M2 主动查询安全治理** | 所有主动查询路径默认安全 | 写入点清单、全局/插件/设备门控、握手覆盖、直接 HID 覆盖、测试矩阵 | M1 | 未登记设备零写入；能力未验证零写入；已授权路径有回归测试 | **进行中（阶段 1 完成）**；`4c08ba3` 已落地北通先行门控与 `activeAllowed` 接缝；盘点见 [审计报告](../reviews/2026-11-23-active-query-write-audit.md)，方案见 [门控设计](../reviews/2026-11-23-active-query-gate-design.md)，实施待能力覆盖 |
| **M3 协议回归测试与证据体系** | Golden Frame 与覆盖矩阵可追溯 | 证据索引、最小测试集、主动查询授权测试、执行规范 | M1、M2 | 每个协议族有来源明确的样本；每个主动查询插件有零写入测试 | **待开展**；现有测试分散，无统一证据目录 |
| **M4 协议模型与架构边界收敛** | JSON/C#/插件边界稳定 | `codeFamily` 边界、品牌常量治理、能力枚举冲突、目录加载器边界 | M1、M2 | 边界有明文；无新增通用执行器/加载器；品牌事实不进入 Core | **待开展**；已有 `CapabilitySet`、身份快照等局部基础，不等于阶段完成 |
| **M5 设备目录、插件与统一遥测衔接** | 目录、能力、身份、遥测形成一致链路 | 能力证据、身份归一、遥测语义、厂商差异保留 | M3、M4 | 新厂商可按清单接入；能力/来源/时刻/未知值语义一致 | **待开展（按需）**；已有三类目录加载器与指针遥测基础 |
| **M6 运行诊断与长期维护** | 治理结果可观察、可维护 | 门控日志、诊断信息、文档审查节奏、回归维护 | M2、M3 | 能回答“为什么没查询/为什么离线”；路线图与规范定期复核 | **待开展（按需）** |

阶段依赖不是流水线：M4 可以在 M2 进行时并行做边界梳理；M6 可以在 M2/M3 有稳定输出后按需推进。但 M2 未完成前，不应把“主动查询已安全”写成完成状态。

## 7. 当前进度与下一步

M1 的代码部分已经提交：`ProtocolSpecLoader` 对协议、设备、嵌套 DTO 与 `override` 开启严格加载，错误带资源、条目、`VID:PID` 与 JSON Path；对应测试在 `ProtocolSpecLoaderTests`。本轮补齐高层规范与文档索引，M1 的文档收尾算完成。

M2 已完成阶段 0 和阶段 1：写入点静态盘点见 [主动查询写入点审计](../reviews/2026-11-23-active-query-write-audit.md)，门控方案见 [主动查询门控方案](../reviews/2026-11-23-active-query-gate-design.md)。已有先行成果：

- `4c08ba3` 为 `ProtocolEngine.ProbeAsync` / `Poll` 增加 `activeAllowed`；
- `BatteryPluginBase` 增加 `BatteryReadVerified`；
- 北通按目录 `batteryRead=supported` 门控主动查询；
- `BetopActiveGateTests` 覆盖未授权零写入、已授权查询、被动帧、未登记接口和写入关闭。

但 M2 还不能标完成：能力声明只覆盖北通、雷柏和迈从的少量通道，绝大多数主动查询插件没有设备级能力数据；全面开门控会误停现有设备。下一步先补能力覆盖，再按插件逐批实施。

## 8. 明确不纳入治理范围

- 设备配置写入：DPI 写入、回报率写入、灯光、宏、固件更新；
- 通用协议 DSL、通用状态机执行器、统一目录加载器；
- 把所有品牌插件重构成同一种架构；
- 新建报文采集平台或要求本次采集新硬件；
- 重设计整个统一遥测系统；
- HTTP API、账号体系、游戏内显示；
- UI 改版。

这些事项不是“永远不做”，而是不属于当前治理阶段；要做必须另立设计、证据和安全边界。

## 9. 专项规范索引

| 文档 | 回答什么 | 什么时候读 |
|---|---|---|
| [protocol-definition-standard.md](protocol-definition-standard.md) | JSON 与 C# 的高层边界、加载与兼容原则 | 新增/修改协议 JSON、评估 `schemaVersion` |
| [active-query-safety.md](active-query-safety.md) | 哪些写入需要什么授权，如何失败关闭 | 任何主动查询、握手、会话帧、探测 |
| [device-identity-and-capabilities.md](device-identity-and-capabilities.md) | 设备身份、接口匹配、能力声明与证据 | 新增型号、合并双连接、声明能力 |
| [protocol-regression-testing.md](protocol-regression-testing.md) | Golden Frame、测试分层与最小可靠测试集 | 改帧解析、加插件、补回归 |
| [protocol-telemetry-integration.md](protocol-telemetry-integration.md) | 厂商语义如何进入统一遥测 | 新增遥测字段、处理 Unknown/来源/过期 |

现有文档继续承担原职责：

- [`docs/dev/protocol-json.md`](../dev/protocol-json.md)：JSON 字段、示例、加载期校验；
- [`docs/dev/plugin-standard.md`](../dev/plugin-standard.md)：插件契约、三种架构、目录/协议切分；
- [`docs/dev/vendor-onboarding-process.md`](../dev/vendor-onboarding-process.md)：新厂商六步流程；
- [`docs/hardware/safety.md`](../hardware/safety.md)：事故复盘、写入规则、逐插件开关；
- [`docs/project/architecture.md`](../project/architecture.md)：模块边界、线程模型、数据存储；
- [`docs/project/pointer-telemetry.md`](../project/pointer-telemetry.md)：指针遥测模型与 UI 口径；
- [`docs/hardware/support-matrix.md`](../hardware/support-matrix.md)：逐台验证状态与文档入口。

## 10. 维护规则

- 本文只改“方向、阶段、依赖、完成标准、当前状态”，不追加某次实现过程。
- 阶段状态变化必须有代码、测试、真机证据或 Git 提交支撑；没有证据就写“待审计”。
- 新增专项规范前先检查现有文档是否已覆盖；能引用就不复制。
- 每次阶段完成，更新本文状态表和 `docs/project/changelog.md` 一条原因；不在本文抄测试命令和文件清单。
- 阶段编号与迈从历史里程碑冲突时，写“治理阶段 Mx”，不改历史文档编号。

## 11. 相关文档

- [协议定义与加载规范](protocol-definition-standard.md)
- [主动查询安全规范](active-query-safety.md)
- [设备身份与能力声明规范](device-identity-and-capabilities.md)
- [协议回归测试规范](protocol-regression-testing.md)
- [统一遥测衔接规范](protocol-telemetry-integration.md)
- [厂商插件架构标准](../dev/plugin-standard.md)
- [新厂商接入流程](../dev/vendor-onboarding-process.md)
- [硬件安全规则](../hardware/safety.md)
