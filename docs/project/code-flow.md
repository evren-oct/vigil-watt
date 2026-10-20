# 代码流程

> 运行时的每条数据流，按真实调用顺序写。方法名均可搜索定位。

## 1. 启动序列（App.OnLaunched）

1. **单实例检查**：命名 Mutex `VigilWatt_SingleInstance`，已存在则直接退出。
2. `new AppServices()`：加载 AppSettings → 打开 HistoryStore（建库/建表）→ `new DeviceManager(settings, history)`（构造里挂 BLE + 经典蓝牙的 DeviceUpdated → Upsert）。
3. `Manager.Start()` → `BleBatteryWatcher.Start()` + `BluetoothClassicWatcher.Start()`（见 §3）+ 启动三个定时器：
   - 轮询：**300ms 后首跑**（探测已并行/短路，不再需要 2 秒缓冲），之后每 **1 分钟**（`RequestPollAsync` → `PollOnceAsync`）
   - 快速发现：**15 秒**（`RequestDiscoveryAsync` → `DiscoverOnlyAsync`，见 §2.1）
   - 采样：1 分钟后首跑，之后每 `SampleIntervalMinutes` 分钟（`SafeSample`）
4. `new MainWindow()`（NavigationView 三页），然后 TrayService.Initialize()（Win32 消息窗口 + Shell_NotifyIcon）。
5. `MainWindow.Activate()` → 若 `StartMinimized` 立即 `AppWindow.Hide()`（关窗也是隐藏，见 §7）。
6. 若 `WidgetEnabled` → `new WidgetWindow()` + Activate（DIP×缩放尺寸、普通层级不置顶、工具窗口样式）。
7. 全程异常兜底：App.UnhandledException / AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException → Log.Error。

## 2. 轮询主循环（DeviceManager.RequestPollAsync → PollOnceAsync，每 1 分钟 + 手动刷新共用）

```
RequestPollAsync                       # 若已有整轮在跑 → 直接复用它的 Task 返回（不静默丢弃）
└── RunExclusiveAsync(PollOnceAsync)   # Task.Run + _passGate(SemaphoreSlim) 串行：同一时刻只有一段 pass 在动插件
PollOnceAsync
├── HidDeviceScanner.Scan()            # 枚举路径；路径未变则复用元数据缓存，不再 CreateFile
├── 未启用插件 → Detach + ReleaseAll（串行）
├── Task.WhenAll(各启用插件 AttachAsync)  # 不同接收器并行探测；同一插件内候选仍串行
│      → 全部结束后再逐个 Upsert（历史写入不跨任务）
├── foreach 插件 Poll()                # 已认领设备仍按插件顺序轮询
├── await _ble.PollConnectedAsync()
├── _classic.RefreshBattery()
└── DevicesChanged?.Invoke()
```

`BatteryPluginBase.AttachAsync` 先释放本轮已消失接口的监听、句柄与认领，再探测新接口；同一身份重连成功时，新状态替换返回列表中的旧离线快照，探测失败则保留旧离线状态。仍存在的同身份别名只释放新探测句柄，不创建重复条目。设备路径、探测退避键与全局接口认领均忽略大小写。

### 2.1 快速发现（DiscoverOnlyAsync，每 15 秒）

`RequestDiscoveryAsync` 走**同一个闸门**，所以永远不会和主轮询并发；主轮询在跑时它直接返回那一轮。

```
DiscoverOnlyAsync
├── HidDeviceScanner.Scan()
└── Task.WhenAll(SupportsFastDiscovery || SupportsDiscoveryProbe 的启用插件 AttachAsync)
       → 只认领，不 Poll；Upsert 仍在闸门线程
```

**并行粒度（2026-10-16）**：不同品牌各持自己的 HID 句柄，慢探测可重叠；同一句柄（含罗技多槽）仍排队。发现路径用 `ReadInputAsync`（雷柏/英菲克）或把同步 `Probe` 丢到线程池（迈从/北通默认），禁止用 `Parallel.For` 包阻塞的 `GetResult()`。

存在的理由（2026-10-06 实测）：雷柏鼠标**只在醒着时说话**（dongle 休眠即全静音，用户动一下才恢复心跳）。
主轮询 60 秒一拍，撞上鼠标睡着的窗口就必然落空，而首扫时更是必然落空（用户刚启动应用、还没碰鼠标）。
15 秒节拍把"发现时延"从最长一个主周期压到 ~15 秒。

两个标志（都默认 false，由插件自己声明）：

| 标志 | 谁能置 | 探测写帧 | 已认领后每拍代价 |
|---|---|---|---|
| `SupportsFastDiscovery` | **只有纯被动零写入插件**（Rapoo） | 否 | 0 |
| `SupportsDiscoveryProbe` | 探测要写帧、但写入频率由自己 `ProbeCooldown` 兜住的插件（Mchose） | 是（≈3 帧/次） | **0**（`Attach` 对已知设备直接返回，不探测） |

会写帧的插件**不得**用 `SupportsFastDiscovery`——加快节拍等于成倍抬高写入频率，违反 hardware/safety.md。
`SupportsDiscoveryProbe` 是 2026-10-08 为"键盘开了半天才显示在线"开的第二条路：代价只出现在
**设备不响应**期间（最坏 ≈12 帧/分钟），认领成功后归零。

`ProbeCooldown` **必须小于 15 秒节拍**，否则又被相位吃掉（重试间隔翻倍）：雷柏 5 秒、迈从 10 秒
——迈从一次探测本身约 3.7 秒，`10 + 3.7 < 15` 才能每拍都重探。

**首扫提速（2026-10-06）**：`Attach` 的返回值曾被丢弃 → 探测刚读到的电量被丢、紧接着 `Poll` 又完整重读一遍（雷柏白多花 5s）。现在：
- `Attach` 返回值立即 `Upsert` → **新设备探测出来就进 UI**，不等整轮扫描结束；
- 基类给本轮新认领的设备打 `FreshlyClaimed` 标记，**本轮 `Poll` 跳过**（探测值即最新读数），下一轮起正常轮询；
- 雷柏 `IsCandidate` 收敛到"输入长度 ==19"（协议文档里状态报表就是 19 字节那一条）——旧版放行 19/32/33，本机 0x0011/32B 与 0x000E/33B 两条 vendor 通道从不发 0x07，每次首扫各白等一整个窗口；收敛后一个 dongle 只剩唯一候选，探测窗口 7s→5s；
- 迈从 `TryQhw` 改官方时序（**先握手**，非 QHW dongle 写入立即失败即快速退出，省掉旧版先空等 3s 被动监听），并按接口路径缓存已判定协议。

**探测失败冷却（`ProbeCooldown`）必须明显小于轮询周期**：失败发生在轮询开始后几秒处，冷却若等于一整轮，
下一拍只剩 5x 秒会被判"冷却未过"而整轮跳过，实际重试间隔翻倍（雷柏 3 分钟 → 4 分钟、北通 1 分钟 → 2 分钟，
都是这么来的）。基类默认 **30 秒** = "每一轮都允许重探"。

> 同一插件内多候选仍串行（候选通常已收敛到 1 条）。跨插件并行见上。日志"安静"≠"卡死"。

**罗技空槽（CR-62）**：首次打开全扫目录槽位；超时槽位负缓存 5 分钟，期满后每轮最多补探 1 个。接收器一打开就 `EnsureListening`（只读），通知到未知 `devIdx` 时立刻补槽、不受冷却限制。

每轮的插件结果逐个 `Upsert(d)`（见 §5）。任何插件抛异常只记日志，不影响其他插件。

## 3. 蓝牙设备流（BleBatteryWatcher + BluetoothClassicWatcher）

**发现**：`BluetoothLEDevice.GetDeviceSelector()` 建 DeviceWatcher，属性候选列表逐个试（本机只有 `System.Devices.Aep.IsConnected` 可用）。Added/Updated 回调 → HandleDevice → 组 DeviceStatus（含名称、连接态）→ DeviceUpdated → Manager.Upsert。

**电量**：系统属性可靠时直接用；否则 `ReadBatteryViaGattAsync`——连接 → GetGattService(0x180F) → 读 characteristic(0x2A19) → 字节[0] 即百分比，8 秒超时，SemaphoreSlim 限流，每设备记录上次尝试时间避免频繁重试。

**离线**：IsConnected=false → 标记离线（保留最后电量显示）。

**经典蓝牙（BluetoothClassicWatcher，2026-10-06 新增）**：`BluetoothLEDevice.GetDeviceSelector()` 只覆盖 BLE 端点（ProtocolId `{BB7BB05E-…}`），经典蓝牙音频设备（BR/EDR，ProtocolId `{E0CBF06C-…}`，如 EDIFIER W820NB）根本不进那个 watcher——这就是"蓝牙耳机已连接但列表没有"的根因。此 watcher 用 `BluetoothDevice.GetDeviceSelector()`（已配对经典设备）单开一路。**纯被动：不打开设备、零写入。** 两个修正 BLE 版的坑：Updated 只带变化属性，IsConnected 缺失时沿用现状不误判离线；Removed 发离线状态而非静默删除。已知取舍：所有已配对且已连接的经典蓝牙设备（含手机）都会显示。

**设备类型不预设（2026-10-09 修）**：正因为"含鼠标/键盘/手机"，`Kind` 不能硬编码成 Headset——真机 RAPOO BT3.0 鼠标就是这么被显示成耳机的。类型改由 `Models/ClassicDeviceKindResolver` 按"Windows 设备类别 → 服务 UUID → 显示名"三级判定，名字/类别后到时重试，详见 dev/runtime-pitfalls.md §3.1。

**经典蓝牙电量与类型信号（ClassicDevNodeReader，电量 2026-10-06 打通、类型信号 2026-10-09 并入）**：AEP 属性（`System.Devices.AepBattery_Level` 等）在本机全部无效，但 Windows 设置页显示的耳机电量另有来源——**HFP 免手操作音频网关设备节点的 PnP 属性 `DEVPKEY_Device_BatteryLevel`**（`{104EA319-6EE2-4701-BD47-8DDBF425BBE5},2`，BYTE）。`ClassicDevNodeReader` 用 SetupAPI（`SetupDiGetClassDevsW` → `SetupDiEnumDeviceInfo` → `SetupDiGetDeviceInstanceIdW` → `SetupDiGetDevicePropertyW`）枚举 `BTHENUM\*` 节点，**按设备 MAC 归并**出三样东西：电量（HFP 网关节点）、Windows 设备类别（父节点 `DEVPKEY_DeviceDisplay_Category`）、服务 UUID（子节点实例 ID 前缀）；watcher 侧用 `MacFromAepId`（AEP ID 形如 `Bluetooth#Bluetooth<适配器MAC>-<设备MAC>`，取最后一段）对上号。整表结果缓存 5 秒；`RefreshBattery()` 挂在轮询尾部（`PollOnceAsync` 的 `_classic.RefreshBattery()`）每分钟重读，有变化才通知。实测 EDIFIER W820NB 读数与设置页一致。排查手法见 scripts/probe-devnode-battery.py、scripts/probe-classic-kind.py。

## 3.1 本机电量流（SystemBatteryWatcher，2026-10-08 新增）

**不是插件**。系统电量没有 HID 接口可"认领"，也不是设备事件源，所以照 `BluetoothClassicWatcher` 的范式做成事件式数据源：

```
DeviceManager 构造 → _system.DeviceUpdated += Upsert
DeviceManager.Start() → if (Settings.MonitorSystemBattery) _system.Start()
PollOnceAsync 尾部 → if (Settings.MonitorSystemBattery) _system.Refresh()
                      （与 _classic.RefreshBattery() 并排，每分钟一次）
```

**读数**（`SystemPowerReader`，P/Invoke `GetSystemPowerStatus`，零写入）：
- `BatteryFlag` 128（无电池）/ 255（未知）→ **不出现在列表**（台式机场景，用户明确要"检测不到就不显示"）。
- `BatteryLifePercent` 255 → `Percent=null` 但**仍算有电池**（有电池但一次读不到，不该整行消失）。
- 翻译逻辑抽成 `internal static Interpret(acLineStatus, batteryFlag, batteryLifePercent)`，纯函数，`SystemPowerReaderTests` 覆盖。

**充电推导**（`SystemBatteryWatcher.DeriveCharging`，本类唯一的逻辑）：
`GetSystemPowerStatus` 只说"是否接着交流电"，不说"是否在充电"，所以用满电与否区分：
接电 + 100% → `Full`；接电未满 → `Charging`；未接电 → `Discharging`；百分比未知 → `Unknown`。
**注意**：插着电且 100% 时显示「已充满」而不是「充电中」，与 Windows 电池图标一致，不是 bug。

**设备标识**：固定 `Id = "sys:battery"`（`SystemBatteryWatcher.DeviceId`），`Source = DeviceSource.System`，`Kind = DeviceKind.Computer`，
显示名「本机电池」，列表徽标「本机」（`DeviceRowModel.SourceText`）。Id 永不变——历史曲线、低电量"每档只提醒一次"都靠它做键。

**开关语义**（`MonitorSystemBattery`，默认关）：
- `ApplySettings()` → `ApplySystemBatterySetting()`：开 → `_system.Start()`；关 → `_system.Stop()` **并从 `_devices` 撤掉条目**
  （理由同 `DropDevicesOf`：留着就会带着最后一帧电量继续显示，用户以为开关没生效）。
- `RestoreOfflineTombstones()` 跳过它：开关关着时，历史里的系统电量记录不该把它复活成离线占位。

**各处的取舍**（用户 2026-10-08 拍板）：
| 位置 | 行为 |
|---|---|
| 设备列表 / 桌面小组件 | 正常显示（`WidgetWindow.Glyph` 有 `Computer` 的显示器形状） |
| 历史统计 | 正常写入 + 可选曲线（走既有 `SafeSample` / `Upsert` 路径，无特殊代码） |
| 低电量提醒 | 正常参与（共用 `CheckLowBattery` 阈值与气泡通道） |
| **托盘"最低电量"** | **排除**——`TrayService.Render()` 里 `if (d.Source == DeviceSource.System) continue;` |

## 4. 2.4G 插件流（以 MchosePlugin 为例，最重要的一条链路）

### 4.1 认领（Attach，每轮调用、幂等）

```
BatteryPluginBase.Attach(hidDevices, claims)
├── IsCandidate(info)                  # vendor usage + in/out≥20，排除 consumer/system 小接口
├── 已认领 → 直接返回缓存状态
├── 1 分钟内探测失败过 → 跳过（冷却，防打扰）
├── claims.TryClaim(路径, "Mchose")    # 中央认领，被别的插件持有则跳过
└── 新设备：
    ├── info.Open() → HidDeviceHandle（长持有，退出/禁用时释放）
    ├── Probe(handle)：
    │   ├── 守卫：输出/输入 < 20 字节 → 放弃
    │   ├── KnownProtocolByPath 命中 → 只试那一条通道，**失败直接返回、不清记忆**
    │   │   （2026-10-08 实修：旧代码失败即 TryRemove 并回落完整探测，于是每 15 秒发现节拍
    │   │    都白写一次 QHW 握手 + 重试一遍 keyboard-v1，写入翻倍、WARN 刷屏）
    │   ├── TryQhw：                    # 新世代 dongle 协议（本机不命中）
    │   │   握手 0x4D+AA55… → getBattery(0x0904) → 解析（未授权写入时退化为被动监听 CZ 推送 3s）
    │   │   握手写用 quiet: true —— 非 QHW 通道必然 ERROR_INVALID_PARAMETER，那是判据不是故障
    │   └── TryKeyboardV1：             # 本机 G87 dongle 协议 ✅
    │       SendKeyboardV1Frame(0x05,0x01) 开会话 → 收 ACK/推送
    │       SendKeyboardV1Frame(0x4A,0x01) 查电量 → [5]=% [6]=充电标志
    ├── 失败 → 关句柄 + 释放认领 + 进入冷却
    └── 成功 → 建 DeviceStatus{Id="mchose:"+路径哈希, Protocol="keyboard-v1"} 存表
```

**探测期的日志一律"状态变化才记"**（`Log.Once` / `Log.ResetOnce`，key = `MchosePlugin.LogKey(handle, 用途)`
即 `路径|用途`；2026-10-08 加，2026-10-10 收拢到 `Log` 公共入口）：发现节拍每 15 秒重探一次，
而设备睡着 / 未授权写入时这些路径**永远走不到成功**，逐次记录会把日志文件刷满且没有新信息。
现在三个位置各只记第一次——"这条通道不是 QHW"、"未授权写入"、"keyboard-v1 两次询问无响应"，
恢复正常时 `Log.ResetOnce` 清标记，于是"好了之后又出问题"会重新记一条（与北通占位帧同一记法）。

`AttachAsync` 先释放扫描中已消失或请求重认领的接口；返回移除设备的离线快照，供 DeviceManager 更新独立副本。若同一轮已由新路径认领相同稳定 ID，就只返回新状态，避免旧离线快照覆盖新在线状态。候选路径按 Windows 大小写不敏感去重；同一物理 ID 的重复探测句柄也会立即释放。Haute 用芯片标识生成稳定 ID，其他插件沿用原 ID 规则。

### 4.2 轮询（Poll，基类骨架 + 子类 PollDevice）

`PollDevice` 按 `dev.Protocol` 分派：`keyboard-v1` → `PollKeyboardV1`（发一条 `13 4A 01`，1.2 秒内解析 4A 响应或 `13 0A` 自发推送）。**所有帧解析走纯函数单元 `MchoseV1Codec`**（`BuildFrame`/`ParsePush`/`ParseBatteryResponse`，零依赖，单测在 `src/tests/VigilWatt.Core.Tests/`）。关键：0A 推送帧的**类型位（下标 5）必须先认**——0x05=电量（[6]=%、[7]=标志），0x02=设备状态通知（实测键盘休眠，[6] **不是电量**）。失败记账：迈从把 `OfflinePolicy.CountPollFailures` 置 false（`null` 与"没开写入 / 熔断中"不可区分），改由 `PollDevice` 在**发过帧**的分支调 `ReportPollFailure` / `ReportPollAnswered` 自己报账；**连续 `OfflinePolicy.MaxFailures`（默认 3）次 → `WritesDisabled=true` 熔断停写**，之后各协议分支只走被动监听；退避期**递增**（30 秒起、封顶 5 分钟，`Plugins/WriteBackoff.cs`），期满由 `CanWrite` 自动放行一轮（对付键盘休眠唤醒，safety.md §3 规则 3）。`WritesEnabled=false`（插件关闭）时轮询/探测直接短路，不写任何字节。子类判定"设备已休眠"时调基类 `RequestOffline(dev)` → 立即标离线，不等 3 次失败（省约 3 分钟）。

**读数异常过滤（`BatteryReadingFilter`，每设备一个实例，基类 `BatteryPluginBase.Poll` 调用，2026-10-06 加）**：判定只做两件事——① **数值有效性**：低于 `MinValidPercent`（默认 2，即 0/1 是休眠占位帧）或 >100 → `InvalidValue`，永不采信；② **变化幅度**：与 `LastAcceptedPercent` 相比涨跌超过 `MaxPlausibleStepPercent`（默认 20 点）→ `RejectAnomaly`，本轮**不写入 status**（UI 和低电量判定都看不到），需持续 `AnomalyPersistWindow`（默认 5 分钟）才升级为 `AcceptConfirmed` 采信；读数回到基准附近则持续计时清零。基准**不随离线清空**（伪读数常出现在"刚掉线/刚回来"的窗口）。越界/异常都打 `读数异常已过滤` 日志。阈值均为 `protected virtual`，子类可覆盖。单测见 `src/tests/VigilWatt.Core.Tests/`。

**离线判定的五条路径（2026-10-08 补第三、五条）**：

| 路径 | 触发方式 | 延迟 | 谁在用 |
|---|---|---|---|
| `RequestOffline` | 子类发现**零歧义**离线证据 | **立即** | 北通 T9 占位帧（`byte23=0x50`） |
| `MarkOfflineCandidate` + `OfflinePolicy.Confirm` | 被动帧里的**有歧义**证据 | 确认窗口（雷柏 **6s**） | 雷柏 `[18].bit7=0`（RF 断链位） |
| `OfflinePolicy.Silence` | 接口**彻底不说话** | 阈值 + 最多约 500ms（被动监听空闲 tick；轮询仍保留兜底） | 雷柏 / 英菲克 |
| `OfflinePolicy.MaxFailures` 次查询失败 → 熔断 | 主动查询类插件（发过帧却没应答） | ~3 分钟 | 迈从 / 北通主动路径 |
| `RequestConfirm` → 空闲 tick 立刻核实 | 被动帧里的**触发器**（**不是证据**） | **秒级** | 迈从 `0A` 状态通知 `payload=0x00` |

**第③层参数集中在 `OfflinePolicy`（`Plugins/OfflinePolicy.cs`，2026-10-08 收敛）**：
`Silence` / `Confirm` / `MaxFailures` / `CountPollFailures` 四个参数装在一个 `record` 里，
每插件只写一行 `protected override OfflinePolicy Offline { get; } = new() { … };`。
收敛的理由：**机制是同一套，只有参数不同**——不像第①层（协议事实：帧结构、有没有查询命令、
有没有连接状态字段）和第②层（安全声明：要不要写帧、用户开关、探测冷却）那样**必须** per-brand。
`CountPollFailures` 是本次拆出来的关键一项：它回答"轮询返回 null 算不算失联"，
**必须**与"要不要起被动监听"（`SupportsPassiveListen`，机制事实）分开——
曾经把两者合成一个布尔量，于是迈从（要被动监听、但轮询空读确实是失联）键盘关机后永远不显示离线。
默认 `true`（传统查询型插件）；雷柏（无查询命令）/ 迈从 / 北通（null 与"没开写入、熔断中"不可区分）
置 `false`，改由插件在 `PollDevice` 里调 `ReportPollFailure` 自己报账。

`OfflinePolicy.Silence` 和延迟确认的**检查点都在被动监听循环的空闲 tick（约 500ms 一次）**，
不必等待 60 秒轮询；轮询还保留静默检查作为兜底。`MarkOfflineCandidate` 是**幂等**的（候选已开始就不刷新
起始时刻），否则持续抖动会把判定无限往后推。判定逻辑是 `OfflinePolicy.IsConfirmExpired`
（纯函数，有单测 `OfflinePolicyTests`）。恢复在线**不必显式做**——设备回来后 `TryApplyReading`
自己会把 `IsOnline` 翻回来。所有离线路径最后都落到私有 `MarkOffline(dev, reason)`（幂等，已离线则无操作）。

**`RequestConfirm` 的"立刻核实"（第五条的机制）**：插件在被动帧里发现"值得核实"的信号时调它，
基类在**空闲 tick** 上跑一次与轮询同款的 `PollDevice`。怎么知道"插件确实发了帧却没应答"？
看 `ConsecutiveFailures` 有没有被插件推高——插件没发帧就不会报账，计数不动，于是不会误判
（这条也是 `CountPollFailures=false` 的插件必须自己报账的原因之一）。
计数被推高 ⇒ 核实失败 ⇒ 不等 3 次，当场判离线；插件的失败记账同时把退避**重新起算并升档**。
**熔断期内这条路径照跑**（放行频率 ≤1 帧/30 秒）——它是设备自发信号触发的、不是轮询重试，
挡住它等于把唤醒信号丢掉（CR-53，实测盲区 12 分 52 秒）；核实的半开重试另由被动空闲 tick 的
`MaybeHalfOpenBreaker` 在退避期满时立刻发起，不必等 60 秒轮询拍。

### 4.3 读取通道（HidDeviceHandle）

WriteOutput：异步 WriteFile（500ms 超时，失败记 Win32 错误码）。可传 `quiet: true` 让**预期失败**不记 WARN——
唯一用途是"探测一条不属于本协议的通道"（设备描述符里没有那个 report ID，写必然失败，而写失败正是判据）；
调用方此时会自己记一条更精确的 INFO。**别用它掩盖真正的写入故障。**
ReadInput：**单悬空读**——句柄生命周期只有一个挂着的 ReadAsync，超时让它继续挂着，下次复用（runtime-pitfalls §1，改坏这条所有协议集体失灵）。

## 5. 状态汇聚与历史（DeviceManager.Upsert / SafeSample）

```
Upsert(incoming):
  新 Id → 立即 DevicesChanged + DeviceUpdated     # 新设备不等下一轮轮询（2026-10-06 起）
  GetOrAdd(Id) → 原地同步 Name/电量/充电/LastUpdated/IsOnline
  changed?（电量或在线状态变了）
  ├── history.Insert(status)              # 变化即写一条（保证曲线不漏跳变）
  └── DeviceUpdated?.Invoke               # UI 单设备刷新
  CheckLowBattery(status)                 # 见 §6

SafeSample（每 SampleIntervalMinutes 分钟）:
  foreach 在线且有电量的设备 → history.Insert     # 无变化也写点，保证曲线连续
  → 更新最近 90 天放电估算及不可用原因，变化时推送 UI；列表与任务栏悬停共用结果
```

### 5.1 离线占位（墓碑，`DeviceManager.RestoreOfflineTombstones`）

设备"消失"有两条路径，处理不同：

- **2.4G 拔掉 / 关机**：插件把它标离线，条目**留在 `_devices`**（字典从不移除）。
- **蓝牙 / dongle 不被枚举**：它**从未进过列表**——北通关机后接收器可能连 HID 枚举都退出（实测 19:02 之后扫描里再无 `VID_20BC`），此时没有任何数据源，界面上它等于"从未存在过"，「共 N 台」恒等于「N 台在线」，这个总数没有信息量。

解法：用历史库里**最近见过**的记录补一个离线条目（`TombstoneWindow` 内有历史、当前不在位），既让总数有意义，又不凭空造设备（有真实历史为凭）。**只在启动后跑一次**——进程中设备一旦被认领就常驻 `_devices`，不需要反复补。开关关着的系统电量（`sys:`）要跳过，别把它复活成离线占位。

## 6. 低电量提醒（DeviceManager.CheckLowBattery → App.AttachTray）

条件：在线 && 电量 ≤ LowBatteryThreshold && 非充电/已充满。**按设备去重 + 自动复位**：`_lowBatteryNotified[Id]` 是一个布尔量，`TryAdd` 兼任"未提醒过"的判定（天然幂等），跨过阈值只弹一次。复位点有三类——① 电量回到阈值之上、或转为充电/已充满；② 提醒阈值被设为 ≤0（关掉提醒）；③ 设备条目被移除（`ExpireTombstones` / `ApplySystemBatterySetting` / `DropDevicesOf` 三处一并 `TryRemove`）。于是"充到 100% 再放电回 20%"会重新提醒，同一轮持续掉电却不会逐 1% 刷屏（CR-15/CR-23 修的正是旧实现"精确档位且永不复位"的两个反向 bug）。触发 → `LowBatteryDetected` 事件 → App 层 `DispatcherQueue.TryEnqueue` → `Tray.ShowLowBattery(device)`（Win32 NIF_INFO 气泡）。

**骤降保护（2026-10-06 加）**：`Upsert` 在覆盖前记下 `previousPercent`，若本次低电量值比上次已知值低超过 20 点，则**暂缓本轮提醒**并记日志（`低电量提醒暂缓`），等下次读数确认。这是低电量告警的最后一道闸——即使某个通道漏进了休眠伪读数，也不会立刻弹告警。

## 7. UI 更新流

- **DevicesPage**：订阅 DevicesChanged（全量重建行）/ DeviceUpdated（单行刷新），事件都在后台线程来 → `DispatcherQueue.TryEnqueue` 切 UI 线程。行模型 DeviceRowModel 一次性 x:Bind。
- **SettingsPage**：插件开关按 `Manager.Plugins`（反射发现结果）动态生成，保存时遍历开关写回 `PluginEnabled`——新增插件无需改设置页。
- **StatsPage**：设备下拉 + 时间范围按钮组（24h/7d/30d ToggleButton 互斥）→ 查 `History.Query(deviceId, from, to)` → 顶部 4 张指标卡（当前/最高/最低/平均，当前值优先取实时在线读数）+ BatteryChartControl 画曲线。事件处理器开头判空（XAML 解析期会触发 SelectionChanged）。**设备下拉按历史库取**：所选时间范围内有过采样的设备都在列（含已下线 / 换过 Id 的历史设备），**不跟随当前在线设备**——否则昨天用旧协议采的曲线永远选不到。
- **HistoryPage**（历史管理，2026-10-06 加）：`History.GetStats()` + `GetDeviceStats()` 填概览卡与设备列表 → 勾选/单行删除 → ContentDialog 二次确认 → `DeleteDevices(ids)` → **`Compact()`（VACUUM）** → 重新 Reload。**不订阅设备事件**：后台每分钟采样会重建列表、打断用户勾选（StatsPage 踩过同款坑），数据陈旧靠手动刷新。
- **MainWindow**：存在托盘、任务栏贴片或小组件入口时，关闭隐藏主窗口；全部入口关闭时走统一退出并释放资源。启动隐藏同样遵循 `AppVisibilityPolicy`，避免后台运行却无法重新打开。
- **WidgetWindow**：订阅同样的设备事件 → 重建芯片（颜色按电量分档/充电蓝色，充电中额外在百分比左侧显示 ⚡，与列表页同款标识）→ 按芯片数重算窗口宽度（DIP×RasterizationScale 物理像素）→ ClampToWorkArea 收边。可拖动（GetCursorPos 偏移）。

## 8. 托盘（TrayService，纯 Win32）

Initialize：RegisterClassW 建隐藏消息窗口（类名带 GUID）→ Shell_NotifyIconW NIM_ADD（图标 = System.Drawing 画 32×32 彩色圆+百分比文字 → GetHicon）。消息窗口 WndProc 收到回调消息 0x8C40：左键/双击 → ShowMainWindow；右键 → SetForegroundWindow + TrackPopupMenuEx(TPM_RETURNCMD)（打开主界面 / 退出）。任务栏重建（Explorer 重启）监听 TaskbarCreated 重新添加图标。低电量气泡 = NIF_INFO。

`Render()` 遍历 `Manager.Devices` 取**在线且有电量**的最低者 → 图标显示其百分比/⚡，`szTip` 显示其名字。**`DeviceSource.System`（本机电量）在此被 `continue` 跳过**（2026-10-08）——本机是"用到 30% 也正常"的另一类设备，不该抢托盘那一个数字，否则真正的"外设快没电了"信号会被盖掉。托盘提示文字变化才记日志（`托盘提示已更新`），是最常用的托盘排错线索。

`TrayIconEnabled` 默认 true，与 `TaskbarBatteryEnabled` 独立保存。关闭时 `NIM_DELETE` 并释放图标，保留消息窗口以便重新开启；设备更新、Explorer 重建和气泡均不能重新添加已关闭的图标。贴片仍可双击打开主窗口，右键打开/退出；不改贴片内的设备小图标。设置页修改后点击保存生效。

## 9. 设置流（SettingsPage.Save）

收集 UI 值 → AppSettings.Save()（settings.json）→ 按改动项生效：AutostartService 写/删 HKCU Run 键、小组件开/关、Manager.ApplySettings()（重建定时器 + 同步本机电量开关 + 刷新展示）、CleanupHistoryNow()（按保留天数清理）。

剩余时间估算按设备从历史库读取最近 90 天样本，充电或满电样本会重置放电区间；仅在线、未充电且未满电时展示。至少需要 3 个不同时间点和可测到的净下降（≥1 点），不再设最短观察时长或固定下降门槛；列表和任务栏悬停共用缓存结果。设置 `RemainingTimeEstimateEnabled` 默认开启。

## 10. 退出流

托盘、贴片或小组件的退出，以及无后台入口时关闭主窗口，均走 `TrayService.ExitApplication`：删托盘图标 → `AppServices.Dispose` 释放组件、设备管理器和历史库 → 释放互斥量/日志 → `Environment.Exit(0)`。
