# 审查报告复核与优化路线图（2026-10-06）

> 复核对象：`docs/reviews/archive/code-review-2026-10-06.md`（下称"原报告"）。
> 基线：同为 `main` / `ddd3b39`。
> 性质：**只读静态复核**——未启动应用、未向任何 HID 设备发帧、未强杀进程。
> 与原报告一致：*"代码路径已核实"不等于"后果已在真机复现"*；涉及硬件后果的推断均已单独标注。
> 本文件不修改原报告，两份并列便于对照执行进度。

## 0. 复核方法与边界

- 对原报告全部 18 条论断逐条回到源码核对，行号以复核时实际读到的为准（原报告行号基本准确，少数偏差已在下文标注）。
- 额外做了两件原报告未覆盖的事：① **全仓 `WriteOutput` 调用点穷举**，查是否有遗漏的写入路径；
  ② 把分散的缺陷**按合成后果串联**，看单点之外的实际损害。
- 未做：真机验证、性能压测、SQLite 数据量的实测统计。凡属推断的结论，均在第 6 节单独列出。

## 1. 总体判断

**原报告质量高，18 条论断绝大多数属实，定级基本合理，可以直接作为整改依据。**

做得特别好的三点：

1. **主动声明验证边界**——明确写出"只跑通了构建与单测，67/67 通过不能证明跨模块路径安全"，没有把静态审查包装成实测结论。
2. **对 Elan 那条主动区分了 `GetFeature` 读取与 `WriteOutput` 写入**（§1.3），没有为凑 P0 而夸大定级。这一点很多审查会做错。
3. **给正确参照系**——指出 `BluetoothClassicWatcher` 的可空连接判定、`DevicesPage`/`StatsPage` 的解绑写法可作参考，
   整改时不至于重新发明。

需要修正的只有 3 处（第 3 节），但**遗漏了一类重要问题**（第 4 节），其中"罗技无写入闸门"这条按影响面看应补进 P0。

## 2. 复核确认属实的部分（可直接采信）

| 原报告条目 | 复核结论 | 关键证据 |
|---|---|---|
| §1.3 Elan 默认枚举 Feature ID | **属实**，且"是 GetFeature 不是写入"判断准确 | `ElanHeadsetPlugin.cs:35` `FeatureSweep(handle,16)` → `HidWriteProbe.cs:71-79` `GetFeature` → `HidDeviceInfo.cs:71-79` `HidD_GetFeature`；全仓 `SetFeature` 无调用点。规则 7（`safety.md:33`）确对得上 |
| §2.1 共享可变 `DeviceStatus` | **属实**，两个后果均成立 | `DeviceStatus.cs:19` 普通可变 class；`DeviceManager.cs:287` `GetOrAdd(id, _ => incoming)` 存本体；`BatteryPluginBase.cs:275-284` 先原地改再上报 → `:297` `changed` 恒假、`:295` `previousPercent` 已是新值 → `:328` 骤降保护永不成立 |
| §2.2 BLE 三条相反路径 | **三条全部属实** | `BleBatteryWatcher.cs:78` `bool connected = false;`（默认假）；`:119` `IsOnline = true` 无条件且在 `:93` 分支之后；`:69` `Removed` 只 `TryRemove` 不发离线。对照 `BluetoothClassicWatcher.cs:80` `bool? connected` + `:99-109` 顺序正确 |
| §2.3 插件禁用后设备不下线 | **属实** | `DeviceManager.cs:222-227` 只 `Detach()` + `ReleaseAll`；`DeviceStatus` 无 owner 字段；托盘 `TrayService.cs:149/252`、小组件 `WidgetWindow.xaml.cs:85` 继续读 `Manager.Devices` |
| §2.4 历史清理只在保存设置时 | **属实** | `CleanupHistoryNow` 全仓唯一调用点 `SettingsPage.xaml.cs:115`；`Start()`/`StartTimers()`（`DeviceManager.cs:89-111`）均不调用；UI 文案 `SettingsPage.xaml:113` 确写"自动清理" |
| §3.1 设置页缺写入许可控件 | **属实**（且不止迈从） | 无控件的字段：`MchoseProbingEnabled`、`BetopProbingEnabled`、`WidgetX/Y`。`MchosePlugin.cs:319` `if (!ProbingEnabled) return null;` 导致"开了品牌开关但完全静默" |
| §3.2 罗技两条卡死路径 | **属实** | `LogitechPlugin.cs:77` `session.Io == null && !session.OpenFailed`，两个 catch 均置 `OpenFailed=true`；`DiscoverDevices` 只在 `:84`（新建会话块内）调用一次；`HidppIO.cs` 无任何重连/冷却 |
| §3.3 小组件匿名订阅未解绑 | **属实** | `WidgetWindow.xaml.cs:58-59` 两个捕获 `this` 的匿名 lambda，全文件无解绑；关闭路径 `SettingsPage.xaml.cs:108-112` 仅 `Close()` + 置空 |

## 3. 需要修正的三处

### 3.1 §1.1 北通：属实，但**低估了频率**（最重要的一处修正）

原报告说"首次扫描仍可能写一帧"。**实际是每轮、每个候选接口都写一帧，且持续不断。**

- 路径确认无闸门：`Attach` → `RunProbe`（`BatteryPluginBase.cs:151-157`）→ `Probe`（`BetopPlugin.cs:94-100`）
  → `Query`（`:133`）→ **`WriteOutput`（`:143`）**。`CanWrite` 全仓唯一调用点是 `PollDevice` 首行（`:105`），
  探测路径上没有任何许可检查。
- **为什么不是"只有首次"**：`ProbeCooldown = 30s`（`:70`）< 60s 轮询周期，探测失败后下一轮必然重探、必然再写。
  即 1 帧 / 接口 / 分钟，持续。
- **加重情节**：`BetopPlugin.cs:61` 与 `AppSettings.cs:42` 的注释明确承诺
  *"关掉即完全静默（不认领、不写入）"*——注释与行为**直接矛盾**。这不只是逻辑错误，是文档在误导后续维护者，
  整改时必须一并修正注释，否则下一个人还会照着注释相信它是安全的。

### 3.2 §1.2 迈从：部分属实，**原报告把轻重说反了**

- 筛选条件如原报告所述（`MchosePlugin.cs:38-51` 仅排除三个 VID，无 VID/PID 白名单）。
- 但**迈从的 gating 写在写入点**：`MchosePlugin.cs:223`（未许可只被动 `ListenCzPush` 后 return null）、
  `:319`（`if (!ProbingEnabled) return null;`）——比北通**严格得多**，且默认关闭。
- 建议：**§1.1 与 §1.2 的优先级对调**。真正"默认开启 + 无闸门"的是北通和罗技，不是迈从。
  迈从的风险只在用户显式开启后才存在，属"用户启用后的条件风险"，原报告这句判断本身是对的。

### 3.3 §3.4 文档漂移：两处小偏差

- 测试数"65 vs 67"**不是单纯过时**：`README.md:23` 已写 67（正确），`:70` 仍写 65——**同一文件自相矛盾**。
- `docs/project/code-flow.md` 是**准确的**（第 9-10 行已写"三个定时器"、300ms 首跑、第 22 行 SemaphoreSlim），
  不应与 `architecture.md` 并列归为漂移。真正漂移的只有 `architecture.md:19`（称 `HidWriteProbe` 已退役，
  但 Elan 仍在调用）和 `:84`（Timer ×2 + SafePoll + Interlocked，实为 3 个 Timer + Task/信号量）。
  另有 `DeviceClaimRegistry.cs:7` 残留注释仍提 `DeviceManager.SafePoll`。

## 4. 原报告遗漏的部分（本次复核的主要增量）

### 4.1 遗漏一（最严重）：`LogitechPlugin` 完全没有写入闸门，且默认开启

原报告 §3.2 只谈了罗技的"重试"问题，**没有把它识别为写入路径**。这是穷举 `WriteOutput` 调用点时发现的。

- **`LogitechPlugin` 直接实现 `IBatteryPlugin`，不继承 `BatteryPluginBase`** → 因此没有 `WritesEnabled`、
  没有 `CanWrite`、没有熔断退避，绕过了基类全部安全机制。
- `HidppIO.cs:58` **无条件** `WriteOutput`；`LogitechPlugin.cs:175-179` 连续 3 次失败**只标离线、永不停止写入**
  → 违反 `safety.md:29` 规则 3。
- **默认 `PluginEnabled["Logitech"] = true`**（`AppSettings.cs:31`）——与北通一样是"默认开启"。
- 写入规模（代码推断，未实测）：`DiscoverDevices` 对 devIdx 1..6 + 0xFF 各一次 Ping（≤7 帧），
  `ReadDeviceName` 每台再 1+≤32 帧，之后每分钟轮询继续写。

**但要做公允判断，不应与北通并列成同等级 P0**：HID++ ping / feature 0x1004 / 0x1000 是**有出处的标准协议帧**，
不是盲扫也不是字节猜测，符合 `safety.md` 规则 1。所以它缺的是**规则 2（统一开关）和规则 3（熔断退避）**，
是**合规缺口**，性质上不等同于迈从那种"未知协议试探"。整改目标是补齐闸门，不是禁用它。

**附带发现——规则文档本身已脱节**：`safety.md:28` 规则 2 原文写"一切写入受 `MchoseProbingEnabled` 开关控制"。
但北通走 `BetopProbingEnabled`、罗技无任何开关 → **这条规则已经描述不了现状**，必须改写，否则它保护不了任何东西。

已确认**无遗漏**的写入路径：`RapooPlugin.cs:53` `WritesEnabled=false` 纯 `ReadInput`；Elan 仅 GetFeature；
BLE 侧无 HID 写入；`Betop.SupportsFastDiscovery` 默认 false（15 秒节拍不会放大写入）。
另：`HidWriteProbe.Run:83-109`（WriteOutput 变体矩阵）及 `PingVariants`/`MchoseClassicVariants` 当前无调用点（死代码），
但仍是潜在写入路径，建议随 Elan 整改一并移出 Core。

### 4.2 遗漏二：§2.1 少说了两个后果

- **新设备首轮也漏写**：`isNew` 时 `changed=false` → `DeviceManager.cs:308` 的 `Insert` 不执行，
  首条样本要等 1 分钟后的 `SafeSample` 才补上。
- **`_lastNotifiedPercent` 永不复位**（`:336-342`）：在 10% 提醒过一次后 `lastSlot=10`，
  此后充满再放电到 12% / 10% 都**不会再提醒**（只有低于历史最低值才触发）→ 低电量提醒实际"一次性失效"。
  这是个用户可感知的功能缺陷，原报告未提。

### 4.3 遗漏三：三个缺陷的合成故障——`history.db` 无界增长

原报告分别点到了 BLE `Removed` 不发布离线、历史清理只在保存设置时执行，**但没有串联起来**：

```
_devices 无 Remove 路径
  + BLE Removed 不发布离线        → 幽灵设备永久 IsOnline=true
  + SafeSample 每分钟插一行       → 幽灵设备持续产生历史行
  + 超期记录只在手动保存设置时清理 → 从不自行过期
  = history.db 无界增长
```

这是三个单点缺陷叠加后的**实际长期损害**，比任何单点都更值得优先处理。
（规模为推断：默认 5 分钟采样 × 90 天 ≈ 单设备 2.6 万行；幽灵设备按每分钟 1 行计，约 1440 行/天。）

### 4.4 遗漏四：性能与生命周期（原报告几乎没覆盖）

| 问题 | 证据 |
|---|---|
| **UI 线程同步跑 SQLite 全表统计** | `HistoryPage.xaml.cs:32-33` `GetStats()` + `GetDeviceStats()`、`StatsPage.xaml.cs:96` `History.Query()` 直接在 UI 线程执行，且与后台 `Insert` 共用 `HistoryStore._writeGate`（`HistoryStore.cs:19`）→ UI 会阻塞在写锁上。多设备下卡顿可感知 |
| 未开 WAL、每条 Insert 一次隐式事务 | `HistoryStore.cs:23-29`；读写互斥被放大 |
| `SafeSample` 无去重/节流 | `DeviceManager.cs:271-275` 只要在线且有电量就插，与电量是否变化无关 |
| 跨线程共享可变对象且无锁 | `SafeSample`（采样线程）读 `d.IsOnline`/`d.BatteryPercent` 的同时 `Upsert`（轮询线程）在写同一实例；`int?` 为 8 字节结构体，读取非原子 → 存在撕裂读出垃圾电量的可能。`ConcurrentDictionary` 只保证字典安全，不保证对象字段安全 |
| `Upsert` 漏拷字段 | `:299-303` 只拷 `Name/BatteryPercent/Charging/LastUpdated/IsOnline`，**漏 `Kind` 与 `Protocol`**，来源后续更新设备类型时管理器保留旧值 |
| **退出路径资源从不释放** | `TrayService.cs:292-297` 只 Dispose 托盘后 `Environment.Exit(0)`，**`AppServices.Dispose()` 从未被调用**（Manager / History / 全部 HID 句柄 / 3 个 Timer）。`MainWindow.xaml.cs:38-42` 关窗只 `e.Cancel = true` 隐藏，无退出钩子 |
| 小组件刷新无合并 | `WidgetWindow.RefreshContentCore` 每次事件都 `Clear()` 后全量重建；`TrayService.RequestUpdate`（`:58-60`）已用 `Interlocked` 做合并，Widget 缺同款节流 |
| 全局异常被吞 | `App.xaml.cs:59-63` `UnhandledException` 一律 `e.Handled = true`，应用继续带病运行 |
| BLE 串行 await 拖长整轮 | `BleBatteryWatcher.PollConnectedAsync`（`:185-197`）串行，每设备最多 8s×3；`DeviceManager.cs:243` 在持有 `_passGate` 时 await |

## 5. 优化路线图

用户已确认四块全做。设计原则：**个人自用工具，克制功能范围**——不引入 DI 容器 / MediatR / 重型框架，
优先"最小改动消除根因"。**每阶段结束应用均可正常构建运行**，阶段 1 与 2 可并行开发（仅在 `Upsert` 几行有交叉）。

### 块 1 · 统一写入闸门（P0）

**核心思路**：把"能不能写"收敛到一个可测试的纯逻辑内核，让所有插件共用——包括不继承基类的罗技。

1. **新增 `src/VigilWatt.Core/Plugins/WriteBudget.cs`**（纯逻辑、不碰 HID，可直接链进 net8.0 测试项目）：
   ```csharp
   public sealed class WriteBudget {
       public WriteBudget(int maxFailures = 3, TimeSpan? backoff = null);
       public bool IsEnabled { get; set; }                      // 插件总开关
       public bool TryAcquire(DateTimeOffset now, out string reason);
       public void RecordSuccess();                             // 清零连续失败
       public void RecordFailure(DateTimeOffset now);           // 连 3 败 → 停写 10 分钟退避一轮
       public bool IsDisabled(DateTimeOffset now);
       public int SuppressedCount { get; }                      // 被挡下次数，测试断言用
   }
   ```
   `BatteryPluginBase.CanWrite(dev)` 改为委托给 `dev.Budget.TryAcquire`，取代 `WritesDisabled/WritesDisabledAt`。

2. **新增 `Plugins/WritePolicy.cs`**：解决 `AppSettings.Load()` 是进程级缓存（`AppSettings.cs:56`）导致测试无法改开关的问题。
   生产环境读 AppSettings，测试可直接替换 `WritePolicy.Default`。

3. **北通探测路径**：`Probe` 抽象签名加 `WriteBudget` 参数，`RunProbe` 内临时建 budget，`Query` 在 `WriteOutput` 前 `TryAcquire`。
   **许可关闭时的正确行为＝降级为"只监听不写"**：不发帧，仅在 2s 窗口内 `ReadInput` 解析 T9 自发状态流；
   解析到就认领并置在线，解析不到返回 null → 基类释放认领、设备不进列表。
   这样才真正兑现了注释里"关掉即完全静默"的承诺（与迈从只读分支同套路）。

4. **罗技**：`DevEntry` 增 budget；`HidppIO` 加**兜底闸门**（唯一写点 `:58`，构造注入 `Func<bool>? writeAllowed`）。
   注意：被闸门挡下**不得计入 `ConsecutiveFailures`**（否则会误标离线），只计 `SuppressedCount`。
   建议新增 `LogitechWritesEnabled`（**默认 true**）：① 消除 `safety.md` 规则 2 的唯一例外；
   ② 默认 true = 保持现状，不打断任何用户，且 `AppSettings.Load()` 已有缺键补默认值逻辑（`AppSettings.cs:82-86`），
   老 `settings.json` 自动补 true；③ 设置页已有按字典生成开关的先例，UI 成本极低。

5. **伪 HID 后端**：`HidDeviceHandle` 目前 sealed + internal 构造。抽出 `IHidBackend` 接口 + 内部构造重载
   （保留现有 FileStream 实现），测试侧提供 `FakeHidBackend`（暴露 `WriteCount`、可脚本化响应）。
   退路：`HidDeviceInfo.Open()` 加可选 factory + `InternalsVisibleTo`。

6. **改写 `safety.md` 规则 2**：判定入口唯一为 `WriteBudget.TryAcquire`；逐条列出迈从/北通/罗技三个开关及其默认值；
   明确"新插件必须显式声明 `WritesEnabled`，禁止沿用基类默认 true"；
   明确"许可关闭时只允许被动监听（ReadInput / GetFeature）或直接跳过"。

**验收**：`dotnet test` 全绿；**三家（Betop / Logitech Discover+Poll / Mchose）在许可关闭时断言 `WriteCount == 0`**；
真机验证须用户知情后另行安排，且不动"关闭会话"类帧。

### 块 2 · 状态模型（P0）

**核心思路**：不改 `DeviceStatus` 为不可变——UI 层（托盘、小组件、DevicesPage）直接消费可变实例，
改 record 会牵动整条绑定链。只在 Manager 侧断开别名。

1. `Models/DeviceStatus.cs` 加 `Snapshot()` / `CopyFrom()`（全字段，含 `Kind`/`Protocol`/`Source`）。
   `DeviceManager.Upsert` 三行改动：
   ```csharp
   bool isNew = !_devices.ContainsKey(incoming.Id);
   var status = _devices.GetOrAdd(incoming.Id, _ => incoming.Snapshot());  // 不再存本体
   int? previousPercent = isNew ? null : status.BatteryPercent;            // 现在是真正的旧值
   bool changed = status.BatteryPercent != incoming.BatteryPercent || status.IsOnline != incoming.IsOnline;
   status.CopyFrom(incoming);                                              // 补上漏拷的 Kind/Protocol
   ```
   → `changed` 判定、骤降保护（`prev - percent > 20`）随之自动恢复正确。

2. 新增 `_stateGate` 锁，`Upsert` / `SafeSample` / `Devices` / `Find` 全部进锁；`Devices` 返回快照列表。
   → 顺带消除 `int?` 撕裂读取。

3. `_devices` 的值改跟踪壳：`Tracked { Status, OwnerPlugin, LastSampleAt }`。
   所有来源（插件 Attach/Poll、BLE、Classic、SafeSample）收敛到同一个 `Commit(incoming, owner)`（锁内）：
   比对 → `CopyFrom` → 判历史 → 发事件 → 低电量检查。新建 / 变化 / 离线恢复只是 `changed` 取值差异。
   历史判据合并为一条：
   ```csharp
   bool due = changed || now - t.LastSampleAt >= TimeSpan.FromMinutes(_settings.SampleIntervalMinutes);
   if (due && status.BatteryPercent.HasValue) { _history.Insert(status.Snapshot()); t.LastSampleAt = now; }
   ```
   → 行数降到约 1/5，曲线连续性不受影响（间隔仍是用户设定的采样间隔）。

4. **确定的下线事件**：插件禁用时在 `Detach()` **之前**先按 `OwnerPlugin` 把设备置离线并发 `DeviceUpdated`/`DevicesChanged`；
   BLE `Removed` 置离线后发 `DeviceUpdated`。`_devices` **不硬删**——离线设备作墓碑保留
   （历史页消费的是 `HistoryStore` 按 DeviceId 聚合的 `DeviceHistoryStat`，不依赖 `_devices`）；
   仅在"离线超 30 天且历史中无记录"时由清理任务一并移除。

5. **可测性**：把 `Commit` 的纯逻辑抽成 `Devices/DeviceStateTracker.cs`（只依赖 `DeviceStatus` + `IHistorySink`），
   `DeviceManager` 只剩调度与事件转发。否则 DeviceManager 因依赖 `BleBatteryWatcher`（WinRT）链不进 net8.0 测试项目。

**验收**：单测覆盖"电量变点写一行 / 新设备首轮写一行 / 骤降 >20 暂缓 / 跨阈值可重复提醒 / 节流后行数符合预期"；
真机观察 `history.db` 日增速降到约 1/5，插件禁用后设备即时显示离线。

### 块 3 · 性能与生命周期（P1）

- `HistoryStore` 构造后执行 `PRAGMA journal_mode=WAL; synchronous=NORMAL; busy_timeout=3000`；
  新增 `Storage/SerialBackgroundQueue.cs`（`Channel<Action>` + 单消费者 + `DrainAsync()`），
  `Insert` / `CleanupOlderThan` 走队列，UI 查询仍同步直读（WAL 下不阻塞）。
- **保留策略自动执行**：`StartTimers()` 加第 4 个 Timer（启动后 30s + 每 6h）跑 `CleanupHistoryNow()`；
  `SettingsPage.xaml.cs:115` 的保存动作改 `await Task.Run(...)`。
- **UI 异步化**：`HistoryPage.xaml.cs:32-33`、`StatsPage.xaml.cs:96` 改 `await Task.Run(...)`。
  WinUI 有 SynchronizationContext，await 后直接赋值即可；`DispatcherQueue.TryEnqueue` 只在后台线程回调里用。
- **退出释放**：`Environment.Exit(0)` 前调 `App.Services.Dispose()`，加 `ProcessExit` 兜底 + `_disposed` 幂等。
  顺序：停 Timer → 插件 Detach（关 HID 句柄，避免"会话占死"重演）→ `History.Dispose()`（Drain 队列）。
- **小组件**：匿名 lambda 改命名方法 + `Unloaded` 解绑（照抄 `DevicesPage.xaml.cs:18-24`）；
  刷新复用 `TrayService.RequestUpdate` 的 `Interlocked` 合并；Chip 按 Id 复用、只改文本。
- `App.xaml.cs:59-63`：保留 `Handled=true`，但加计数器，连续超 20 次后不再 Handled（避免静默错乱）。
- `BleBatteryWatcher.PollConnectedAsync`：`Task.WhenAll` + `SemaphoreSlim(4)`；`_passGate.WaitAsync` 加 30s 超时。

### 块 4 · 文档与测试（P2）

**文档同步**（含修正原报告指出的漂移 + 本次复核新增）：
- `safety.md`：规则 2 改写（见块 1.6）、规则 7 补"Elan FeatureSweep 不得留在正常路径"条目。
- **修正失实注释**：`BetopPlugin.cs:61`、`AppSettings.cs:42` 的"关掉即完全静默"——改到与行为一致为止。
- `README.md:70` 65→67；`architecture.md:19`（`HidWriteProbe` 已退役 → 仍被 Elan 调用）、`:84`（Timer ×2 → ×3）；
  `README.md:35` 雷柏"实验性、需抓包校准"（`handoff.md:18` 已记实测完成）；`DeviceClaimRegistry.cs:7` 残留注释。
- `protocols.md` 补"新插件写入许可契约"；`debugging.md` 补幽灵设备与 `history.db` 增长排查。

**测试清单**（测试项目是 `<Compile Include>` **链接 Core 源码**，新增纯逻辑文件即可纳入）：
1. `WriteBudgetTests`：连 3 败后 `TryAcquire=false`、10min 后恰好重试一轮、`IsEnabled=false` 时 0 次、成功清零。
2. `WriteGateTests`（伪 HID）：Betop / Logitech(Discover+Poll) / Mchose 许可关闭时 `fake.WriteCount == 0`；
   罗技连败 3 次后 `WriteCount` 不再增长。
3. `DeviceStatusTests`：`Snapshot`/`CopyFrom` 字段全量（防再漏拷 `Kind`/`Protocol`）。
4. `DeviceStateTrackerTests`：变点写一行、首轮写一行、骤降暂缓、跨阈值可重复提醒、节流后行数。
5. `HistoryStoreTests` 补：WAL 已开、并发 Insert 不抛、Cleanup 行数正确。

### 执行顺序

| 阶段 | 内容 | 交付标准 | 主要文件 |
|---|---|---|---|
| **1（P0）** | WriteBudget + WritePolicy + 北通 Probe 闸门 + 罗技熔断与开关 + HidppIO 兜底 + 伪 HID 后端 | `dotnet test` 全绿；三家许可关闭时 `WriteCount == 0` | 新增 `Plugins/WriteBudget.cs`、`Plugins/WritePolicy.cs`、`Hid/FakeHidBackend`（测试侧）；改 `BatteryPluginBase.cs`、`BetopPlugin.cs`、`LogitechPlugin.cs`、`HidppIO.cs`、`AppSettings.cs`、`Hid/HidDeviceInfo.cs`、`safety.md`、测试 csproj |
| **2（P0）** | 快照副本 + `_stateGate` + OwnerPlugin + 确定下线 + 采样节流 + DeviceStateTracker | 单测覆盖历史写入/骤降保护/低电量重复触发；`history.db` 日增速降到约 1/5 | `Models/DeviceStatus.cs`、`Devices/DeviceManager.cs`、新增 `Devices/DeviceStateTracker.cs`、`Ble/BleBatteryWatcher.cs` |
| **3（P1）** | WAL + 后台写队列 + 保留策略定时 + UI 异步 + 退出 Dispose + 小组件解绑/节流 | 历史/统计页 <200ms；退出日志出现插件卸载；生成 `history.db-wal` | `Storage/HistoryStore.cs`、新增 `Storage/SerialBackgroundQueue.cs`、`Views/HistoryPage.xaml.cs`、`Views/StatsPage.xaml.cs`、`Services/TrayService.cs`、`App.xaml.cs`、`WidgetWindow.xaml.cs` |
| **4（P2）** | Elan FeatureSweep 移出正常路径 + BLE `bool? connected` 对齐经典版 + PollConnectedAsync 并发化 + 文档同步 | 日志不再每 5 分钟 32 次 GetFeature；BLE 稀疏更新不再误判离线 | `Plugins/Elan/ElanHeadsetPlugin.cs`、`Ble/BleBatteryWatcher.cs`、`docs/**` |

## 6. 复核未能证实的部分（诚实标注）

以下内容**基于代码路径推断，未经真机或实测验证**，不应据此下结论：

1. **北通"每轮都写"**——由 `ProbeCooldown(30s) < 轮询周期(60s)` 推出，未实测写入次数。
   验证方式：把 `BetopProbingEnabled=false` 后观察 `app.log` 是否仍出现北通 `WriteOutput` 记录。
2. **罗技写入规模**（≤7 帧发现 + 每台 ≤32 帧读名 + 每分钟轮询）——由代码路径累加得出，未计数。
3. **`history.db` 无界增长**——三个缺陷的合成推断，未统计实际库大小与日增速。
4. **UI 卡顿**（历史/统计页）——由"UI 线程同步全表扫描 + 共用写锁 + 2.6 万行量级"推出，未做性能测量。
5. **`int?` 撕裂读取**——理论风险（8 字节结构体非原子读），未在真机复现过读出垃圾电量。
6. **原报告 §1.1 的"首次写一帧"与本次复核的"每轮都写"**——两者都需要真机日志才能最终判定频率。

验证 1 与 3 的成本最低、收益最大，建议阶段 1 / 阶段 2 收尾时顺手做掉。
