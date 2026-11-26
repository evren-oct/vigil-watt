# 运行时坑

> **本页是索引，正文已按主题拆到 [runtime-pitfalls/](runtime-pitfalls/) 下的 7 个分册。**
> **§N 是稳定编号**：代码、BACKLOG 和其它文档一律写 `runtime-pitfalls.md §N`；改标题不要改序号。
> 下面保留每一节的一句话结论，方便按症状定位；细节点进分册。跨文档的症状速查在 `docs/README.md`。

## 目录（按主题）

| 分册 | 覆盖小节 |
| --- | --- |
| [01-hid-ble.md](runtime-pitfalls/01-hid-ble.md) | §1–3 |
| [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md) | §4、§6–11、§13、§18–19、§22、§25 |
| [03-tray.md](runtime-pitfalls/03-tray.md) | §5 |
| [04-widget.md](runtime-pitfalls/04-widget.md) | §14–15、§17 |
| [05-taskbar-pill.md](runtime-pitfalls/05-taskbar-pill.md) | §16 |
| [06-runtime-env.md](runtime-pitfalls/06-runtime-env.md) | §20–21 |
| [07-device-state.md](runtime-pitfalls/07-device-state.md) | §12、§23–24、§26–27 |

## 1. HID 读取必须用"单悬空读"模式（HidDeviceInfo.ReadInput）

每个句柄生命周期只挂一个 `ReadAsync`；超时后旧读继续挂着并复用，否则迟来响应被旧读吃掉。→ [01-hid-ble.md](runtime-pitfalls/01-hid-ble.md)

## 2. HID 其余小坑

`HidP_GetCaps` 成功码是 `0x00110000`；系统标准 collection 打不开是正常的；无输出通道的小接口必须排除；输出缓冲长度必须正好等于 `OutputReportLength`。→ [01-hid-ble.md](runtime-pitfalls/01-hid-ble.md)

## 3. BLE 电量属性名在本机 Windows 上不可用

任何 AEP 电量属性都拿不到，电量走 GATT 0x180F / 0x2A19；经典蓝牙耳机改读 PnP 节点的 `DEVPKEY_Device_BatteryLevel`。→ [01-hid-ble.md](runtime-pitfalls/01-hid-ble.md)

### 3.1 经典蓝牙设备类型：不能假设"经典蓝牙 = 耳机"（2026-10-09 实修）

`Kind` 不能硬编码，按设备类别 → 服务 UUID → 名字三级判定，猜不出就是 Unknown。

## 4. XAML 事件在解析期间就会触发

`SelectedIndex="0"` 会让 `SelectionChanged` 在 `InitializeComponent()` 内部、`x:Name` 字段尚未赋值时触发——事件处理器开头必须判空。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 5. 托盘：纯 Win32，别用 H.NotifyIcon

H.NotifyIcon 在 WinUI 3 代码创建场景下右键菜单点不动、退出失灵；已改用 `Shell_NotifyIconW` + 隐藏消息窗口 + `TrackPopupMenuEx`。→ [03-tray.md](runtime-pitfalls/03-tray.md)

### 5.1 托盘提示"永远显示暂无在线设备"——只渲染一次就再没刷新过（2026-10-06 实修）

`UpdateIcon()` 原先只在 `Initialize()` 和 `TaskbarCreated` 调用，启动那刻 `Manager.Devices` 还是空的，设备变化推不到托盘。

### 5.2 "程序没有图标"——非打包 WinUI 要两处都设（2026-10-06 实修）

工程里没有 `<ApplicationIcon>`、没有 `.ico`，PE 里压根没有 `RT_GROUP_ICON`；不是图标不好看，是根本没有。

## 6. DPI：AppWindow.Resize 是物理像素

XAML 按 DIP 布局，必须乘 `RasterizationScale` 再调 `Resize`；小组件还要 `WS_EX_TOOLWINDOW` 与就近显示器收边。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 7. WinAppSDK 1.7 API 名称

是 `DisplayAreaFallback.Nearest`；无边框用 `OverlappedPresenter.SetBorderAndTitleBar(false, false)`，没有 `IsBorderless` 属性。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 8. 外部程序/浏览器脚本调试的坑

`PrintWindow` 与 `GetWindowDC` 的区别、DPI 虚拟化坐标、HID 选择器弹窗自动化，见 `docs/hardware/debugging.md`。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 9. 历史页（StatsPage）切换"没反应"的两类原因（2026-10-05 实修）

图表轴自适应数据掩盖了时间窗切换；周期性重建下拉框又会打断用户操作并弹回选择。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 10. 前端美化实修的新坑（2026-10-05）

`x:Bind` 把 null 绑到 `Foreground` 会让文字整段不可见；横向 `StackPanel` 放在 `*` 列会顶掉后面的 Auto 列；`GetDpiForWindow` 要直接 P/Invoke；PowerShell 5.1 读无 BOM 脚本会把中文变量名煮成乱码。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 11. DispatcherQueue 回调里抛异常 = 进程静默消失（2026-10-06 实修）

`TryEnqueue` 回调里的异常不经过 `Application.UnhandledException`，直接在 combase fail-fast；回调体必须整体 `try/catch`。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 12. 休眠伪读数：电量突然变 1 或 0 → 误报低电量（2026-10-06 加固）

类型位 `0x05` 才是电量，`0x02` 是设备状态通知；链路里的三层合理性校验改协议时别绕过。→ [07-device-state.md](runtime-pitfalls/07-device-state.md)

## 13. Win10 与 Win11 的显示差异：Mica 不支持 → 双标题栏（2026-10-08 实修）

背景材质与标题栏融合不能绑在同一个 `MicaController.IsSupported()` 条件里，Win10 要分开回退。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 14. 桌面小组件在 Win10 上"平淡、圆角不圆润"（2026-10-08 实修）

Win10 没有 `DWMWA_WINDOW_CORNER_PREFERENCE`，且 `SetWindowRgn` 裁不动走 DirectComposition 的 WinUI 3 内容。→ [04-widget.md](runtime-pitfalls/04-widget.md)

## 15. 小组件"钉在桌面"只能靠 SetWindowPos(HWND_BOTTOM)，不能靠 IsAlwaysOnTop（2026-10-08）

`IsAlwaysOnTop` 是 bool，表达不了"沉到最底"；第三档必须走 `SetWindowPos`。→ [04-widget.md](runtime-pitfalls/04-widget.md)

## 16. 任务栏电量贴片：真嵌进任务栏（2026-10-10 建；**2026-10-12 改为 SetParent 方案**）

deskband 在 Win11 已移除、AppBar 会挤占屏幕；最终把原生分层子窗口 `SetParent` 进 `Shell_TrayWnd`。→ [05-taskbar-pill.md](runtime-pitfalls/05-taskbar-pill.md)

### 16.1 三道锁（Win11 任务栏是 XAML 架构，缺一不可）

`SetParent` 挂子窗口、`WS_EX_LAYERED` 获得独立合成面、逐像素 alpha 绘制，缺一就显示不出来。

### 16.2 不需要抢 Z 序（SetParent 的直接红利）

子窗口天然在父窗口之上，旧覆盖窗每 16ms 抢 topmost 的拍频与"点任务栏闪一下"一并消失。

### 16.3 绝不抢焦点

`WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW` 三条一起才够；代价是菜单的 light-dismiss 信号永远不会来（§18）。

### 16.4 贴片不画底框，靠分层窗口的逐像素 alpha

用户否掉了"白底圆角胶囊"：只有文字与设备图形浮在任务栏上，观感与任务栏图标一致。

### 16.5 拖动 = 沿任务栏方向的偏移

位置是"默认位 + offset"：横向只取 X、竖向只取 Y，且只有拖动中才反推 offset。

#### 16.5.1 拖动消息被 Win11 任务栏吃掉：轮询 fallback（2026-10-20 实修）

`SetCapture` 对 `WS_EX_NOACTIVATE` 窗口无效，拖动改 16ms 轮询驱动，松手靠 `GetAsyncKeyState` 判定。

#### 16.5.2 位置锁定与拖动阈值（2026-11-01 加）

单击手抖也会改位置并落盘；新增默认关闭的锁定开关 + 系统 `SM_CXDRAG` 阈值，锁定时停掉轮询。

### 16.6 本机电量恒定排除

判据 `d.Source != DeviceSource.System`——任务栏自己有电池图标，重复显示是噪音。

### 16.7 定位数学：屏幕坐标 → 任务栏客户区坐标

`TaskbarLayout.Place(...)` 给的是屏幕坐标，`ApplyPosition` 必须再走 `ScreenToClient` 换算。

### 16.8 显示配置变化：重新挂住 + 分层窗口重建

父窗口可能被 explorer 拆掉、DPI 也可能变；两拍一致才重建，顺带清扫残留窗口。

### 16.9 耳机头梁不要用 GDI+ DrawArc（2026-10-14）

`DrawArc` 两端不等高且偏左；改用两段对称三次贝塞尔，XAML 那份描边也要同步。

### 16.10 悬停设备名：自绘提示窗 + TME_HOVER（2026-10-15）

`ToolTip` / `TOOLTIPS_CLASS` 对跨进程分层子窗口都不稳，提示窗自己 `CreateWindowEx` 并用 `TaskbarTipRenderer` 绘制。

### 16.11 贴片尺寸必须每拍按当前缩放重算（2026-10-20）

换线后缩放变了而任务栏像素尺寸没变时，旧代码一直沿用旧尺寸；每拍按 `rect + dpi/96` 重算。

### 16.12 显示配置切换时贴片"多一个并重叠"（2026-10-26 修）

根因未在复现时抓死；做法是四条缺口全堵 + 自愈（连续 2 拍一致才重建、建窗后清残留、构造前收旧实例、`Dispose` 兜底）。

## 17. WinUI3 窗口的两层背景：小组件白底（2026-10-10 起；§17.1 于 10-12 更正）

贴片改原生子窗口后本节只对小组件适用；下面留着当年的实测过程，别再拿它去改贴片。→ [04-widget.md](runtime-pitfalls/04-widget.md)

### 17.1 根因：可见的是 **Win32 那一层**，不是内容岛（**该结论只在 Win11 上成立**）

哪一层可见必须像素级实测，不能靠资料推断。

### 17.2 通道实测表（Win11 26200.9445；accent 一行已在 Win10 上补测）

逐个通道本机试过；`SetWindowCompositionAttribute` 在 Win11 返回非 0 但完全无效。

### 17.3 "静默失效"到底有多静默

调用方从返回值上分辨不出来——"日志干净"不能当作"通道生效了"，只能靠肉眼/像素确认。

### 17.4 为什么不用 colorkey（虽然它是唯一直接见效的）

深色主题下贴片文字就是白色，键色会把文字一起挖掉。

### 17.5 采用的修法（`Services/TransparentWindowSurface.cs`）

让 DWM 改用窗口表面的逐像素 alpha 合成，再把 Win32 那层的背景清掉，两步缺一不可。

### 17.6 "点任务栏闪一下"（**已随贴片改子窗口消失**）

旧覆盖窗与任务栏同为 topmost 时的 Z 序空档；子窗口方案下不再存在。

### 17.7 未验证 / 已知边界

旧贴片的拖动手感、深色主题等条目已随方案切换失效，保留只为解释当年结论。

### 17.8 Win10 白框的真身是「内容岛那层的默认背板」；小组件另有 DWM 非客户区渲染（2026-10-12 实修）

Win10 19045 上实测：第一轮修错了层；白框来自内容岛默认背板，最右一格 `%` 被非客户区裁掉。

## 18. 贴片/小组件的右键菜单点别处不消失：WinUI 的 light-dismiss 靠"窗口去激活"（2026-10-12 实修）

`ContextFlyout` 靠宿主窗口 `Deactivated` 关闭；带 `WS_EX_NOACTIVATE` 的贴片从不激活，那个信号永远不来。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 19. WinUI3 页面缓存：设了 `NavigationCacheMode` 之后，「构造函数订阅 + `Unloaded` 解绑」会变成 bug（2026-10-12 实修）

页面实例被复用后构造函数只跑一次，切走时解绑、切回时订不回来；数据停在离开那一刻且不报错。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 20. 从 `bin` 启动后写盘全被拒、没有托盘图标：exe 继承了工作区的 Low 完整性标签（2026-10-20 定案）

工作区目录带 Low IL，构建出的 exe 继承后写不了用户目录、发不了 shell 消息；构建后 `icacls` 提到 Medium。→ [06-runtime-env.md](runtime-pitfalls/06-runtime-env.md)

## 21. 历史库时间戳带 +00:00 偏移，离线设备的「更新于」少 8 小时（2026-10-24 实修）

`DateTimeOffset.ToString("HH:mm:ss")` 按自身偏移渲染；六个读库路径统一走 `HistoryStore.LocalTime()`。→ [06-runtime-env.md](runtime-pitfalls/06-runtime-env.md)

## 22. 设备列表行把图标元素存进模型：行一移位就 `0xc000027b`（2026-10-25 实修）

行复用把同一个 `UIElement` 交给第二个 `ContentPresenter`；模型只存 `IconKind` / `IconColor`，由 presenter 现画。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 23. 设备睡醒后电量不刷新：退避档位只升不降 + 没有被动监听的插件享受不到 CR-53（2026-10-27 实修）

新增 `BreakerProbeInterval` 半开探活通道：熔断期按间隔发一帧核实，失败不记账不升档，成功即解除。→ [07-device-state.md](runtime-pitfalls/07-device-state.md)

### 23.1 哪些品牌该开、间隔定多少（适配者按实际设备判断，没有标准答案）

各厂商对"睡着的设备被戳一帧"容忍度不同，按四条口径判断；没有标准答案，建议 30 秒起步并实测。

## 24. 「组合识别」的判据写了却没生效：数据源没接上（2026-10-26 实修，CR-112）

`VgnDevicePairing` 靠 SN 但扫描器从不读、ContainerId 本机读不到——两套归一判据都没接上数据源。→ [07-device-state.md](runtime-pitfalls/07-device-state.md)

## 25. 设备列表每次刷新整行闪一下：`ObservableCollection` 的 Replace 会重建容器（2026-11-07 实修）

指纹里带着"更新于"导致每次轮询全表重建；行模型改成可变 + `INotifyPropertyChanged` 原地更新。→ [02-shell-ui.md](runtime-pitfalls/02-shell-ui.md)

## 26. 隐藏到托盘后「内存」不回落：WinUI 3 只摘内容树，私有页仍驻留（2026-11-25 实修）

任务管理器看的是专用工作集，要等系统内存压力才回落；`SetProcessWorkingSetSizeEx` 只裁驻留页、不改提交。→ [07-device-state.md](runtime-pitfalls/07-device-state.md)

## 27. 拆掉页面缓存反而更涨：重建出来的合成器资源不回收（2026-11-25 实测，**结论是别拆**）

第一轮持平、重复访问每轮 +28MB 且不回收，手感也没变好；`NavigationCacheMode.Required` 保持不动。→ [07-device-state.md](runtime-pitfalls/07-device-state.md)
