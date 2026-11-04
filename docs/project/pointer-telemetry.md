# 指针遥测（DPI / 回报率）

> 现状：PR-A 已落地模型与状态流转；PR-B 接入**第一台设备**——雷柏 VT3（`24AE:1412`，2.4G），
> 走 `0x07` 被动心跳，零写入。PR-C 落地**设备能力声明**（六项 × 三态，挂在设备目录，不在协议上），
> 但**没有**接入新的设备型号，见 §6。
> 本文只记"现在是什么"；能力盘点见 `hardware/` 各品牌文档。

## 0. 已接入的设备

| 设备 | 通道 | 字段 | 证据 |
|---|---|---|---|
| 雷柏 VT3 `24AE:1412` | `0x07` 被动心跳（19 字节） | `[2]` 档位、`[3..4]`/`[5..6]` DPI X/Y（u16 LE）、`[11]` 回报率设置 | `hardware/rapoo.md` §1.1 真机逐字节校准；单测用真机帧 `071004800C800C01…` |

偏移写在 `rapoo-catalog.json` 里 `0x1412` 的**设备级 override**（不在协议本体），
所以同族另外 97 台设备不会顺带产出遥测。来源全部是 `DeviceReported`，包括 `[11]` 的码表映射。

## 1. 三个类型

| 类型 | 职责 |
|---|---|
| `TelemetryValue<T>` | 一个值 + 来源 + 观测时刻；`Unknown` 与 `0` 严格区分 |
| `PointerTelemetry` | 指针类遥测快照，6 个字段全部可空；`default` = 全未知 |
| `TelemetrySource` | 值的来源分类，判定口径见 §2 |

两者都是 `readonly record struct`：`DeviceStatus.Clone()` 只需赋值，不会与数据源共享引用
（`Upsert` 存的是 `incoming.Clone()`，用 class 会让插件后续改动绕过变化判定）。

## 2. 来源判定口径（`TelemetrySource`）

**来源描述"谁告诉我们的"，不描述"数值经过了几次转换"。**

| 情况 | 来源 |
|---|---|
| 设备直接给出，或只做协议已定义且**已验证**的解码（字节序 / 位域 / 码表） | `DeviceReported` |
| 需要设备未确认过的假设（型号映射表未验证、"配置值等于生效值"） | `Inferred` |
| 厂商 SDK / 官方服务返回 | `VendorSdk` |
| 系统事件统计（只允许用在实测回报率上） | `Estimated` |
| 没有可靠途径 | `Unknown` |

例：雷柏 `[11]=1` → 1000Hz 是 `DeviceReported`（码表已真机校准）；迈从 QHW 的
`usbRateIndex → Hz` 是 `Inferred`（型号表未验证）。

## 3. 字段语义

| 字段 | 含义 | 禁止 |
|---|---|---|
| `DpiX` / `DpiY` | 当前生效 DPI 数值 | 不得填型号标称上限 |
| `DpiLevel` | 档位索引，不是 DPI 值 | 范围由厂商协议决定，**Core 不设上下界** |
| `DpiLevelCount` | 档位总数（可选） | 缺就 `Unknown`，UI 不要假设存在 |
| `ReportRateSettingHz` | 设备**保存的**回报率设置 | 不得说成"实际回报率" |
| `ReportRateMeasuredHz` | 系统估算的实测值 | **只允许 `Estimated`** |

`MaxDpi`（型号上限）故意不放进模型，也**不是** Capability：能力声明（`capabilities` 三态）
只回答"这个型号在当前适配下能不能读/写 DPI"，**不携带任何数值**
（[dev/protocol-json.md](../dev/protocol-json.md) §2.1）。型号上限属于规格数据，真要显示就另开字段。

## 4. 合并与变化判定

- `PointerTelemetry.Merge`：逐字段"无值不覆盖"，有值则连来源与时刻一起覆盖；**不比较时间戳**。
  这条保证"轮询应答不带 DPI 时不会抹掉被动帧读到的值"。
- `PointerTelemetry.ValueDiffers`：只比 `Value` + `Source`，**不比 `ObservedAt`**。
- `DeviceChangeDetector.HasPointerChanged` 用它；`HasChanged` **不含** Pointer——
  否则 DPI 一变就会写出一条电量采样（历史只存电量）。

## 5. 过期

`PointerTelemetry.DefaultMaxAge = 5 分钟` 是**应用层展示策略，不是协议或设备规则**。
当前只与雷柏的 90 秒离线兜底错开（保证"刚离线时遥测仍然新鲜"），**其他厂商接入时需单独核对**。

- `ObservedAt == default` 或时间在未来（时钟回拨）→ **不判过期**；`default` 只用于时间未知/迁移/测试，
  正常解析必须给有效时间。
- 模型**不主动通知过期**：过期是时间的函数，不是状态迁移。UI 自己按节拍重算 `IsStale(now)`。
- 30 秒补推（`SetPointerTelemetry`）只为把最新 `ObservedAt` 同步进 `DeviceManager` 的内存状态，
  它**不保证触发 `DeviceUpdated`**（变化判定不看时间）。

## 6. 不在这一层做的事

| 项 | 位置 |
|---|---|
| 其余厂商与雷柏其他型号的解析接线 | 后续 PR（PR-C 只做能力声明，没接新设备） |
| 设备能力声明（目录 `capabilities` 三态） | **已落地（PR-C）**：`ProtocolEngine.CapabilityOf`，见 [dev/protocol-json.md](../dev/protocol-json.md) §2.1 |
| UI 渲染与过期文案重算 | 后续 PR-D |
| 实测回报率（RawInput 估算） | 后续 PR-F |
| 改 DPI / 改回报率（写控制） | 后续 PR-G，独立闸门 |
| 遥测进历史库 | 不做：`samples` 只有电量，DPI/回报率是当前值不是时序采样 |

## 7. 接线约束（PR-B 定下的规矩）

1. **只在已通过电量判据的被动帧上采信遥测**：休眠/占位帧结构合法但整帧是 0，
   `RapooPlugin` 在 `percent is null` 时直接返回，不调 `SetPointerTelemetry`。
2. **不新增任何查询帧**：VT3 的 DPI 与回报率就在电量心跳里，为了一行 UI 多发帧不划算。
3. **设备上报的回报率只进 `ReportRateSettingHz`**，`ReportRateMeasuredHz` 留给系统估算。
