# 运行时坑 · 任务栏电量贴片

> 正文分册。索引与其余分册见 [runtime-pitfalls.md](../runtime-pitfalls.md)；**§N 是稳定编号**，代码与文档一律按 `runtime-pitfalls.md §N` 引用。

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

#### 16.5.2 位置锁定与拖动阈值（2026-11-01 加）

轮询 fallback 把"按下"的判据放宽到了"光标在矩形内 + 左键按下"，于是**单击也算拖动起点**：
想双击开窗口、想看悬浮提示，手抖 1px 位置就被改掉并落盘，用户看到的是"贴片随时会跑、锁不住"。
两道闸：

- **阈值（默认生效）**：位移不到 `DragThresholdPx` 不写 `_offsetPx`。这个值读系统
  `SM_CXDRAG`/`SM_CYDRAG`（横向任务栏取 CX、竖向取 CY，Windows 默认 4px），读不到才用 4 DIP
  按缩放兜底——写死 DIP 在高缩放屏上会和系统其它拖动判定不一致。轮询路径与 `WM_MOUSEMOVE`
  路径共用同一个属性，否则消息到达时会被绕过。
- **锁定（默认关闭）**：`AppSettings.TaskbarBatteryLockPosition` 默认 `false`，与升级前行为一致，
  要彻底固定再自己打开。锁定时 `OnLButtonDown` 直接返回，并且**把轮询表停掉**（`SyncDragTimer`）
  ——常驻 20Hz 读全局按键只为支持拖动，锁住就没必要跑。解锁时重新起表，间隔回到 `DragIdleTickMs`。

两个入口都走 `TaskbarPillManager.SetLockPosition`（唯一落盘点）：贴片右键菜单的可勾选项
（`AppPopupMenu.Show` 的可选参数，小组件不传、菜单不变）与设置页开关（`Toggled` 即时生效，
程序化赋值也会触发该事件，靠"值没变就返回"兜住）。贴片关着时 Manager 为 `null`，
所以 `Save_Click` 里还补写一次，免得开关显示与实际相反。

**真机 A/B**（合成 `mouse_event` 拖动，贴片矩形由 `FindWindowExW(Shell_TrayWnd, …)` 取）：
锁定拖 60/120/150/180px → offset 一律不变、日志无"拖动结束"；解锁拖 160px → 正好位移 160px；
解锁拖 2px/3px → 不变；解锁拖 5px/8px → 位移 5px/8px。右键菜单与设置页开关点击后 `settings.json` 立即翻转。

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
所以修法是"堵死四个缺口 + 自愈"，不是单点定因。建窗与清残留都会记日志，下次复现能自证。见已归档 CR-107（[BACKLOG-CLOSED.md](../../reviews/archive/BACKLOG-CLOSED.md)）。
