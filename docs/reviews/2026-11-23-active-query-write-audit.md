# 主动查询写入点审计（2026-11-23）

> 状态：M2 阶段 0 完成；只读静态审计，未向任何设备发帧。
> 基线：`dev` 分支 `d117b59`；本文是审计证据，长期规则见 [主动查询安全规范](../architecture/active-query-safety.md)。

## 1. 目的与范围

盘点所有主机→设备写入路径，回答四个问题：

1. 写在哪里发生；
2. 当前被哪一层闸门挡住；
3. 是否按设备能力授权；
4. 有没有对应的零写入测试。

覆盖 `src/VigilWatt.Core` 中的 `WriteOutput`、`SetFeature`、`SendOutputReport`、`HidD_SetFeature` 和 ATK 的原生 `HidD_SetOutputReport` 后备路径。纯被动读取、文件写入、日志写入不在范围内。

## 2. 方法与限制

- 使用 CodeGraph 与 `rg` 静态扫描调用点，再核对 `BatteryPluginBase`、`ProtocolEngine` 和各插件的门控代码。
- 未运行应用、未打开设备、未发送任何报文。
- 这是静态盘点，不是运行时跟踪；某个分支是否真的被走到，仍以插件候选、接口指纹和真机测试为准。
- “当前闸门”只描述代码里已有的检查，不把规划写成已实现。

## 3. 结论摘要

- 基类 `BatteryPluginBase.RunProbeAsync` 在探测前检查 `ProbeWrites && WritesEnabled`；轮询由各插件在 `PollDevice` 中调用 `CanWrite`。
- `CanWrite` 只检查插件开关、熔断/退避和事件放行，不检查设备能力。
- `BatteryReadVerified` 目前只有北通调用；其它插件的主动查询没有设备级 `capabilities` 门控。
- `ProtocolEngine` 已有 `activeAllowed`，但默认值是 `true`；只有北通传入了设备能力结果，Rapoo 传 `false`，Mchose 没有传。
- 直接 HID 插件普遍有插件开关或 `CanWrite`，但没有统一的“能力 supported 才允许发帧”检查。
- 只有 `BetopActiveGateTests` 覆盖了主动查询的零写入/授权行为；其它插件主要只有 codec 单测。

## 4. 写入点矩阵

“基类探测门”指 `RunProbeAsync` 的 `ProbeWrites && WritesEnabled`；“轮询门”指插件 `PollDevice` 里的 `CanWrite`。两者都不等于设备能力门控。

| 入口 | 写入点 | 当前闸门 | 设备级能力门控 | 现有测试 / 缺口 |
|---|---|---|---|---|
| `ProtocolEngine` / `QueryRunner` | `ProbeAsync`、`Poll` → `QueryRunner.RunAsync/Run` | 调用方 `writesEnabled` + `activeAllowed`（默认 `true`） | 仅北通传 `BatteryReadVerified`；Mchose 未传；Rapoo 传 `false` | `BetopActiveGateTests`；其它引擎调用无零写入测试 |
| `BetopPlugin` | `ProbeAsync`、`PollDevice` | `WritesEnabled` + `CanWrite` + `activeAllowed` | `BatteryReadVerified` | `BetopActiveGateTests`，当前参考实现 |
| `RapooPlugin` | 引擎探测/轮询 | `WritesEnabled => false` | 不适用（纯被动） | `RapooSpecTests`；零写入 |
| `MchosePlugin` / `MchoseA5Plugin` / `MchoseMouseProtocol` | 引擎探测/轮询 + 直接 `WriteOutput` / `SetFeature` | `IsWritesEnabled` + `CanWrite`；直接方法再查开关 | 无 | codec/协议单测；无零写入门控测试 |
| `LogitechPlugin` / `HidppIO` | HID++ `HidppIO.Request`；Centurion `WriteOutput` | `WritesAllowed`（可空）/ `WritesEnabled` / `CanWrite` | 无 | 连接/编解码测试；无零写入门控测试 |
| `RazerPlugin` / `RazerKrakenPlugin` | `SetFeature` / `WriteOutput` | `WritesEnabled`、`CanWrite`、`authorized` | 无 | codec 单测；无零写入门控测试 |
| `AsusPlugin` | `WriteOutput` | 基类探测门 + `WritesEnabled` + `CanWrite`；`VerifyPairedModel` 只做身份核实 | 无 | codec 单测；无零写入门控测试 |
| `AtkPlugin` | `WriteOutput` + `NativeSetOutputReport` 后备 | `IsWritesEnabled` + `CanWrite`；后备 helper 自身不查开关 | 无 | codec 单测；后备写入路径需单独回归 |
| `AulaPlugin` / `AulaHero68Transport` | 会话帧 / 查询帧 `WriteOutput` | 基类探测门 + `WritesEnabled` / `CanWrite`；传输层每片重查开关与接口 | 无 | 协议单测；无零写入门控测试 |
| `CherryPlugin` | `WriteOutput` | 基类探测门 + `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `DarkrimPlugin` | `WriteOutput` | 基类探测门 + `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `EweadnPlugin` | `WriteOutput` / `SetFeature` | 基类探测门 + `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `FlydigiPlugin` | `WriteOutput` | 基类探测门 + `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `GameSirPlugin` | `WriteOutput` | 基类探测门 + `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `GanssPlugin` | `WriteOutput` | 基类探测门 + `CanWrite`；探测另查 `WritesEnabled` + `IsCandidate` | 无 | codec 单测；无零写入门控测试 |
| `HecatePlugin` | `WriteOutput` | 探测 `WritesEnabled && ProbeWrites`；轮询 `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `HecateWirelessPlugin` | `WriteOutput` | 轮询 `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `InphicIn9Plugin` | `SetFeature` | 基类探测门 + `CanWrite`；`RefreshAndRead` 接收 `canWrite` | 无 | codec 单测；无零写入门控测试 |
| `LingbaoPlugin` | `SetFeature` / `WriteOutput` | 基类探测门 + `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `MojhonPlugin` | `WriteOutput` | 基类探测门 + `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `PulsarPlugin` | `WriteOutput` | 基类探测门 + `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `Sony InzoneH9Plugin` | `SendOutputReport` | 基类探测门 + `CanWrite` | 无 | 协议单测；无零写入门控测试 |
| `ThunderobotPlugin` | `WriteOutput` | 基类探测门 + `WritesEnabled` / `CanWrite` | 无 | codec 单测；无零写入门控测试 |
| `VgnPlugin` | `WriteOutput` | 基类探测门 + `CanWrite`；查询 helper 自身不查开关 | 无 | codec 单测；helper 依赖调用方门控 |
| `AjazzPlugin` | `SetFeature` | 基类探测门 + `CanWrite` + `WritesEnabled` | 无 | codec 单测；无零写入门控测试 |
| `AkgPlugin` | `WriteOutput` | 基类探测门 + `CanWrite` / `WritesEnabled` lambda | 无 | `AkgN9Tests` 测传输开关；无设备能力门控测试 |

## 5. 重点风险

### 5.1 引擎默认允许主动查询

`ProtocolEngine.ProbeAsync` / `Poll` 的 `activeAllowed` 默认 `true`。如果调用方只传 `writesEnabled`，未验证设备仍可能进入主动查询。当前北通是唯一显式传设备能力结果的调用方。

### 5.2 基类探测门是插件级，不是设备级

`RunProbeAsync` 只检查 `ProbeWrites && WritesEnabled`。它能挡住“插件关闭”，不能挡住“插件开启但该型号能力未验证”。

### 5.3 直接 HID 插件没有统一能力门控

`CanWrite` 不理解 `DeviceCapability`。Mchose、Logitech、Razer、Asus、Atk、Vgn 等直接 HID 路径需要在插件内补设备能力判断，不能只依赖 `CanWrite`。

### 5.4 ATK 后备写入是独立路径

`AtkPlugin.WriteQuery` 先走 `WriteOutput`，失败后回落 `NativeSetOutputReport`。后备 helper 自身不检查插件开关或设备能力，当前安全性依赖调用方；这是 M2 需要单独回归的路径。

### 5.5 罗技 `WritesAllowed` 可空

`HidppIO.Request` 在 `WritesAllowed == null` 时不会拦截写入。当前会话创建处会赋值，但这是“初始化正确才安全”的隐式前提，建议 M2 明确为必需闸门或加防御性失败。

### 5.6 测试覆盖偏窄

`BetopActiveGateTests` 覆盖了引擎级零写入、授权、未登记接口和 `WritesEnabled=false`；其它插件没有等价的 Fake Handle 写入计数测试。`AkgN9Tests` 只测传输层 `enabled` 回调，不等于设备能力门控。

## 6. 现有测试覆盖

| 测试 | 覆盖 | 不覆盖 |
|---|---|---|
| `BetopActiveGateTests` | 北通未授权/已授权、被动帧、未登记接口、写入关闭 | 其它品牌、直接 HID、握手 |
| `ReadingStateTests` | `CanWrite`、熔断、半开探活、失败保留旧值 | 设备能力授权 |
| `BreakerProbeTests` | 半开探活与失败不记账 | 设备能力授权 |
| `AkgN9Tests` | AKG 传输层 `enabled` 回调与应答归属 | 设备目录能力门控 |
| `HidDeviceHandleTests` | 低层 `WriteOutput` 失败 | 插件级/设备级授权 |
| 各品牌 codec 测试 | 帧构造与解析 | 写入是否发生 |

## 7. M2 建议顺序

1. 先给 `ProtocolEngine` 的所有调用方补 `activeAllowed`：Mchose 等未传的调用点显式传设备能力结果；未登记设备传 `false`。
2. 再按品牌补直接 HID 路径的设备能力判断；`CanWrite` 继续负责插件开关与熔断，不承担能力语义。
3. 对每个“当前靠主动查询工作、但目录没有 `batteryRead=supported`”的型号做证据回填或停查评估，避免门控上线后静默失去读数。
4. 单独审计握手/会话帧、发现探测、半开探活和 ATK 后备写入，确保不能绕过同一授权链。
5. 补测试：每个主动查询插件至少一条未授权零写入；直接 HID 插件补等价 Fake Handle 测试。
6. 不新增通用 `ActiveQueryPolicy`；按插件复用现有 `WritesEnabled` / `CanWrite` / 能力查询，逐点补齐授权。只有未来出现多个完全同构且经审查的实现时，才另行评估抽象。

## 8. 纯被动与无写入入口

当前静态扫描未发现主动写入的入口包括：`RapooPlugin`、`DeluxPlugin`、`HautePlugin`、`IlovbeePlugin`、`LangtuT98Plugin`、`AulaG9Plugin`、`HyperXPlugin`、`DualSensePlugin`、`InphicPlugin`（ZHEN）。这些入口仍应在插件关闭、熔断和异常路径上保持零写入。

## 9. 边界声明

- 本文没有修改任何 `.cs`、JSON、设备目录或测试。
- 本文没有执行真机写入；所有“当前闸门”结论来自静态代码。
- 本文不把 M2 标为完成；它只完成阶段 0 的写入点盘点。

## 10. 相关文档

- [主动查询安全规范](../architecture/active-query-safety.md)
- [主动查询门控方案（M2 阶段 1）](2026-11-23-active-query-gate-design.md)
- [通用协议引擎治理路线图](../architecture/protocol-engine-roadmap.md)
- [设备身份与能力声明规范](../architecture/device-identity-and-capabilities.md)
- [协议回归测试规范](../architecture/protocol-regression-testing.md)
- [硬件安全规则](../hardware/safety.md)
