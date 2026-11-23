# 厂商插件架构标准

> 适用范围：新增品牌、接入新型号、修改 `VigilWatt.Core.Plugins` 下的插件与协议层。
> 本文是**标准**，不是审计报告；它把四大厂商横向架构审计中已经由当前代码支撑的结论固化为规则，未落地的建议一律标“规划”。
> 基线：2026-11-18，`dev` 分支。类名、路径以当前仓库为准；引用到方法级为止，不写行号。
> 配套阅读：[项目架构](../project/architecture.md) §3 / §6、[协议 JSON 写法](../dev/protocol-json.md)、[硬件安全规则](../hardware/safety.md) §3、[审查标准](../dev/review-standard.md) §2。

## 0. 状态词表

| 标记 | 含义 | 使用要求 |
|---|---|---|
| **已实现** | 当前代码里存在对应类型、方法或配置 | 可以据此写接入方案 |
| **已验证** | 有真机读数、抓包或单测钉住的证据 | 只有这一档才能把能力写成 `supported` |
| **规划** | 审计建议或未来方向，代码尚未落地 | 不得写成“已有能力”，不得据此假设接口存在 |
| **禁止** | 违反只读原则、安全规则或本标准的做法 | 不进入实现与评审 |

本文引用的“四厂商”指当前仓库里已经形成参考实现的雷柏、北通、迈从、罗技。其它品牌按同一套规则接入，不另立体系。

## 1. 职责边界

### 1.1 四层分工

| 层 | 负责 | 不负责 |
|---|---|---|
| **Core** | HID 枚举、接口认领、插件发现与生命周期、轮询节拍、状态汇聚、历史、设置、日志 | 任何品牌的帧偏移、VID/PID、命令码、显示名规则 |
| **插件** | 品牌级候选过滤、探测编排、协议分派、身份归一、显示名、离线参数、能力绑定 | 跨品牌通用机制；把品牌事实写进 Core |
| **协议 JSON** | 接口指纹、帧布局、字段偏移、充电码表、查询模板、设备目录、能力声明 | 握手顺序、会话状态机、校验和算法、电压换算 |
| **App** | 展示、托盘、小组件、设置页 | 解析协议、决定设备是否在线 |

依赖方向只有 `VigilWatt.App → VigilWatt.Core`。Core 不依赖 UI，插件不依赖 App。见 [项目架构](../project/architecture.md) §1。

### 1.2 硬边界

- 品牌协议一律走 `IBatteryPlugin`；新增品牌 = 新增插件文件，不改 `DeviceManager` 的发现逻辑。`DeviceManager.DiscoverPlugins()` 反射扫描 Core 程序集里的实现类。
- Core 只认“接口、认领、状态、节拍”，不认品牌。设备身份、接口指纹、帧偏移、充电码表、协议名等**能由现有协议/目录 JSON 规格表达的数据**，优先放进品牌 JSON（`AGENTS.md` §10）；JSON 无法合理表达的状态机、特殊接口分类、会话分派等流程逻辑允许留在 C#。C# 中保留品牌专用常量时，必须有明确的协议或实现理由；不得为了 JSON 化把流程逻辑强行塞进 JSON，也不得把这条原则理解为可以随意硬编码本应配置化的数据。存量例外见 §9.2。
- 非 HID 数据源（BLE、经典蓝牙、本机电量）不塞进 `IBatteryPlugin`，按事件式 watcher 接入；判据见 [项目架构](../project/architecture.md) §6“新‘非插件’数据源清单”。
- 只读原则优先于“功能完整”：不实现 DPI、回报率、灯光等设备配置写入（`AGENTS.md` §11）。

## 2. 插件契约与基类

### 2.1 `IBatteryPlugin` 的最小契约

`src/VigilWatt.Core/Plugins/IBatteryPlugin.cs` 定义：

- `AttachAsync(availableDevices, claims)`：每轮收到全量 HID 接口列表，认领自己的设备；必须幂等。
- `Poll()`：轮询一轮，返回有更新的设备状态。
- `ReconcileRemovals(availableDevices)`：只做消失检测，不探测、不写帧。
- `Detach()`：释放句柄、认领、监听任务与按设备缓存。
- `StatusPushed`：轮询之外的状态推送；DeviceManager 与 `Poll()` 走同一条 `Upsert`。
- 发现节拍相关：`SupportsFastDiscovery`、`SupportsDiscoveryProbe`、`HasHalfOpenProbe`、`NudgeHalfOpen()`。

### 2.2 `BatteryPluginBase` 的适用范围

**已实现。** 基类面向“一台设备 = 一个长持有的 HID 接口句柄”的插件，统一承载：

- 认领：`DeviceClaimRegistry`、`ClaimId` 与设置键分离；同一品牌多个插件类必须各给 `ClaimId`。
- 生命周期：探测冷却、失败熔断、递增退避、句柄失效重建、`Detach` 释放。
- 读数：`BatteryReadingFilter` 的数值有效性与变化幅度过滤；电量与充电各自独立，`null` / `Unknown` 不覆盖已知值。
- 被动监听：`SupportsPassiveListen` + `TryParsePassiveInput` + 后台只读线程；与 `OfflinePolicy.CountPollFailures` 是两件事。
- 身份：默认 `IdFor` 用 `PluginUtils.StableDeviceHash`；需要 SN、芯片 ID、槽位等身份时覆写 `BuildDeviceId`。
- 离线：`RequestOffline`、`MarkOfflineCandidate`、`RequestConfirm`、`ReportPollFailure`、`ReportPollAnswered` 与 `OfflinePolicy`。
- 写入闸门：`WritesEnabled` 直读插件开关，`CanWrite` 叠加熔断与事件放行；`ProbeWrites` 是机制事实声明，不随用户开关变化。

**不适用或不能直接套用的场景：**

- 数据源不是 HID 接口（BLE / 经典蓝牙 / 本机电源）：不要为了复用基类硬造一个“假接口”。
- 一个接收器接口下挂多台逻辑设备：基类的“一设备一句柄”模型不够，需要自建会话与槽位表；见 §3.3。
- 需要长持有会话、自发通知泵、按槽位寻址的协议：可以在同一个插件里让一部分设备走基类、另一部分自建；必须满足 §2.3 的 R2 例外。

### 2.3 R2 与罗技混合型的唯一例外

`docs/dev/review-standard.md` §2 的 R2 要求插件继承 `BatteryPluginBase`，禁止直接实现 `IBatteryPlugin`。当前唯一例外是 `LogitechPlugin`：

```csharp
public sealed class LogitechPlugin : BatteryPluginBase, IBatteryPlugin
```

原因：Centurion（PRO X 2）可以走基类；HID++ 需要接收器级长会话、槽位寻址与自发通知泵，和基类“一台设备一个句柄”的模型不合。该类仍自建了 `HidppIO.WritesAllowed` 闸门、连续失败熔断与递增退避，并复用 `src/VigilWatt.Core/Plugins/WriteBackoff.cs`。

这不是“可以随便绕过基类”的先例。默认仍应继承 `BatteryPluginBase`；只有确实存在基类无法承载的会话型设备，并且经过实际设计确认后，才考虑混合型。采用混合型时必须逐条补齐基类提供的能力、明确基类生命周期方法与接口重新实现之间的调用关系，并在类头写明为什么不能复用；复核时按 R2 例外处理，而不是按普通插件处理。

另有一条实现约束：基类的 `AttachAsync` 是 public virtual，`Poll` / `ReconcileRemovals` 是 public 非 virtual；`LogitechPlugin` 对三者都用 `new` 隐藏（`AttachAsync` 也未改成 `override`），并通过重新声明 `IBatteryPlugin` 让接口调用落到派生实现。**不要以 `BatteryPluginBase` 静态类型持有它再调用**，否则 `new` 隐藏的方法不会参与虚分派，HID++ 分支会被静默跳过（CR-93）。

### 2.4 `ProtocolEngine` 的适用范围

**已实现。** `src/VigilWatt.Core/Plugins/Protocol/ProtocolEngine.cs` 是“纯数据协议”的解释器：

- `Resolve(info)`：目录优先，未收录才按协议注册顺序匹配接口；`implementation=none` 不认领。
- `ProbeAsync(handle, writesEnabled)`：先被动听一个窗口，听不到再按 `active` 规格查询；零写入协议只有前半段。
- `Poll(handle, dev, protocol, writesEnabled)`：只有 `Pollable` 且 `active` 存在时才发帧；明确区分“没发帧”和“发了没应答”。
- `Parse(dev, protocol, frame)`：解析被动帧，产出电量、充电、链路、连接方式、指针遥测。
- `CapabilityOf` / `CapabilitiesOf`：纯配置查询，不开句柄、不发帧、不动解析状态。

**不在 `ProtocolEngine` 里做的事：**

- 握手、会话、密码门、型号 ACK；
- 校验和 / CRC / 加密；
- 多帧组合、跨报告状态机；
- 电压 → 百分比、线性换算、大端/有符号/跨字节位域；
- 同一接口多 report ID 且每个 ID 布局不同；
- 离线判定、读数过滤、显示名启发式、身份归一。

这些属于插件或 `codeFamily` 指向的 C# 族，见 §4.6。

### 2.5 `codeFamily` 的当前真实状态

**现状：只有声明与加载期校验，没有执行器。**

- `ProtocolSpec.CodeFamily` 是字符串；`ProtocolSpecLoader` 只校验 `implementation=code` 时它非空。
- `ProtocolEngine.ProbeAsync` / `Poll` 只处理 `Implementation == Data`；`implementation=code` 的帧处理由插件自己的 `Probe` / `ProbeAsync` / `PollDevice` 分派。
- 当前 JSON 里的 `codeFamily` 多数直接填插件名（如 `MchosePlugin`），表示“这套协议由这个类负责”，不是运行时分发键。

**规划：** 只有在两个以上品牌出现同构的有状态协议、且真机证据证明可以共用时，才评估执行器。当前**不实现**，也不要把 `codeFamily` 写成不存在的族名。

## 3. 三种插件架构

### 3.1 纯数据协议

**形态：** 帧布局固定，解析只依赖偏移、掩码、码表；没有会话、没有校验和、没有跨帧状态。插件只负责认领、委托解释器、把链路/离线信号转成基类动作。

**参考实现：雷柏 `RapooPlugin`。**

- 协议：`src/VigilWatt.Core/Plugins/Rapoo/specs/rapoo.protocols.json` 的 `status-report7` 及其 `extends` 变体；设备目录：`src/VigilWatt.Core/Plugins/Rapoo/rapoo-catalog.json`。
- 纯被动零写入：`WritesEnabled => false`、`ProbeWrites => false`，探测只被动听心跳。
- `SupportsFastDiscovery => true`：零写入，允许 15 秒发现节拍；`ProbeCooldown` 5 秒，保证每拍可重探。
- 离线：`OfflinePolicy { Confirm = 6s, Silence = 90s, CountPollFailures = false }`。`[18].bit7` 只作候选，6 秒确认；90 秒是兜底，不是主判据。
- 身份：`RapooDeviceIdentity.StableId` 用“目录型号 + SN”，没有 SN 回落路径哈希；插线时 `WirelessPathsToSkip` 摘掉同组无线接口。
- 适用：单设备、帧结构稳定、能靠被动流或固定查询拿到电量；新型号通常只改目录/协议 JSON。

### 3.2 多协议与有状态协议

**形态：** 一个品牌下有多代、多通道、多设备类型；部分协议是纯数据，部分需要会话、ACK、型号密码、分块读或跨帧稳定判定。插件按接口指纹和探测结果分派，JSON 只描述“数据长什么样”，流程留在代码。

**参考实现一：北通 `BetopPlugin`（数据优先的多协议）。**

- `src/VigilWatt.Core/Plugins/Betop/specs/betop.protocols.json` 当前有 3 套 `implementation=data` 协议：`betop-status15`、`betop-status15-nocharge`、`betop-zeus-keyevent`。
- `BetopPlugin.ProbeAsync` 先委托 `ProtocolEngine.ProbeAsync`；`PollDevice` 对可轮询协议先听 1.5 秒，听不到再查 1.5 秒；宙斯 `pollable=false` 不轮询。
- `T9` 是 250Hz 被动流，`BetopPlugin.IsSettled` 要求同一读数稳定 500ms 才采信；这是传输层稳定判定，不是 JSON 字段。
- 离线：链路 `Down` 走 `RequestOffline`；占位帧立即离线；`CountPollFailures=false`，主动路径自己报账。
- 身份：`BetopDeviceIdentity.GroupId` 只认目录显式 `group`；当前鲲鹏 20 的有线/2.4G 共用一个设备位。
- 适用：同一品牌多套数据协议、以 JSON 为主，但需要少量传输编排（先听后查、稳定窗口、占位帧）。

**参考实现二：迈从 `MchosePlugin`（真正的有状态多协议）。**

- 一个插件管键盘、鼠标、耳机等多类设备；`MchoseInterfaceClassifier` 按 VID/PID、usage、报告长度、`pathContains` 分类。
- 有状态协议留在 C#：`keyboard-v1`（`0x13` 会话 + ACK + 电量查询）、`mchose-0x4d`（`0x0900` 查询、`0x0600` 电源事件、`0x0003`/`0x0002` 遥测）、`mouse-v2`（E2 被动帧 + `0x11/0x06` 触发）。
- 形态固定的 `cz-block`（分块读）/ `gmouse` / `headset-v9` / `headset-x9` 走 `MchoseSpecs.Engine`；有状态的三套在 `MchosePlugin` 内分派。`MchoseA5Plugin` 是另一个插件类，共用 `Mchose` 设置键，靠 `ClaimId` 保持认领互斥。
- 身份：`MchoseDeviceIdentity` 把 USB 枚举身份与协议自报身份分开；`KnownProtocolByPath` 只在探测成功后写入，失败不清记忆，避免每 15 秒重复白写。
- 离线：`Silence=0`（推送稀疏）、`CountPollFailures=false`；`PollDevice` 只在确实发过帧的分支调 `ReportPollFailure` / `ReportPollAnswered`。`0x4D` 的 `payload[10]` 连接位只刷徽标，不当离线判据。
- 适用：协议有握手/会话/密码门/分块/多代共存；新增型号优先加目录和协议规格，只有流程差异才加代码分支。

### 3.3 多设备接收器

**形态：** 一个 HID 接口（接收器）下挂多台逻辑设备，按槽位或设备索引寻址；设备可能休眠、空槽、直连，接收器可能自报容量。插件必须维护“接收器会话 → 槽位 → 设备”的三级关系。

**参考实现：罗技 `LogitechPlugin`。**

- 一个 `ReceiverSession` 对应一条接收器接口；`session.Devices` 是 `devIdx → DevEntry`。直连设备用 `0xFF`，接收器槽位用 `1..SlotCapacity`。
- 槽位容量：`ProbeSlotCount` 读 `0xB5/0x03` 参数区 `[6]`，成功则优先；读不到退回 `HidppCatalog.MaxSlots`。C548 真机确认不提供该寄存器，走目录 6 槽。
- 发现：`DiscoverDevices` 先扫槽位，空槽有 5 分钟负缓存；首次整轮无应答时丢弃句柄、清冷却、下一轮重扫，连续 5 次才认命。
- 身份：`SlotId` = 接收器路径稳定哈希 + `:` + 槽位；换 USB 口不产生新设备。
- 连接：`LogitechConnectionPolicy` 只看“非 Direct + 目录族属于 2.4G 体系 + 槽位在容量内”，记 `Inferred(Wireless24G)`，**永不写 `DeviceReported`**；接收器体系（Bolt / Unifying / LIGHTSPEED）是 `ReceiverIdentity`，与链路是两个维度。
- 通知：`HidppNotificationQueue` 只入队，派发在 I/O 锁外；通知里出现未知槽位只登记，下一拍再发现，避免递归打穿调用栈。
- 写入：HID++ 经 `HidppIO.WritesAllowed` 读插件开关；Centurion 在发查询前直接查 `WritesEnabled`；两条路径都有熔断与退避。
- 适用：一个接收器多台设备、按槽位寻址、需要自发通知与容量推断。**不要**把罗技的槽位模型抽象成所有品牌的通用接收器模型；其它品牌的“接收器”可能只是一个透明通道。

### 3.4 选型矩阵

| 你的协议特征 | 选哪种 | 首选参考 | 关键约束 |
|---|---|---|---|
| 单设备、固定帧、纯被动/固定查询 | 纯数据 | 雷柏 | 帧事实全部进 JSON；插件只做认领与转交 |
| 多套数据协议、少量传输编排 | 多协议（数据优先） | 北通 | 流程留插件，偏移/码表进 JSON |
| 握手、会话、密码门、分块、多代共存 | 有状态多协议 | 迈从 | 流程留 C#；`codeFamily` 只做声明，不假设执行器 |
| 一个接收器多台逻辑设备 | 多设备接收器 | 罗技 | 会话/槽位/通知自建；身份与链路分开 |
| 非 HID 数据源 | 事件式 watcher | BLE / 经典蓝牙 / 本机电源 | 不进 `IBatteryPlugin` |

## 4. 设备目录与协议 JSON

### 4.1 当前文件布局

| 品牌 | 协议规格 | 设备目录 | 备注 |
|---|---|---|---|
| 雷柏 | `src/VigilWatt.Core/Plugins/Rapoo/specs/rapoo.protocols.json` | `src/VigilWatt.Core/Plugins/Rapoo/rapoo-catalog.json` | `ProtocolSpecLoader` 加载；`ProtocolEngine` 解释 |
| 北通 | `src/VigilWatt.Core/Plugins/Betop/specs/betop.protocols.json` | `src/VigilWatt.Core/Plugins/Betop/betop-catalog.json` | 3 套 data 协议；目录含 `officialDevices[]` 只读登记 |
| 迈从 | `src/VigilWatt.Core/Plugins/Mchose/specs/mchose.protocols.json` | `src/VigilWatt.Core/Plugins/Mchose/mchose-catalog.json` + `src/VigilWatt.Core/Plugins/Mchose/MchoseDeviceCatalog.cs` | 协议规格走 `ProtocolSpecLoader`；传输通道目录另走 `MchoseDeviceCatalog` |
| 罗技 | 无协议规格 JSON（HID++ 由代码处理） | `src/VigilWatt.Core/Plugins/Logitech/logitech-hidpp.json` | 目录只登记接收器族/槽位/电压曲线 |

**现状：没有统一目录加载器。** `ProtocolSpecLoader` 服务雷柏、北通、迈从的协议规格；迈从的传输通道目录另走 `MchoseDeviceCatalog`，罗技接收器目录走 `HidppCatalog`。这不是缺陷，也不在本次标准里统一；新增品牌按已有形态选一个，不要为了“整齐”造第四套通用加载器。

### 4.2 `devices[]` 字段

`ProtocolSpecLoader` 的设备条目至少包含：

| 字段 | 作用 | 规则 |
|---|---|---|
| `vid` / `pid` | 设备身份 | 十六进制字符串；同一 `devices[]` 内不得重复登记。厂商同一 PID 多型号放 `officialDevices[]` 或文档，不重复进 `devices[]` |
| `model` | 官方型号 | 用于显示名；不要写品类 |
| `kind` | 设备类型 | `mouse` / `keyboard` / `headset` / `speaker` / `gamepad` / `unknown` |
| `protocol` | 协议名 | 必须指向 `protocols` 里存在的键 |
| `pairedPid` | 有线/无线配对 | 非 0 时两组都要写，且恰有一条 `wired: true` |
| `wired` | 配对组里的有线本体 | 只影响配对与摘接口，不按 PID 大小猜 |
| `role` | 目录角色 | `controller` / `dongle` / `dock`；**不参与认领** |
| `group` | 同型号多通道归一 | 只有显式声明才归一；不按系列、PID 邻近、名字推导 |
| `capabilities` | 本型号能力 | 只写已验证项；不继承、不放协议层 |
| `override` | 同型号偏移差异 | 结构化深合并；只写要改的叶子 |

加载器按 `VID:PID` 建字典并直接赋值（`devices[key] = entry;`、`deviceSpecs[key] = ...;`）。当前**没有**重复键拒绝校验：同一 `devices[]` 里写重复条目不会合并，也不会报错，而是后写条目覆盖前写条目。新接入或修改目录时必须人工保证同一 `devices[]` 内不重复登记 `VID:PID`；重复本身就是配置错误。若未来要增加显式拒绝校验，应作为独立 BACKLOG/任务处理，不能把“当前能加载”当成允许重复。

`officialDevices[]` 是目录之外的只读登记表：加载器完全忽略它，不参与认领、显示名或能力查询。厂商同一 PID 多型号、未验证型号、官方注册表都放这里，**不要**为了“先占位”写进 `devices[]`。

### 4.3 `protocols[]` 字段

`ProtocolSpec` 的稳定字段：

| 字段 | 作用 |
|---|---|
| `kind` | 协议产出的设备类型 |
| `pollable` | 是否有主动查询命令 |
| `chargeDebounce` | 是否连续两帧才判充电（算法在解释器） |
| `probeWindowMs` | 探测被动窗口 |
| `implementation` | `data` / `code` / `none` |
| `codeFamily` | `implementation=code` 时必填；当前只做声明 |
| `extends` | 继承另一套协议，只写差异 |
| `interface` | 认领指纹；字段间 AND，`anyOf` 组间 OR |
| `passive` | 被动帧规格 |
| `active` | 主动查询规格 |

`implementation=none` 表示“目录收录但没适配”：`Resolve` 返回 null，不认领。不要把未验证型号塞进 `devices[]` 占位——目录优先，一有记录就会按它认领并顶掉 fallback 显示名。

### 4.4 接口指纹与加载期校验

- `interface` 的 `usagePage` / `usage` / `inputLength` / `outputLength` / `featureLength` / `reportId` / `pathContains` 之间是 AND；`anyOf` 内部每组仍 AND，组间 OR。
- `reportId` 指接口的输出 report ID（`HidDeviceInfo.OutputReportId`），不是帧里的第一个字节。
- `ProtocolSpecLoader` 在插件构造时校验：负偏移、字段宽度越界、查询帧超过输出长度、`type` 不在闭集合、`map` 键非数字、`extends` 成环、`implementation=code` 缺 `codeFamily`、`capabilities` 写错层/拼错名。失败直接抛异常，`DeviceManager` 跳过该插件并记 Error。
- 校验口径是“宁可启动失败，不要静默不认领”。不要为了绕过一个配置错误去改 `FrameReader` 的回退顺序或 `IntMatch.Matches`。

### 4.5 能力声明

**唯一合法位置：`devices[].capabilities`。** `ProtocolSpec` 上没有这个字段；写到协议层或 `override` 的任意层级，加载器会报错。

| 状态 | 含义 | 证据要求 |
|---|---|---|
| `supported` | 已实现，且当前适配与连接方式下有足够证据 | 真机读数 / 被单测钉住的解析 |
| `unsupported` | 已确认当前 VigilWatt 适配不提供 | 有明确的接口/协议证据 |
| `unverified` | 尚未验证或证据不足 | 默认态；省略即此值 |

规则：

- 不继承：`extends`、`override`、共享协议引用都不会把能力带过去，每条设备各自声明。
- 判据与认领同源：VID/PID 命中目录后还要过设备 `interface` 指纹；未收录、接口不符、`implementation=none` 一律 `unverified`。
- 能力只描述“这个型号、当前适配、当前连接方式下”的事实，不是厂商理论规格。
- 只读能力才允许声明。`DeviceCapability` 里仍有历史成员 `DpiWrite` / `RateWrite`，`ProtocolSpecLoader.CapabilityNames` 也仍接受这两个键；但当前没有任何写入实现，目录也不应声明它们为 `supported`。这是**已知的文档/枚举与产品原则冲突**，记录在 §9，不在本次改动里修。

### 4.6 数据与代码的切分线

| 优先进 JSON（现有规格能表达时） | 留代码 |
|---|---|
| VID/PID、型号、kind、role、group、pairedPid、wired、capabilities | 握手、会话、密码门 |
| 接口指纹、报告长度、`pathContains`、report ID | 校验和 / CRC / 加密 |
| 查询帧模板、窗口、重试次数 | 多帧组合、跨报告状态机 |
| 字段偏移、类型、位宽、mask、xor | 电压 → 百分比 / 线性换算 |
| 充电码表、链路码表、`fullAt`、`chargeWhenMissing` | 大端 / 有符号 / 跨字节位域 |
| 指针遥测字段与码表 | 不同离线策略、轮询节拍、稳定窗口、满电口径 |

判断口径：**“这个值是什么意思”优先进 JSON，“知道意思之后做什么”留代码。** 现有 JSON 规格能表达的设备身份、接口指纹、帧偏移、码表等，应优先放入 JSON；状态机、特殊接口分类、会话分派等无法合理数据化的流程留在 C#。C# 中保留品牌专用常量时，必须在代码或设计说明里写清协议/实现理由；这条边界不构成随意硬编码的许可，本应配置化的数据仍应进 JSON。详细词表与加载期校验见 [协议 JSON 写法](../dev/protocol-json.md)。

## 5. 身份、离线与能力

### 5.1 设备身份归一

默认规则（`BatteryPluginBase.BuildDeviceId`）：

- `PluginUtils.StableDeviceHash` 先抹掉 HID 实例段前两段（随 USB 口变化），再 FNV-1a 哈希；换 USB 口不产生新设备。
- ID 形如 `{插件名小写}:{16 位哈希}`；认领、黑名单、历史、上报必须共用这一处。

需要更强身份时只覆写 `BuildDeviceId`，不要在调用点另算一套：

| 场景 | 当前实现 | 规则 |
|---|---|---|
| 同一鼠标有线/无线两个 PID | `RapooDeviceIdentity.StableId` | 目录型号 + SN；没有 SN 回落路径哈希 |
| 同型号多通道归一 | `BetopDeviceIdentity.GroupId` | 只认目录显式 `group`，且必须 `engine.Matches` |
| 接收器多设备 | `LogitechPlugin.SlotId` | 接收器稳定路径哈希 + 槽位；直连 `0xFF` |
| 协议自报身份 | `MchoseDeviceIdentity` / `KnownProtocolByPath` | USB 身份与自报身份分开存；探测成功才写缓存 |

硬规则：

- 一个物理设备只保留一条记录；插线双连接用 `SkipPathsForThisPass` 摘掉接收器/无线接口，优先保留可读的本体。
- 不按型号系列、PID 邻近、显示名相似度合并设备；只有目录显式声明才归一。
- 改身份规则会改变历史库键，旧曲线对不上新 ID；必须单独评估迁移，不能顺手改。
- 接收器“在位”不等于设备在线；正在读接收器配置通道可以推断 2.4G，但来源记 `Inferred`，等设备自报再升级。

### 5.2 离线判定

离线分三层，不能混：

1. **协议事实**：这套协议有没有链路位、占位帧、休眠通知、查询命令；必须 per-brand。
2. **安全声明**：会不会写帧、插件开关默认值、探测冷却；必须 per-brand。
3. **策略参数**：`OfflinePolicy` 的 `Silence` / `Confirm` / `MaxFailures` / `CountPollFailures`；机制由基类实现，插件只填参数。

当前五条路径（`BatteryPluginBase` + `OfflinePolicy`）：

| 路径 | 触发 | 适用证据 | 典型 |
|---|---|---|---|
| `RequestOffline` | 零歧义证据，立即离线 | 设备明确休眠/关机帧、链路 `Down` | 北通 T9 占位帧 |
| `MarkOfflineCandidate` + `Confirm` | 有歧义证据，等确认窗口 | 链路位丢失但可能只是丢帧 | 雷柏 `[18].bit7=0`，6 秒 |
| `OfflinePolicy.Silence` | 被动接口彻底不说话 | 有心跳/推流，沉默才有信息 | 雷柏 90 秒兜底 |
| `OfflinePolicy.MaxFailures` / `ReportPollFailure` | 发过帧却没应答 | 主动查询型协议 | 迈从、北通主动路径 |
| `RequestConfirm` | 被动帧只是触发器，不是证据 | 语义未定论的状态通知 | 迈从 `0A` `payload=0x00` |

规则：

- `CountPollFailures` 与 `SupportsPassiveListen` 必须分开。前者回答“轮询 null 算不算失联”，后者回答“要不要起后台只读线程”；合成一个布尔会让“要监听但空读确实失联”的设备永远不离线。
- 插件自己知道这一轮到底发没发帧时，用 `ReportPollFailure` / `ReportPollAnswered` 报账；不要让基类猜。
- 空扫描（`availableDevices.Count == 0`）视为枚举失败，不标全部离线。
- 离线阈值是拿真机噪音校准的，不能为了“更快”缩短；改阈值必须带真机样本。
- 离线只改状态，不删历史；删除设备是另一条用户动作。

### 5.3 能力声明与只读原则

- `CapabilitySet` 是不可变集合；未声明项一律 `Unverified`，非法枚举值抛异常，不回落默认值。
- 认领落地时由插件把 `CapabilitiesOf(info)` 写进 `DeviceStatus.Capabilities`；UI 靠它区分“能读但还没读到”和“不支持”。
- 能力查询必须是纯配置查询：不开句柄、不发帧、不动解析状态。
- 只读原则（`AGENTS.md` §11）高于能力枚举：不新增写入能力，不把 `DpiWrite` / `RateWrite` 声明为 `supported`，不实现配置写入接口。

## 6. 特殊处理的分类标准

遇到“这家要和别人不一样”时，先归入下面六类之一，再决定放哪：

| 类型 | 判据 | 放哪 | 例子 |
|---|---|---|---|
| **A. 协议数据差异** | 同一流程，偏移/码表/查询帧/接口指纹不同 | 协议 JSON 或 `devices[].override` | 雷柏 `extends`、北通充电掩码 |
| **B. 设备目录差异** | VID/PID、型号、角色、配对、group、能力不同 | `*-catalog.json` 的 `devices[]` | 鲲鹏 20 三通道、罗技接收器族 |
| **C. 插件编排差异** | 先听后查、稳定窗口、会话、ACK、分块、按路径记忆 | 插件 C# | 北通 T9 settle、迈从 `keyboard-v1` |
| **D. 跨品牌通用能力** | 两个以上品牌同构，且不依赖品牌事实 | Core（按 §7 准入） | `DeviceClaimRegistry`、`OfflinePolicy` |
| **E. 没有证据** | 只有理论规格、官方声明或单点观测 | 文档标 `unverified` / `unsupported` | 未验证的 DPI/回报率 |
| **F. 写入/配置** | 会改变设备配置 | 禁止 | DPI 写入、灯光、回报率写入 |

特殊处理必须留下“为什么”：

- 在代码类头或方法注释里写一句理由，并指向真实专题文档小节；
- 在硬件文档里写证据范围（真机 / 抓包 / 单测 / 未验证）；
- 在接入设计里写适用边界与移除条件（什么证据出现时可以合并回通用路径）。

当前值得记住的特殊处理：

| 特殊处理 | 类型 | 证据状态 |
|---|---|---|
| 罗技混合型 R2 例外 | C | 已实现；`review-standard.md` §2 有明文例外 |
| 雷柏 SN 身份归一 | C | 已实现；VT3 真机，见 `rapoo.md` §4.3 / §4.4 |
| 北通 T9 500ms 稳定判定 | C | 已实现；T9 真机，见 `betop.md` §1.4 |
| 迈从按路径记忆协议 | C | 已实现；G87 真机，见 `mchose.md` §1.3 |
| 罗技自报槽位优先 | C | 已实现；C548 读不到时走目录兜底 |
| AULA / 迈从共用指纹按 ACK 分流 | C | 已实现；贡献者真机，见 `mchose.md` §5 |
| 高斯 / 狼蛛同指纹按产品名分 | C | 已实现；贡献者真机，见 `ganss.md` §7 |

发现问题的处理顺序：

1. **先判断是否影响实际设备**：读数错误、离线误判、写入风险、设备断连属于必须处理；纯命名、重复代码、文件长度属于可延后。
2. **再看是否属于本轮范围**：不在本轮目标里的问题记进 `docs/reviews/BACKLOG.md`，不顺手扩大改动面。
3. **最后才决定改哪一层**：按上表的 A–F 分类落位，不因为“看到了”就改 Core。

## 7. 修改 Core 的准入条件

只有同时满足以下条件，才允许改 Core：

1. **至少两个插件/品牌**需要同一机制，且形态一致；只有一个品牌的需求留在插件里。
2. 机制里**没有品牌常量**：VID/PID、帧偏移、命令码、协议名、型号名都不得进 Core。
3. 有**真机证据**或可复现的失败样本，证明现有扩展点不够用；不能只凭“以后可能用得上”。
4. 不破坏只读原则与写入闸门；不新增设备配置写入抽象。
5. 纯决策逻辑可抽成自由函数或纯类型，能进 `src/tests/VigilWatt.Core.Tests`；不把 WinRT 依赖带进 Core。
6. 默认行为对现有插件不变；若必须改变，先写影响清单、回滚方案与迁移说明。
7. 现有扩展点确实不够：先检查 `BatteryPluginBase` 的 virtual、`OfflinePolicy`、`ProtocolSpec`、`DeviceClaimRegistry` 能否解决。
8. 改动面有测试与审查：按 `docs/dev/review-standard.md` 的 R 清单过一遍；未修项进 `docs/reviews/BACKLOG.md`。

允许的 Core 改动示例：认领表、基类生命周期、离线策略参数、能力模型、调度节拍。

不允许的 Core 改动示例：把某品牌协议塞进 `DeviceManager`、为单个品牌加 `if (vid == ...)`、为了“统一”新建通用加载器、在没有第二个真实实现前实现 `codeFamily` 执行器。

## 8. 不值得提前抽象的内容

以下内容在只有一个实现或没有真机证据前，**不建抽象**：

- **统一目录加载器**：雷柏/北通是协议规格 + 设备目录，迈从是协议规格 + 额外传输目录，罗技是接收器目录 + 代码协议；这些形状不同，强行统一只会增加适配层。
- **`codeFamily` 执行器**：当前只是声明；没有两个同构有状态协议的真实需求。
- **通用状态机 DSL**：握手、ACK、密码门、分块读的差异大于共性；写进 JSON 会把“流程”伪装成“数据”。
- **通用多设备接收器模型**：罗技的槽位/直连/容量/通知模型不是所有接收器的模型；其它品牌可能只是透明通道。
- **通用身份解析器**：SN、group、槽位、自报身份各有证据边界；默认路径哈希 + 少量覆写已经够用。
- **通用显示名推断**：品牌/型号/产品串的歧义必须按目录和真机处理；跨品牌启发式会制造误判。
- **通用写入/配置能力抽象**：产品原则禁止配置写入，不需要为不存在的功能设计接口。
- **通用 UI 品牌模型**：设置页只列品牌中英文名，型号是扫描期产物；UI 不应认识品牌协议。

抽象的门槛是“第二个真实实现出现，且两者形态一致”，不是“看起来可以复用”。

## 9. 当前实现对照与已知冲突

### 9.1 四厂商对照

| 厂商 | 参考实现 | 架构 | 当前证据边界 |
|---|---|---|---|
| 雷柏 | `RapooPlugin` | 纯数据、纯被动、零写入 | VT3 / VT2S Air Max 本机验证；其余型号按目录，未逐台复测 |
| 北通 | `BetopPlugin` | 多协议、数据优先、少量传输编排 | T9、鲲鹏 20 2.4G/有线本机验证；`20DD` 电量格式未定，宙斯新 PID 待真机 |
| 迈从 | `MchosePlugin` / `MchoseA5Plugin` | 多协议、有状态、多代共存 | G87、官方通用 dongle 本机验证；多数鼠标/耳机为贡献者真机或声明，本机未复测 |
| 罗技 | `LogitechPlugin` | 多设备接收器 + 混合型 | C548 + MX Master 3S 本机验证；C53F 名称读取待复验；槽位枚举部分族未实测 |

状态与逐台清单以 [support-matrix.md](../hardware/support-matrix.md) 为准；协议细节以各品牌文档为准。不要把“代码已接入”当成“本机已验证”。

### 9.2 已知冲突（记录，不在本次修改）

审计报告、专题文档与代码不一致时，以**当前代码 + 真机证据**为准；冲突先记录，不擅自改代码或扩大任务范围。

1. **能力枚举仍含写入项**：`DeviceCapability` 有 `DpiWrite` / `RateWrite`，`ProtocolSpecLoader.CapabilityNames` 仍接受 `dpiWrite` / `rateWrite`；`docs/dev/protocol-json.md` §2.1 也列了这两项。但 `AGENTS.md` §11 与本文都禁止设备配置写入，当前没有任何写入实现，目录也没有把它们声明为 `supported`。处理方式：新接入不声明、不实现；是否删枚举/改文档另开任务。
2. **`codeFamily` 被误读为执行器**：当前只有声明与加载期校验，没有运行时执行器；`implementation=code` 的协议由插件自己处理。本文按现状写，不把规划写成已实现。
3. **目录加载器不统一**：协议规格走 `ProtocolSpecLoader`，迈从传输目录走 `MchoseDeviceCatalog`，罗技接收器目录走 `HidppCatalog`；这是现状，不是待修的缺陷，除非出现第二个同构品牌且维护成本有证据。
4. **插件代码仍有品牌常量**：例如 `RapooPlugin.RapooVid`、`LogitechPlugin.LogitechVid`、`MchosePlugin.IsSharedWithAula` 引用的 AULA VID/PID。按 §1.2 / §4.6，新增代码中的品牌专用常量必须有明确协议或实现理由；能由 JSON 表达的仍应优先数据化。存量是否迁移另开任务，不在本次修改。

### 9.3 规划边界

| 事项 | 状态 | 说明 |
|---|---|---|
| `codeFamily` 执行器 | 规划 / 未实现 | 只有两个以上同构有状态协议且真机证据充分时才评估 |
| 统一目录加载器 | 当前不做 | 现有三类目录形状不同；没有第二个同构实现，强行统一会增加适配层 |
| 新抽象接口 | 当前不做 | 现有 `IBatteryPlugin` / `BatteryPluginBase` / `OfflinePolicy` / JSON 已覆盖当前需求 |
| 设备配置写入 | 禁止 | 违反只读原则；不设计、不实现、不声明 `supported` |
| 未验证能力升级 | 等证据 | 只有真机读数或录制帧单测才能从 `unverified` 升级 |

## 10. 提交前检查清单

- [ ] 新品牌有独立插件文件，默认继承 `BatteryPluginBase`；只有基类无法承载的会话型设备且设计确认后才用 R2 例外混合型。
- [ ] 设置页只有品牌级中英文两行，不带型号/品类/协议/通道。
- [ ] 能由 JSON 规格表达的设备身份、接口指纹、帧偏移、码表、查询帧、协议名优先放进 JSON；C# 中保留品牌专用常量时有明确协议/实现理由，没有随意硬编码。
- [ ] 新增/修改 `devices[]` 时人工检查没有重复 `VID:PID`；当前加载器不会拒绝重复项。
- [ ] 新型号优先加目录；只有真实流程差异才加代码。
- [ ] 能力只写在 `devices[].capabilities`，`supported` 有真机/单测证据。
- [ ] 会写帧的插件默认关闭；写入经过 `WritesEnabled` / `CanWrite` 或等价闸门。
- [ ] 身份归一有明确规则；没有按型号/PID/名字猜合并。
- [ ] 离线路径与 `OfflinePolicy` 匹配协议事实；`CountPollFailures` 与 `SupportsPassiveListen` 分开。
- [ ] 新增纯逻辑有单测；帧解析/读数过滤/离线判定改动跑了对应测试类。
- [ ] 没有新增抽象接口、没有实现 `codeFamily` 执行器、没有统一目录加载器。
- [ ] 没有引入设备配置写入。
- [ ] 文档写清“已实现 / 已验证 / 规划 / 禁止”，没有把审计建议写成现状。

## 11. 相关文档

- [通用协议引擎治理路线图](../architecture/protocol-engine-roadmap.md)：治理阶段、当前问题与下一步。
- [设备身份与能力声明规范](../architecture/device-identity-and-capabilities.md)：身份、接口匹配、能力证据边界。
- [协议回归测试规范](../architecture/protocol-regression-testing.md)：Golden Frame、授权测试与最小测试集。
- [统一遥测衔接规范](../architecture/protocol-telemetry-integration.md)：厂商语义到统一模型的边界。
- [项目架构](../project/architecture.md)：模块边界、线程模型、状态合并、非插件数据源。
- [运行时数据流](../project/code-flow.md)：认领、轮询、离线五条路径。
- [协议 JSON 写法](../dev/protocol-json.md)：字段词表、`extends` / `override`、加载期校验。
- [硬件安全规则](../hardware/safety.md)：零写入、写入闸门、熔断与退避。
- [审查标准](../dev/review-standard.md)：R 清单与 R2 例外。
- [新厂商接入流程](../dev/vendor-onboarding-process.md)：六步流程与三种模板。
- 品牌专题：[雷柏](../hardware/brands/rapoo.md)、[北通](../hardware/brands/betop.md)、[迈从](../hardware/brands/mchose.md)、[罗技](../hardware/brands/logitech.md)。
