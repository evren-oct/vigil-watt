# 审查发现 BACKLOG

> 只放 **`open` / `doing`**。`done` / `wontfix` 在 [archive/BACKLOG-CLOSED.md](archive/BACKLOG-CLOSED.md)，编号关闭后不复用。
> 每条一行：编号、一句话、`文件:符号`。调查过程写审查报告或专题文档，不写在这里。
> handoff §4 只放这一轮要人做的事，不抄本表。全文超过 120 行，先把已关闭的挪走（`AGENTS.md` §3）。

## 未修

| 编号 | 一句话 | 证据 | 状态 |
|---|---|---|---|
| **CR-67** | DualSense 插件有电量条目与经典蓝牙 watcher 的无电量条目可并存，尚未跨来源合并 | `DualSensePlugin.cs`、`BluetoothClassicWatcher.cs:HandleDevice` | `open` |
| **CR-66** | BLE GATT 的 WaitAsync 超时只结束等待，底层晚返回的服务结果仍需完整清理；既有生命周期，本次仅释放正常返回结果 | `BleBatteryWatcher.cs:ReadBatteryViaGattAsync` | `open` |
| **CR-57** | 小组件自绘投影未做。原判据基于 Win10 19045，本机已升到 26200（Win11），需按新系统重判 | `WidgetWindow.xaml.cs:ApplyWin10Chrome` | `open` |
| **CR-60** | 贴片右键菜单的 owner 是任务栏子窗口，弹出和关闭未真机看 | `TaskbarPillWindow.cs:OnRightClick` | `open` |
| **CR-63** | 贴片与 explorer 输入队列串在一起，UI 线程长阻塞可能冻住任务栏。未实测 | `TaskbarPillWindow.cs` | `open` |
| **CR-13** | 两个蓝牙 watcher 的发现循环和快照仍然重复。`ReadConnected` 已抽到 `WatcherProps` | `Ble/BleBatteryWatcher.cs`、`Ble/BluetoothClassicWatcher.cs` | `open` |
| **CR-16** | 多个文件超过 400 行。拆分见下面的 D 项，不要先按行数硬拆 | `WidgetWindow.xaml.cs`、`LogitechPlugin.cs`、`BatteryPluginBase.cs`、`MchosePlugin.cs`、`DeviceManager.cs`、`BatteryChartControl.xaml.cs` | `open` |
| **CR-35** | 罗技打开成功后清了 `Io`，没清 `OpenFailed`。当前无实际后果 | `LogitechPlugin.cs:AttachCore` | `open` |
| **CR-36** | `TryEnqueue` 失败时 `_refreshQueued` 不会复位。窗口关掉后不再收请求，当前无实际后果 | `WidgetWindow.xaml.cs:RequestRefresh`、`DevicesPage.xaml.cs:RequestRefresh` | `open` |
| **CR-39** | 贴片默认贴在托盘左边，左键被拖动吃掉，可能盖住任务栏按钮 | `TaskbarPillWindow.cs:OnLButtonDown`、`TaskbarLayout.cs:Place` | `open` |
| **CR-41** | 竖向任务栏、多屏、自动隐藏没有真机看过 | `TaskbarPillWindow.cs:TryGetTaskbar`、`TaskbarLayout.cs:Place` | `open` |
| **CR-42** | 贴片颜色跟应用主题，不跟任务栏实际明暗 | `TaskbarPillRenderer.cs:SkinFor` | `open` |
| **CR-48** | 贴片拖动手感没真机试过。留白靠 alpha=1 的命中层 | `TaskbarPillWindow.cs:OnLButtonDown` | `open` |
| **CR-50** | 日志跨午夜换文件没有单测。要测得注入时钟 | `Utils/Log.cs:EnsureWriter` | `open` |
| **CR-52** | `ShutdownLogging` 的注释仍说没人调用 `Dispose`，实际退出路径会调 | `App.xaml.cs:ShutdownLogging` | `open` |
| **CR-61** | 日志在锁里同步刷盘，UI 线程可能等磁盘。改缓冲会丢掉崩溃前最后几行，所以先不改 | `Utils/Log.cs:Emit` | `open` |

## 重构候选（未做）

纯结构调整，没有对应的界面自动化。顺序：D1 剩下的基类 → D3 → D4 / D5 → 再处理 CR-16。

| 编号 | 抽什么 | 状态 |
|---|---|---|
| **D1** | 两个 watcher 的 `Start` / `HandleRemoved` / `Snapshot`。属性读取已经抽走 | `open` |
| **D3** | 图表的坐标和命中，抽成纯函数 | `open` |
| **D4** | 小组件的排序和宽度，抽出视图模型 | `open` |
| **D5** | 设置页保存时的副作用，抽出视图模型 | `open` |
| **D6** | 各插件「发帧再读到截止」的循环 | `open` |
