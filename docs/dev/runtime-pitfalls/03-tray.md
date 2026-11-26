# 运行时坑 · 托盘

> 正文分册。索引与其余分册见 [runtime-pitfalls.md](../runtime-pitfalls.md)；**§N 是稳定编号**，代码与文档一律按 `runtime-pitfalls.md §N` 引用。

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
