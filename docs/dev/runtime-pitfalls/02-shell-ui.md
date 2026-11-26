# 运行时坑 · 窗口、页面与列表

> 正文分册。索引与其余分册见 [runtime-pitfalls.md](../runtime-pitfalls.md)；**§N 是稳定编号**，代码与文档一律按 `runtime-pitfalls.md §N` 引用。

## 4. XAML 事件在解析期间就会触发

ComboBox 设 `SelectedIndex="0"` → `SelectionChanged` 在 `InitializeComponent()` 内部、x:Name 字段尚未赋值时就会触发。**事件处理器开头必须判空**（StatsPage.RenderChart 的 NRE 即此因）。

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

## 25. 设备列表每次刷新整行闪一下：`ObservableCollection` 的 Replace 会重建容器（2026-11-07 实修）

**症状**：设备卡片在数据变化时"闪一下"——图标重画、文字重排。VT3 改 DPI 时最明显，实际上每次轮询
（`subText` 里的 `更新于 HH:mm:ss` 变了）所有行都在闪。

**根因**：`RefreshCore` 用内容指纹判断"变没变"，变了就 `_rows[i] = fresh[i]`。这是
`ObservableCollection` 的 **Replace** 通知，`ListView` 对它的处理是**回收旧容器 + 新建一个**，
整行重新渲染（`DeviceIconPresenter` 随之重画）。指纹机制还反过来放大了它：时间戳变化本来不需要重建行，
但它在指纹里，于是每拍全表重建。

**修法**：行模型改成可变 + `INotifyPropertyChanged`，`Update()` 原地写字段，只有真正变了的属性才发
通知；`_rows` 只在设备增删 / 换位时才动（`Insert` / `Move` / `Remove`），同实例同位置一律跳过。
XAML 的 `x:Bind` 全部显式 `Mode=OneWay`。Brush 也只在颜色变化时重建——`UiPalette.Brush` 每次都 `new`，
直接比 Brush 引用会永远不等，所以比的是 `Color`。

**规则**：

1. 增量刷新的 ItemsSource **优先原地改模型**，别替换元素——Replace 一定重建容器。
2. 判"要不要刷新"时**别把只用于展示的时间戳算进指纹**；原地更新后这个问题自然消失（改了就改一个 TextBlock）。
   桌面小组件走的是另一条路——它的元素树由代码构建、原地更新成本高于重建，所以保留"清空重建"，
   但先用内容指纹判一次"画出来会不会变"，没变就整块跳过（心跳 30 秒补推一次，不跳过等于每 30 秒重建一次）。
3. `x:Bind` 在 DataTemplate 里默认 `OneTime`，配可变模型必须显式写 `Mode=OneWay`，否则值变了界面不动。
4. 与 §22 的区别：§22 是"模型里存了 UIElement"，这里是"模型对象被替换"。两者都靠
   "行模型只放值、元素交给模板"这条活；§22 的教训顺带解释了为什么 Replace 特别贵。
5. **"值没变就不赋值"的哨兵不能和合法值撞**。原地更新靠"字段旧值 == 新值就 return"省通知，于是
   字段的初始值必须是一个**永远算不出来**的值。`PercentBrush` 用 `Color?` 存"主题前景色 / 状态色"，
   `null` 是合法值（正常态），而字段默认值也是 `null` → 首帧被判"没变化"、刷子停在构造函数那个灰色，
   **在线且正常的设备百分比一直是灰的**，直到它充一次电或掉到 20% 以下才自愈（2026-11-07 实修）。
   现在统一用透明色 `Unset` 当哨兵；新增这类字段时先问一句"它的默认值会不会被算出来"。
