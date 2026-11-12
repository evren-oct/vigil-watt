# 项目结构与架构

> 配套阅读：[code-flow.md](code-flow.md)（运行时数据流）、[protocols.md](../hardware/protocols.md)（协议细节）。
> 以下所有路径相对仓库根，行号会漂移，引用到方法级为止。

## 1. 总览

两个项目、一条依赖方向：`VigilWatt.App → VigilWatt.Core`。Core 不依赖任何 UI（WinRT 例外：BLE 用 WinRT API，属平台能力不算 UI），App 不写协议、只消费 Core 的事件。

```
VigilWatt.sln
├── src/VigilWatt.Core/            # 无 UI 核心库（net8.0-windows10.0.19041.0）
│   ├── Ble/BleBatteryWatcher          # BLE 设备发现 + GATT 电量直读
│   ├── Ble/BluetoothClassicWatcher    # 经典蓝牙 A2DP/HFP：连接状态 + 显示名
│   ├── Ble/ClassicAudioBattery        # 经典蓝牙电量：HFP 网关节点 PnP 属性（SetupAPI）
│   ├── Devices/DeviceManager          # 中枢：合并 BLE+插件状态、定时器、历史、低电量
│   ├── Power/
│   │   ├── SystemPowerReader          # 本机电源读数（GetSystemPowerStatus，纯逻辑 Interpret 可单测）
│   │   └── SystemBatteryWatcher       # 本机电源事件式数据源（非插件，Id = sys:battery）
│   ├── Hid/
│   │   ├── NativeHid                  # Win32 P/Invoke（CfgMgr32 / hid.dll / CreateFileW）
│   │   ├── HidDeviceScanner           # 枚举在位 HID 接口 + 读元数据
│   │   └── HidDeviceInfo              # 元数据 + HidDeviceHandle（读写通道，单悬空读）
│   ├── Plugins/
│   │   ├── IBatteryPlugin             # 插件契约：Attach / Poll / Detach
│   │   ├── BatteryPluginBase          # "接口认领"型基类：冷却/熔断/生命周期/稳定Id/被动实时监听(StatusPushed)
│   │   ├── DeviceClaimRegistry        # 中央认领注册表（同一接口同时只归一个插件）
│   │   ├── PluginUtils                # 共享工具（路径 FNV-1a 稳定哈希）
│   │   ├── Logitech/                  # HID++ 1.0/2.0（接收器会话型）
│   │   ├── Rapoo/RapooPlugin          # 雷柏 status-report7（纯被动零写入）
│   │   ├── Mchose/MchosePlugin        # 0x13 会话 / 0x4D 通道（mchose-0x4d）/ CZ 分块读 / Gmouse / 耳机
│   │   ├── Betop/BetopPlugin          # 北通手柄：厂商接口 0x15 状态报告 + T9 被动流 + BetopCodec（纯逻辑）
│   │   ├── Inphic/                    # 英菲克 ZHEN 被动帧 / IN9 feature 0x0C 刷新查询
│   │   ├── Hecate/                    # HECATE report 0x04 被动状态帧
│   │   ├── Delux/                     # DELUX 0x000A 被动状态帧
│   │   ├── Aula/                      # F99 Pro 0x13 会话 + HERO 68 Air Bytech 8K 分片，共用品牌开关，默认关
│   │   ├── Akg/                       # N9 Hybrid RACE 固定查询 + 控制输入读取，默认关
│   │   └── Sony/                      # DualSense 蓝牙 HID 只读电量
│   ├── Models/DeviceStatus            # 统一设备状态快照（Id/电量/充电/在线）
│   ├── Models/PointerTelemetry        # 指针遥测（DPI/回报率）模型；见 project/pointer-telemetry.md
│   ├── Media/DeviceImageCatalog       # 本地产品图清单（型号匹配；见 project/device-images.md）
│   ├── Settings/AppSettings           # settings.json 读写
│   ├── Storage/HistoryStore           # SQLite（Microsoft.Data.Sqlite）：写入/查询/按设备删除/VACUUM
│   ├── Capture/                       # 设备诊断：USB/HID 只读采集（模型/分析/报告/打包/存储 + Hid 采集层）
│   ├── Utils/LogLevel                 # 日志级别枚举（Debug<Info<Warn<Error + Off 哨兵）与名称解析
│   └── Utils/Log                      # 分级文本日志：%LOCALAPPDATA%\VigilWatt\logs\app-yyyy-MM-dd.log（按日分文件，详见 dev/logging.md）
└── src/VigilWatt.App/             # WinUI 3 前端（net8.0-windows10.0.19041.0，非打包）
    ├── App.xaml.cs                    # AppServices 容器 + 启动序列 + 全局异常兜底
    ├── MainWindow(.xaml)              # NavigationView 三页导航；关窗=隐藏到托盘
    ├── Views/
    │   ├── DevicesPage                # 设备列表（一次性 x:Bind 行模型）
    │   ├── StatsPage                  # 历史统计：筛选卡 + 指标摘要卡 + 曲线（设备/时间范围选择）
    │   ├── HistoryPage                # 历史管理：概览卡 + 按设备删除（二次确认 + VACUUM 收缩）
    │   ├── BatteryChartControl        # 自绘 Canvas 折线图（面积填充 + hover 提示，无第三方图表库）
    │   └── SettingsPage               # 设置项 + 应用按钮
    ├── WidgetWindow(.xaml)            # 桌面小组件（无边框悬浮卡片，纯代码构建设备行）
    ├── ViewModels/DeviceRowModel      # 设备行的绑定模型
    └── Services/
        ├── TrayService                # 托盘（纯 Win32 Shell_NotifyIcon）+ 全应用唯一退出通道
        ├── WidgetDisplayMode          # 小组件层级取值（Topmost/Normal/Desktop）+ 条数上限范围
        ├── DeviceImageService         # 产品图包解析与位图缓存（只给详情页用）
        ├── LogLevelOptions            # 设置页日志级别下拉框的档位（关闭/详细/信息/警告/错误）
        ├── Native                     # App 侧 Win32 声明（托盘/菜单/光标/窗口属性/Z 序）
        └── AutostartService           # HKCU\...\Run 注册表自启
```

产物：`publish\VigilWatt.App.exe`（self-contained 单目录发布，102MB，已 gitignore）。

配套目录：`src/tests/VigilWatt.Core.Tests/`（xUnit 单测，`<Compile Include>` 链接 Core 的纯逻辑真源文件）、`src/tests/VigilWatt.BleScan/`（蓝牙诊断小工具）、`src/tests/VigilWatt.ProtocolCapture/`（USBPcap / WebHID / BLE 被动协议采集 CLI）、`scripts/`（临时调试脚本，用完即删）。

## 2. 关键类型与关系

```
AppServices ──持有──> Settings / History / Manager / Tray / Widget / MainWindow
DeviceManager ──持有──> BleBatteryWatcher + List<IBatteryPlugin> + ConcurrentDictionary<string,DeviceStatus>
IBatteryPlugin 实现 ──认领──> HidDeviceInfo（扫描结果）──打开──> HidDeviceHandle（长持有句柄）
DeviceManager.Upsert() 是唯一的状态汇聚点（BLE 和插件都汇到它）
事件方向：Core ──DeviceUpdated/DevicesChanged/LowBatteryDetected──> App（DispatcherQueue.TryEnqueue 切 UI 线程）
```

## 3. 设计决策与理由

| 决策 | 理由 |
|---|---|
| **插件制（IBatteryPlugin）+ 反射自动发现** | 每家 2.4G 协议完全私有，隔离在一个类里；DeviceManager 反射扫描 Core 程序集实例化全部实现类。新增品牌 = 加一个类文件，DeviceManager/设置页零改动 |
| **BatteryPluginBase 基类** | 冷却/熔断/设备生命周期/稳定 Id / 被动实时监听（`SupportsPassiveListen`→`TryParsePassiveInput`→`StatusPushed`）沉到基类；会话型插件（罗技）自建通知泵，直接实现接口 |
| **DeviceClaimRegistry 中央认领** | 按接口路径记账，同一接口同一时刻只归一个插件，从结构上杜绝两个插件同时开句柄/抢会话（此前靠各插件手动排除对方 VID） |
| **插件每轮收到全量 HID 接口列表（Attach 幂等）** | 设备插拔天然由扫描驱动，插件无需自己监听硬件事件；消失的设备在 Attach 里标记离线 |
| **Core 无 UI 依赖** | 协议调试可以脱离 WinUI（控制台/脚本复用 Core）；WinUI 编译器崩溃也不影响核心逻辑 |
| **BLE 不依赖系统电量属性** | `System.Devices.AepBattery_Level` 在本机 Windows 上不可用（runtime-pitfalls §3），直接 GATT 读标准 Battery Service，任何实现 0x180F 的设备都通用 |
| **自绘折线图（BatteryChartControl）** | 最初计划用 LiveCharts2，后放弃：只画一条电量曲线，自绘 100 行可控、无依赖、无 SkiaSharp 体积 |
| **托盘纯 Win32** | H.NotifyIcon 在 WinUI 3 代码创建场景下事件失灵（runtime-pitfalls §5） |
| **小组件窗口高度按"最多显示数"封顶 + ScrollViewer** | 高度若随设备数变化，插拔/休眠期间卡片会一跳一跳；封顶后只在跨过上限那一刻变尺寸。滚动条用 Overlay 常隐，不挤占宽度 |
| **"固定在桌面" = SetWindowPos(HWND_BOTTOM)** | 不挂 WorkerW：`SetParent` 改父窗口会连累拖拽/`WS_EX_TOOLWINDOW`/Win10 透明外壳三条既有逻辑，点击落点也要重算。压到底层观感等价（桌面露出时才可见），且零侵入 |
| **退出只有一条实现（TrayService.ExitApplication）** | 托盘与小组件右键都走它，保证先 `Shell_NotifyIcon(NIM_DELETE)` 摘图标再结束进程，避免残留点不动的托盘图标 |
| **历史写入双通道** | 定时采样（SampleIntervalMinutes，无变化也写点）+ 电量变化立即写（Upsert 里），保证曲线既平滑又不漏跳变 |
| **删除历史后立即 VACUUM** | SQLite 的 `DELETE` 只把页标成空闲、**不缩小文件**；不 VACUUM 的话用户删完看到文件大小没变，会以为删除没生效。删除是低频操作，VACUUM 的整库重写开销可接受 |
| **HistoryStore 支持注入库路径** | `HistoryStore(dbPath)` 可选参数让单测指向临时库，使删除/统计逻辑能进纯 net8.0 测试项目跑真 SQLite（不碰真实 history.db） |
| **统一 DeviceStatus.Id** | BLE=蓝牙地址、2.4G=**端口无关键**哈希（`mchose:{StableHash(StableDeviceKey)}`，换 USB 口不变，见 `code-flow.md` §2.2）。改协议或改身份规则后设备 ID 会变（旧曲线对不上新 ID）——历史库里同时存在 `mchose:D78E…`（错误时代）和 `mchose:0F60…`（现在）即此原因 |
| **采集引擎与适配器解耦** | 未知设备没有适配器也要能采证据，所以 `CaptureSession` 只依赖 `ICaptureSource`，与 `IBatteryPlugin` 无关；`Capture/Hid/` 是唯一碰 `hid.dll` 的一层，其余零 Win32 才能链进单测（R8）。详见 `capture.md` |
| **非打包部署（WindowsPackageType=None + SelfContained）** | 免安装免签名，双击就跑；代价是要自己带 Windows App SDK 运行时（`publish/` 已含） |

## 4. 线程模型

- **定时器线程**（`System.Threading.Timer` ×3）：主轮询 1 分钟（首扫 300ms 起跑）、快速发现 15 秒（`FastDiscoveryInterval`）、采样 `SampleIntervalMinutes`（首拍 1 分钟）。全部硬件 I/O 都在这几个线程上。
- **设备拔出**：托盘窗口的 `WM_DEVICECHANGE` 立刻跑一次 `RequestReconcileAsync`（只做消失检测，不认领不写帧），15 秒节拍兜底。插入不做即时检测，靠原节拍或主界面「刷新」（`code-flow.md` §2.3）。
- **轮次不并发、也不静默丢弃**：`RequestPollAsync()` / `RequestDiscoveryAsync()` 在 `_pollSync` 下复用进行中的 Task（`_currentPoll` / `_currentDiscovery`）；所有 pass 由 `SemaphoreSlim _passGate` 串行执行（插件内部字典不是线程安全的）。
- **UI 线程**：只收事件（DispatcherQueue.TryEnqueue）和发起手动刷新（`await RequestPollAsync()`，返回的 Task 在整轮结束后完成，含 BLE 异步补读）。
- **BLE**：DeviceWatcher 回调线程 + GATT 异步（SemaphoreSlim 限流，8 秒超时）。
- **托盘**：Shell 消息窗口线程回调 → 显示气泡前切 UI 线程。

## 5. 数据存储

| 位置 | 内容 |
|---|---|
| `%LOCALAPPDATA%\VigilWatt\settings.json` | AppSettings（阈值/采样/保留天数/自启/小组件（开关/位置/显示方式/最多显示数）/插件开关（**即写入授权**）/MonitorSystemBattery/**LogLevel**/**LogRetentionDays**/按设备 ID 保存的名称、图标与产品图配色覆盖） |
| `%LOCALAPPDATA%\VigilWatt\history.db` | SQLite `samples(device_id, name, percent, charging, ts)`，索引 (device_id, ts)。**ts = UTC 毫秒**（`DateTimeOffset.ToUnixTimeMilliseconds`） |
| `%LOCALAPPDATA%\VigilWatt\logs\app-yyyy-MM-dd.log` | **按本地日期每天一个文件**的运行日志（协议原始报文也在）。行首为**本地时间**；文件首行是"时间基准"声明，用于与 history.db 的 UTC 时间对齐 |
| `%LOCALAPPDATA%\VigilWatt\logs\log-error.txt` | 兜底文件：连续写日志失败时记录（带节流与上限），正常运行时不产生 |
| `%LOCALAPPDATA%\VigilWatt\device-images\` | 本地产品图包（详情页 Hero 用；不联网、不入库；Debug 在 exe 旁 `data\device-images\`） |

> 日志按日分文件，跨天自动切换；默认保留最近 **14 天**（`LogRetentionDays`，`0` = 不清理），超期文件在启动/跨天时清理。
> 日志分级：`Debug < Info < Warn < Error`，外加 `Off`（全关）；默认 `Info`，在设置页选，**改后需重启应用生效**。
> 完整设计见 [dev/logging.md](../dev/logging.md)。

## 6. 约定（改代码前先看）

- Core 里的日志用中文描述 + 原始报文 hex（`Log.Hex`），协议排错全靠它——**新协议解析代码必须打原始帧日志**。
- **日志分级约定**（完整设计见 [dev/logging.md](../dev/logging.md)）：
  - `Log.Debug` 打"读了但没解析出东西"的逐帧报文 / 探测明细（高频，默认不输出，调协议时在设置页切到"详细"）；
  - `Log.Info` 只留**状态变化**与**协议解析结果**（行尾带 `原始=<HEX>`，这是默认档下仍能排障的前提）；
  - `Log.Warn` / `Log.Error` 照旧。
  - 热路径先 `Log.IsEnabled(level)` 短路，省掉 `$"..."` 插值开销。
  - 轮询型重复日志用 `Log.InfoOnChange(key, state, message)`（状态变了才记，返回"是否变化"）或
    `Log.InfoThrottled(key, message, interval)` 降噪，**不要在各调用点自己攒去重字段**。
- 插件所有硬件写入必须能被熔断/开关控制：**授权入口就是插件开关**（`AppSettings.PluginEnabled[Name]`）——基类 `WritesEnabled => IsWritesEnabled(Name)` 直读它，发帧前再检查 `CanWrite(dev)`；熔断（连续 3 次失败停写）由基类自动执行，见 safety.md §3。**没有独立的"写入许可"开关**（2026-10-10 取消，见 CR-37）。
  - 零写入插件（雷柏、英菲克）覆盖 `WritesEnabled => false` **且** `ProbeWrites => false`——它们是机制事实声明，与用户开关无关。
  - `LogitechPlugin` 不继承基类（会话型，自建通知泵），**故自建了等价的闸门 + 熔断 + 退避**：`HidppIO.WritesAllowed`（唯一写点内检查，读插件开关）、连续 3 次失败停写 10 分钟、打开失败 10 分钟退避。CR-01 已关闭，改动时别把这道闸门拆掉。
- 新插件清单：加一个 `IBatteryPlugin` 实现类（最简：继承 `BatteryPluginBase`，实现 IsCandidate/Probe/PollDevice）→ 设置页自动出现开关，注册/发现/熔断/认领全自动。
  - ⚠️ **别忘了在 `AppSettings.PluginEnabled` 默认表里加该插件的键**（`AppSettings.Load` 会给老 settings.json 补缺失键，但默认表是唯一来源）。
    不加的话 `TryGetValue` 判成"关闭"，新插件默认不工作——反序列化会用 JSON 的字典整体替换默认字典，缺键就是缺键。
  - ⚠️ **必须声明 `Kind`**（`DeviceKind`，基类是 abstract 的，编译器会逼你写）：UI 靠它选图标。
    **不要靠显示名猜类型**——真机显示名（`Rapoo Gaming Device` / `MCHOSE 2.4G Wireless` /
    `EDIFIER W820NB 双金标版`）里一个关键字都没有，猜的结果是全退化成通用蓝牙图标。
    分不出来时才写 `Unknown`，由 `DeviceKindGuesser` 兜底。
- **状态合并的"声明覆盖猜测"规则**（`Devices/DeviceStatusMerger.cs`，`Upsert` 与"墓碑接管"都走它）：
  离线墓碑先于插件建条目，`Kind` 只能按历史名猜（真机名一个关键字都没有 → 全 `Unknown`）；
  设备真正上线后插件声明的 `Kind`/`Protocol` **必须能覆盖**墓碑的猜测，否则图标在进程内**冻结**到永远。
  反向不成立——`Unknown` / 空串视为"本次未声明"，不得把已知类型洗掉。
- **连接方式（`ConnectionKind`）与来源（`DeviceSource`）是两件事**：`DeviceStatus.Connection` 是
  `TelemetryValue<ConnectionKind>`，带 `TelemetrySource` 与观测时刻；合并走 `MergePreferTrusted`
  （设备上报 > 厂商 SDK > 推断，反向不覆盖，无值不清洗），**`Unknown` 值即便带了来源也不算链路**
  （`DeviceStatusMerger.MergeConnection` 先归成空遥测），`Unknown` 与空遥测都表示"没有证据"。
  `DeviceSource.Hid24G` 只是接入栈（哪个插件认领的），**不能推断成 2.4G**——只有 BLE / 经典蓝牙 /
  本机三类来源本身就是传输事实。展示统一走 `Models/DeviceConnectionInfo`（标签 / 离线最后已知 / 取色档），
  UI 不再各自拼文案。接收器体系（Unifying / Bolt / LIGHTSPEED）不是链路，属于 `ReceiverIdentity`。
  体系标签是**第二个展示维度**：`DeviceConnectionInfo.ReceiverLabel` 只回目录登记的 `DisplayName`（Bolt / Unifying /
  2.4G 接收器），返回 null 就不显示。**展示层不认识任何品牌**——新体系只在目录 JSON 加 `familyDisplay` / `roleDisplay`，
  不改代码（`AGENTS.md` §10）。图标同理由目录声明：`ReceiverIconKey` 只回语义 key（`bolt` / `unifying` / `nano` /
  `lightning` / `multi` / `wireless`），`App/Services/ReceiverIconFactory` 按 key 画内置几何，未知 key 回退 `wireless`。
  `bolt` 与 `unifying` **直接复用 OpenLogi（MIT / Apache-2.0）`action-icons/` 下的官方 path 原文**，不做几何简化；
  2026-11-11 用户明确授权推翻原先"不复刻厂商商标"的决定，理由是通用几何无法与 Nano / 27MHz 区分。WinUI 没有 WPF 的
  `Geometry.Parse`，path 由工厂内一个只支持 M/L/H/V/C/Z 的私有解析器转成 `PathGeometry`（含 MIT 归属说明）。
  范围仅限设备列表的小尺寸体系标签，不用于应用图标、安装包与宣传物料；`nano` / `lightning` / `multi` / `wireless` 仍是自绘几何。
  设备列表在链路徽标右侧显示这枚次级标签，贴片与小组件暂不显示（空间按文案算）。
  罗技**目录登记过的接收器族全量填身份**（Bolt / Unifying / LIGHTSPEED / Nano / Legacy27Mhz，条件是目录写了 `familyDisplay`）；
  未收录 PID 与 Direct 路径保持 null。身份是"这颗 PID 属于哪一族"的目录事实，**不等于链路**——`LogitechReceiverIdentityPolicy`
  与 `LogitechConnectionPolicy` 是两个独立策略，各自读目录记录，一个填体系标签、一个填 `Connection`。
  链路门（`LogitechConnectionPolicy`，2026-11-11 由"仅 Bolt"放开到全 2.4G 族）的判据只有两项：**目录族属于 2.4G 体系
  （Bolt / Unifying / LightSpeed / Nano）、且槽位落在容量内**，满足即 `Inferred(Wireless24G)`。
  **容量优先取接收器自报值**（`0xB5/0x03` 参数区 `[6]`，`ProbeSlotCount` 每开一次句柄读一次），读不到才退回目录 `maxSlots`——
  目录是静态表、可能过时，自报值是这颗固件的事实（Solaar / OpenLogi 同判据）。两者都没有（未收录 PID）就只能 Unknown。
   **C548 真机（2026-11-11）**：`0xB5/0x03` 三次读取均无回包（同会话 Root.Ping 0.61s 正常），`ReportedSlotCount` 保持 null、
   `SlotCapacity` 取目录 6 槽；这是已验证的兜底路径，不是"尚未验证"。
  放开的依据是协议同源而非"放宽标准"：OpenLogi 的接收器注册表把 Nano / LightSpeed 一律记为 `ReceiverProtocol::Unifying`，
  槽位枚举与寻址是同一套 HID++ 1.0 寄存器流程，只有 C548 走 Bolt 变体。
  排除的两类是**没有证据**而非"没验证"：`Legacy27Mhz` 不在 2.4GHz 频段、`ConnectionKind` 也没有对应值；`Unknown` 族
  目录本身没定性。Direct `0xFF`、空槽、越界槽位、未收录 PID 与 PID 缺失同样保持 Unknown。
  该策略**不读 `0xB5` 配对寄存器**——那是设备类型 / WPID 的事，与链路无关（CR-153 与本门无关，不要再混为一谈）。
  Slot Ping 只证明该槽位路径有 HID++ Root 响应，不证明设备在线或链路已直接确认，故来源固定 `Inferred`，**永不写 `DeviceReported`**。
  罗技指针遥测走 HID++ `0x2201`（DPI）/ `0x8060`（回报率）只读查询，2 分钟一次，来源记 `DeviceReported`；
  feature 不支持或读不到就留空（`Unknown`），**不按型号补默认 DPI / 回报率**（细节见 `hardware/logitech.md` §4）。
   设备列表离线徽标统一走 `DeviceConnectionInfo.Badge`（`最后 X` / `离线`）并置灰，历史链路不再显示成当前连接。
  **接收器"在位"不能推断链路**（设备可能只是插线充电）；但**正在读接收器的配置通道**时可以推断为 2.4G
  ——有线数据不经过 dongle，这类推断记 `Inferred`，等设备自己上报再升级成 `DeviceReported`。
- UI 侧 XAML 事件处理器开头判空（dev/runtime-pitfalls.md §4）；新增 XAML 属性先确认控件真有（dev/build.md §1）。
- **新"非插件"数据源清单**（本机电量就是这么加的）：数据源不是 HID 接口、
  没有东西可"认领"时，不要硬塞进 `IBatteryPlugin`——照 `BluetoothClassicWatcher` /
  `SystemBatteryWatcher` 的范式做一个事件式类：
  1. 类里维护自己的 `DeviceStatus`，变化时 `event Action<DeviceStatus> DeviceUpdated`；
  2. `DeviceManager` 构造函数订阅该事件 → `Upsert`；
  3. `PollOnceAsync` 尾部挂一次主动刷新（和 `_classic.RefreshBattery()` 并排）；
  4. **Id 前缀要在 `SourceFromId` 里映射到对应的 `DeviceSource`**（如 `sys:` → `System`）。
  - ⚠️ **命名空间不要叫 `VigilWatt.Core.System`**：它会遮蔽 BCL 的 `System`，
    让同文件里的 `System.Threading.Timer` / `System.Text.*` 全部解析失败（第 16 轮踩过，改用 `Power/`）。
  - ⚠️ **设备类型别预设**：一个 watcher 可能收到多种设备——经典蓝牙 selector 就同时含耳机、鼠标、键盘、手机，
    把 `Kind` 写死成 `Headset` 会让蓝牙鼠标显示成耳机（真机 RAPOO BT3.0 踩过）。
    要按设备自身的信号判定（`Models/ClassicDeviceKindResolver`：设备类别 → 服务 UUID → 显示名），
    见 dev/runtime-pitfalls.md §3.1。
  - 若要给开关（如 `MonitorSystemBattery`）：bool 字段反序列化缺省即用属性默认值，无需补键；
    但**关闭时必须把设备条目从 `_devices` 撤掉**（同 `DropDevicesOf` 的理由），且 `RestoreOfflineTombstones` 要跳过它。
- **托盘"最低电量"要显式排除 `DeviceSource.System`**（TrayService.Render）：本机电量是"正常用到 30% 是常态"的
  另一类设备，不该抢托盘那一个数字。列表/小组件/历史统计/低电量提醒则一视同仁。
