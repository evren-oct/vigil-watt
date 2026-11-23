# 主动查询安全规范

> 状态：规范生效；M2 阶段 1 方案定稿，全面门控受能力声明覆盖阻塞，尚未实施。
> 本文定义所有主机→设备写入的授权边界；事故复盘、逐插件开关现状和写入规则仍以 [`docs/hardware/safety.md`](../hardware/safety.md) 为准。
> 基线：2026-11-23，`dev` 分支 `d117b59`。

## 1. 目的与覆盖范围

主动查询是主机向设备发送数据的行为。只要字节从主机流向设备，就属于本文范围，不论它叫查询、握手、会话、唤醒还是探测。

覆盖：

- Output Report 写入；
- Feature Report 写入 / `SetFeature`；
- 固定查询帧与动态查询模板；
- 会话帧、握手帧、ACK 帧；
- 发现探测、半开探活、事件驱动核实；
- `ProtocolEngine` 的主动查询路径；
- 厂商插件直接操作 HID 的路径；
- 诊断采集中的手动发帧（另有人工授权要求）。

不覆盖：

- 纯被动 `ReadInput` / `GetFeature` 读取；
- BLE GATT 读取、经典蓝牙属性读取、本机电源读取；
- UI、历史库、日志等纯软件行为。

## 2. 安全原则

1. **被动监听优先**：能靠设备上报或只读 Feature 拿到的，不写帧。
2. **未登记设备默认禁止主动查询**：没有目录条目、没有接口指纹确认、没有能力证据时，只能被动听。
3. **设备身份先确认**：先确认“这是哪台设备、这条接口是不是它的通道”，再谈查询；接收器在位不等于设备在线。
4. **能力逐项授权**：读电量不等于可以读 DPI、回报率、充电或连接方式；每项能力独立判断。
5. **最小权限**：每轮只发完成当前能力所需的最少帧；重试的永远是同一条已知命令。
6. **失败关闭**：授权链任何一层不满足，就不发帧；不能“先试一下再说”。
7. **禁止枚举与猜测**：不做命令扫描、report ID 扫描、字节猜测；帧必须有官方抓包、公开文档或真机证据。

## 3. 授权层级与优先级

主动查询必须同时通过以下四层。任何一层不满足，结果都是“不写”。

| 层级 | 约束 | 当前实现锚点 | 说明 |
|---|---|---|---|
| **全局** | 产品只读原则；不实现设备配置写入；不发送用于修改配置的帧 | `AGENTS.md` §11；[`hardware/safety.md`](../hardware/safety.md) §3 | 不是 UI 开关，不提供“全局写入许可” |
| **插件** | 会写帧的插件默认关闭；用户打开插件即授权其工作所需写入 | `PluginEnabled`；`WritesEnabled`；`CanWrite`；`ProbeWrites` | 唯一用户授权入口，不新增第二套写入开关 |
| **设备** | 目录能力为 `supported`；接口指纹命中；身份已确认 | `devices[].capabilities`；`BatteryReadVerified`；`ProtocolEngine.CapabilityOf` | 能力未声明 / `unverified` / `unsupported` 都不授权 |
| **运行时** | 熔断、递增退避、发现探测预算、半开探活间隔 | `BatteryPluginBase.CanWrite`；`WriteBackoff`；`BreakerProbeInterval` | 自动降级，不能绕过插件开关 |

优先级不是“高层覆盖低层”，而是“低层不能突破高层”：全局禁止的写入，插件开关打开也不能发；设备能力未验证，插件开关打开也不能发；运行时熔断中，事件驱动核实也只能按放行规则走。

## 4. 设备身份确认与握手

### 4.1 什么时候算“已确认”

至少满足：

- VID/PID 命中目录，或插件白名单 + 接口指纹明确命中；
- 接口形态（Usage Page/Usage、输入/输出/Feature 长度、report ID、路径片段）符合协议；
- 设备身份与协议匹配不是同一件事：接收器 PID 命中不等于本体型号已确认；
- 多接口设备只对插件实际认领的那条接口授权，不跨接口继承。

未登记、接口不符、`implementation=none` 一律按“未确认”处理。

### 4.2 握手与会话帧

握手、会话建立、ACK、密码门都是写入，不是“读取前的免费动作”。规则：

- 只对已确认设备发送；
- 受插件开关、熔断、退避和预算约束；
- 每句柄只发一次或按协议明确的最小次数，不循环重试；
- 不能用握手去识别未知设备，也不能为了“试试看”对未登记接口发会话帧；
- 握手失败按失败处理，不得换命令或扩大扫描范围。

## 5. 能力级授权

每项主动能力独立判断，不允许“读电量成功，所以 DPI 也能读”：

| 能力 | 允许的主动行为 | 授权条件 |
|---|---|---|
| `BatteryRead` | 电量查询 | 插件授权 + 设备 `batteryRead=supported` + 接口确认 |
| `DpiRead` | 读取当前生效 DPI | 插件授权 + 设备 `dpiRead=supported` + 协议/接口确认 |
| `DpiLevelRead` | 读取 DPI 档位索引 | 独立能力，不由 `DpiRead` 自动授权 |
| `RateRead` | 读取保存的回报率设置值 | 独立能力，不由 `DpiRead` 自动授权 |
| `ChargingRead` | 读取充电状态 | 独立能力；没有充电位时保持 `Unknown` |
| `ConnectionRead` | 读取当前链路 | 独立能力；推断值必须标 `Inferred` |

`unsupported` 与 `unverified` 都按“不授权”处理；只有 `supported` 才能进入主动查询分支。能力声明不跨型号、不跨接口、不通过 `extends` / `override` 继承。

## 6. 公共引擎与厂商直接 HID 的覆盖

| 路径 | 当前授权方式 | 现状 | M2 要求 |
|---|---|---|---|
| `ProtocolEngine.ProbeAsync` | `writesEnabled` + `activeAllowed` | 已有接缝；默认 `activeAllowed=true` | 调用方必须传设备级能力结果，未知设备传 `false` |
| `ProtocolEngine.Poll` | `writesEnabled` + `activeAllowed` | 已有接缝；默认 `activeAllowed=true` | 同上；未授权返回 `NotAttempted`，不记失败 |
| 北通 | `BatteryReadVerified` + `WritesEnabled` + `CanWrite` | 已落地；`BetopActiveGateTests` 覆盖 | 保持并作为参考实现 |
| 其它数据协议插件 | 多数只检查 `WritesEnabled` / `CanWrite` | 设备级能力门控未统一 | 逐插件补能力授权与测试 |
| 厂商直接 HID | `WritesEnabled`、`CanWrite` 或等价闸门 | 罗技用 `HidppIO.WritesAllowed`；其它插件各自实现 | 必须有等价设备级授权，不能只靠插件开关 |
| BLE / 经典蓝牙 / 本机电源 | 无主动写入 | 不属于主动查询 | 不引入插件写入抽象 |
| 诊断采集手动发帧 | 用户在会话中显式勾选并授权 | 独立路径，见 [`capture.md`](../project/capture.md) §11 | 不并入插件开关，也不得自动发帧 |

2026-11-23 已完成静态写入点盘点，矩阵与风险见 [主动查询写入点审计](../reviews/2026-11-23-active-query-write-audit.md)。完整运行时行为仍需逐插件测试；方案已定稿，实施尚未开始。

门控方案见 [主动查询门控方案](../reviews/2026-11-23-active-query-gate-design.md)：先补能力覆盖，再按插件逐批启用；不能把 `Unverified` 当 `Supported`。

## 7. 失败处理

- 写失败：视为当前通道不可用，不换命令、不扩大重试范围；必要时标记句柄失效。
- 读超时：设备可能在睡，不立即判离线；按插件离线策略处理。
- 查询回显：接收器可能把主机写入回显到输入队列；整帧等于查询帧时丢弃。
- 无应答：区分“没发帧”（`NotAttempted`）和“发了没应答”（`Failed`）；零写入协议不得被误记成失败。
- 熔断：连续失败达到阈值后停写，只留被动监听；退避期满按同一条已知命令半开重试。
- 发现探测 / 半开探活：失败不计失败次数、不升退避档位；间隔按品牌实测，不批量照抄。
- 安全失败默认“少做”：授权信息缺失、能力状态不明、身份不确定时，宁可不查询，也不猜测。

## 8. 测试与验收

主动查询改动至少覆盖：

- 未授权探测零写入；
- 未授权轮询返回 `NotAttempted`，不记失败；
- 未授权时被动帧仍可读；
- 已授权查询正常解析；
- 未登记接口 / 接口指纹不符不查询；
- `WritesEnabled=false` 时不写；
- 握手 / 会话帧受同一授权链约束；
- 熔断、退避、半开探活不绕过开关；
- 直接 HID 路径有与 `ProtocolEngine` 等价的零写入测试。

参考实现：`BetopActiveGateTests`（北通先行门控）、`ReadingStateTests`（熔断/半开探活）、`AkgN9Tests`（写入闸门与应答归属）。测试用可记录写入的 Fake Handle，不依赖真机。

## 9. 当前状态与 M2 后续

已完成：

- 2026-11-23 完成写入点静态盘点；
- 2026-11-23 完成阶段 1 门控方案定稿；
- `ProtocolEngine` 有 `activeAllowed` 参数；
- `BatteryPluginBase` 有 `BatteryReadVerified`；
- 北通按目录能力门控主动查询；
- 北通门控测试覆盖未授权、已授权、被动帧、未登记接口、写入关闭。

未完成：

- 其它引擎调用点没有统一传设备级能力；
- 直接 HID 插件没有统一的设备级能力授权；
- 握手、会话、发现探测、半开探活的覆盖矩阵未完成；
- 完整写入点清单未审计。

本任务只写规范，不实施门控。M2 的实现工作应另立任务，并按本文的授权层级和测试要求验收。

## 10. 相关文档

- [通用协议引擎治理路线图](protocol-engine-roadmap.md)
- [硬件安全规则](../hardware/safety.md)
- [设备身份与能力声明规范](device-identity-and-capabilities.md)
- [协议回归测试规范](protocol-regression-testing.md)
- [厂商插件架构标准](../dev/plugin-standard.md)
- [代码审查标准与流程](../dev/review-standard.md)
- [运行时数据流](../project/code-flow.md)
