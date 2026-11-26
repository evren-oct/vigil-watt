# 运行时坑 · 桌面小组件

> 正文分册。索引与其余分册见 [runtime-pitfalls.md](../runtime-pitfalls.md)；**§N 是稳定编号**，代码与文档一律按 `runtime-pitfalls.md §N` 引用。

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
