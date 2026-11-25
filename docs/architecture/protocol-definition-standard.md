# 协议定义与加载规范

> 状态：规范生效；M1 严格加载已提交，本文档收尾未提交。
> 本文定义 JSON 与 C# 的高层边界、定义结构和兼容原则；字段词表、示例与逐项校验仍以 [`docs/dev/protocol-json.md`](../dev/protocol-json.md) 为准。
> 基线：2026-11-23，`dev` 分支 `d117b59`。

## 1. 适用范围与文档关系

本文适用于 `VigilWatt.Core.Plugins.Protocol` 以及使用协议规格/设备目录的插件：

- `src/VigilWatt.Core/Plugins/Protocol/` 下的 `ProtocolSpecLoader`、`ProtocolSpec`、`FrameReader`、`ProtocolEngine`；
- `Plugins/<品牌>/specs/*.protocols.json`；
- `Plugins/<品牌>/*-catalog.json`；
- 仍由品牌自己的加载器读取的目录（当前如 `MchoseDeviceCatalog`、`HidppCatalog`）。

分工如下：

| 文档 | 职责 |
|---|---|
| 本文 | 长期边界、定义结构、兼容与失败原则 |
| [`docs/dev/protocol-json.md`](../dev/protocol-json.md) | 字段词表、JSON 写法、加载期校验的具体规则 |
| [`docs/dev/plugin-standard.md`](../dev/plugin-standard.md) | 插件契约、三种架构、目录/协议切分 |
| [`docs/hardware/safety.md`](../hardware/safety.md) | 写入安全、事故复盘、逐插件开关 |

本文不复制字段表；如果字段的写法与本文冲突，以 `protocol-json.md` 的具体字段规则为准，并回到本文检查边界是否被破坏。

## 2. JSON 与 C# 的基本边界

判断口径：**“这个值是什么意思”进 JSON，“知道意思之后做什么”留 C#。**

JSON 适合表达：

- 设备身份与目录：VID/PID、型号、`kind`、`role`、`group`、`pairedPid`、`wired`、`capabilities`；
- 接口指纹：Usage Page、Usage、输入/输出/Feature 长度、`reportId`、`pathContains`；
- 帧布局：偏移、类型、位宽、`mask`、`xor`、前缀、长度条件；
- 语义映射：充电码表、链路码表、电量映射、指针遥测映射；
- 查询模板：固定字节、动态 report ID/长度、窗口、重试次数；
- 设备级 `override`。

C# 适合表达：

- 握手、会话、密码门、型号 ACK；
- 校验和、CRC、加密、解密；
- 多帧组合、跨报告状态机、分块读；
- 电压到百分比的换算、线性换算、大端/有符号/跨字节位域；
- 离线判定、读数过滤、显示名启发式、身份归一；
- 按路径记忆协议、稳定窗口、轮询节拍等流程判断。

不应为了“JSON 化”把流程伪装成数据。出现“如果 A 且 B 则发 C，否则等 D”这类需求时，默认应写代码，而不是继续给 JSON 加表达式。

## 3. 定义结构原则

一份品牌定义至少区分三类数据：

| 类型 | 位置 | 是否参与运行时 |
|---|---|---|
| 协议规格 | `specs/*.protocols.json` 的 `protocols` | 是，由 `ProtocolEngine` 或插件使用 |
| 设备目录 | `*-catalog.json` 的 `devices` | 是，决定认领、显示名、能力与 override |
| 研究型数据 | `officialDevices`、`deviceNotes`、厂商目录说明等 | 否，仅供人读与追溯 |

规则：

- `devices[]` 只放**准备让 VigilWatt 认领**的型号；未验证型号不要为了“先占位”写进去。
- `officialDevices[]` 是目录之外的只读登记表；加载器完全忽略它，不参与认领、显示名或能力查询。
- `deviceNotes` 是研究/说明数据，不参与认领、显示名、能力或解析。
- 同一份数据只保留一个权威位置；不要在协议、设备条目和品牌文档里各维护一份相同状态。
- 设备能力只写在 `devices[].capabilities`，不写在协议层，也不通过 `extends` / `override` 继承。

## 4. 字段语义与校验原则

### 4.1 字段类型与取值

- 字段类型只接受闭集合：`u8`、`u16`、`u24`、`u32`、`bit4`、`bit`。
- 偏移、宽度、长度、`mask`、`xor`、前缀和码表必须是可静态验证的整数或字符串。
- 取值映射必须显式：`map` 用于码到枚举/百分比，`mapValue` 用于码到数值；未命中时给“无值”，不拿 0 冒充。
- 电量与标志位同处一字节时使用 `mask`；`mask: 0` 是配置错误，加载期拒绝。

### 4.2 长度与边界

- 帧长只认该帧自己的 `FrameSpec.Length`，不得退回 `interface.inputLength` 当帧长。
- `{"eq": N}` / 数字表示精确长度；`{"min": N}` 表示最短合法长度；数组表示任一合法长度；没有可靠下界时跳过宽度检查，但负偏移仍拒绝。
- `active.query` 只在 `interface.outputLength` 为精确值时卡上界；查询帧可以更短，不能更长。
- 宽度计算使用 `long`，防止接近 `int.MaxValue` 时回绕成负数。

### 4.3 未知字段与重复项

- `ProtocolSpecLoader` 对协议 DTO、设备 DTO、嵌套 DTO 与 `override` 对应 DTO 开启 `JsonUnmappedMemberHandling.Disallow`；拼写错误、旧字段（如 `caps`、`devices[].note`）会在加载期失败，而不是静默忽略。
- 顶层研究字段（`officialDevices`、`deviceNotes`、部分品牌目录的 `transports` 等）由各自读取方处理，不属于当前 DTO 严格校验范围；不得把它们当成“可以随意加字段”的入口。
- 当前 `devices[]` 重复 `VID:PID` 不会报错，而是后写覆盖前写。新接入必须人工保证唯一；是否增加拒绝校验另开任务。

### 4.4 失败原则

加载期错误直接抛异常，由 `DeviceManager` 跳过该插件并记 Error。宁可启动失败，不要静默不认领；不要为了绕过一个配置错误去改 `FrameReader` 的回退顺序或 `IntMatch.Matches`。

## 5. `extends` 与 `override`

### 5.1 `extends`

- 子协议先展开父协议，再合并自己的字段；支持多级继承。
- 子级只写差异；继承来的字段在子级 `null` 表示删除，不是“保留父值”。
- 继承按声明顺序解析；已解析的父协议返回展开后的对象。
- `extends` 指向不存在的协议或形成环，加载期直接失败。

### 5.2 设备 `override`

- 设备 `override` 叠在“已展开 `extends`”的协议对象上，否则会丢掉继承来的字段。
- 只写要改的叶子；结构化深合并，不要求重写整段。
- `override` 不能声明 `capabilities`；能力只写在设备条目顶层。
- 同型号偏移差一位时优先 `override`；差异扩大到流程或语义时，应新建协议或写代码。

## 6. `schemaVersion` 兼容性原则

当前事实：

- 只有 `src/VigilWatt.Core/Plugins/Betop/betop-catalog.json` 顶层出现 `schemaVersion`；
- `ProtocolSpecLoader` 尚未把它作为通用版本门控，也没有按版本分支迁移；
- 因此现在不能声称“协议 JSON 已有统一 schema 版本兼容机制”。

后续原则：

1. 先实现版本读取、允许范围校验和失败信息，再允许新字段依赖版本。
2. 不支持的版本必须失败关闭，不得“按旧格式猜着读”。
3. 不静默迁移、不忽略未知版本、不在加载器里保留多个隐式分支。
4. 版本字段只在确实需要破坏性变更时引入；普通新增可选字段优先保持向后兼容。

## 7. 加载错误定位

错误信息必须能回答“哪份文件、哪个条目、哪个字段、为什么错”。最低要求：

- 资源名或内嵌资源路径；
- 协议名或设备 `VID:PID`；
- 设备条目序号（设备错误）；
- JSON Path 或字段路径（含嵌套对象、数组元素）；
- 期望值与实际值（类型、长度、枚举、码表键等）。

设备级错误必须带 `VID:PID`，不能只报“某个字段非法”。能力写错层时，错误应指出合法位置是 `devices[].capabilities`。

## 8. 不应继续扩展 JSON 表达能力的情形

以下需求默认写 C#，不要给 JSON 增加表达式、条件、循环或通用脚本：

- 握手顺序、会话保持、密码门、型号 ACK；
- 校验和、CRC、加密；
- 多帧组合、跨报告状态机、分块读；
- 电压到百分比、线性换算、查表以外的算术；
- 大端/有符号/跨字节位域；
- 同一接口多 report ID 且每个 ID 布局不同；
- 身份归一、显示名启发式、离线策略、轮询节拍、稳定窗口；
- 设备配置写入。

如果现有 JSON 无法表达，但差异只是数据（偏移、码表、查询帧、接口指纹），仍应先扩展数据字段；只有“新的判断或状态逻辑”才写代码。扩展 JSON 前必须同时补加载期校验、测试和文档，不能只加字段不校验。

## 9. 评审与验收

修改协议定义或加载器时至少检查：

- [ ] 新字段属于数据，不是流程伪装；
- [ ] 新字段有加载期校验与失败测试；
- [ ] 未知字段仍失败关闭，没有新增静默忽略路径；
- [ ] `extends` / `override` 的能力不继承规则未被破坏；
- [ ] 错误信息能定位到文件、条目和字段；
- [ ] 改 JSON 后跑了对应品牌测试类（至少 `Rapoo`、`Betop`、`Mchose` 中受影响者）；
- [ ] 文档写清“已实现 / 待治理”，没有把 `schemaVersion` 或统一加载器写成已完成。

## 10. 相关文档

- [通用协议引擎治理路线图](protocol-engine-roadmap.md)
- [协议 JSON 怎么写](../dev/protocol-json.md)
- [厂商插件架构标准](../dev/plugin-standard.md)
- [新厂商接入流程](../dev/vendor-onboarding-process.md)
- [设备身份与能力声明规范](device-identity-and-capabilities.md)
- [硬件安全规则](../hardware/safety.md)
