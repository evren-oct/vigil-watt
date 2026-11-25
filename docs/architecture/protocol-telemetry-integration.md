# 统一遥测衔接规范

> 状态：规范生效；指针遥测与连接方式已有统一模型，电量/充电仍是原始字段。
> 本文定义厂商协议与统一遥测的衔接边界；具体模型、UI 口径和过期规则继续以 [`docs/project/pointer-telemetry.md`](../project/pointer-telemetry.md) 为准。
> 基线：2026-11-23，`dev` 分支 `d117b59`。

## 1. 职责边界

| 层 | 负责 | 不负责 |
|---|---|---|
| 协议 JSON / 插件 | 解析厂商帧、码表、位域、会话与稳定判定 | 统一展示口径、历史策略 |
| Core 遥测模型 | `TelemetryValue<T>`、`PointerTelemetry`、`DeviceStatus` 字段 | 猜测厂商协议、补默认值 |
| 状态汇聚 | 合并、变化判定、离线墓碑、历史采样 | 重新解释原始帧 |
| App | 展示来源、未知、过期和离线状态 | 解析协议、决定设备是否在线 |

引擎不推断设备语义。JSON 没写的字段，插件不能从“同族”或“理论规格”补出来。

## 2. 遥测的六个语义维度

### 2.1 可用性

- `Unknown`：当前没有可靠值；
- 有值：值 + 来源 + 观测时刻。

`Unknown` 与数值 0 严格不同。电量 0 是有效值；DPI 0 不是“没读到”，而是越界/无效。

### 2.2 来源

| 来源 | 含义 |
|---|---|
| `DeviceReported` | 设备直接上报，或只做协议已定义且已验证的解码 |
| `VendorSdk` | 厂商 SDK / 官方服务返回 |
| `Inferred` | 依赖未确认假设（型号表、配置值等于生效值等） |
| `Estimated` | 系统事件统计，只允许用在实测回报率等估算场景 |

来源描述“谁告诉我们的”，不描述“数值经过几次转换”。

### 2.3 能力

`CapabilityState` 三态：

- `Supported`：已实现并有足够证据；
- `Unsupported`：已确认当前适配不提供；
- `Unverified`：尚未验证或证据不足，默认态。

文档里可以用“NotVerified”作为说明词，但代码中的正式名称是 `CapabilityState.Unverified`；不要新增 `NotVerified` 枚举或运行时状态。

### 2.4 新鲜度

- 每个有值遥测应带 `ObservedAt`；
- 过期是应用层展示策略，不是协议规则；
- 指针遥测当前使用 `PointerTelemetry.DefaultMaxAge = 5 分钟`；
- 过期不主动改模型，UI 按节拍重算。

### 2.5 充电

`ChargingState`：`Unknown` / `Discharging` / `Charging` / `Full`。

- 没有充电位就保持 `Unknown`，不按电量猜；
- 满电优先由协议 `fullAt` 或明确状态码判定；
- 充电失败/故障码不得当成“充电中”。

### 2.6 连接

`ConnectionKind`：`Unknown` / `Wired` / `Wireless24G` / `Bluetooth`。

- 连接是遥测值，带来源与时刻；
- 接收器在位不等于设备在线，也不等于 2.4G；
- 读接收器配置通道可以推断 2.4G，但来源必须 `Inferred`，等设备自报再升级。

## 3. `Unknown`、`Unsupported`、`Unverified` 的区别

| 词 | 所属层 | 含义 | 是否允许主动查询 |
|---|---|---|---|
| `Unknown` | 运行时值 | 当前没有可靠值 | 不代表能力；由能力层另行授权 |
| `Unsupported` | 能力 | 已确认当前适配不提供 | 不允许 |
| `Unverified` / NotVerified | 能力 | 尚未验证或证据不足 | 不允许 |

`Unknown` 不是“不支持”，`Unsupported` 也不是“没读到”。UI 文案与主动查询必须分别使用这三层语义，不能互相替代。

## 4. 来源优先级与合并

默认信任顺序：

`DeviceReported > VendorSdk > Inferred > Estimated`

规则：

- 无值不覆盖已知值；
- `Unknown` 值即便带来源也不算链路证据；
- 连接方式合并走 `TelemetryValue.MergePreferTrusted`；
- 指针遥测逐字段合并，空 patch 不覆盖；
- 电量 `null` 与充电 `Unknown` 表示“本次没有新信息”，保留旧值；
- 观测时刻只由解析边界盖，JSON 不写时间；
- 30 秒指针补推只为刷新 `ObservedAt`，不保证触发设备变化事件。

## 5. 不同厂商差异如何保留

- 协议差异留在插件/JSON：码表、位域、会话、稳定窗口、满电口径。
- 统一模型只承载共性字段：电量、充电、连接、指针遥测、来源、时刻。
- 不为统一遥测强行重构厂商插件；某个厂商的私有字段先留在插件日志或扩展模型，等第二个同构实现出现再抽象。
- 不把厂商理论规格写成设备上报值；`MaxDpi`、电池容量等规格数据不属于 `Capability`，也不冒充遥测。

## 6. 重连、刷新与离线

- 离线墓碑保留最后已知值与时刻；UI 显示“离线 · 最后已知”或“离线 · 未知”。
- 设备重连后，新读数按来源覆盖旧值；`Unknown` 不清洗已知值。
- 离线期间不继续补推遥测；在线后由心跳或轮询刷新。
- 指针遥测不进历史库；历史只存电量采样。
- 过期展示与离线展示是两件事：设备在线但遥测过期，按“未获取/过期”处理；设备离线，整行按离线规则处理。
- 每个厂商的离线策略仍按 `OfflinePolicy` 和品牌文档，不在统一遥测层猜。

## 7. 能力与 UI 的关系

| 能力状态 | 有值 | 无值 |
|---|---|---|
| `Supported` | 正常显示，按来源加标记 | 显示“未获取” |
| `Unverified` | 有值照常显示；不因未验证而隐藏 | 不显示该字段 |
| `Unsupported` | 不显示 | 不显示 |

能力只决定“能不能说未获取”，不决定“有值时是否显示”。来源标记只给非 `DeviceReported` 的值；`DeviceReported` 不额外挂“设备上报”标签。

## 8. 当前实现与缺口

已统一：

- `TelemetryValue<T>`：值、来源、观测时刻；
- `PointerTelemetry`：DPI、档位、回报率设置/实测；
- `DeviceStatus.Connection`：链路 + 来源 + 时刻；
- `PointerTelemetryFormatter`：列表、详情、小组件/贴片的展示口径。

未统一：

- 电量仍是 `int?`，没有来源与观测时刻；
- 充电仍是 `ChargingState`，没有独立来源；
- 没有统一遥测信封承载所有字段；
- 不同厂商的 `Unknown` 文案与过期策略仍分散在插件/UI。

本任务只定义衔接原则，不重构遥测模型。新增统一字段必须先有第二个真实实现和明确展示需求。

## 9. 新厂商接入检查清单

- [ ] 能力只写在 `devices[].capabilities`，`supported` 有真机或录制帧证据；
- [ ] 每个遥测值标注来源；
- [ ] 每个有值遥测盖 `ObservedAt`；
- [ ] 没有可靠值就留 `Unknown`，不补 0、不按型号猜；
- [ ] 充电没有证据就保持 `Unknown`；
- [ ] 连接推断标 `Inferred`，不写 `DeviceReported`；
- [ ] 补对应解析/合并测试；
- [ ] 更新品牌文档与 [`support-matrix.md`](../hardware/support-matrix.md)。

## 10. 相关文档

- [通用协议引擎治理路线图](protocol-engine-roadmap.md)
- [指针遥测（DPI / 回报率）](../project/pointer-telemetry.md)
- [设备身份与能力声明规范](device-identity-and-capabilities.md)
- [协议定义与加载规范](protocol-definition-standard.md)
- [项目结构与架构](../project/architecture.md)
- [协议 JSON 怎么写](../dev/protocol-json.md)
