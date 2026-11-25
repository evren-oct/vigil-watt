# 协议回归测试规范

> 状态：规范生效；M3 待开展，现有测试分散但可复用。
> 本文定义 Golden Frame、测试分层与最小可靠测试集；具体命令、构建环境和沙箱限制仍以 [`docs/dev/build.md`](../dev/build.md) 与 `AGENTS.md` §4 为准。
> 基线：2026-11-23，`dev` 分支 `d117b59`。

## 1. 目的与范围

协议回归测试要证明三件事：

1. 真实帧能被正确解析，且不会把无效帧读成有效读数；
2. 主动查询只在授权条件下发生，未授权时零写入；
3. 设备匹配、身份归一和能力声明不会因为新增型号而串台。

本文覆盖 `VigilWatt.Core.Tests` 中的协议相关测试，不要求建立采集平台，也不要求本次采集新硬件报文。

## 2. 测试分层

| 层 | 验证什么 | 典型测试 |
|---|---|---|
| 加载期 | JSON 结构、字段边界、`extends` / `override`、能力位置 | `ProtocolSpecLoaderTests`、`DeviceCapabilityTests` |
| 设备匹配 | 目录优先、接口指纹、未收录兜底、多接口 | `BetopProtocolMatchTests`、`RapooSpecTests` 的匹配组 |
| 被动解析 | 电量、充电、链路、连接方式、指针遥测 | `RapooSpecTests`、`BetopSpecTests`、`MchoseCzSpecTests` |
| 主动查询授权 | 未授权零写入、已授权查询、写入关闭 | `BetopActiveGateTests` |
| 身份归一 | 有线/无线、SN、`group`、槽位、自报身份 | `RapooIdentityTests`、`BetopDeviceIdentityTests`、`MchoseIdentityTests` |
| 运行策略 | 熔断、退避、半开探活、离线 | `ReadingStateTests`、`BreakerProbeTests`、`OfflinePolicyTests` |
| 真机验收 | 官方读数对照、插拔、充电、休眠 | 品牌文档中的验证记录与 `support-matrix.md` |

单测不依赖硬件；真机验收不由单测冒充。

## 3. Golden Frame 的用途与要求

Golden Frame 是一条有明确来源的真实报文，用来钉住某套协议在某个型号、固件、连接方式下的布局。

每条 Golden Frame 至少记录：

- 设备型号与 `VID:PID`；
- 连接方式（有线 / 2.4G / 蓝牙）与接口段（如 `MI_01&Col09`）；
- report ID 与实际帧长；
- 采集时间或版本（能拿到就写，拿不到写“未知”）；
- 来源（真机、官方软件抓包、公开文档、单测录制）；
- 预期电量、充电、链路、连接方式、指针遥测。

写法要求：

- 真实帧放在测试类常量中，旁边用注释写来源与适用边界；
- 合成帧只用于边界、异常和长度测试，不冒充真机证据；
- 不把一个型号的帧外推到全族；同族不同固件需要各自样本；
- 帧样本不写入历史库或运行数据，只作为测试输入。

## 4. 预期结果

解析测试必须断言具体值，而不是“非 null”：

- 电量：精确百分比，或明确 `null`；
- 充电：`Unknown` / `Discharging` / `Charging` / `Full`；
- 链路：`Unknown` / `Up` / `Down`；
- 连接方式：`Unknown` / `Wired` / `Wireless24G` / `Bluetooth`；
- 指针遥测：DPI、档位、回报率的值、来源与是否未知。

无效帧、越界帧、占位帧应返回 `null` 或 `Unknown`，不得用 0 冒充“读到 0%”。帧长、偏移、掩码、码表未命中都要有边界用例。

## 5. 被动解析测试

现有模式可直接复用：

- `RapooSpecTests`：真机正常帧、断链帧、重连帧、指针遥测、能力边界；
- `BetopSpecTests`：状态 15、无充电变体、宙斯被动流；
- `MchoseCzSpecTests` / `Mchose0x4D*Tests`：分块读、连接/充电映射、DPI/回报率；
- 其它品牌 codec 测试：各协议自己的帧构造与解析边界。

新增协议族至少要有：

- 一条真实帧解析成功；
- 一条长度/前缀/校验不匹配的无效帧；
- 一条电量越界或占位帧；
- 一条码表未命中或未知值。

## 6. 主动查询授权测试

参考 `BetopActiveGateTests`，用可记录写入的 Fake Handle 验证：

- 未授权 `ProbeAsync` 零写入；
- 未授权 `Poll` 返回 `NotAttempted`，且不记失败；
- 未授权时被动帧仍可读；
- 已授权查询能发出并解析应答；
- 未登记接口 / 接口指纹不符不查询；
- `WritesEnabled=false` 时不写；
- 握手 / 会话帧同样受授权链约束；
- 熔断、退避、半开探活不绕过插件开关。

直接 HID 插件也要有等价测试，不能只测 `ProtocolEngine`。

## 7. 设备匹配边界测试

至少覆盖：

- 目录命中且接口匹配；
- 目录命中但接口不符；
- 未收录设备按协议顺序兜底；
- `implementation=none` 不认领；
- 同一物理设备多接口只认领一条；
- 接收器与本体、空槽与越界槽位；
- 插线 + 接收器同时在位只保留一条记录；
- 同一 VID/PID 多品牌共用指纹时按 ACK/型号码分流；
- 身份归一后历史键稳定，换 USB 口不产生新设备。

## 8. Mock HID / Fake Transport 原则

- 单测不打开真实设备，不依赖真机在位；
- Fake Handle 用临时文件流或等价可读可写载体，写入可计数；
- 不 `Sleep` 等真实超时；窗口、重试、退避用可控参数或纯函数测试；
- 测试接缝只供测试使用，不把生产路径改成“为测试服务”；
- 纯逻辑保持零 WinRT 依赖，能被测试项目链接；
- 真实帧常量与测试辅助类放在测试项目，不放进 Core 生产代码。

## 9. 最小可靠测试集

新协议/新插件至少满足：

- [ ] 每个已接入协议族至少 1 条有来源的真实帧；
- [ ] 每个协议族至少 1 条无效帧/边界帧；
- [ ] 每个主动查询插件至少 1 条未授权零写入测试；
- [ ] 每个主动查询插件至少 1 条已授权查询测试；
- [ ] 每个身份归一规则至少 1 条边界测试；
- [ ] 每条加载期规则至少 1 条失败测试；
- [ ] 每个 `supported` 能力有真机或录制帧证据指针。

## 10. 执行要求

标准命令：

```powershell
dotnet build VigilWatt.sln -c Debug
dotnet test src/tests/VigilWatt.Core.Tests --no-build --filter "FullyQualifiedName~<类名>"
```

- 改帧解析、读数过滤、历史库或离线判定，必须重跑对应测试类；
- 合并/发布前跑全量回归；
- Codex/AI 沙箱内不要构建或跑测试，先申请沙箱外执行权限，按 `AGENTS.md` §4 原样运行；
- 测试失败先区分环境问题、测试问题、实现问题，不通过改断言掩盖。

## 11. 当前缺口与 M3 工作

- Golden Frame 分散在各测试类，缺少统一证据索引；
- 主动查询授权测试只覆盖北通先行路径；
- 设备匹配、身份、能力之间没有一张覆盖矩阵；
- 没有规定新增协议族的最小样本数量（本文先给出最低要求）。

M3 应优先做“证据索引 + 覆盖矩阵 + 主动查询测试补齐”，不要求建设新的采集平台。

## 12. 相关文档

- [通用协议引擎治理路线图](protocol-engine-roadmap.md)
- [主动查询安全规范](active-query-safety.md)
- [设备身份与能力声明规范](device-identity-and-capabilities.md)
- [设备诊断（USB/HID 采集）](../project/capture.md)
- [调试手法手册](../hardware/debugging.md)
- [构建与环境坑](../dev/build.md)
- [代码审查标准与流程](../dev/review-standard.md)
