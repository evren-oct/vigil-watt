# 主动查询门控方案（M2 阶段 1）

> 状态：方案定稿；未改代码。全面门控被能力声明覆盖阻塞，先补数据再逐插件启用。
> 基线：`dev` 分支 `d117b59`；写入点证据见 [主动查询写入点审计](2026-11-23-active-query-write-audit.md)。

## 1. 设计结论

主动查询统一按以下顺序判定，任何一步失败都不发帧：

1. `WritesEnabled`：插件开关关闭，直接拒绝；
2. `ProbeWrites` / 当前操作类型：该路径本来就不写，直接返回“未尝试”；
3. 设备能力：对应能力必须是 `CapabilityState.Supported`；
4. 设备身份与接口：目录/白名单命中，接口指纹匹配；
5. 运行时：`CanWrite` 未被熔断/退避挡住。

授权结果需要带一个可记录的原因：

- `writes-disabled`
- `capability-unverified`
- `capability-unsupported`
- `interface-unmatched`
- `breaker-open`
- `probe-disabled`

不新增第二套全局写入开关。插件开关仍是唯一用户授权入口；设备能力是代码侧授权，不是用户开关。

## 2. 引擎与直接 HID 的统一规则

### 2.1 `ProtocolEngine`

- `activeAllowed` 不再依赖默认 `true`；实施时改为调用方必须显式传入。
- 调用方用设备能力计算结果：只有 `BatteryRead` 为 `Supported` 才传 `true`。
- 未登记、接口不符、`implementation=none`、能力 `Unverified` / `Unsupported` 一律传 `false`。
- 被动解析不受影响；`false` 只禁止主动查询。

### 2.2 厂商直接 HID

- 每个直接写入点前都必须有等价的设备能力判断；`CanWrite` 继续只管插件开关、熔断和退避。
- 有目录的插件从 `devices[].capabilities` 读；没有目录的插件必须先建立能力数据源，不能在 C# 里按品牌硬编码“全部支持”。
- 握手、会话帧、ACK 与查询帧使用同一授权链；不得用握手去识别未知设备。
- 发现探测和半开探活只对已确认且能力 `Supported` 的设备生效；未验证设备只能被动监听。

### 2.3 失败语义

- 未授权主动查询返回 `NotAttempted`，不记失败、不触发熔断。
- 已授权但写失败/无应答，仍按现有插件规则记账。
- 授权拒绝要能写日志；默认 Info/Once，避免每轮刷屏。

## 3. 能力覆盖现状

| 品牌/入口 | 能力来源 | 当前覆盖 | 能否直接开门控 |
|---|---|---|---|
| 北通 | `BetopPlugin.CapabilitiesOf` → `ProtocolEngine` | `5073`、`5158`、`5159` 为 `batteryRead=supported`；`5191` 等未声明 | **可以**，北通已先行 |
| 雷柏 | `RapooPlugin.CapabilitiesOf` → `ProtocolEngine` | VT3、VT2S Air MAX V2 等少量声明；纯被动 | 不需要主动门控 |
| 迈从 | `MchosePlugin.CapabilitiesOf` + `MchoseDeviceCatalog` | 仅 `3837:1018` 传输通道声明三项能力；设备目录没有逐型号 `batteryRead` | **暂不可以** |
| 罗技 | 无 `CapabilitiesOf` 覆写 | 无设备级能力声明 | **不可以** |
| Razer / Asus / Atk / VGN / Cherry / Pulsar / Lingbao / Hecate / Inphic IN9 / Mojhon / Flydigi / GameSir / Darkrim / Eweadn / Aula / AKG / Sony INZONE / Ajazz / Thunderobot | 基类默认 `CapabilitySet.Empty` | 没有设备级能力声明 | **不可以** |
| 纯被动插件 | 无需主动授权 | Rapoo、Delux、Haute、Ilovbee、Langtu、AulaG9、HyperX、DualSense、Inphic ZHEN 等 | 不适用 |

结论：**不能把“能力未声明”直接解释为“该型号支持主动查询”**。若立即全面开门控，大量当前可用的主动查询设备会停查。

## 4. 分步实施顺序

### 4.1 先补能力数据，不改变行为

- 按 `support-matrix.md` 和品牌文档逐台确认已有证据；只有真机读数或录制帧单测支撑的型号才写 `batteryRead=supported`。
- 没有证据的型号保持 `unverified`，并明确“门控启用后只被动监听/不再主动查询”。
- 有目录的插件补 `devices[].capabilities`；没有目录的主动查询插件先建立最小能力数据源，再谈门控。
- 这一步只改数据/文档，不切换查询行为，便于单独审查证据。

### 4.2 不抽通用策略层

不新增通用 `ActiveQueryPolicy`，也不为手动探测另建一套授权框架。自动查询授权按插件现有结构逐点补齐：

- `WritesEnabled` / `IsWritesEnabled` 负责插件开关；
- `CanWrite` 负责熔断与退避；
- `BatteryReadVerified` 或对应能力查询负责设备能力；
- 插件自己的接口匹配负责身份边界。

只有未来出现两个以上完全同构、且经独立审查的真实实现时，才另行评估是否抽象；不在本方案内。

手动目标探测是另一条受控只读路径，设计见 [手动目标探测设计](2026-11-24-manual-target-probing-design.md)；它同样不新增通用 `ActiveQueryPolicy`。

### 4.3 按插件逐批启用

1. 北通保持现状，作为参考实现；
2. 迈从先补设备级能力，再让 `Engine.ProbeAsync/Poll` 显式传 `activeAllowed`；
3. 直接 HID 插件按品牌补能力源和门控；
4. 最后处理握手、发现探测、半开探活与 ATK 后备写入。

每批只影响一个品牌/插件，失败可单独回滚；不引入全局功能开关。

手动目标探测的 `ProbePlan`、设备实例证据和 UI 另按 [手动目标探测设计](2026-11-24-manual-target-probing-design.md) 推进，不并入本方案的自动门控实施。

## 5. 验收标准

- 未登记设备、能力 `Unverified` / `Unsupported`、接口不符时，主动查询零写入。
- 能力 `Supported` 的设备行为不回退。
- `WritesEnabled=false`、熔断、退避、半开探活均不能绕过设备能力。
- 直接 HID 与 `ProtocolEngine` 有等价测试，不只是 codec 单测。
- 日志能说明拒绝原因；同一原因不刷屏。
- 能力声明与 `support-matrix.md`、品牌文档证据一致；没有证据的型号不写 `supported`。

## 6. 当前阻塞与待决事项

- 没有设备目录的主动查询插件，能力数据源放在哪里：新增最小 catalog，还是先补现有目录？需要按品牌逐个定，不能统一造一个“通用能力表”。
- 迈从的 `MchoseDeviceCatalog` 是传输通道能力，不是本体型号能力；需要明确两者的合并边界。
- 罗技是接收器/槽位模型，能力应绑定“本体型号”还是“接收器通道”需要单独设计。
- `support-matrix.md` 的“贡献者真机”能否直接支撑 `batteryRead=supported`，要按现有证据标准逐条判断；不能批量升级。
- 握手/会话型协议如果“必须先握手才能读到能力”，需要单独的安全设计；不能用握手探测未知设备。

## 7. 不在本方案范围

- 设备配置写入（DPI、回报率、灯光、宏、固件）；
- 协议引擎重构、通用状态机 DSL、统一目录加载器；
- 统一遥测重设计；
- 采集平台建设。

## 8. 相关文档

- [主动查询写入点审计](2026-11-23-active-query-write-audit.md)
- [手动目标探测设计](2026-11-24-manual-target-probing-design.md)
- [主动查询安全规范](../architecture/active-query-safety.md)
- [通用协议引擎治理路线图](../architecture/protocol-engine-roadmap.md)
- [设备身份与能力声明规范](../architecture/device-identity-and-capabilities.md)
- [协议回归测试规范](../architecture/protocol-regression-testing.md)
- [设备支持矩阵](../hardware/support-matrix.md)
