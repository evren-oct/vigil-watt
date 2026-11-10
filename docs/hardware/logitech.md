# 罗技 Logitech (VID_046D) —— ✅ Bolt 真机验证（2026-10-08）；协议驱动 + 嵌入目录

> 从 [protocols.md](protocols.md) 拆出（2026-10-21）。结论均来自真机验证或官方软件抓包；改协议代码前先读本文。

## 1. HID++ 接收器与直连设备

HID++ 1.0/2.0 协议发现（无型号白名单）。电量级联：
`0x1004` Unified Battery → `0x1000` Battery Status → `0x1001` Battery Voltage → `0x1F20` ADC Measurement → 1.0 寄存器 `0x07`/`0x0D`。

嵌入资源 [`logitech-hidpp.json`](../../src/VigilWatt.Core/Plugins/Logitech/logitech-hidpp.json) 提供接收器槽位/报文偏好与毫伏曲线；**目录是补充不是白名单**——未知 PID 仍打开 HID++ 接口（默认最多 6 槽、有长则跳短）。

| 新设备类型 | 怎么扩展 |
|---|---|
| 新的 HID++ 鼠标 / 键盘 / 演示器（已有特征） | **零代码**——ping + GetFeature 自动发现 |
| 新的接收器族（确认 PID / 槽位 / 长短偏好后） | 在 JSON `receivers` **加一行** |
| 某 WPID 电压曲线与默认锂电差很大 | JSON `wpidCurves` + `voltageCurves` |
| 全新电量特征 ID，或根本不是 HID++ | **改 C#**（耳机 / Centurion / 仅 G HUB 等） |

蓝牙罗技走标准 GATT `0x180F`（`BleBatteryWatcher`），不在此插件。

**本机 Logi Bolt 接收器 `046D:C548` + MX Master 3S（2026-10-08）：**
- 必须走 20 字节长接口（usage `FF00/0002`，report **0x11**）；旁边 7 字节短接口（`0x10`）对 Bolt 鼠标不应答，应跳过（目录 `reportPreference=long`）。
- Unifying（`C52B`/`C532`）常只在短接口应答 → 目录 `reportPreference=short`，有短同伴时跳过长接口。
- 把 `0x10` 补零写进长接口 → 驱动 `ERROR_INVALID_FUNCTION`（与北通硬编码错 report ID 同类）。
- `Request()` 返回缓冲从 feature 字节起，**参数在下标 2**：GetFeature 下标 / 电量% / 充电态分别是 `resp[2]` / `resp[2]` / `resp[4]`（旧代码读 `resp[0]` 会把 feature 下标当成电量）。
- 发现 ping 醒着约 1s 才回，超时宜 ≥1.5s；设备名走 `GetFeature(0x0005)` 再 GetCount/GetChar；设备类型优先读接收器配对寄存器 `0xB5`（Unifying `0x2N` / Bolt `0x5N`），读不到再 `DeviceKindGuesser`。
- Unified Battery `0x1004`：**func 1 = GetStatus**（电量% + 充电态），**func 0 = GetCapabilities**（不能当电量）。组装请求必须 `(feature<<8)|(func<<4)`，写成 `|func` 会被 SwId 补丁抹成 func 0（真机曾把 Capabilities 的 15 误当电量，充着电也读不到 charging）。
- **实时充电**：插拔电时设备会发 HID++ 自发通知（`sw` 低 4 位=0，payload 与 GetStatus 同布局）。插件后台 `PumpNotifications` 只读不写，经 `StatusPushed` 即时 Upsert——不必等 60 秒轮询（与 Logi Battery Dashboard 同源）。未知 `devIdx` 的通知会当场补 ping 发现。
- 每次 `Attach` 按目录槽位数补扫尚未发现的索引（接收器上第二台不必等重启）。

**接收器身份（M4-3B-2B1）**：`AddDevice` 只在非 Direct 路径、且 `HidppCatalog.Family()` 为 Bolt 时填 `ReceiverIdentity(Role: null, Family: "bolt")`。当前目录只有 `C548` 为 Bolt，因此首版仅开放 C548；`C52B/C532`、LIGHTSPEED、Nano、Legacy27Mhz 与未收录 PID 全部保持 `null`。`ReceiverIdentity` 描述接收器/传输通道，不是鼠标本体；本次不填 `Connection`，Slot Ping 成功也不写成 `DeviceReported`。`DirectDevice`、认领、去重、`SlotId` 与配对读取逻辑未改。

**HID++ 报文层（字节级；实现见 `Plugins/Logitech/HidppIO.cs` / `HidppCodec.cs`）：**

| 报文 | 布局 |
|---|---|
| 短（7 字节） | `[0x10, devIdx, feature, (func<<4)\|sw, p0, p1, p2]` |
| 长（20 字节） | `[0x11, devIdx, feature, (func<<4)\|sw, p0..p15]` |
| 错误响应 | `data[0]` = `0x8F`（1.0）/ `0xFF`（2.0）；`data[1..2]` 回显请求前两字节；`data[3]` = 错误码 |
| 设备自发通知 | `sw` 低 4 位 = 0（本应用请求应答用 SwId `0x0B`，据此区分） |

**各特征的电量 / 充电编码（实现见 `HidppCodec.cs`）：**

| 特征 | 编码 |
|---|---|
| Battery Voltage `0x1001` | 大端毫伏 + flags；`bit7`=充电中、低 3 位 `1`=充满、`bit5`=危急 |
| ADC Measurement `0x1F20` | 大端毫伏 + flags；`0x01`=放电 / `0x03`=充电 / `0x07`=充满 |
| 统一充电状态码（`0x1004` / `0x1000` 共用） | `1`/`2`/`4`=充电中、`3`=充满、其余=放电 |
| 配对信息 `0xB5` 字节布局 | Unifying `0x2N`：参数区 `[3..4]`=WPID、`[7]`=kind；Bolt `0x5N`：`[1]`=kind、`[2..3]`=WPID（小端，交换后成大端） |
| 设备名称/类型 `0x0005` | func 0 = 名称字符数、func 1 = 按位置取字符（一帧一个字符，最多 32）、func 2 = 营销类型字节 |

**设备类型的判定顺序**：`0x0005` func 2 的营销类型（权威，能正确区分触控板/轨迹球/演示器）→ 配对寄存器 `kind`（粗，会把触控板报成键盘鼠标）→ 按产品名猜。没有对应 `DeviceKind` 的类型（触控板、演示器、接收器、摄像头等）一律回 `Unknown` 交给下一级，不硬套。2026-11-01 加入 func 2；此前只用配对寄存器。

## 2. PRO X 2 LIGHTSPEED（Centurion）

**适用范围**：LIGHTSPEED 2.4G 接收器 `046D:0AF7` 的 vendor HID collection `UsagePage=0xFFA0, Usage=0x0001`，输入/输出报文均至少 64 字节。此接口不是 HID++ `0xFF43`。蓝牙由项目通用经典蓝牙监视器读取 Windows 电量属性，不经过 Centurion 插件；USB 有线模式当前不认领。

**协议依据**：HeadsetControl 的 PRO X 2 设备说明标出 PID `0x0AF7`、`0xFFA0:0x0001`、64 字节 report `0x51`；其已公开的只读 legacy 电池查询为固定报文 `51 08 00 03 1A 00 03 00 04 0A` 后补零至 64 字节。预期应答以 `51 0B` 开头，`[8]=0x04`，电量在 `[10]`（0–100）。VigilWatt 只发送该固定查询，不枚举 feature / 命令。

| 字节 | 含义 | 当前处理 |
|---|---|---|
| 请求 `[0..9]` | 固定电池状态请求 | 每次探测 / 轮询最多发送一帧 |
| 响应 `[0]` / `[1]` | report `0x51` / 电池应答 `0x0B` | 严格匹配 |
| 响应 `[8]` | 电池项标记 `0x04` | 严格匹配 |
| 响应 `[10]` | 电量百分比 | 超出 0–100 即丢弃 |
| 响应 `[12]` | 充电状态 | `0x00` 放电、`0x02` 充电；其余 Unknown，待本机核验 |

来源中的独立真机报告对充电状态 `0x03` 的解释与 HeadsetControl 当前 legacy 解析不完全一致，因此 VigilWatt 暂不把 `0x03` 推断为“已充满”。设备至少需要开机并与接收器连接才能通过电量应答认领；休眠 / 关机的离线事件未在此处实现。写入始终受现有“罗技”插件开关控制；Centurion 连续 4 次查询无应答才熔断并标记离线（默认其他主动查询插件仍为 3 次），按项目退避策略重试。2026-10-23 真机拔下充电线时，连续 3 次短时查询失败但耳机仍正常使用，下一轮查询成功；因此为 Centurion 额外容忍 1 次失败，避免短暂链路抖动造成离线闪烁。

**真机验证（2026-10-23）**：Windows PnP 枚举到 `046D:0AF7`；VigilWatt 扫描出 16 个 HID 接口，并以 `UsagePage=0xFFA0 / Usage=0x0001`、IN/OUT 各 64 字节、report ID `0x51` 认领 PRO X 2 LIGHTSPEED。Centurion 查询成功解析为 **45% / Discharging**；充电测试时应用显示 **43% / Charging**，设备页有闪电标识及“充电中，暂停估算”，更新时间 18:37:43（用户提供的真机截图）。这确认了应用对充电状态的端到端呈现；截图未包含原始响应字节，故未独立核验状态字节 `0x02`。测试使用临时数据目录，未改正式设置；进程日志显示以管理员 / High 完整性运行。

同机 `046D:C54D` 接收器上的 HID++ 设备也显示为 PRO X 2，读到 78%；用户确认这是另一台鼠标，不是耳机。休眠时应用标记离线、唤醒后恢复为充电中并重新上线（用户截图：18:41:46 离线、18:43:51 在线，电量均为 43%）。插件关闭零写入已于 2026-10-23 按后文方式核验；蓝牙模式另由通用经典蓝牙监视器真机读到 Windows HFP 电量，USB 有线模式未覆盖。

**诊断版补充验证（2026-10-23，2.4G）**：用户在 19:29 后、19:30 前插入充电线，约 19:31 拔下。诊断日志 `app-2026-10-23.log` 记录 19:29:10 首次读到 53% / Discharging，19:30:10 读到 53% / Charging，19:31:10 仍为 53% / Charging，19:32:10 读到 55% / Discharging。之后轮询每分钟一次，查询耗时约 8–13 ms。与用户截图一致：19:32:11 鼠标行更新时间为 19:32:11，耳机为 19:32:10；诊断版的成功读数时间戳现能同步到列表，即使状态未变化也会刷新。按用户给出的约略插拔时间，状态在下一分钟轮询中反映，延迟约不超过 70 秒；这是分钟级轮询的实测表现，不代表更快的事件通知。此轮用日志确认了系统返回的解析值和截图状态，但原始响应字节未落日志，故未单独复核充电状态字节。插件关闭零写入见下段；USB 有线模式未覆盖。

**插件关闭核验（2026-10-23）**：用户在诊断版设置中关闭“罗技”，并保持应用运行约两分钟。设置文件记录 `PluginEnabled.Logitech=false`；日志于 19:42:40.058 记录 Logitech 插件卸载、释放 1 台 Centurion 设备、HID++ 插件释放 1 个接收器会话，并移除 2 台罗技设备。最后一条 Centurion 查询在 19:42:11.534；禁用后的约两分钟内没有新的查询记录。代码检查确认 Centurion 查询在 `WriteOutput` 前检查 `WritesEnabled`，HID++ 唯一写点也由 `WritesAllowed` 拦截。由应用日志、设置与代码闸门可确认 VigilWatt 在开关关闭后停止了罗技通信；本次没有 USB 总线级抓包，因此结论范围是 VigilWatt 本身。

**蓝牙补充验证（2026-10-23）**：用户将 PRO X 2 切换并连接到 Windows 蓝牙音频。VigilWatt 日志 19:50:08.330 将经典蓝牙设备 `PRO X 2` 识别为耳机，19:50:15.017 记录 `经典蓝牙电量(HFP 节点): PRO X 2 60%`；同一时刻用户截图中 Windows 蓝牙设置与 VigilWatt 蓝牙行均为 60%。这是 Windows HFP 设备节点电量属性的端到端验证，不是 BLE GATT，也没有使用 Centurion / Logitech HID++ 查询。VigilWatt 同时显示另一条 2.4G 耳机接口离线（55%），符合切换离开 LIGHTSPEED 模式后的表现；鼠标仍独立显示 77%。用户 G HUB 截图将 77% 鼠标标为 PRO X SUPERLIGHT 2、55% 耳机标为 PRO X 2 LIGHTSPEED，因此经典蓝牙类型确认是耳机且原始名称为 `PRO X 2` 时，将列表名规范为 `PRO X 2 LIGHTSPEED`；其他名称不作型号推断。列表提示“采集不足”是剩余时间估算还未满足多个时间点，不影响当前 60% 电量读数。

详细来源见 [sources.md](sources.md)；外部协议参考：[HeadsetControl PRO X 2 实现](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_gpro_x2_lightspeed.hpp)、[LogitechBatteryMonitor 协议与实机记录](https://github.com/Recopec/LogitechBatteryMonitor)。两项目均注明 GPL-2.0；本项目只参考公开协议事实，未复制其实现代码。

## 3. 通知派发与递归事故（issue #55）

**事故**：G102 Lightsync 上有线 HID++ 通道的 `WriteOutput` 恒报 `ERROR_INVALID_PARAMETER`，对端永不回包；等包窗口里到达的每条通知都被同步 `Invoke` 给订阅方，订阅方（`OnHidppNotification → TryDiscoverSlot`）又发新请求 → 同线程重入 `lock (_ioGate)`（Monitor 可重入，锁拦不住）→ 栈耗尽 `0xc00000fd`。贡献者转储实测 **1335 层完全同构的调用环**。

**修法（三层，均在 2026-11-01 落地）**：

| 层 | 位置 | 做法 |
|---|---|---|
| 通知不再内联回调 | `HidppNotificationQueue` | 读报文的一方只入队；`Drain` 在 `_ioGate` **之外**执行，`Interlocked` 守卫保证派发期间重入只入队（由外层循环接手），递归变平循环 |
| 通知不再就地发现 | `LogitechPlugin.OnHidppNotification` | 未认过的槽位只记进 `PendingDiscover`，由监听线程在 `PumpNotifications` 返回后补发现，一拍只探一个 |
| 写不通就停手 | `HidppIO.Request` | `WriteOutput` 失败即停写 **30 秒**，冷却期内不写不读不派发 |

配套两条：队列上限 256 条（超出丢最旧并计数）、单次泵出预算 200ms（**按时间不按条数**，否则 N 条 × 请求超时会把线程占满几分钟）。

**空槽冷却**：`TryDiscoverSlot` 过去先 `EmptySlotUntil.Remove` 再 `Ping`，等于通知路径完全绕过 5 分钟负缓存——每来一条通知就重探一次，是递归的放大器。现在对所有调用方一律先查冷却。

**未修**：G102 那条接口本身写不进去是另一个问题（疑似该走 `SetOutputReport` 而非 `WriteOutput`），并入 `reviews/BACKLOG.md` 的 CR-88 一起查，要设备才能验。
