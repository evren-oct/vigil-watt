# 运行时坑

## 1. HID 读取必须用"单悬空读"模式（HidDeviceInfo.ReadInput）

**症状**：写入成功，但响应"永远收不到"。

**根因**：HID 的 ReadAsync 超时后并不会取消——旧读仍挂在驱动队列上。如果每次超时后开一个新读，设备迟来的响应会被旧读吃掉，新读继续干等。

**正确姿势**：每个句柄生命周期只保留一个 pending read：

```csharp
private Task<int>? _pendingRead;
public byte[]? ReadInput(TimeSpan timeout)
{
    if (_pendingRead == null) { _readBuffer = new byte[len]; _pendingRead = _stream.ReadAsync(...); }
    var completed = Task.WhenAny(_pendingRead, Task.Delay(timeout)).GetAwaiter().GetResult();
    if (completed != _pendingRead) return null;   // 超时，旧读继续挂着
    ...
}
```

**别用阻塞式 `ReadFile` 循环 + 每次重开**，也别用 `CancelIo` 后立刻重读（驱动队列行为不定）。

## 2. HID 其余小坑

- `HidP_GetCaps` 的成功码是 **0x00110000**，不是 0。判错会把正常接口当失败。
- 键盘/鼠标等系统标准 collection（如 dongle 的 MI_00）被 Windows 保护，CreateFile 打不开是正常的，不要当故障修。
- dongle 上的 consumer/system 等小接口（in=3/out=0 之类）**没有输出通道**——插件认领候选必须排除它们（MchosePlugin.Probe 开头的 `OutputReportLength < 20` 守卫），否则发帧时数组越界崩溃（踩过：IndexOutOfRangeException）。
- WriteFile 的输出缓冲长度必须正好等于接口的 OutputReportLength；report ID 不在描述符里时报"参数错误"（ERROR_INVALID_PARAMETER）——这个报错本身就是"该接口不认这个 report ID"的证据（41E4:2001 不认 0x4D 报文即由此发现）。

## 3. BLE 电量属性名在本机 Windows 上不可用

Windows 11 26200 上 `System.Devices.AepBattery_Level` 和 `System.Devices.Aep.Battery_Level` 都无效（CreateWatcher 报 COMException 0x8002802B）。BleBatteryWatcher 用属性候选列表逐个回退（只有 `System.Devices.Aep.IsConnected` 可用），电量直接走 GATT 读 Battery Service（0x180F/0x2A19），8 秒超时。别再尝试加新属性名，GATT 直读是可靠路径。

2026-10-06 补充：**经典蓝牙 selector 下同样不可用**（BluetoothClassicWatcher 首个候选 `AepBattery_Level` 也抛"找不到元素"，自动回退）。即本机任何 AEP 电量属性都拿不到。

**但经典蓝牙耳机的电量并非拿不到——换属性体系就行（2026-10-06 打通）**。Windows 11 设置页给耳机/音箱显示的电量来自 **PnP 设备节点属性**，不是 AEP 属性：

| 项 | 值 |
|---|---|
| 属性名 | `DEVPKEY_Device_BatteryLevel` |
| KEY | `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2` |
| 类型 | `DEVPROP_TYPE_BYTE`（也可能 UINT16/UINT32，读的时候按类型分支） |
| 挂在哪 | HFP 免手操作音频网关节点：`BTHENUM\{0000111E-…}_LOCALMFG&005D\7&1D3D8B&0&0CAEBDC6CDA7_C00000000` |
| 怎么读 | 只能用 SetupAPI：`SetupDiGetClassDevsW` → `SetupDiEnumDeviceInfo` → `SetupDiGetDeviceInstanceIdW` → `SetupDiGetDevicePropertyW`（WinRT `DeviceInformation` 拿不到 PnP 属性） |

实测 EDIFIER W820NB 双金标版读数 60%，与设置页逐字一致。要点：

- 实例 ID 末段 `&<12位十六进制>_C…` 就是设备 MAC（`0CAEBDC6CDA7`）；AEP ID 形如 `Bluetooth#Bluetooth<适配器MAC>-<设备MAC>`，取**最后**一段 MAC 才是设备本身，别取错。
- 过滤条件用 `BTHENUM\` 前缀，把 BLE（`BTHLE\`）节点排除，免得和 GATT 通道重复上报。
- 纯读、零写入，不打开设备、不建立连接；整表枚举一次约几毫秒，**做 5 秒缓存**再挂到每分钟的轮询上。
- 属性名别再造新词（`System.Devices.BatteryLevel` 之类在 WinRT 侧同样取不到），P/Invoke SetupAPI 是可靠路径。
- 排查脚本：`scripts/probe-devnode-battery.py`（纯 ctypes，`--all` 可 dump 任意节点全属性）。

### 3.1 经典蓝牙设备类型：不能假设"经典蓝牙 = 耳机"（2026-10-09 实修）

**症状**：`RAPOO BT3.0 Mouse`（经典蓝牙 BT3.0 鼠标）在列表里被显示成**耳机**（耳机图标 + 协议"经典蓝牙 A2DP/HFP"）。

**根因**：`BluetoothClassicWatcher` 新建条目时把 `Kind` 硬编码成 `DeviceKind.Headset`，理由是"A2DP/HFP 音频设备基本都是耳机/音箱"。
但 `BluetoothDevice.GetDeviceSelector()` 匹配的是**所有已配对经典蓝牙设备**（§3 自己也写了"含手机"），蓝牙鼠标/键盘同样会进来。
更糟的是 `DeviceStatusMerger` 是"声明覆盖猜测"（非 `Unknown` 才覆盖），这个错误类型会被**冻结**在进程内，永远改不回来。

**修法**：不再预设，按三级信号判定（`Models/ClassicDeviceKindResolver`，纯逻辑可单测）：

| 优先级 | 信号 | 来源 | 真机取值 |
|---|---|---|---|
| ① | **Windows 设备功能类别** | `DEVPKEY_DeviceDisplay_Category` = `{78C34FC8-104A-4ACA-9EA4-524D52996E57}, 90`（STRING_LIST），挂在**父节点** `BTHENUM\DEV_<MAC>` | `Input.Mouse` / `Audio.Headphone` |
| ② | **经典蓝牙服务 UUID** | **子节点**实例 ID 前缀 `BTHENUM\{00001124-…}_…` | `00001124`=HIDP；`0000111e`+`0000110b`=HFP+A2DP |
| ③ | 显示名兜底 | `DeviceKindGuesser` | `RAPOO BT3.0 Mouse` → Mouse |

判定顺序：类别命中即返回；否则**有音频服务（HSP/HFP/A2DP/AVRCP）= 耳机/音箱**（名字含"音箱"则 Speaker）；
都没有再按名字猜，**猜不出就 `Unknown`，绝不退回 Headset**（那正是本次 bug 的根因）。

**实测确认（本机）**：`BTHENUM` 共 3 个节点，全属同一台设备（MAC `B4EE25E54627`，VID_0002AE24 雷柏）：

```
BTHENUM\DEV_B4EE25E54627\…      ← 父节点：类别 = ["Input.Mouse"]
BTHENUM\{00001200-…}\…          ← 设备标识服务（SDP）
BTHENUM\{00001124-…}\…          ← HIDP（HID Profile）
```

关键细节（改代码时别踩）：

- **设备类别只在父节点上，服务 UUID 只在子节点上** → 必须按 MAC 归并两类节点（`ClassicDevNodeReader.ReadByMac`）。
- **实例 ID 里的 MAC 不能用"全局找 12 位十六进制"**：服务 UUID 段里的 `00805F9B34FB` 会先被匹配上。
  用 `&(?:BLUETOOTHDEVICE_)?([0-9A-Fa-f]{12})` 才能同时覆盖子节点末段 `<MAC>_C…` 与父节点的 `DEV_<MAC>` / `&BLUETOOTHDEVICE_<MAC>`。
- **设备类别依赖设备元数据包**，廉价设备可能没有（属性缺失），必须靠 ②③ 兜底。
- 类型**只在还没解析出来时重算**（名字与类别常常后到：WinRT 首次 `Added` 常只给设备 ID），一旦非 `Unknown` 就锁定。
- 类别取值是厂商可自定义的字符串（XSD 允许任意值），按关键字匹配而不是穷举枚举。
- 排查脚本：`scripts/probe-classic-kind.py`（`--all` dump 节点全属性）。

## 4. XAML 事件在解析期间就会触发

ComboBox 设 `SelectedIndex="0"` → `SelectionChanged` 在 `InitializeComponent()` 内部、x:Name 字段尚未赋值时就会触发。**事件处理器开头必须判空**（StatsPage.RenderChart 的 NRE 即此因）。

## 5. 托盘：纯 Win32，别用 H.NotifyIcon

H.NotifyIcon 在 WinUI 3 代码创建场景下：右键菜单点不动、退出失灵、图标转换内部抛异常。已彻底移除，TrayService 用纯 Win32：`Shell_NotifyIconW`（NIF_MESSAGE|ICON|TIP|INFO）+ 隐藏消息窗口 + `TrackPopupMenuEx(TPM_RETURNCMD)`（弹菜单前先 `SetForegroundWindow`）。相关注意：

- 改名的 DllImport 包装（如 `GetCursorPosRaw`）必须加 `EntryPoint = "GetCursorPos"`，漏了运行时 EntryPointNotFoundException。
- 模拟验证托盘回调可以往消息窗口 PostMessage CallbackMessage，日志能看到"收到托盘回调"即链路通。

### 5.1 托盘提示"永远显示暂无在线设备"——只渲染一次就再没刷新过（2026-10-06 实修）

**症状**：最小化到托盘后，右键/悬停看到的提示始终是「VigilWatt · 暂无在线设备」，图标也一直是 `–`，哪怕主窗口里三台设备都在线。

**根因**：`TrayService.UpdateIcon()` 只在两处被调用——`Initialize()`（启动那一刻）和 `TaskbarCreated`（explorer 重启）。
启动时 `UpdateIcon` 跑在 `DeviceManager.Start()` 之后、首轮扫描之前，`Manager.Devices` 还是空的，
于是渲染出的提示/图标被**永久定格**在"无设备"状态。**不是渲染错了，是根本没再渲染过。**

**修法**（`App.AttachTray` 里挂事件 → `TrayService.RequestUpdate()`）：

```csharp
Manager.DeviceUpdated += _ => tray.RequestUpdate();   // 电量/在线状态变化
Manager.DevicesChanged += tray.RequestUpdate;         // 设备增删
```

`RequestUpdate()` 做两件事，缺一不可：
1. **合并**：`Interlocked.Exchange(ref _updateQueued, 1)`，同一时刻只排一次队。
   设备事件来自后台线程且一轮里会连打十几次（每个设备一次 `Upsert`），不合并就是十几次
   `Bitmap`+`GetHicon`+`Shell_NotifyIcon`。
2. **回 UI 线程**：`MainWindow.DispatcherQueue.TryEnqueue`，与其它 UI 更新同线程，
   避免和 `Dispose()` 抢 `_currentHIcon`。窗口还没建好（`DispatcherQueue == null`）时就地渲染兜底。

**两个都要订**：`DeviceUpdated` 只在电量/在线**变化**时发（覆盖"掉到 0 变离线"这类状态跃迁），
`DevicesChanged` 覆盖设备**增删**——只订前者会漏掉新设备首次出现，只订后者会漏掉电量变化。

**排查手法（可复用）**：`UpdateIcon` 里加"提示变了才记日志"（`_lastTip` 比对）——
轮询期全打会把日志冲爆，但"提示到底变没变"正是这类问题唯一想知道的信息。
本次实测日志：`17:07:49 托盘提示已更新: 暂无在线设备` → `17:07:56 托盘提示已更新: 北通 T9 67%`，
一句话证明链路通了。

**顺带记住**：`Shell_NotifyIcon` 用 `NIM_MODIFY` 改提示时必须带 `NIF_TIP`，
`NIM_ADD` 之后忘了带就等于没改（这里两处 uFlags 都写了，别在重构时删）。

### 5.2 "程序没有图标"——非打包 WinUI 要两处都设（2026-10-06 实修）

**症状**：exe / 任务栏 / Alt-Tab 里都是 Windows 默认的空白程序图标。

**根因**：`VigilWatt.App.csproj` 里**从来就没有 `<ApplicationIcon>`**，工程里也没有任何 `.ico`
（`Assets/` 目录根本不存在）——所以 PE 里压根没有 `RT_GROUP_ICON` 资源，不是"图标画得不好看"，是"没有图标"。

**修法（两处，都要）**：

1. **编译期**：`<ApplicationIcon>Assets\VigilWatt.ico</ApplicationIcon>` → 把图标写进 exe 的 Win32 资源。
   资源管理器、任务栏、Alt-Tab 都读它。
   同时要保证 ico 文件随输出拷贝（`<None Update="Assets\VigilWatt.ico" CopyToOutputDirectory="PreserveNewest" />`），
   因为第 2 步运行时还要从 exe 同目录读它。
2. **运行期**：`MainWindow` 构造里 `AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "VigilWatt.ico"))`。
   **非打包（`WindowsPackageType=None`）WinUI 3 有时不自动继承 exe 的 Win32 图标资源**，只做第 1 步任务栏仍是空白方块。
   这一步在**窗口构造期**执行，异常必须自己 try/catch 吞掉——构造期抛出会让应用直接起不来。

**验证手法（不靠肉眼）**：直接读 exe 字节，把 ico 里每个尺寸的载荷拿去 `bytes.count()`——
本次 9 档尺寸（16/20/24/32/40/48/64/128/256）全部命中 1 次以上即证明资源已嵌入。
比"打开资源管理器看看"可靠，也能在 CI 里跑。

**图标源文件别手改**：`Assets/VigilWatt.ico` 由 `scripts/make-app-icon.py` 生成（纯标准库，解析式绘制 + 超采样），
改配色/字形改脚本重跑；`--preview x.png` 出放大预览图，16px 档位是否还认得出靠它一眼判断。

## 6. DPI：AppWindow.Resize 是物理像素

XAML 内容按 DIP 布局，125%/150% 缩放下直接用 DIP 尺寸调用 Resize 会系统性压小窗口（小组件"看不全"的根因）。必须乘 `Root.XamlRoot.RasterizationScale`（XamlRoot 未就绪时回退 1.0）：

```csharp
double Scale => Root.XamlRoot?.RasterizationScale is > 0 ? Root.XamlRoot.RasterizationScale : 1.0;
AppWindow.Resize(new SizeInt32((int)(widthDip * Scale), (int)(WindowHeight * Scale)));
```

小组件还需：`WS_EX_TOOLWINDOW`（不进 Alt-Tab/任务栏）、`DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)` 收边。

## 7. WinAppSDK 1.7 API 名称

- 是 `DisplayAreaFallback.Nearest`（没有 GetDisplayAreaFallback、没有 NearestNeighbor）
- 无边框用方法 `OverlappedPresenter.SetBorderAndTitleBar(false, false)`（没有 IsBorderless 属性）

## 8. 外部程序/浏览器脚本调试的坑

见 debugging.md（PrintWindow 与 GetWindowDC 的区别、DPI 虚拟化坐标、HID 选择器弹窗自动化）。

## 9. 历史页（StatsPage）切换"没反应"的两类原因（2026-10-05 实修）

1. **图表轴自适应数据而不是所选时间窗**：BatteryChartControl 旧版把 X 轴缩到数据起止，历史不足 24h 时"24 小时/7 天/30 天"渲染像素级相同，看起来像档位失灵。修法：`SetData` 接受显式 domain（from/to），轴强制用所选窗口，空数据显示"暂无采样"提示。
2. **周期性重建下拉框 ItemsSource 打断用户**：`ReloadDeviceList` 挂在 `DevicesChanged`（每 60 秒必发一次）上，重建 ItemsSource 会关闭打开中的下拉框并把选择弹回。修法：设备集合没变（按存活对象引用比较）就不重建；顺带 ItemsSource 直接放 DeviceStatus + DisplayMemberPath（按引用保选择，还修了重名设备 FirstOrDefault 串台的隐患）。
3. 构造函数里订阅 `Manager.DevicesChanged` 必须配 `Unloaded` 解绑——导航每次 `Frame.Navigate` 都新建页面实例，不解绑则旧页泄漏、回调叠加。
4. **`DisplayMemberPath` 写错属性名 → 下拉项静默空白**（不报错）：换 ItemsSource 条目类型时（如 DeviceStatus → 自定义 record）路径必须同步改。

## 10. 前端美化实修的新坑（2026-10-05）

1. **x:Bind 把 null 绑到 Foreground → 文字整段不可见**（不报错）：本地 null 值会压过隐式样式的默认前景刷。DeviceRowModel.PercentBrush 一度在"正常电量"分支返回 null，右侧大百分比直接消失。修法：模型属性保证非空，正常态显式给 `TextFillColorPrimaryBrush`（从 `Application.Current.Resources` 取，自动适配明暗）。
2. **横向 StackPanel 放在 Grid 的 `*` 列里会溢出**：StackPanel 不可收缩，长设备名 + 徽章会把同行后面的 Auto 列（百分比）整个顶出可视区。行内"名称+徽章"结构要用嵌套 Grid（`*,Auto,Auto`）+ `TextTrimming="CharacterEllipsis"`。
3. **`Microsoft.UI.Win32Interop` 没有 `GetDpiForWindow`**：主窗口构造期取 DPI 直接 P/Invoke user32 的 `GetDpiForWindow(hwnd)`（配合 §6：`AppWindow.Resize` 传物理像素）。
4. **UI 自动化验证（PowerShell 驱动 WinUI 窗口）三个坑**：
   - Git Bash 写出的 .ps1 是无 BOM UTF-8，PowerShell 5.1 按 GBK 解析，脚本里的中文字面量全部变乱码 → `PropertyCondition` 按名称找元素永远 NOT FOUND 且不报错。**脚本必须带 UTF-8 BOM**（`printf '\xEF\xBB\xBF' > file` 再追加）。
   - 窗口只是 TOPMOST 但不是前台时，第一次合成点击会被"激活窗口"吞掉，导航点击看似无效。**先点标题栏/先 SetForegroundWindow 激活，或对目标连击两次**。
   - 窗口被最小化后 `IsWindowVisible` 仍为 true，但 UIA BoundingRectangle 返回 -32000 系坐标、GetWindowRect 同理——读数先判负再做点击。

## 11. DispatcherQueue 回调里抛异常 = 进程静默消失（2026-10-06 实修）

症状：进程无任何日志、无 UnhandledException 记录地消失；WER 归档（`C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_*`）里是 `combase.dll`、异常码 `0x80004005 (E_FAIL)`。事件日志 Application 里没有对应条目。

根因：**`DispatcherQueue.TryEnqueue` 回调里未捕获的异常不经过 `Application.UnhandledException`**（它只覆盖 XAML 框架的消息循环），而是直接在 combase fail-fast。本次两个触发点：
1. 对已挂树但未参与布局 pass 的元素手动调 `Measure()`（小组件测高）——直接原生崩溃。
2. `Application.Current.Resources["不存在的键"]` ——WinUI 的 ResourceDictionary 索引器对缺失键**抛异常**（不像 WPF 返回 null），在回调里抛 = fail-fast。

防御：
- 所有 `TryEnqueue` 的回调方法体整体 `try/catch` + `Log.Warn`（WidgetWindow.RefreshContent 已如此）。
- 代码里按 key 查 `Resources` 前先确认键存在；拿不准就用固定色值/常量，别赌主题资源键名。
- 排查这种死法：WER ReportArchive 按进程名 grep，能拿到故障模块和异常码；`tasklist //FI "IMAGENAME eq xxx.exe"`（过滤形式）比 `tasklist | grep` 可靠，后者在 Git Bash 管道下会间歇性漏报。

## 12. 休眠伪读数：电量突然变 1 或 0 → 误报低电量（2026-10-06 加固）

**症状**：键盘（或鼠标）休眠、或快休眠前，监控里电量突然变成 `1`（偶尔 `0`），弹出低电量告警，一会儿又恢复。

**根因**：设备进入休眠/关机的瞬间，dongle 会推一帧**占位读数**——真机 66% 时推 `pct=1`。旧代码只在解析层挡了 `pct == 0`，`1` 直接漏进 UI 和低电量判定。更深层的问题是：**整条链路对"电量突变"没有任何合理性校验**，任何解析出的数字都会被无条件采信。

**加固三层（都在代码里，改协议解析时别绕过）**：

0. **真凶已抓到（2026-10-06 12:02 实测）**：休眠帧不是"电量位=1"，而是一个**类型位不同的推送帧**——
   正常电量推送 `13 0A 01 00 04 **05** 42 01 …`（0x05=电量，[6]=66%），休眠时推
   `13 0A 01 00 04 **02** 01 …`（0x02=设备状态通知，[6]=0x01 是 payload 不是电量）。
   旧代码不看下标 5 的类型位、直接读下标 6，于是把 payload 的 0x01 显示成 1%。
   **正确做法：先认类型位**（`MchoseV1Codec.ParsePush`），并把这个 0x02 帧当"设备已休眠"信号
   立即标离线（基类 `RequestOffline`）。原始帧两边的校验和都对得上，可自洽验证。
1. **解析层**（`MchosePlugin` + `MchoseV1Codec`）：按类型位识别（见上）；另有 `InvalidPercentFloor = 1`
   兜底（≤1 一律无效）。判无效后返回 null → 计入熔断失败 → 3 次后设备标离线，符合"键盘确实睡了"。
2. **基类层**（`BatteryPluginBase.Poll` → `BatteryReadingFilter`）：每设备一个过滤器实例，两条规则——① 数值低于 2（即 0/1）或 >100 直接判无效，永不采信；② 与上次采信值相比涨跌超过 20 点则本轮不采用，需**持续 5 分钟**才采信（回弹即清零计时）。这是通用的，挡所有插件的任意异常值，涨跌双向都管。基准**不随离线清空**。
3. **告警层**（`DeviceManager.CheckLowBattery`）：低电量值比上次已知值低超过 20 点时暂缓提醒一轮。最后一道闸。

**注意**：`0` 和 `1` 都不会是真实电量——低电量阈值是 20%，正常放电到 1% 之前早就提醒过了，所以拒绝它们不会漏掉任何真实告警。

**单测**：`src/tests/VigilWatt.Core.Tests/`（xUnit，18 个用例），帧解析用例全用真机原始帧，含休眠帧回归用例。改解析或过滤器后跑 `dotnet test src/tests/VigilWatt.Core.Tests`，全绿即通过。

## 13. Win10 与 Win11 的显示差异：Mica 不支持 → 双标题栏（2026-10-08 实修）

**症状**：Win11 上主窗口正常（Mica 半透明底 + 标题栏融入内容），同一份程序跑到 Win10 上"不好看"。

**根因**：`MainWindow` 构造里把"背景材质"和"标题栏融合"绑在了同一个条件里：

```csharp
if (MicaController.IsSupported())   // Win10 → false
{
    SystemBackdrop = new MicaBackdrop();
    ExtendsContentIntoTitleBar = true;
    SetTitleBar(AppTitleBar);
}
```

Mica 是 **Windows 11 专属**（微软文档原文："在 Windows 10 上会回退为纯色主题色"）。于是 Win10 上整个分支不执行，三条后果：

1. **双标题栏**（最扎眼）：系统照常画一条标准标题栏（带应用名），而内容区第 0 行那个 48px 的
   `AppTitleBar` 又画了一遍应用名 —— 窗口里出现两个标题，中间还夹一条硬分隔线。
2. **背景一片死白**：没有 Mica 也没有任何背景层，卡片直接贴在纯白上，层次感全丢。
3. **字体变宽**：Win11 自带 Segoe UI Variable（窄、有光学字号），Win10 回退 Segoe UI（宽 5~8%），
   同一套 FontSize 在 Win10 上更容易挤出去 / 换行。

**修法**：
- `MainWindow.xaml.cs`：`ExtendsContentIntoTitleBar = true; SetTitleBar(AppTitleBar);` **无条件执行**，
  与背景材质彻底解耦 —— 这是本次修复的核心，其它都是锦上添花。
- 新增 `Services/WindowBackdrop.cs`：取值常量 + `Resolve(偏好, mica支持, acrylic支持)` 纯函数，
  三级回退 **Mica（Win11）→ Acrylic（Win10 1809+）→ 纯色分层底**。
  `MicaController.IsSupported()` / `DesktopAcrylicController.IsSupported()` 必须运行时问，不能写死。
- `AppSettings.WindowBackdrop`（默认 `Auto`），设置页「外观」分区可切 Auto/Mica/Acrylic/Solid，
  保存即时生效（调 `MainWindow.ApplyBackdrop()`）。下拉框显示的是**用户偏好**，副标题写的是
  **这台机器上实际会生效什么** —— 两者在 Win10 上不一样（选 Mica 会静默降级成 Acrylic），分开显示。
- `App.xaml` 把 `ContentControlThemeFontFamily` / `TextControlThemeFontFamily` 显式钉成 `Segoe UI`，
  消除两个系统的排版宽度差。

**两个关键坑**：
1. **有材质时窗口根容器背景必须是 null**。铺任何不透明背景都会把 Mica 整个盖掉 ——
   只有 Solid 模式才设 `Root.Background`（用 `SolidBackgroundFillColorBaseBrush`）。
2. **ExtendsContentIntoTitleBar 之后系统按钮不跟随主题**。右上角的最小化/最大化/关闭由 DWM 绘制，
   Win10 深色主题下是"黑按钮配深底"几乎看不见 —— 必须手动刷
   `AppWindow.TitleBar.ButtonForegroundColor`，并订阅 `Root.ActualThemeChanged` 跟着改。

**API 差异（WASDK 1.7）**：`MicaBackdrop` 有 `Kind`（`MicaKind.Base`），
**`DesktopAcrylicBackdrop` 没有 `Kind`**，写了直接 CS0117（它本身就只有默认那一档）。

## 14. 桌面小组件在 Win10 上"平淡、圆角不圆润"（2026-10-08 实修）

**症状**：同一份小组件，Win11 上观感正常，Win10 上"平淡、圆角也不圆润"。

**根因（三条叠加，都被"无边框窗口"这条设定放大）**：

1. **圆角做不出来**。Win10 没有 `DWMWA_WINDOW_CORNER_PREFERENCE`（Win11 专属）；
   而 XAML `Border.CornerRadius` 只画卡片那一层，窗口矩形背板会在四角把它糊住。
2. **`SetWindowRgn` 裁不动 WinUI 3**（**最关键，第一版就栽在这里**）：WinUI 3 的内容走
   **DirectComposition**，由合成器直接画到窗口表面；`SetWindowRgn` 只裁传统 GDI 绘制区域，
   对 DComp 内容**完全无效**。实测表现 = "设了圆角区域，窗口看起来仍是直角"（用户截图确认）。
   所以"裁窗口形状"这条路在 WinUI 3 上根本走不通。
3. **无边框窗口没有 DWM 投影**：`SetBorderAndTitleBar(false,false)` 后 DWM 不再画投影，
   卡片"贴死"在桌面上、毫无层次 —— 这是"平淡"的主因。

**修法**（`Services/WidgetCompat.cs` 纯逻辑分流 + `WidgetWindow` 透明外壳）：

- 新增 `WidgetCompat.NeedsTransparentChrome(build)` / `SupportsDwmRoundedCorner(build)`，
  把"哪个系统走哪条路"集中到一处，并有单测（W1–W4）保护 —— **Win11 路径一个字都不动**。
- **Win10 = 透明窗口 + XAML 自绘圆角**：窗口表面整体镂空（不画任何像素），
  轮廓完全由 `Root` Border 的 `CornerRadius` 决定（WinUI 画圆角自带抗锯齿，干净）。
  半径用 16 DIP（Win11 保持 12），大一圈观感更接近现代磁贴。
- **恢复 DWM 投影**：`DWMWA_NCRENDERING_POLICY = DWMNCRP_ENABLED` + `SetWindowPos(SWP_FRAMECHANGED)`。
  透明窗口同样能拿到投影，卡片重新有了悬浮层次。

**透明从哪来 —— 两条路都试过，只有第三条能跑（别白试）**：

1. ❌ XAML `<Window Background="Transparent">`：**WinUI 3 的 `Window` 根本没有 `Background`
   属性**，XAML 编译器直接失败（且代码侧也没这个属性）。
2. ❌ 自定义 `SystemBackdrop` 子类：**C# 里不可实现**。`OnTargetConnected` 的参数类型
   `ICompositionSupportsSystemBackdrop` 是 **ABI 接口**，存在于 winmd 但**未投影到 C#**
   （`ISystemBackdropOverrides` 同理），写了必然 CS0246；`SystemBackdrop` 的构造函数也是
   `protected`、无法直接 `new`。
3. ❌ **`SetWindowCompositionAttribute(WCA_ACCENT_POLICY=19, ACCENT_ENABLE_TRANSPARENTGRADIENT=2)`**：
   本节当时把它当成唯一可行通道（"把 accent 设为全透明，窗口表面不再画任何像素"），并封装成
   `Native.MakeWindowTransparent`。**2026-10-12 在 Win10 19045 上像素级实测证伪**：它并不能让窗口
   表面变透明——叠在 `DwmExtendFrameIntoClientArea(-1)` 之上时四角是**accent 蓝 `#1883D7`**，
   把它撤掉反而立刻透出真桌面（`#1E1E1E`/`#252526`）。**该封装已删除**，
   现在贴片与小组件统一走 §17.5 的 extend-frame 路线（`Services/TransparentWindowSurface.cs`）。
   `AccentFlags=0x20`、`ACCENT_ENABLE_BLURBEHIND(3)` 两个变体也一并试过，同样不行。

**为什么内置材质（Acrylic/Mica）救不了**（中间版本试过，已废弃）：
`DesktopAcrylicBackdrop` 只能**铺满整个窗口矩形**，没有任何 API 能把材质本身裁成圆角，
于是四个角会留下方形材质残影 —— 比不做还难看。`DesktopAcrylicController.SetTarget()` 也
只接受 `WindowId` / `CoreWindow` / `CompositionTarget`（**不接受 `Window`**，写
`SetTarget(this)` 报 CS1501）。**材质与"圆角轮廓"是互斥需求，必须让 XAML 当轮廓。**

**API 差异（WASDK 1.7，实测结论）**：
- `SystemBackdrop` / `MicaBackdrop` / `DesktopAcrylicBackdrop` 都在
  **`Microsoft.UI.Xaml.Media`**（不是 `Microsoft.UI.Composition.SystemBackdrops`）。
- ⚠ **本节原写"`ICompositionSupportsSystemBackdrop` 是 ABI 接口、C# 用不了"——2026-10-10 证伪**：
  它在 WASDK 1.7.260224002 里**有 C# 投影**（`Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop`），
  `window.As<ICompositionSupportsSystemBackdrop>()` 编译通过且可用，是 Win11 上唯一的透明通道，见 §17。
  （"不能自定义 `SystemBackdrop` 子类"这条仍然成立，但不需要它了。）
- `Window` **没有** `Background` 属性（XAML 与代码侧都没有）。
- `DesktopAcrylicController.SetTarget` 没有 `SetTarget(Window)` 重载。

**验证**：`dotnet build VigilWatt.sln -c Debug` 0 错误；`dotnet test` **101/101**。

**环境提醒**：本节写于 Win10 19045（2026-10-08）；2026-10-10 的复核记录称"开发机已是 Win11 build 26200"，
但 **2026-10-12 复核：会话所在机器是 Windows 10 build 19045.6456**（`cmd /c ver`）——
**写结论前先自己确认本机版本，别照抄本节的描述**（这类"机器是什么系统"的记录已经错过一次，
并且直接导致 §17.1 把"哪一层可见"判反）。
好消息是：在 Win10 上"Win10 兼容外壳"这条路是**真的会执行**的（`WidgetCompat.NeedsTransparentChrome` 为 true），
所以本节与 §17.8 的结论都能在本机直接复现、像素级验证。
（用 shell / `nohup` 拉起 exe 会被进程树清理带走这条现象是 Win10 时期的记录。）

## 15. 小组件"钉在桌面"只能靠 SetWindowPos(HWND_BOTTOM)，不能靠 IsAlwaysOnTop（2026-10-08）

**需求**：小组件要支持三档层级 —— 浮在其它窗口上方 / 普通窗口 / 固定在桌面。

**API 事实**：WinUI 3 的 `OverlappedPresenter.IsAlwaysOnTop` 是 **bool**，只表达
"置顶 / 非置顶"，**表达不了"沉到最底"**。`非置顶` 得到的是普通层级（会被后续激活的窗口遮挡），
不是"压在其它窗口下方"。所以第三档必须走 `SetWindowPos`：

```csharp
// 桌面档：压到 Z 序最底（只有桌面露出时才可见）
Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
// 切回置顶/普通档：显式抬回顶层，HWND_TOP + NOACTIVATE 不抢焦点
Native.SetWindowPos(hwnd, Native.HWND_TOP, 0, 0, 0, 0,
    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
```

`Native.HWND_TOP = IntPtr.Zero`、`Native.HWND_BOTTOM = new IntPtr(1)`。
**不要**混用 `HWND_TOPMOST`：置顶交给 `IsAlwaysOnTop` 管，两套机制同时改 Z 序会互相打架。

**切换必须"先复位再设"**（`ApplyDisplayMode` 每次两档都写一遍）：只单向设置会残留
上一个模式的状态 —— 例如从"置顶"切到"桌面"时，`IsAlwaysOnTop` 仍为 true 会让窗口
**又置顶又沉底**，行为不确定。

**为什么不挂 WorkerW（真·嵌入桌面）**：`SetParent` 改父窗口会同时牵动三条既有逻辑 ——
拖拽（`AppWindow.Move` 在子窗口坐标系下语义变了）、`WS_EX_TOOLWINDOW`、
Win10 的透明外壳（`ApplyWin10Chrome`）。压到底层的观感与"钉在桌面"等价
（两者都只在桌面露出时可见），却零侵入。这是有意的取舍，不是偷懒。

**"固定"是懒执行的**：`HWND_BOTTOM` 是"放到**当前** Z 序底部"的快照，不是持久约束；
之后点桌面（`SHELLDLL_DefView`）刷新、或任何窗口被激活，小组件可能重新出现在上层。
对自用小工具可接受；真要强约束得用 `SetWinEventHook` 监听
`EVENT_SYSTEM_FOREGROUND` 再重新压底，复杂度不成比例。

**验证**：`dotnet build` 0 错误；`dotnet test` **101/101**。

## 16. 任务栏电量贴片：真嵌进任务栏（2026-10-10 建；**2026-10-12 改为 SetParent 方案**）

**需求**：把在线设备电量做成一条读数，固定在任务栏里显示。

**四条路里只有一条是"真嵌入"**：

- **deskband / Toolbar**（传统意义上的"真任务栏工具栏"）：要注册 COM 组件并由 explorer 加载，
  **Win11 已移除该机制**；Win10 上也是"注入 explorer 进程"，系统更新一次就可能失效。不做。
- **AppBar（`SHAppBarMessage`）**：会真的向系统**预留**一块屏幕空间，与任务栏自身（它也是一个 AppBar）
  的布局互相挤压。不做。
- **覆盖窗**：普通顶层窗口摆进任务栏矩形、靠反复抢 Z 序压住任务栏。**2026-10-10～10-12 用的是这条**，
  观感对但行为别扭（要 16ms 抬 Z 序、自动隐藏只能靠"厚度 ≤ 4px"猜、点任务栏会闪）。**已废弃**。
- **`SetParent` 子窗口**（**现用**）：把自己的窗口挂成任务栏（`Shell_TrayWnd`）的**子窗口**，
  跟随/隐藏/Z 序全部由系统负责，**不需要抢 Z 序**。TrafficMonitor、TrayStats、多个开源"任务栏歌词"
  都是这条（2026-10-12 查证）。

> **本节结论曾写错过一次**：10-10 那版断言"deskband 是唯一的真任务栏方案"，只对比了
> deskband / AppBar / 覆盖窗三条，**漏了 `SetParent`**。补上这条后覆盖窗方案不再必要。

### 16.1 三道锁（Win11 任务栏是 XAML 架构，缺一不可）

1. **`SetParent(自己的窗口, Shell_TrayWnd)`** —— 挂成子窗口，随任务栏移动/隐藏；副屏是 `Shell_SecondaryTrayWnd`。
2. **`WS_EX_LAYERED`** —— 普通子窗口会被任务栏的 XAML 合成层盖住；**分层子窗口有自己的 DWM 合成面，
   能显示在 XAML 之上**。
3. **内容不走 `WM_PAINT`，走 `UpdateLayeredWindow`** —— 渲染成 32 位预乘 ARGB 位图，
   经 `CreateDIBSection` + `UpdateLayeredWindow` 提交（**同时完成定位**）。

**MSDN 要求自己同步样式**：`SetParent` **不会**自动改 `WS_CHILD` / `WS_POPUP`，调用后要手动清
`WS_POPUP`、置 `WS_CHILD`，否则坐标仍按桌面算、裁剪也不对。

**命中测试按 alpha 判定**：分层窗口里 alpha=0 的像素会把鼠标消息漏给任务栏。所以整块位图先铺一层
**alpha=1 的"不可见底"**（`TaskbarPillRenderer.Render` 里那个 `hit` 画刷）——否则贴片只有文字/图形
能拖动，留白处拖不动。alpha=1 的视觉影响是 0.4% 黑，实测与任务栏背景只差 1 个色阶。

### 16.2 不需要抢 Z 序（SetParent 的直接红利）

**旧覆盖窗方案最麻烦的一环在这里消失了**：任务栏是 topmost，覆盖窗必须每 16ms `SetWindowPos(HWND_TOPMOST)`
把自己重新压上去，否则点一下任务栏它就沉到贴片之上、闪一下（那段历史见 §17.6、BACKLOG CR-46）。

**挂成子窗口后不再需要**——子窗口天然在父窗口之上，Z 序由系统维护。旧方案那套
`DispatcherQueueTimer` 16ms 抬 Z 序、`HWND_TOPMOST`、几何跟随降频（每 60 拍读一次任务栏矩形）
**整块删除**，只留一条 1 秒的 `Update()` 轮询做"几何跟随 + 内容刷新"。

**仍然用轮询而不是 `SetWinEventHook`**：要监听的类型太多（任务栏移动 / 尺寸变化 / 显示隐藏 /
自动隐藏弹出），轮询开销可忽略、代码少一大截。1 秒一拍足够——子窗口的**移动/隐藏本身由系统负责**，
轮询只为两件事：① 任务栏尺寸/位置变了要重算贴片尺寸（换 DPI、改任务栏大小、切换显示配置）；
② 设备数据变了要重绘位图。

**注意与 §15 的分工**：那里说"别混用 HWND_TOPMOST"，指的是**小组件**（层级由 `IsAlwaysOnTop` 表达三档语义）。
贴片现在两者都不用——它是子窗口，不存在层级语义。

### 16.3 绝不抢焦点

三条一起才够：

1. `WS_EX_NOACTIVATE`（建窗口时经 `CreateWindowExW` 的 `dwExStyle` 传入）——点击贴片不激活窗口；
2. `WS_EX_TOOLWINDOW`——不进 Alt-Tab 与任务栏按钮区；
3. 显示用**原生 `ShowWindow(SW_SHOWNOACTIVATE)`**，**不要用 `AppWindow.Show()`**——
   后者会尝试激活窗口，而贴片被"显示"一次就把用户正在打的字打断了。

> 与旧方案的差别：旧贴片是 WinUI 窗口，`WS_EX_NOACTIVATE` 在构造函数里补设，
> 且必须靠"调一次 `Window.Activate()` 让 XAML 内容加载"这一矛盾操作才能渲染。
> **原生窗口没有这个问题**——`ShowWindow(SW_SHOWNOACTIVATE)` 直接可见，全程不激活。

**代价（见 §18）**：`WS_EX_NOACTIVATE` 让窗口永不激活 → WinUI 的 `ContextFlyout` 靠"去激活"做
light-dismiss，**用它右键菜单就关不掉**。所以贴片的右键菜单走 Win32 `TrackPopupMenuEx`
（`Services/AppPopupMenu.cs`），不用 `ContextFlyout`。

### 16.4 贴片不画底框，靠分层窗口的逐像素 alpha

**贴片刻意没有底色与边框**：第一版做成了"白底圆角胶囊"，用户实测反馈"能显示，但很突兀"——
任务栏上凭空多出一个方块。现在只有**文字与设备图形**浮在任务栏上，观感与任务栏自身的图标一致。

**这一处从"最麻烦"变成了"免费"**：旧方案要给 WinUI 3 窗口做透明，得连闯两层背景 + DWM 圆角外描边
（§17 整节，试过 8 条通道、4 组组合），因为 WinUI 窗口的背板不归我们管。
**现在窗口是我们自己 `CreateWindowExW` 建的**——`hbrBackground = IntPtr.Zero`（类背景刷就是空），
内容整块由 `UpdateLayeredWindow` 的 32 位预乘 ARGB 位图决定，**alpha=0 的像素就是真透明**，
没有第二层背景可漏。§17 那一整节只对**小组件**（仍是 WinUI 窗口）适用。

**代价**：前景色（文字/图形/分隔线）不能走 `ThemeResource`（原生窗口里根本没有 XAML 资源），
`TaskbarPillRenderer` 里按主题**硬编码两套** `Skin`（`SkinFor(bool light)`，读注册表 `AppsUseLightTheme`）。
**另一个已知局限**：文字颜色跟的是**应用主题**、不是"任务栏实际看起来的明暗"。浅色主题 + 深色壁纸
（任务栏半透明、透出壁纸）时，深色文字会不够清楚——**未处理**，见 BACKLOG CR-42。

### 16.5 拖动 = 沿任务栏方向的偏移

贴片位置不是自由坐标，而是"**默认位 + offset**"：

- 横向任务栏只取鼠标位移的 **X**、竖向只取 **Y**（斜着拖不会把贴片拽离任务栏）；
- **只有拖动中**才把"实际落点 - 默认位"反推回 `_offsetPx`（拖到头再往回拖不会"空走"）。
  平时过渡帧（拔插显示器时任务栏/托盘矩形短暂错位）也会夹紧，若写回记忆，分辨率往返后偏移就被污染；
- 松手时写入 `TaskbarBatteryOffsetX` + `TaskbarBatteryOffsetDpi`（记下偏移时的任务栏 DPI），重启沿用；
- 落位时用 `TaskbarLayout.ScaleOffset` 从**同一对规范值**换算到当前 DPI（不能逐帧累乘）。

拖动用原生 `SetCapture` / `ReleaseCapture` + `WM_MOUSEMOVE`：按下用**当帧可见（已夹紧）偏移**做起点、
移动改 `_offsetPx` 后立刻 `ApplyPosition()`（`UpdateLayeredWindow` 同时完成定位，不需要额外的 `SetWindowPos`）。
**注意命中测试按 alpha 判定**（§16.1）：整块位图铺了 alpha=1 的"不可见底"，
所以留白处也能按下拖动；否则只有文字/图形像素能拖。

#### 16.5.1 拖动消息被 Win11 任务栏吃掉：轮询 fallback（2026-10-20 实修）

**症状**：拖动"有时失效"——按下去贴片跟一下，鼠标稍微上下飘一点就断；开机或解锁后第一次可能正常，之后再也不行。

**根因（三层）**：
1. `SetCapture` 文档写明 **Only the foreground window can capture the mouse**；贴片带 `WS_EX_NOACTIVATE`，本线程几乎永远不是前台线程，所以 `SetCapture` 不报错但等于没抓到。
2. **更深层**：Win11 任务栏（XAML 架构）的输入管理器在 `WM_MOUSEACTIVATE` 阶段就把子窗口的后续点击消息拦截了。诊断日志显示：第一次成功时 `WM_MOUSEACTIVATE → WM_POINTERDOWN → WM_LBUTTONDOWN` 完整链路；失败后只有 `WM_MOUSEACTIVATE`，后续点击消息彻底消失。
3. **连带 bug**：光标在贴片外松手 → `WM_LBUTTONUP` 永远不投递 → `_dragging` 卡在 `true`，之后悬停贴片自己跳，且每秒 `ApplyPosition` 持续写回记忆。

**修法**：
1. **主方案**：`WM_MOUSEACTIVATE` 中让 `DefWindowProcW` 先处理（子窗口默认会把消息发给父窗口），只在父窗口要 `MA_ACTIVATEANDEAT` 时才干预为 `MA_NOACTIVATE`——保留双击等正常行为。
2. **Fallback**：常驻 10Hz 轮询检测按键状态（`GetAsyncKeyState` + `GetCursorPos`）。光标在贴片内 + 左键按下 → 启动拖动；松手 → 结束拖动。拖动中切到 60Hz，手停住 3 拍后回退到 10Hz。
3. **彻底去掉 `SetCapture`**：跨进程子窗口 + 任务栏 XAML 环境下，捕获不仅无效，还会触发输入管理器的拦截行为。

- 不装钩子、不发合成输入；
- 偏移没变就不写设置，单击不产生磁盘写；
- 设置页新增「重置贴片位置」按钮，offset 归零 + 按当前 DPI 绑定。

### 16.6 本机电量恒定排除

Windows 任务栏**自己就有一个电池图标**，贴片再重复一遍纯属噪音。
判据 `d.Source != DeviceSource.System`，与托盘"最低电量"不统计本机是同一个理由（README 的
「托盘图标为什么不显示笔记本电量」那条）。

### 16.7 定位数学：屏幕坐标 → 任务栏客户区坐标

`TaskbarLayout` 那套纯函数算出来的仍是**屏幕坐标**（它们原本服务于覆盖窗方案，屏幕坐标直接可用）。
子窗口的坐标却是**相对父窗口客户区**的，所以 `ApplyPosition` 在 `TaskbarLayout.Place(...)` 之后
必须再走一次 `ScreenToClient(_taskbar, ref pt)` 换算，否则贴片会跑到屏幕左上角之外。
**这是从覆盖窗迁移到子窗口时唯一改动的几何代码**（`TaskbarLayout` 本身一行未动，定位单测保留）。

### 16.8 显示配置变化：重新挂住 + 分层窗口重建

外接屏拔插 / 分辨率往返时三件事要一起做：

1. **父窗口可能丢了**：资源管理器会拆任务栏；每拍若 `GetParent(贴片) != Shell_TrayWnd`，再 `AttachToTaskbar`。
2. **任务栏 DPI 与建窗时不同**：分层子窗口的坐标空间跟创建时的 DPI 绑在一起，只重算矩形不够——
   `DestroyWindow` 后按 §16.1 三道锁重建。重建必须经 `DispatcherQueue`，**不能在 `WndProc` 里同步 Destroy**。
3. **偏移按记下它时的 DPI 换算**（§16.5），夹紧结果不写回记忆，否则过渡帧会把用户位置改坏。

`WM_DPICHANGED` / `WM_DISPLAYCHANGE` 走推迟重建；`WM_SETTINGCHANGE`（主题等）只重绘。

### 16.9 耳机头梁不要用 GDI+ DrawArc（2026-10-14）

**症状**：任务栏贴片上的耳机头梁是斜的。小组件上同一图形的右边没有头梁。

**根因**：贴片走 `DeviceIconFactory.DrawGlyph` 的 GDI 分支。`DrawArc` 画的是 11×14 的半椭圆，并且从 13 宽名义框的左缘起笔。GDI+ 在 Win10 上会把这种弧的两端画得不在同一高度；头梁又偏左，左耳罩接外沿、右耳罩接内侧。XAML 那份 `BorderThickness` 写成了 `(1.2, 1.2, 0, 0)`，右缘没有描边。

**修法**：头梁宽 11、高 7，在名义框里水平居中。上半圆用两段对称三次贝塞尔（不用 `DrawArc` / `AddArc`），圆下用竖线收到开口的底边。XAML 描边改为 `(1.2, 1.2, 1.2, 0)`。两套渲染器改一处要改另一处。

**验证（2026-10-12 实测，本机 Win10 19045）**：

- `dotnet build VigilWatt.sln -c Debug` **0 警告 0 错误**；`dotnet test src/tests/VigilWatt.Core.Tests --no-build`
  **292/292 全绿**。
- 启动应用（PID 8772）后枚举窗口确认**这是真任务栏子窗口**：
  - `GetParent(pill) == Shell_TrayWnd`（`0x1012E`）——决定性证据；
  - `GWL_STYLE = 0x54000000`（`WS_CHILD` 已置、`WS_POPUP` 已清）；
  - `GWL_EXSTYLE = 0x08080080`（`WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`）；
  - `IsWindowVisible = True`。
- **像素级透明**：贴片内部背景 `(48,31,34)` vs 紧邻任务栏 `(49,32,35)`，**只差 1 个色阶**
  （即 alpha=1 的"不可见底"带来的 0.4% 黑），无白框。
- 日志干净（无 `UpdateLayeredWindow 失败` / 无异常）。

**未做**：竖向任务栏 / 多显示器 / 自动隐藏三条路径未真机验证（本机底部任务栏、单屏）；
未向任何设备发帧。相关条目见 BACKLOG CR-41。

### 16.10 悬停设备名：自绘提示窗 + TME_HOVER（2026-10-15）

贴片是跨进程挂进 `Shell_TrayWnd` 的分层子窗口，且带 `WS_EX_NOACTIVATE`：

1. **WinUI `ToolTip` / `TOOLTIPS_CLASS` 都不稳**——系统提示框绑这种子窗口时常常不出现。
2. **提示窗自己 `CreateWindowEx`（分层 + 置顶、不激活、不挂任务栏）**，`TaskbarTipRenderer` 画类 Win11 圆角气泡（浅/深主题、细边框、轻阴影）。
3. **悬停用 `TrackMouseEvent(TME_HOVER)`**——在 `WndProc` 里启 `DispatcherQueueTimer` 不可靠；等 `WM_MOUSEHOVER` 期间不要再次 Track，否则计时被清零。
4. 命中矩形与绘制同源；定位在 `TaskbarLayout.PlaceTooltip` / `HitTest`。

### 16.11 贴片尺寸必须每拍按当前缩放重算（2026-10-20）

**症状**：换线（HDMI → USB-C / 走扩展坞）后贴片大小不对，而且一直不对，重启应用才恢复。

**根因**：高度只在「任务栏矩形变了」时才重算。换线后缩放会变，而任务栏的**像素**尺寸可能恰好没变
（同一分辨率、只换了连接方式），于是尺寸一直沿用旧值。另一个入口是 `GetDpiForWindow` 在句柄重建瞬间返回 0：
那时若按 `scale = 1` 兜底算，会得到 100% 下的尺寸并同样卡住。

**规则**：高度每拍按 `rect` + `dpi / 96` 重算；**高度或缩放任一变化都要重画**（宽度、字号、圆角都按缩放算）。
`dpi == 0` 时直接跳过这一拍，不要按 1.0 兜底。

### 16.12 显示配置切换时贴片"多一个并重叠"（2026-10-26 修）

**症状**：插拔外接屏 / 换缩放之后，任务栏上出现两个贴片、互相叠着（位置差几像素），重启应用才消失。

**现场证据**（只读枚举，用户退出前那台机器）：同一个 `Shell_TrayWnd` 下有 **2 个** `VigilWattTaskbarPill`
子窗口（同 pid、同 tid、都 visible、rect 差 2px），另有 **2 个** `VigilWattTaskbarPillTip` → 说明确实存在过
两个 `TaskbarPillWindow` 实例。残留那个停在 `gap = 8`（120 dpi）算出的默认位，而当时任务栏已是 96 dpi →
**它没有任何实例在驱动**（活实例每秒重算位置、DPI 一变就自毁重建），是"dispose 过但窗口没销毁"的僵尸。

**触发**：日志里 `任务栏贴片：配置的屏幕当前不在，临时贴到 显示器 1` 与 `… 显示器 2` 相隔 **250ms**，
40 秒后又是一对。显示配置过渡期任务栏句柄与 DPI 连续抖，而 `Reconcile` 每抖一次就"销毁旧实例 + 建新实例"，
于是 1 秒内连开两次窗口。

**已排除**：跨进程子窗口的销毁本身不可靠——隔离进程实测 `SetParent` + `WS_CHILD` + `UpdateLayeredWindow`
真位图后 `DestroyWindow` **8/8 成功**、`IsWindow` 立刻为 false。问题在生命周期代码，不在 Win32。

**代码里的四个缺口**（任一命中都会留僵尸）：`ReleaseWindow` 忽略 `DestroyWindow` 返回值；`Dispose()` 里
`EndDrag()`/事件解绑/`HideTip()` 都排在 `ReleaseWindow()` 之前且无 try/finally；`_current` 静态字段在构造里
直接覆盖、无"上一实例先 Dispose"约束；`EnsureWindow()` 只信 `IsWindow(_hwnd)`。

**修法**：
1. `Reconcile` 去抖：**屏幕 / 任务栏句柄 / DPI 连续 2 拍一致**才重建；**任务栏矩形变化不再触发重建**
   （窗口自己每拍按 §16.11 重算尺寸与位置）。启动与设置保存走 `immediate` 跳过去抖。
2. `EnsureWindow` 建窗后清扫同一任务栏下**本进程**的其它 `VigilWattTaskbarPill`（只认同 pid，Debug 版与
   安装版同时跑不能互杀）——漏网的活不过一次建窗。
3. 构造时先 `Dispose()` 上一个 `_current`：进程内只允许一个贴片实例。
4. `Dispose()` 的 `ReleaseWindow()` 移进 `finally`；`DestroyWindow` 失败记 `Log.Warn` 并隐藏重试。

**验证**：`python scripts/check-taskbar-pill.py`（数窗口，>1 返回码 1）。插拔外接屏 + 切
「仅电脑屏幕 / 仅第二屏 / 扩展」各一次，每次之后贴片窗口数必须恒为 1。

**未定论**：本次没有在复现时抓到"到底漏销毁还是实例被覆盖"（`Log.Debug` 没开，也没有实例→hwnd 映射），
所以修法是"堵死四个缺口 + 自愈"，不是单点定因。建窗与清残留都会记日志，下次复现能自证。见 BACKLOG CR-107。

## 17. WinUI3 窗口的两层背景：小组件白底（2026-10-10 起；§17.1 于 10-12 更正）

> **2026-10-12 起，本节只对"小组件"（`WidgetWindow`，仍是 WinUI 3 窗口）适用。**
> 贴片已改成**原生分层窗口**（§16），窗口由自己 `CreateWindowExW` 建、类背景刷为空、
> 内容整块由 `UpdateLayeredWindow` 的 ARGB 位图决定——**没有"两层背景"这回事**，
> 本节当年为贴片白框做的全部工作（8 条通道、4 组组合）对现在的贴片**已不适用**。
> 下面 §17.6 的"点任务栏闪一下"同理：那是覆盖窗抢 Z 序的产物，子窗口方案下已消失。
>
> 本节结论按"**哪一层可见**"组织，而那**取决于系统**（§17.1 只在 Win11 成立，§17.8 是 Win10 的实测）。
> 动任何与窗口透明/无边框有关的代码前，先把 §17.8 的实测表读完——那里有 4 组"试过不行"的组合。

**症状**（用户两轮反馈，第二轮明确说第一轮没修好；**均针对当年的 WinUI 贴片**）：

1. 任务栏上仍是一块**纯白方块**（`#FFFFFF`），文字/图标浮在上面；
2. 点一下任务栏，贴片信息会**闪一下**。

**先记一条排查教训（这是白费两轮的地方）**：一开始用临时 Python 脚本截屏"亲眼看看"，
截出来永远**一片空白**，于是连续两轮都在凭推理改代码。原因是那个进程**没有声明 DPI 感知**：
`GetWindowRect` 返回物理坐标（本机 3840×2160），而 `GetDC(0)` + `BitBlt` 按 DPI 虚拟化后的逻辑坐标
（2560×1440）取像素，两边对不上，取的永远是不相干的区域。
**截屏脚本第一行必须先 `SetProcessDpiAwarenessContext(-4)`（PER_MONITOR_AWARE_V2）**，否则别信它的输出。

### 17.1 根因：可见的是 **Win32 那一层**，不是内容岛（**该结论只在 Win11 上成立**）

WinUI3 的窗口背景确实是两层，但**哪一层可见**必须实测，不能想当然：

| 层 | 是什么 | 实测结论 |
| --- | --- | --- |
| ① **Win32 窗口背景** | 窗口类 `WinUIDesktopWin32WindowClass` 的**类背景刷**，GDI 用它擦客户区 | **这才是"白框"** |
| ② 内容岛 Visual 背景 | `DesktopChildSiteBridge` 那层（`WS_EX_NOREDIRECTIONBITMAP`，纯 DComp） | 本来就是透明的 |

**证据链**（三条互相印证）：

- 读类背景刷：`GetClassLongPtr(hwnd, GCLP_HBRBACKGROUND)` = **`0x6`**，即 `(HBRUSH)(COLOR_WINDOW + 1)`。
  `COLOR_WINDOW` 在浅色主题下**就是纯白 `#FFFFFF`**，与实测到的方块颜色完全一致。
- 用 GDI 往客户区填别的颜色，方块跟着变——说明看到的就是这层 GDI 表面。
- 强制 `RedrawWindow(..., RDW_ERASE)` 触发一次擦背景，白框**立刻回来**——确认是"擦背景"这个动作画上去的。

内容岛那层（②）换透明 `SystemBackdrop` 是**必要的**，但它**从来不是可见的那一层**——
这正是上一轮"日志显示背板设置成功、白框却还在"的原因。

> ⚠ **2026-10-12 更正：以上"①才是可见层"只在 Win11 成立。** 换到 Win10 19045 上实测，
> 可见的**恰恰是 ② 内容岛那层**（不设透明背板时它回退成"纯色主题底"，浅色=纯白）——
> 证据是"往客户区做 GDI 填色，可见像素纹丝不动"，说明看到的不是 GDI 表面。
> 也就是说**两层都可能可见、取决于系统**，所以最终修法是"**两层都清**"，不做 OS 分支，见 §17.8。

### 17.2 通道实测表（Win11 26200.9445；accent 一行已在 Win10 上补测）

不要靠资料推断哪条通道能用，本机逐个试过：

| 通道 | 结果 |
| --- | --- |
| `SetWindowCompositionAttribute` + `ACCENT_ENABLE_TRANSPARENTGRADIENT` | ❌ **返回 1、日志干净、窗口毫无变化**（见 17.3）。**Win10 19045 补测：不是"无变化"而是把四角染成 accent 蓝 `#1883D7`**，同样不是透明 |
| `ICompositionSupportsSystemBackdrop.SystemBackdrop = 透明画刷` | ⚠️ Win11 上只清掉 ② 层、① 层照旧；**Win10 上它正是那块白框的解药**（§17.8） |
| `WS_EX_LAYERED` + `SetLayeredWindowAttributes(...,255,LWA_ALPHA)` | ❌ 无变化（alpha 255 就是不透明） |
| 只加 `WS_EX_LAYERED`（不设任何属性） | ❌ 无变化 |
| 运行期补 `WS_EX_NOREDIRECTIONBITMAP` | ❌ **系统直接把这一位丢掉**（只能建窗口时带） |
| `DwmEnableBlurBehindWindow`（1px 空区域） | ❌ 无变化 |
| `DwmExtendFrameIntoClientArea(-1,-1,-1,-1)` 单独用 | ❌ 无变化 |
| `SetLayeredWindowAttributes(..., 白, 0, LWA_COLORKEY)` | ✅ 有效，**但不采用**（见 17.4） |
| **extend-frame + 自己接管擦背景（填 alpha=0）** | ✅ **采用**（见 17.5） |

### 17.3 "静默失效"到底有多静默

`SetWindowCompositionAttribute` 在 Win11 上**照样返回非 0**，日志里不会出现任何失败。
§14 记的"Win10 靠它做透明"在 Win11 上完全无效，而**调用方从返回值上分辨不出来**。
推论：**"日志干净"不能当作"这条通道生效了"**——只能靠肉眼/像素确认。

### 17.4 为什么不用 colorkey（虽然它是唯一直接见效的）

`WS_EX_LAYERED` + `LWA_COLORKEY(白)` 确实能把白框打掉（实测贴片内外的背景像素完全一致）。
但两条硬伤让它不能上：

1. **深色主题下贴片的文字就是白色 `#FFFFFF`**，键色会连文字一起挖掉；
2. colorkey 命中的像素会**变成点击穿透**——贴片四周那圈留白（`PadH`/`PadV`）就点不到了，
   而"沿任务栏拖动贴片"正是用户要的功能。贴片又紧挨任务栏按钮区，误点会直接启动别的程序。

### 17.5 采用的修法（`Services/TransparentWindowSurface.cs`）

清 ① 层（Win32 那层）两步，缺一不可：

```csharp
// ① 让 DWM 改用窗口表面的逐像素 alpha 合成（否则它一律把窗口当不透明）
var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);

// ② 接管 WM_ERASEBKGND / WM_PAINT，把客户区擦成 alpha=0
//    （GDI 填色不写 alpha 字节，正好留下 0；再叠 DWM 的逐像素合成就是真透明）
if (msg == Native.WM_ERASEBKGND) return 1;   // 认领"已擦"，别让类背景刷上白
if (msg == Native.WM_PAINT) { /* BeginPaint → FillRect(BLACK_BRUSH) → EndPaint */ return 0; }
```

**只有 ② 没有 ① 会剩约 4% 的暗底**（实测 `#B2C8D8` vs 任务栏 `#BACFE2`），
所以 extend-frame 不是可选项。

**两个必须注意的实现细节**：

- **子类化必须幂等**：`Apply` 在构造函数与 `Root.Loaded` 各调一次，重复 `SetWindowLongPtr(GWLP_WNDPROC)`
  会把我们自己的过程再包一层 → 消息一进来就无限递归。判据用"**当前窗口过程是不是已经是自己**"
  （`GetWindowLongPtr(hwnd, GWLP_WNDPROC) == Marshal.GetFunctionPointerForDelegate(...)`），
  比记 HWND 更稳（HWND 会被系统复用）。`TrayService` 里是同一类写法。
- **委托必须用静态字段压住**：交给非托管代码的 `WndProcDele` 一旦被 GC 回收，窗口再来消息就是访问已释放内存。

**顺带确认**：`DwmExtendFrameIntoClientArea` **不会**把客户区变成"标题栏"——
实测 `WM_NCHITTEST` 在中心与四角都仍返回 `HTCLIENT(1)`，自定义拖动不受影响。

### 17.6 "点任务栏闪一下"（**已随贴片改子窗口消失**）

当年是独立问题：贴片与任务栏同为 topmost，点任务栏会把它抬到贴片之上。
"抬回最上层"的拍频从 100ms 压到 **16ms**（系统计时器粒度下限）后，实测遮挡 **6~25ms、中位 19ms**，
约等于一帧，人眼分辨不出。

**2026-10-12 贴片改为 `SetParent` 子窗口后，这个问题的成因整条不复存在**——
子窗口天然在父窗口之上，不再有"抢 Z 序"这回事（§16.2），16ms 抬 Z 序的代码已删除。
留此记录是因为**小组件若将来也要"压住任务栏"会重新遇到同一问题**（它现在不压任务栏，故无此问题）。

### 17.7 未验证 / 已知边界

- **（贴片已不适用）拖动手感**：旧贴片靠 `WM_NCHITTEST` 保证拖动，新贴片走原生
  `SetCapture` + `WM_MOUSEMOVE`（§16.5），`WM_NCHITTEST` 与拖动无关了；手感仍需人手试，见 CR-48。
- **（贴片已不适用）深色主题**：新贴片配色在 `TaskbarPillRenderer.SkinFor` 里硬编码两套（§16.4），
  深色下的观感仍未确认，见 CR-42。
- ~~Win10 真机仍不可得~~ → **2026-10-12 复核：会话所在机器就是 Win10 19045，本节与 §17.8 的结论均为该机像素级实测**。
- 本次验证用的临时脚本（`.workbuddy-ai/tmp/*.py`）用完即删。

### 17.8 Win10 白框的真身是「内容岛那层的默认背板」；小组件另有 DWM 非客户区渲染（2026-10-12 实修）

**症状**（用户 2026-10-12 反馈）：同一份程序，贴片在 Win11 正常，Win10 上①出现**纯白方块**、②最右一格的 `%` 被窗口边缘**裁掉**。

**环境**：本机 = Windows 10 build 19045.6456（`cmd /c ver`）——**bug 本机可复现**，下面全部是实测，不是推断。

**第一轮修错了层（保留过程，这是本节最有用的部分）**：按 §17.1 的结论，先把"清 Win32 那层"
（`DwmExtendFrameIntoClientArea(-1)` + 接管 `WM_ERASEBKGND`/`WM_PAINT`）补进 Win10 分支，**白框照旧**。
于是做判别实验：**往贴片客户区做 GDI 填色**（换成绿色）→ 可见像素**纹丝不动，仍是 `#FFFFFF`**
→ 可见的**不是** GDI 表面，**不是 Win32 那层**。

**真身**：Win10 上可见的是**内容岛那层**——没有显式设透明 `SystemBackdrop` 时，它回退成"纯色主题底"
（浅色主题=纯白），与 §13 那条"Mica 在 Win10 回退为纯色主题色"同源。补上
`ICompositionSupportsSystemBackdrop.SystemBackdrop = 全透明画刷` 后白框立刻消失。

**最终修法**：**两层都清，且不做 OS 分支**。三件事收进 `TransparentWindowSurface.Apply(window)`：
`ApplyBorderlessChrome` + `ClearWin32Background` + `ClearContentIslandBackground`。
`Services/TaskbarTransparency.cs` 与 `Native.MakeWindowTransparent` 已删除（后者见 §14 第 3 条的证伪）。

> **2026-10-12 更新**：**现在只有小组件调 `TransparentWindowSurface.Apply`**（`WidgetWindow.xaml.cs`）——
> 贴片已改成原生分层窗口（§16），没有 WinUI 内容岛、不需要这套修法。
> 下面"贴片"相关的实测记录保留为历史。

**`%` 被裁的两处原因**：

1. Win10 分支原先漏了 `ApplyBorderlessChrome` → 窗口残留 `WS_CAPTION|WS_THICKFRAME|…`，非客户区把客户区挤窄，
   而 `AppWindow.Resize` 设的是**外框**尺寸 → 客户区比实测内容窄；
2. 修法（双保险）：两条分支都跑 `ApplyBorderlessChrome`，且 `RelayoutCore` 改 **`AppWindow.ResizeClient`**（按**客户区**定尺寸）。

**桌面小组件：另一个独立成因，且与 DWM 投影互斥**（本轮连带发现，用户未报）：

`WidgetWindow.ApplyWin10Chrome` 里有一句 `DWMWA_NCRENDERING_POLICY = DWMNCRP_ENABLED`（§14 为找回 DWM 投影而加）。
透明窗口要求 `DwmExtendFrameIntoClientArea(-1)`——**整窗都算"边框"**；此时再让 DWM 去渲染非客户区，
它就把这层边框填成不透明底 → **整个窗口变白方块**（四角 `#FFFFFF`，卡片被包在白底里）。

逐组合实测（改属性 + `RedrawWindow` 后立刻取像素，**不用重编译**）：

| margins | NCRENDERING | 四角 | DWM 投影 |
| --- | --- | --- | --- |
| `-1` | `ENABLED` | ❌ `#FFFFFF` 白框 | ✅ 有 |
| `-1` | `DISABLED` / `USEWINDOWSTYLE` | ✅ = 真桌面（4 个圆角外共 220px） | ❌ 无 |
| `0` / `1` | `ENABLED` | ❌ 不透明（GDI 那层露出来） | — |
| `-1` | `ENABLED` + accent 或 `WS_EX_LAYERED`+`LWA_ALPHA(255)` | ❌ 白框 / accent 蓝 | ✅ 有 |

**在 Win10 上"透明"与"DWM 投影"不可兼得**（试过的旁路全在上表）。取舍：**透明优先**（那是用户报的 bug），
于是小组件在 Win10 上不再有投影。日后若要补投影，正确方向是**在 XAML 里自绘**
（窗口四周留余量 + 卡片视觉加 `DropShadow`），**不是**回去开 `NCRENDERING_POLICY`。

**验证**（本机 Win10 19045，逐像素）：

- **贴片（历史，现已不适用）**：窗口矩形与客户区**同尺寸**（边框差 0）；贴片内背景 `#301F22`/`#311F23` 与紧邻任务栏 `#312023`
  基本一致；内容像素落在 x=7..113（窗口宽 123）→ **右侧留白 9px，末尾 `%` 完整**。
- **小组件**：抬到最上层截一张、`ShowWindow(SW_HIDE)` 后再截同一块，**四角像素与"真桌面"逐字节相同**
  （`#1E1E1E`/`#252526`/`#73DDFC`/`#007ACC`）；透出桌面的像素从修前 4045 → **12940**（圆角外全部透出）。
- `dotnet build VigilWatt.sln -c Debug` 0 警告 0 错误；
  `dotnet test src/tests/VigilWatt.Core.Tests --no-build` **292/292**（2026-10-12 贴片改造后重跑）。

**顺带修 CR-49**：旧窗口过程从"单份静态字段"改成按 HWND 存（`TransparentWindowSurface.PrevProcs`，
`WM_NCDESTROY` 清理）——小组件与贴片可**同时存在**（`App.xaml.cs`），单份字段会让后装的窗口调到前一个的过程。
（贴片改原生窗口后自己不再用它，但小组件仍是 WinUI 窗口、仍需这套。）

## 18. 贴片/小组件的右键菜单点别处不消失：WinUI 的 light-dismiss 靠"窗口去激活"（2026-10-12 实修）

**症状**（用户反馈）：在任务栏电量贴片和小组件上右键，菜单正常弹出，但**点其他任何地方它都不消失**。

**根因**：WinUI 3 的 `ContextFlyout`（`MenuFlyout`）**不是**靠鼠标钩子或全屏透明层关闭的，
而是靠**宿主窗口收到"去激活"**（`Window.Activated` → `Deactivated`）来 light-dismiss。
而贴片为了"绝不抢焦点"带 `WS_EX_NOACTIVATE`（§16.3）——**它从不激活，也就永远不会"去激活"**，
那个信号永远不来 → 菜单弹出后点哪都关不掉。菜单自己的 popup 窗口同样是 `WS_EX_NOACTIVATE`，
**键盘焦点也不在它身上，连 ESC 都送不进去**。

**实测证据**（合成鼠标 + `EnumWindows`，见下"关于手段"）：

- WinUI 3 的 flyout 宿主窗口类名 **`Microsoft.UI.Content.PopupWindowSiteBridge`**（标题"主机弹出窗口"），
  扩展样式 = **`WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST`**；
- 贴片主窗口（**当时还是 WinUI 覆盖窗**）= `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`；
- 一次遗留打开的菜单：**跨应用点击 + 连按 ESC 全部无效**，只有再次右键同一个锚点才把它 toggle 掉。

**修法**：右键菜单改走 **Win32 原生弹出菜单**（`TrackPopupMenuEx`，与托盘菜单同一条路），
封装在 `Services/AppPopupMenu.cs`；两个窗口的 XAML 删掉 `Border.ContextFlyout`，
改挂 `RightTapped="Root_RightTapped"`（**不是** `PointerPressed`：菜单必须在**右键抬起**时弹，
在按下时弹会把紧随其后的抬起当成"选中菜单项"）。原生菜单自带"点别处即关"，且不依赖窗口激活。

三个必须照做的约束：

1. **`TrackPopupMenuEx` 之前必须 `SetForegroundWindow(owner)`**，否则点菜单外不关闭（托盘菜单同款约束，
   见 `TrayService.ShowContextMenu`）。贴片的 `WS_EX_NOACTIVATE` 只挡"鼠标点击激活"，
   显式 `SetForegroundWindow` 仍有效；真失败时 `AppPopupMenu` 会记一条 WARN，便于排查。
2. 之后补一发 `PostMessageW(owner, WM_NULL, 0, 0)`（MSDN 托盘菜单样例的收尾动作，不补偶发关不干净）。
3. ~~**贴片在弹菜单期间停掉跟随定时器**：菜单也是 topmost，而跟随每 16ms 把贴片抬到 topmost 顶端
   （§16.2），会把菜单压到贴片下面。~~ **2026-10-12 已不需要**：贴片改成任务栏的**子窗口**后
   （§16）不再是 topmost，而菜单是 topmost 弹窗 → 菜单天然在贴片之上，不存在"被抬 Z 序压住"。
   `TaskbarPillWindow` 里已没有"菜单期间停表"的逻辑。
   **但引入一个新的待验点**：菜单的 owner 现在是**任务栏的子窗口**（`Shell_TrayWnd` 的 child），
   而 `TrackPopupMenuEx` 的 owner 通常传顶层窗口——本机未真机点检菜单是否正常弹出/关闭，见 BACKLOG CR-58。

**与 §16.3「绝不抢焦点」不冲突**：那条说的是**悬停 / 点击 / 显示**不抢焦点；
菜单是用户主动打开的，**任何菜单本身都要拿前台**（否则点别处关不掉），贴片自己不带边框、
又是 `WS_EX_TOOLWINDOW`，用户看不到任何变化。

**关于手段（重要教训）**：本节证据是用**合成鼠标**（`SendInput`）+ 截屏 + `EnumWindows` 取的。
这**动了用户真实电脑的鼠标和键盘**，用户当场制止。**以后凡需要注入真实输入的验证，先问用户、别直接跑**；
能靠"枚举窗口 / 读样式 / 读日志"拿到的证据，就不要去动输入。

**未验证**：改完只做了 `dotnet build`（0 警告 0 错误），**未真机点检**——菜单外观、"点别处关闭"、
以及小组件（当时是"桌面"档、被别的窗口盖住，自动化点击打到了上层应用，那次测试无效）都需要人手复看。

## 19. WinUI3 页面缓存：设了 `NavigationCacheMode` 之后，「构造函数订阅 + `Unloaded` 解绑」会变成 bug（2026-10-12 实修）

**背景**：切页慢的根因之一是四个页面全吃 `NavigationCacheMode` 默认值 `Disabled`——每次点导航都
`new Page()` ＋ 重新解析 XBF。给四个页面设 `Required` 之后实例被**复用**，构造函数**只跑一次**。

**坑**：页面原先的写法是"构造函数里订阅管理器事件 + `Unloaded` 里解绑"。缓存之后这条链会退化成：
第一次切走 → `Unloaded` 解绑 → 切回来时**不再走构造函数** → 事件永远订不回来，页面从此不刷新。
**它不报错**：页面照常显示，只是数据停在离开那一刻——比崩溃更难发现。

**改法**（`Views/DevicesPage` / `StatsPage` / `HistoryPage` / `SettingsPage` 统一）：

- 订阅与解绑放 **`Loaded` / `Unloaded` 成对**里，并用一个 `_hooked` 标志挡住 `Loaded` 重复触发造成的叠加订阅；
- "每次进来都要重读"的活放 `Loaded`（设备列表刷新、设置同步），"只建一次"的活留在构造函数——
  典型是 `SettingsPage.BuildOptions()`（下拉框档位与插件行动态生成）：缓存页复用同一实例，
  再建一遍会**叠加出重复条目**。

**同源的一条经验**：`SettingsPage.BuildSearchIndex()` 原本在构造函数里递归遍历整棵视觉树。
它既不该占页面首次显示的时间，也不该等用户打字时才第一次付——改到 `Loaded` 里用
`DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, …)` 于布局完成后的空闲时段构建，
并在 `TextChanged` 里兜底（用户抢先打字也不会拿到空索引）。

**预热**：`MainWindow.PrewarmSettingsPage()` 在窗口可见前先导航到设置页再回设备页，
把 XBF 解析与控件构造的一次性开销提前付掉；整段 `try/catch` 兜底，失败只记日志并退回设备页。

**未真机点检**：缓存后的实际切换手感、首次打开设置页的耗时、预热对启动时间的影响，都需要人手复看。

## 20. 从 `bin` 启动后写盘全被拒、没有托盘图标：exe 继承了工作区的 Low 完整性标签（2026-10-20 定案）

**症状**（一起出现，很容易误判成好几个 bug）：

- 应用能启动、能进设置页，但**日志一行不写**（`app-yyyy-MM-dd.log` 的 mtime 停在旧时间）
- 点「保存」弹「设置保存失败，无法写入 settings.json」，或弹「开机自启未生效」
- 托盘没有图标（`Shell_NotifyIcon` 失败）
- 任务栏贴片、小组件跑到屏幕原点（拿不到 `Shell_TrayWnd` 句柄）
- 点几次保存后进程退出（`Microsoft.UI.Xaml.dll` / `0xc000027b`）

**根因（一句话）**：工作区目录被打上了 `Low` 完整性标签，`dotnet build` 产出的 exe 通过继承拿到同一个标签，
于是**进程以 Low IL 运行**。Low IL 不能向 Medium IL 写（No-Write-Up），而 `%LOCALAPPDATA%`、
`HKCU` 下的注册表、explorer 的 shell 消息**全是 Medium IL**——一次降权，四处受挫。

**这只影响开发版**：正式版装在 `%LOCALAPPDATA%\VigilWattApp\`（Medium IL），不经过工作区目录，
所以上面这一串症状在正式版里**不会出现**。遇到它们先确认跑的是不是 `bin\...\win-x64` 那份。

**一条命令定性**（不用管理员、不用探针）：

```
icacls "<exe 路径>" | findstr Mandatory
→ Mandatory Label\Low Mandatory Level:(I)(NW)     ← (I)=继承来的，NW=不许向上写
```

对照本机 2026-10-20 实测：

| 对象 | 完整性标签 |
|---|---|
| 工作区根 `...\Documents\10-project\vigil-watt` | `Low (OI)(CI)(NW)` |
| 其中 `bin\...\win-x64\VigilWatt.App.exe` | `Low (I)(NW)` —— 继承自上面 |
| `C:\Users\<用户>\AppData\Local` | 无 `Mandatory` 行 = 默认 **Medium** |
| 复制到 `C:\VigilWattTest\` 的那份 exe | **Medium**（新目录是默认级别） |

**为什么症状是这一组**：

| 被拒的操作 | 目标 IL | Low 进程的结果 |
|---|---|---|
| 写 `%LOCALAPPDATA%`（日志 / settings / history） | Medium | `Access denied` |
| 写 `HKCU\...\CurrentVersion\Run` | Medium | `Access denied`（键的 ACL 是 FullControl，所以看着像"键坏了"） |
| `Shell_NotifyIcon` / 找 `Shell_TrayWnd` | explorer 是 Medium | UIPI 拦消息 → 没托盘、贴片定位退化到原点 |
| 写 exe 旁边的 `data\` | Low（同目录） | **成功**——所以"工作区能写、用户目录不能写" |

**A/B 实测**（同一份产物，2026-10-20 23:05 与 23:09 两次双击，只差 `icacls` 一行）：

| 条件 | 日志 |
|---|---|
| `Low` | `Shell_NotifyIcon 失败` ×4、`写入开机自启失败: Access denied` |
| `Medium` | `进程身份: … 完整性=Medium …` + `托盘图标已创建`，无任何失败 |

**怎么办（已落地，不要临时手工改）**：

1. **构建后自动提回 Medium**：`VigilWatt.App.csproj` 的 `RestoreExeIntegrityLevel` target 在 Debug 构建后
   执行 `icacls "$(TargetDir)$(AssemblyName).exe" /setintegritylevel Medium`。**每次重建 exe 都会重新继承
   Low，所以这一步不能省**，否则"改完还是崩"会反复出现；
2. `scripts\run-app.ps1` 启动前再提一次，兜住"直接跑旧产物"的情况；
3. **不要改工作区目录本身的标签**（`icacls <dir> /setintegritylevel Medium`）：那是沙箱的隔离边界，
   可能被重置，而且会让沙箱侧的进程写不了源码；
4. 旧办法仍然可用：`run-app.ps1 -Copy` 复制到工作区之外再启动（新目录天然是 Medium）。

**被放大的代码 bug（已修，别改回去）**：`AutostartService.SetEnabled` 把异常直接抛给
`SettingsPage.Save_Click`，于是**每点一次保存就记一次未处理异常**；全局熔断器（`App.xaml.cs`，
1 分钟 5 次）数满 5 次后 `e.Handled = false`，进程按崩溃路径退出。用户看到的是
"点保存没反应，点几次软件就没了"。修法：`TrySetEnabled(enabled, out error)` 内部收口，
失败只弹「开机自启未生效」并继续保存其他设置。
**教训**：UI 事件处理器里不许让资源访问异常逸出——熔断器会把它升级成进程退出。

**排查时踩的三个错判**（都记下来，别再犯）：

- **"桌面目录被保护"**：换目录有时好有时坏 → 真正变的是那个目录有没有 Low 标签，与目录名字无关；
- **"腾讯电脑管家在拦"**：管家的三个 FSFilter 驱动确实在跑，但拦截日志为空、加信任区无效；
  `icacls` 一查就知道是完整性级别，与过滤驱动无关。枚举驱动的方式（`HKLM\SYSTEM\CurrentControlSet\Services`
  下 `Group` 以 `FSFilter` 开头的项）可以留着备用，但**先查 `icacls`**；
- **"进程是沙箱账户跑的"**：22:50 那次看到 `data\` 能写、用户目录不能写，误判成 `CodexSandboxOnline`
  在跑。实际 `data\` 下所有文件的 `Owner` 都是本人、父进程是 `explorer.exe`、日志打的是
  `用户=OCTX-EVREN\20211`——**是同一账户被降权，不是换了账户**。判据：`Owner` + 父进程 + 完整性级别。

**留存的诊断**（一次性探针已删，只留这几处，出问题时会自己说话）：

- `Log.TryProbeWriter(out error)`：启动时试开当天日志，失败就弹窗报出路径与真实异常。
- `AppSettings.LastSaveError`：保存失败的真实异常，随设置页弹窗显示。
- `StartupGuard`：`OnLaunched` 的 `StartCore` 全程 `try/catch`，失败落日志 + 原生弹窗，不再静默死亡。
- `AutostartService.TrySetEnabled(out error)`：写 Run 键失败时弹「开机自启未生效」并带真实异常，
  同时它自己也不再抛——既是诊断，也是"UI 事件不许逸出异常"的示范。
- `ProcessIdentity.Describe()`：启动日志第二行打 `用户= 会话= 管理员= 完整性= 沙箱组=`。
  **`完整性` 不是 Medium 就不用往下查了**——先把 exe 提回 Medium 再说。

## 21. 历史库时间戳带 +00:00 偏移，离线设备的「更新于」少 8 小时（2026-10-24 实修）

**症状**：设备列表里**在线**设备的「更新于 HH:mm:ss」正常，**离线**设备少 8 小时——本机 GMT+8，
X9 实际 09:47:04 却显示 01:47:04；离线的鼠标实际 10-24 01:13:16 却显示 17:13:16。

**根因**：`HistoryStore` 用 `DateTimeOffset.FromUnixTimeMilliseconds(ms)` 读库，得到的 DateTimeOffset
**偏移是 +00:00（UTC）**。`DateTimeOffset.ToString("HH:mm:ss")` 按**自身偏移**渲染，于是显示成 UTC。
在线设备不受影响：它的时间来自 `DateTimeOffset.Now`（本地偏移 +08:00）。

**为什么难发现**：同一批 UI 里两种写法混用——`HistoryDeviceRow` / `BatteryChartControl` / `StatsPage`
都写了 `.LocalDateTime`（自动转本地，对），而 `DeviceRowModel` 与 `DevicesPage` 直接
`{d.LastUpdated:HH:mm:ss}`（错）。而且 `DateTimeOffset` 的相等是**按瞬时**比，偏移不同也算相等，
拿它跟 `DateTimeOffset.Now` 对比根本发现不了偏移变了。

**修法**：在数据源头统一——`HistoryStore.LocalTime()` 把库里的 UTC 毫秒转成本地时区，
六个读路径（`Query` / `DistinctDevices` / `GetStats` / `GetDeviceStats` / `GetDeviceSummary`）全走它。
比较语义不变（相等仍按瞬时比），只是显示变回本地挂钟时间。

**教训**：从库里读出来的时间戳，要么读的时候就把时区定死，要么显示的时候显式 `.ToLocalTime()`，
**别把 DateTimeOffset 的原始偏移直接丢给格式化器**。写单测时也要断言 `.Offset`，
只断言时间值会漏掉这类偏移错误。

## 22. 设备列表行把图标元素存进模型：行一移位就 `0xc000027b`（2026-10-25 实修）

**症状**：关掉任一插件 → 回主界面，被移除的那一行变成空白/深色块，界面像卡住；再点「刷新」进程直接退出。
日志只有一条 `XAML 未处理异常`：

```
System.ArgumentException: Value does not fall within the expected range.
  at ContentPresenter.set_Content
  at DevicesPage_obj7_Bindings.Update_Icon
  at DevicesPage.Refresh() ... DevicesPage.xaml.cs:105
  at DevicesPage.OnPageLoaded(...)
```

事件查看器同一时刻：`Application Error`，`0xc000027b`，模块 `CoreMessagingXP.dll`。

**根因**：`DeviceRowModel.Icon` 存的是**画好的 UIElement**（`DeviceIconFactory.Scaled` 的 Viewbox），
XAML 用 `Content="{x:Bind Icon}"` 塞进 `ContentPresenter`。而 `Refresh()` 按 `Signature` 复用整行对象
（连 Icon 一起）。复用本身没问题——**问题在行对象会换位置**：删掉一行后后面的索引整体前移，
`_rows[i] = fresh[i]` 触发 Replace，ListView 为新位置重建容器，把**同一个** Viewbox 交给第二个
`ContentPresenter`。一个 UIElement 只能有一个父级，WinRT 抛 `E_INVALIDARG`。

**为什么点刷新才崩**：第一次异常被 `App.UnhandledException` 吞掉（日志记「第 1 次」），但那一行
已经渲染不出来（用户看到的"变黑"）、布局也挂了；再点刷新重走一次 `Refresh()`，第二次异常跨 ABI
逸出，`e.Handled = true` 救不回来，进程 fail-fast。`0xc000027b` 是 stowed exception 的通用退出码，
不是内存越界——别往"内存坏了"的方向查。

**为什么必现**：页面 `NavigationCacheMode = Required`，`Unloaded` 只解绑事件、不清 `_rows`，
旧行连同 Icon 一直活着。`DevicesChanged` 在设置页期间发出时页面已 Unloaded、收不到，
回主界面 `Loaded` 才补刷新——删行与刷新之间的空档正好落在这一步。

**修法**：模型不再持有视觉元素。`DeviceRowModel` 只存 `IconKind` + `IconColor`，模板里换成
`Controls/DeviceIconPresenter`（`ContentControl` 子类，两个 DP，值变了才重画），每个行容器各持一个
自己的元素。另外给 `Refresh()` 包了 `try/catch` + `Log.Error`：UI 更新失败该留痕、不该带走进程（R5）。

**教训**：**DataTemplate 里不要绑共享的 UIElement**。行模型只放值，元素交给模板层按值现画；
只要模型能活过容器重建，就不能在里面存 `FrameworkElement`。同理适用于任何 ItemsSource 增量更新
（Replace / Move / Insert 都会重建容器）。

## 23. 设备睡醒后电量不刷新：退避档位只升不降 + 没有被动监听的插件享受不到 CR-53（2026-10-27 实修）

**症状**：ATK F1 Ultimate 2.0 鼠标静置几分钟（固件自动休眠）后动一下，电量卡在旧值不动；
去设备页点「刷新」也不一定变，过好几分钟才自己跳一次。键盘（走 VIA、一直插着）全程正常。

**日志证据**（`app-2026-10-27.log`，同一天内反复出现）：

```
18:22:33 收到设备休眠/关机通知，立即标记离线
18:23:31 停写退避期满（30 秒），恢复查询
18:25:33 连续 3 次查询失败，熔断停写（第 2 次，退避 60 秒）
18:33:33 收到设备休眠/关机通知，立即标记离线
18:42:33 熔断停写（第 2 次，退避 60 秒）
18:46:33 熔断停写（第 3 次，退避 120 秒）
18:51:33 熔断停写（第 4 次，退避 240 秒）
18:56:02 停写退避期满（240 秒），恢复查询      ← 4 分 29 秒没有一次读数
18:56:02 设备重新上线: ATK F1 Ultimate 2.0
```

注意 18:56:01 那三条「打开主窗口被调用」：用户以为是自己点刷新救回来的，
其实退避**正好**在 1 秒后到期——纯属巧合。

**根因（三条叠加，缺一条都不会这样）**：

1. **退避档位只升不降**。`WriteBackoff.For(trips)` 是 30s→1m→2m→4m→5m，而 `BreakerTrips` 只在
   `ReportPollAnswered`（读到应答）时清零。鼠标每睡一次就失败 3 轮 → 顶高一档。用户一天睡几十次，
   档位轻松爬到第 4 档（240 秒）。等他真去用鼠标时，正好撞在最长的那段退避里。
2. **熔断期内一帧都不发**。`CanWrite` 未到期直接 `return false`，`PollDevice` 早退 → 设备醒了
   也没人问它，只能干等退避到期。
3. **CR-53 的 bypass 通道对纯查询型插件无效**。`MaybeHalfOpenBreaker` / `RunPendingConfirm` 都跑在
   **被动监听线程的空闲 tick**（约 500ms 一次）上；ATK 没有 `SupportsPassiveListen`（FF02 口只在
   收到查询帧后才回包，没有自发流），根本没有那条线程。所以"退避期满立刻核实"对它是空转。

**修法**：给"纯查询型插件"补一条独立的**半开探活**通道，与正式轮询的退避解耦。

| 改动 | 位置 |
|---|---|
| `BreakerProbeInterval`（默认 `Zero` = 关闭，行为与旧版完全一致） | `BatteryPluginBase` |
| `TrackedDevice.HalfOpenProbe`；`CanWrite` 放行；`ReportPollFailure` 见到它**直接 return**（失败不记账、不升档） | 同上 |
| `NudgeHalfOpen()`：对每个熔断中的设备按间隔发一帧核实，成功即由插件自己的 `ReportPollAnswered` 解除熔断 | 同上 |
| `Poll()` 里加 halfOpen 分支：熔断期内按间隔把这一轮当探活发（60 秒保底） | 同上 |
| 15 秒发现节拍调 `NudgeHalfOpen()` | `DeviceManager.DiscoverOnlyAsync` |
| `SupportsDiscoveryProbe = true` + `BreakerProbeInterval = 30s` | `AtkPlugin` |

**为什么默认是关闭**：这条通道只对"设备会频繁短睡、且没有被动监听"的插件有意义。
有被动监听的插件（雷柏/迈从/北通…）本来就能靠 CR-53 的 bypass 秒级感知，多一条只会抬高写入频率。
所以 `BreakerProbeInterval` 保持 `Zero` 默认，谁需要谁覆写一行——和 `OfflinePolicy` 一样的"每插件一行配置"。

**验证**（`BreakerProbeTests`，5 个用例）：探活失败档位不动 / 探活成功清零档位并回在线 /
按间隔节流 / 默认关闭时行为与旧版逐帧一致 / `NudgeHalfOpen` 不必等主轮询就能叫醒设备。

**教训**：**"别太频繁打扰设备"和"及时发现设备回来了"是两件事**，不能共用一条退避曲线。
退避该约束的是正式轮询；发现通道必须是独立的、失败不记账的。同理适用于任何"设备会主动睡"的协议
（2.4G 鼠标/耳机、BLE 省电外设）。
