# 审查发现 BACKLOG

> 只放 **`open` / `doing`**。`done` / `wontfix` 在 [archive/BACKLOG-CLOSED.md](archive/BACKLOG-CLOSED.md)，编号关闭后不复用。
> 每条一行：编号、一句话、`文件:符号`。调查过程写审查报告或专题文档，不写在这里。
> handoff §4 只放这一轮要人做的事，不抄本表。全文超过 120 行，先把已关闭的挪走（`AGENTS.md` §3）。

## 未修

| 编号 | 一句话 | 证据 | 状态 |
|---|---|---|---|
| **CR-57** | 小组件自绘投影未做。原判据基于 Win10 19045，本机已升到 26200（Win11），需按新系统重判 | `WidgetWindow.xaml.cs:ApplyWin10Chrome` | `open` |
| **CR-60** | 贴片右键菜单的 owner 是任务栏子窗口，弹出和关闭未真机看 | `TaskbarPillWindow.cs:OnRightClick` | `open` |
| **CR-63** | 贴片与 explorer 输入队列串在一起，UI 线程长阻塞可能冻住任务栏。未实测 | `TaskbarPillWindow.cs` | `open` |
| **CR-13** | 两个蓝牙 watcher 的发现循环和快照仍然重复。`ReadConnected` 已抽到 `WatcherProps` | `Ble/BleBatteryWatcher.cs`、`Ble/BluetoothClassicWatcher.cs` | `open` |
| **CR-16** | 多个文件超过 400 行。拆分见下面的 D 项，不要先按行数硬拆。`MchosePlugin` 并入 V9 Pro 后已 766 行（四套协议），拆分建议等 D6 落地或有了第二台迈从设备再动——现在拆只能靠读代码验证 | `WidgetWindow.xaml.cs`、`LogitechPlugin.cs`、`BatteryPluginBase.cs`、`MchosePlugin.cs`、`DeviceManager.cs`、`BatteryChartControl.xaml.cs` | `open` |
| **CR-35** | 罗技打开成功后清了 `Io`，没清 `OpenFailed`。当前无实际后果 | `LogitechPlugin.cs:AttachCore` | `open` |
| **CR-36** | `TryEnqueue` 失败时 `_refreshQueued` 不会复位。窗口关掉后不再收请求，当前无实际后果 | `WidgetWindow.xaml.cs:RequestRefresh`、`DevicesPage.xaml.cs:RequestRefresh` | `open` |
| **CR-39** | 贴片默认贴在托盘左边，左键被拖动吃掉，可能盖住任务栏按钮 | `TaskbarPillWindow.cs:OnLButtonDown`、`TaskbarLayout.cs:Place` | `open` |
| **CR-41** | 竖向任务栏、多屏、自动隐藏没有真机看过 | `TaskbarPillWindow.cs:TryGetTaskbar`、`TaskbarLayout.cs:Place` | `open` |
| **CR-42** | 贴片颜色跟应用主题，不跟任务栏实际明暗 | `TaskbarPillRenderer.cs:SkinFor` | `open` |
| **CR-48** | 贴片拖动手感没真机试过。留白靠 alpha=1 的命中层 | `TaskbarPillWindow.cs:OnLButtonDown` | `open` |
| **CR-50** | 日志跨午夜换文件没有单测。要测得注入时钟 | `Utils/Log.cs:EnsureWriter` | `open` |
| **CR-52** | `ShutdownLogging` 的注释仍说没人调用 `Dispose`，实际退出路径会调 | `App.xaml.cs:ShutdownLogging` | `open` |
| **CR-61** | 日志在锁里同步刷盘，UI 线程可能等磁盘。改缓冲会丢掉崩溃前最后几行，所以先不改 | `Utils/Log.cs:Emit` | `open` |
| **CR-66** | GANSS 认领把 `&mi_03#` 当硬条件，但 VID/PID 是借用号、usage 是 VIA 标准页；换固件会静默认领失败，同号 VIA 键盘可能被误认领并写帧。已加产品名兜底，仍需有设备的人确认实际产品名并抓包，清单见 `hardware/ganss.md` §6.1 | `Plugins/Ganss/GanssCodec.cs:IsSupportedInterface` | `open` |
| **CR-67** | V9 Pro 只在一个电量点（100%）验证过：非满电是否线性、`[3]` 是否充电标志、低电量有无占位帧都不知道；接收器是否回显主机写入也未确认（已加"整帧等于查询帧即丢弃"的兜底）。需有设备的人补样本，清单见 `hardware/mchose-v9-pro.md` §5 | `Plugins/Mchose/MchoseV9ProCodec.cs:TryParse` | `open` |
| **CR-68** | 社区 PR #14（双通道去重）未纳入：与 #16 的同源合并语义重叠，要先定一套规则；且 `Devices` getter 里改 `Kind` 有副作用，`NormalizeName` 只 Trim 未忽略大小写（注释与实现不符）。合并前这两个点要先修 | PR #14 | `open` |
| **CR-69** | 社区 PR #16（设备管理）未纳入。**删除离线设备已按本项目方案落地**（2026-10-20：删除默认连带清历史 + 永久移除黑名单，见 `code-flow.md` §5.2），PR 里剩「只显示在线」「电量条对齐」仍不做 | PR #16 提交 `6908997f` | `open` |
| **CR-70** | 社区 PR #15 的 BLE 大改未纳入（`BleBatteryWatcher` +38/-22、`WatcherProps`、`DeviceKindGuesser`、`DeviceSourceInfo`、`SettingsPage`）。通用 GATT 路径已能读标准电池服务，故本轮只摘设备插件；蓝牙通道要单独一轮 | PR #15 | `open` |
| **CR-71** | 社区 issue #12（任务栏贴片避让托盘图标）只有根因与修法描述，没有代码。`TaskbarLayout.Place` 现在固定贴托盘左侧，与 CR-39 同源 | issue #12、`Services/TaskbarLayout.cs:Place` | `open` |
| **CR-72** | 社区 issue #11 现象 2（断电后死锁）未处理：涉及基类读实现，风险高于收益，需单独排期并配复现手段 | issue #11 | `open` |
| **CR-74** | 设备 ID 改端口无关键后的代价：同型号两只接收器同时插着会被并成一台（键相同）。需要时用「当前在位集合 + 端口哈希」做次级区分 | `Plugins/PluginUtils.cs:StableDeviceKey` | `open` |
| **CR-79** | **未定级，只有一次观测**：启动后前 30 分钟提交按 ~0.47MB/min 上爬（122.6→136.6），窗口隐藏时照涨；3 小时端点 134.8 未再涨，但 144 分钟无中间采样。倾向高水位，需多次运行 + 中间采样 + 换方向（Release / Win11 / GPU / 设备数）才能定性 | `Services/TaskbarPillWindow.cs:UpdateCore`、`Services/TrayService.cs:Render` | `open` |
| **CR-80** | BLE GATT 直读只 `Dispose` 了 `BluetoothLEDevice`，`svcResult.Services[0]` 与 `chrResult.Characteristics[0]` 没 Dispose（每 2–3 分钟一次，句柄小漏） | `Ble/BleBatteryWatcher.cs:ReadBatteryViaGattAsync` | `open` |
| **CR-81** | 查询型插件休眠即判离线：CHERRY 深度休眠块通道静音 → 3 轮 `ReportPollFailure` 转离线 + 熔断，唤醒后最长等 5 分钟退避才恢复。GANSS / 雷神 ML903 同病，需要一次跨插件的「休眠不等于失联」方案 | `Plugins/Cherry/CherryPlugin.cs:PollDevice`、`Plugins/Ganss/GanssPlugin.cs:PollDevice`（cherry.md §1.6） | `open` |
| **CR-82** | CHERRY 2.4G 与有线是两台设备（`PIXIU75 Dongle` / `PIXIU 75`），显示名不同，历史库各占一行、互报离线；要合并得改设备 ID 口径 | `Plugins/Cherry/CherryPlugin.cs:BuildDisplayName`（cherry.md §1.6） | `open` |
| **CR-83** | DARKRIM 一条状态帧的尾部 CRC 与算法不符：同批另三条向量都能通过，这条实算 `0x5737`、帧里写 `0x3AEF`。疑似抓包转录错误，也可能是另一种 CRC 口径。用例已拆成 `[Fact(Skip)]` 保留证据，**不得改帧里的 CRC 值来让它变绿**；待作者真机复核 | `Tests/DarkrimCodecTests.cs:RealFrames_CrcMismatch_PendingAuthorCheck`（darkrim.md §1.2） | `open` |

## 重构候选（未做）

纯结构调整，没有对应的界面自动化。顺序：D1 剩下的基类 → D3 → D4 / D5 → 再处理 CR-16。

| 编号 | 抽什么 | 状态 |
|---|---|---|
| **D1** | 两个 watcher 的 `Start` / `HandleRemoved` / `Snapshot`。属性读取已经抽走 | `open` |
| **D3** | 图表的坐标和命中，抽成纯函数 | `open` |
| **D4** | 小组件的排序和宽度，抽出视图模型 | `open` |
| **D5** | 设置页保存时的副作用，抽出视图模型 | `open` |
| **D6** | 各插件「发帧再读到截止」的循环（现有 Cherry `RequestBlock`、Pulsar `Query`、迈从、GANSS 各一套） | `open` |
