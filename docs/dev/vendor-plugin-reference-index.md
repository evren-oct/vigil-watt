# 四大厂商参考实现索引

> 适用范围：接入新品牌或新型号时，先按协议形态找参考实现，再只读最相关的少量文件。
> 基线：2026-11-19，`dev` 分支。本文是索引，不是新规范；规则以[厂商插件架构标准](plugin-standard.md)和[新厂商接入流程](vendor-onboarding-process.md)为准。
> 所有路径均为仓库相对路径。先读插件入口与 JSON，再按需读身份/解析辅助类；不要从整个目录开始通读。

## 0. 先分清 Core 通用职责

下列能力由 Core 统一提供，不要误记成某个厂商插件的实现，也不要为新厂商重写一份：

| 职责 | 组件 | 路径 |
|---|---|---|
| 插件生命周期、认领、探测/轮询编排、离线动作、写入闸门 | `BatteryPluginBase` | `src/VigilWatt.Core/Plugins/BatteryPluginBase.cs` |
| 认领互斥 | `DeviceClaimRegistry` | `src/VigilWatt.Core/Plugins/DeviceClaimRegistry.cs` |
| 离线参数 | `OfflinePolicy` | `src/VigilWatt.Core/Plugins/OfflinePolicy.cs` |
| 读数过滤 | `BatteryReadingFilter` | `src/VigilWatt.Core/Plugins/BatteryReadingFilter.cs` |
| 纯数据协议解释与加载 | `ProtocolEngine` / `ProtocolSpecLoader` / `FrameReader` | `src/VigilWatt.Core/Plugins/Protocol/` |
| 路径稳定哈希 | `PluginUtils` | `src/VigilWatt.Core/Plugins/PluginUtils.cs` |
| 发现节拍与插件调度 | `DeviceManager` | `src/VigilWatt.Core/Devices/DeviceManager.cs` |

发现路径实际调用 `BatteryPluginBase.ProbeAsync`；默认实现会转调同步 `Probe`。Rapoo/Betop 的写法是 `Probe` 只转调 `ProbeAsync`，实际探测逻辑只写一份。`codeFamily` 当前只有声明与加载期校验，没有运行时执行器，不要据此新增统一执行器。

## 1. 雷柏 Rapoo

**参考定位：纯数据协议、固定帧字段提取。**

### 入口与配置

- 插件入口：`RapooPlugin`，`src/VigilWatt.Core/Plugins/Rapoo/RapooPlugin.cs`
- 协议规格：`src/VigilWatt.Core/Plugins/Rapoo/specs/rapoo.protocols.json`
- 设备目录：`src/VigilWatt.Core/Plugins/Rapoo/rapoo-catalog.json`
- 身份归一：`src/VigilWatt.Core/Plugins/Rapoo/RapooDeviceIdentity.cs`
- 显示名兜底：`src/VigilWatt.Core/Plugins/Rapoo/RapooDisplayName.cs`

### 认领与身份

- `RapooPlugin.IsCandidate` 委托 `ProtocolEngine.Matches`：目录命中先定协议，未收录才按 `interface` 注册顺序匹配。
- `RapooPlugin.BuildDeviceId` 委托 `RapooDeviceIdentity.StableId`：目录型号 + SN；没有 SN 回落到基类路径哈希。
- `RapooPlugin.SkipPathsForThisPass` 委托 `RapooDeviceIdentity.WirelessPathsToSkip`：有线本体可认领时摘掉同组无线接口。
- 认领互斥由 `BatteryPluginBase` + `DeviceClaimRegistry` 完成；Rapoo 不自己维护认领表。

### 读取与解析

- `RapooPlugin.ProbeAsync` 委托 `ProtocolEngine.ProbeAsync`；`Probe` 只做同步桥接。
- `RapooPlugin.PollDevice` 委托 `ProtocolEngine.Poll`；`TryParsePassiveInput` 委托 `ProtocolEngine.Parse`。
- 帧布局、字段偏移、充电码表、链路码表都在 `rapoo.protocols.json`；C# 只把链路位转成离线候选。

### 特殊逻辑与边界

- 零写入纯被动：`WritesEnabled => false`、`ProbeWrites => false`、`SupportsPassiveListen => true`。
- `Confirm=6s` / `Silence=90s` / `CountPollFailures=false` 是雷柏心跳实测参数，不是通用默认值。
- SN 归一依赖目录和真实 SN；没有 SN 的设备必须回路径哈希，不能假设所有设备都有 SN。
- `AttachAsync` 里的 `_mousePids` 只辅助显示名/图标判断，不是认领判据。

### 适合参考

固定帧、固定字段提取、纯被动或固定查询的设备；新型号通常只改协议 JSON 和目录 JSON。

## 2. 北通 BETOP

**参考定位：多型号、多协议族、设备目录管理。**

### 入口与配置

- 插件入口：`BetopPlugin`，`src/VigilWatt.Core/Plugins/Betop/BetopPlugin.cs`
- 协议规格：`src/VigilWatt.Core/Plugins/Betop/specs/betop.protocols.json`
- 设备目录：`src/VigilWatt.Core/Plugins/Betop/betop-catalog.json`（`devices[]` + 只读 `officialDevices[]`）
- 身份归一：`src/VigilWatt.Core/Plugins/Betop/BetopDeviceIdentity.cs`
- 显示名：`src/VigilWatt.Core/Plugins/Betop/BetopDisplayName.cs`
- 官方软件观测：`src/VigilWatt.Core/Plugins/Betop/BetopOfficialAppGuard.cs`

### 认领与身份

- `BetopPlugin.IsCandidate` 用 `_engine.Resolve` 判接口；未登记且只有被动协议的接口只识别不探测，避免主动写帧。
- `BetopPlugin.BuildDeviceId` 委托 `BetopDeviceIdentity.GroupId`：只认目录显式 `group`，其余回路径哈希。
- `BetopPlugin.CapabilitiesOf` 直接读 `ProtocolEngine.CapabilitiesOf`；能力不按协议族外推。
- 认领互斥由 `BatteryPluginBase` + `DeviceClaimRegistry` 完成。

### 读取与解析

- `BetopPlugin.ProbeAsync` 委托 `ProtocolEngine.ProbeAsync`；链路 `Down` 时返回“已认领但无电量”。
- `BetopPlugin.PollDevice` 先 `Listen` 1.5 秒，再按需 `ProtocolEngine.Poll` 1.5 秒；`pollable=false` 的协议不轮询。
- `BetopPlugin.TryParsePassiveInput` 委托 `ProtocolEngine.Parse`；只有 passive-only 协议走 `IsSettled` 的 500ms 稳定判定。
- `BetopPlugin.FinalizeCharging` 负责 100% + 未充电补判 `Full`。

### 特殊逻辑与边界

- 当前 3 套 `implementation=data` 协议：`betop-status15`、`betop-status15-nocharge`、`betop-zeus-keyevent`。
- T9 是 250Hz 流，`IsSettled` 要求同一读数稳定 500ms；不要把这套窗口套到 1Hz 广播协议。
- 先听后查的 1.5 秒窗口、占位帧立即离线、`Silence=0`/`CountPollFailures=false` 都是北通协议事实。
- `BetopOfficialAppGuard.Observe` 目前只观测、不拦认领；不要把它当成已有的门控。
- 不按系列、PID 邻近或名字合并设备；只有目录显式 `group` 才归一。

### 适合参考

同一品牌多型号、多 PID、多协议族，且需要设备目录管理、先听后查或少量传输编排的设备。

## 3. 迈从 MCHOSE

**参考定位：有状态协议、握手、会话及特殊协议处理。**

### 入口与配置

- 主插件入口：`MchosePlugin`，`src/VigilWatt.Core/Plugins/Mchose/MchosePlugin.cs`
- 第二个插件类：`MchoseA5Plugin`，`src/VigilWatt.Core/Plugins/Mchose/MchoseA5Plugin.cs`
- 协议规格：`src/VigilWatt.Core/Plugins/Mchose/specs/mchose.protocols.json`
- 设备/传输目录：`src/VigilWatt.Core/Plugins/Mchose/mchose-catalog.json`
- 接口分类：`src/VigilWatt.Core/Plugins/Mchose/MchoseInterfaceClassifier.cs`
- 目录加载：`src/VigilWatt.Core/Plugins/Mchose/MchoseDeviceCatalog.cs`
- 身份快照：`src/VigilWatt.Core/Plugins/Mchose/MchoseDeviceIdentity.cs`
- 共用协议引擎：`src/VigilWatt.Core/Plugins/Mchose/MchoseSpecs.cs`

### 认领与身份

- `MchosePlugin.IsCandidate` 委托 `MchoseInterfaceClassifier.Classify`；分类器是正向 VID 白名单，并单独处理与 AULA 共用的 `3554:FA09` 指纹。
- `MchoseA5Plugin` 是“同品牌第二个插件类”的范例：共用 `Name = "Mchose"`，用 `ClaimId = "Mchose#A5"` 保持认领互斥；主插件明确排除 `MouseA5`。
- `MchosePlugin` 不覆写 `BuildDeviceId`，默认使用基类路径哈希；型号/自报身份通过 `MchoseDeviceIdentity`、`KnownIdentityByPath`、`FillIdentitySnapshot` 作为快照补充，不拿来改设备 ID。
- `KnownProtocolByPath` 只在探测成功后写入，失败不清记忆；`AttachAsync` 清理已消失路径，`Detach` 清空全部缓存。

### 读取与解析

- `MchosePlugin.Probe` 按接口分类分派：共用 AULA 指纹走 `TryKeyboardV1`，固定形态协议走 `TrySpec`（`MchoseSpecs.Engine.ProbeAsync`），鼠标走 `Try0x4D` / `TryMouseV2`，键盘走 `TryKeyboardV1`。
- `MchosePlugin.PollDevice` 对 `gmouse` / `headset-v9` / `headset-x9` / `cz-block` 走 `ProtocolEngine.Poll`；`mchose-0x4d` 走 `Poll0x4D`；`keyboard-v1` 走 `PollKeyboardV1`。
- `MchosePlugin.TryParsePassiveInput` 按协议调用 `MchoseMouseCodec.ParseStatus`、`MchoseV1Codec.ParsePush`、`Mchose0x4DCodec.TryParseStatusEvent`。
- 关键编解码：`src/VigilWatt.Core/Plugins/Mchose/MchoseV1Codec.cs`、`src/VigilWatt.Core/Plugins/Mchose/Mchose0x4DCodec.cs`、`src/VigilWatt.Core/Plugins/Mchose/MchoseMouseCodec.cs`；同目录还有 `MchoseGmouseCodec.cs`、`MchoseV9ProCodec.cs`、`MchoseX9Codec.cs`、`MchoseA5Codec.cs`，是各自家族的薄壳或判据。

### 特殊逻辑与边界

- 分类器顺序有硬约束：耳机先于鼠标、`0x4D` 先于 V2、A5 先于通用鼠标；改顺序会把触发帧发给错误接口。
- 与 AULA 共用指纹时，必须靠会话 ACK 的型号密码定归属；不能只按 VID/PID 抢接口。
- `KnownProtocolByPath` 只适合“同路径永远同型号”的有状态设备；纯数据协议不要加这层缓存。
- `0x4D` 的 `payload[10]` 连接位只刷徽标，不当离线判据；0x4D 的“1 字节 0xFF”也算应答，失败记账不能套通用规则。
- `keyboard-v1` 的 `0A` 状态通知语义未定论，只作为 `RequestConfirm` 触发器，不当离线证据。
- `MchosePlugin` 用 `SupportsDiscoveryProbe => true` + `ProbeCooldown = 10s` 参与发现节拍；这是有写入探测的专门配置，不要套到纯被动插件。

### 适合参考

有握手、会话、ACK、密码门、分块读、多帧交互、多代协议共存的设备；也适合学习同品牌多插件类如何用 `ClaimId` 分工。

## 4. 罗技 Logitech

**参考定位：多设备接收器、逻辑设备管理。**

### 入口与配置

- 插件入口：`LogitechPlugin`，`src/VigilWatt.Core/Plugins/Logitech/LogitechPlugin.cs`
- HID++ 目录：`src/VigilWatt.Core/Plugins/Logitech/logitech-hidpp.json`
- 目录加载：`src/VigilWatt.Core/Plugins/Logitech/HidppCatalog.cs`（接收器族、槽位、报文偏好、电压曲线）
- 底层 I/O：`src/VigilWatt.Core/Plugins/Logitech/HidppIO.cs`
- 报文编解码：`src/VigilWatt.Core/Plugins/Logitech/HidppCodec.cs`
- 通知队列：`src/VigilWatt.Core/Plugins/Logitech/HidppNotificationQueue.cs`
- 指针编解码：`src/VigilWatt.Core/Plugins/Logitech/HidppPointerCodec.cs`
- 连接推断：`src/VigilWatt.Core/Plugins/Logitech/LogitechConnectionPolicy.cs`
- 接收器身份：`src/VigilWatt.Core/Plugins/Logitech/LogitechReceiverIdentityPolicy.cs`
- Centurion 编解码：`src/VigilWatt.Core/Plugins/Logitech/CenturionCodec.cs`

### 认领与身份

- `LogitechPlugin.IsCandidate` 只判 Centurion（PRO X 2）；HID++ 设备不走基类 `IsCandidate`。
- HID++ 认领在 `LogitechPlugin.AttachCore`：`IsHidppInterface` 判接口，`claims.TryClaim` 认领，`ReceiverSession` 保存“接收器 → 槽位 → 设备”。
- `LogitechPlugin.SlotId` 用“接收器路径稳定哈希 + 槽位”；直连设备用 `0xFF`，换 USB 口不产生新设备。
- `AttachAsync` / `Poll` / `ReconcileRemovals` 都用 `new` 隐藏基类方法并重新声明 `IBatteryPlugin`；不要用 `BatteryPluginBase` 静态类型持有后调用。

### 读取与解析

- Centurion 分支：`LogitechPlugin.Probe` / `PollDevice` 调 `TryReadCenturionBattery`，帧由 `CenturionCodec` 处理。
- HID++ 分支：`AttachCore` → `DiscoverDevices` → `AddDevice`；`Poll` → `RefreshBattery`；电量级联在 `HidppCodec`（`0x1004` → `0x1000` → `0x1001` → `0x1F20` → HID++ 1.0 寄存器）。
- 被动通知：`HidppIO` 把通知入队，`HidppNotificationQueue` 在 I/O 锁外派发；未认过的槽位只登记，下一拍再发现。
- 指针遥测：`RefreshPointer` + `HidppPointerCodec`，与电量节拍分开。

### 特殊逻辑与边界

- `LogitechPlugin` 是 R2 混合型唯一例外：Centurion 走基类，HID++ 自建接收器会话；不是所有新厂商的默认模板。
- 接收器槽位容量优先读 `0xB5/0x03` 自报值，读不到才回 `HidppCatalog.MaxSlots`；空槽有负缓存，首次整轮无应答会丢句柄重开。
- `LogitechConnectionPolicy` 只产出 `Inferred(Wireless24G)`，不冒充 `DeviceReported`。
- 写入闸门分两条：HID++ 走 `HidppIO.WritesAllowed`，Centurion 在发查询前查 `WritesEnabled`。
- `HidppCatalog` 是补充目录，不是 HID++ 白名单；未收录 PID 仍按默认行为打开 HID++ 接口。
- 不要把罗技的槽位/直连/容量/通知模型推广成所有品牌的通用接收器模型；其它品牌的“接收器”可能只是透明通道。

### 适合参考

一个接收器下挂多台逻辑设备、需要槽位寻址、容量推断、自发通知和独立设备身份的接入。

## 5. 模板选择

选型判据与矩阵以 [插件架构标准](plugin-standard.md) §3.4 为准；本索引只负责“每个模板的代码入口在哪”，不再维护第二张选择表。

## 6. 相关文档

- [厂商插件架构标准](plugin-standard.md)：职责边界、R2 例外、目录/协议规则、Core 准入。
- [新厂商接入流程](vendor-onboarding-process.md)：六步流程、实施红线、验证与文档沉淀。
- [协议 JSON 写法](protocol-json.md)：字段词表、`extends` / `override`、加载期校验。
- [硬件安全规则](../hardware/safety.md)：零写入、写入闸门、熔断与退避。
