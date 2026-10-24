# 代码审查标准与流程

> **一句话**：改动分三层过闸——机械层（自动）、自查清单（改的人自己过）、专家审查（按触发执行）；
> 审查发现必须进 `docs/reviews/BACKLOG.md` 走完闭环，**不允许只写在报告里**。
>
> 立此文档的原因：2026-10-06 做过两份高质量全仓审查（存于 `docs/reviews/archive/`），并给出了四阶段路线图。
> 报告归档后没有跟踪载体。2026-10-10 逐条核对，其中「罗技无写入闸门」「北通探测路径无闸门」
> 「共享可变 `DeviceStatus` 导致 `changed` 恒假」等 P0 **全部仍然成立**，而路线图里设计的
> `WriteBudget` / `DeviceStateTracker` / `SerialBackgroundQueue` 三个文件从未被创建。
> **审查做了，问题没关**——这是本文档要解决的核心问题，不是"再写一份规范"。
>
> 与 `docs/dev/comment-style.md` 的分工：那份管**注释**，本文管**代码本身与审查动作**。

## 1. 严重级

| 级别 | 判据 | 处置 |
|---|---|---|
| **P0** | 硬件误写 / 数据损坏（`history.db`、`settings.json`）/ **累积型**资源泄漏（运行期反复发生、不释放会越积越多）/ 安全红线 | 立即修，或在 BACKLOG 里写明暂缓理由 |
| **P1** | 用户可感知的功能错误、崩溃、界面长期不刷新、**退出型**资源未释放（只在进程终止路径发生，OS 会回收句柄、不累积） | 排进最近一轮 |
| **P2** | 可维护性：重复实现、死代码、文件过长、注释失实 | 攒批处理，不阻塞 |

定级只按**后果**，不按"改起来麻不麻烦"。

> **"资源泄漏"要分两类**（2026-10-10 修订，起因是 CR-07/CR-09 定级时发现的字面张力）：
> - **累积型 = P0**：反复触发、每次泄漏一点（如小组件反复开关累积事件订阅，CR-09）——最终会耗尽资源。
> - **退出型 = P1**：只在退出路径发生（如 `Environment.Exit` 前没调 `Dispose`，CR-07）——进程一结束 OS 就回收，
>   不累积，后果是"非确定性释放"而非"泄漏"。**但 R4 仍要求走 `Dispose`**：HID 句柄不释放会把 dongle 会话占死，
>   那是硬件侧后果，与内存泄漏是两回事。

## 2. R 系列 · 红线（违反即不可合）

| 编号 | 红线 | 为什么（不写会踩什么坑） | 怎么自查 |
|---|---|---|---|
| **R1** | 任何 `WriteOutput` / `SetFeature` 调用点必须能被一个开关挡住，且新插件默认关闭 | 硬件写入是用户两次事故换来的最高优先级约束（`docs/hardware/safety.md`） | 全仓 grep 写入调用点，逐个确认有闸门 |
| **R2** | 插件必须继承 `BatteryPluginBase`，禁止直接实现 `IBatteryPlugin` | 基类承载熔断、退避、认领释放；绕过它 = 绕过全部安全机制。历史上唯一一次"无闸门写入"就是绕过基类造成的 | grep `: IBatteryPlugin` |

> **R2 的唯一例外（2026-10-10 明确）**：会话型插件可以**直接实现 `IBatteryPlugin`**，前提是它**逐条自建**了基类提供的那几样东西，
> 并在类头写明为什么不能复用基类。当前唯一一例是 `Plugins/Logitech/LogitechPlugin.cs`（HID++ 需要长持有会话 + 自发通知泵，
> 与基类"一台设备一个句柄 + 轮询"的模型不合）：它自建了 `HidppIO.WritesAllowed` 写入闸门（读插件开关 `PluginEnabled["Logitech"]`，默认 false）、
> 连续 3 次失败熔断 + 递增退避（30 秒起、封顶 5 分钟，与基类共用 `Plugins/WriteBackoff.cs`）、接口打开失败 10 分钟退避重试。**复核 R2 时看到这一例不算违规**；
> 但它同时**不受基类后续加固的保护**——改基类安全机制时要同步检查这个文件（CR-01 就是这么来的）。
| **R3** | 跨线程共享的可变对象：要么快照传递，要么全程加锁 | `ConcurrentDictionary` 只保证字典安全，**不保证它存的字段安全**；且共享实例会让"变化判定"恒为假（见 BACKLOG CR-03） | 搜 `GetOrAdd` / `_devices[...]` 的赋值，看存进去的是副本还是调用方本体 |
| **R4** | 退出路径必须释放 HID 句柄 / Timer / DB 写队列 / 事件订阅 | 句柄不释放会把 dongle 会话占死，之后必须重新插拔（`docs/hardware/safety.md` §2 事故二） | 找 `Environment.Exit` 的调用点，确认它之前走了 `Dispose` |
| **R5** | 异常不得静默：`catch` 必须留痕（日志 + 计数）；UI 全局兜底不得无限 `Handled = true` | 静默失败会让应用"看起来在跑、实际什么都没做"，是最难排查的一类 | grep 空 `catch`；看全局兜底有没有计数熔断 |
| **R6** | 注释配额（类头 ≤5 行 / 方法 ≤2 行 / 字段 ≤1 行）+ `docs/x.md §y` 指针必须真实存在 | 注释与行为不符时，它就从文档退化成误导。详见 `docs/dev/comment-style.md` | 读改动附近的注释，逐条对着代码验 |
| **R7** | 改帧解析 / 读数过滤 / 历史库 / 离线判定，必须带回归单测 | 这几处全是纯逻辑、有现成测试类，不测没有理由 | 看测试项目里对应类有没有新用例 |
| **R8** | 新增的纯逻辑必须零 WinRT 依赖，能被测试项目 `<Compile Include>` 链进去 | 这条治本：`DeviceManager`(499 行) 至今测不了是因为碰了 WinRT；但**别把"测不了"一律归因于 WinRT**——`BatteryPluginBase` 实测只依赖纯 P/Invoke 的 `Hid` 层、并不碰 WinRT，它测不了是因为**决策逻辑与真实 HID I/O 耦合**（正确解法是把纯决策抽成自由函数，如 `DeviceChangeDetector` / `DeviceStatusFormatter` / `WatcherProps`） | 新文件能否加进 `src/tests/VigilWatt.Core.Tests/VigilWatt.Core.Tests.csproj` |

R8 是前置约束而非事后补测。**写代码时就把逻辑和 WinRT 调用分开**，比写完再想"怎么测"便宜得多。

## 3. S 系列 · 建议（不阻塞提交）

| 编号 | 建议 |
|---|---|
| **S1** | 单文件 ≤ 400 行；超了就按职责拆（当前超限：`WidgetWindow.xaml.cs` 679 / `LogitechPlugin.cs` 634 / `BatteryPluginBase.cs` 599 / `MchosePlugin.cs` 584 / `DeviceManager.cs` 499） |
| **S2** | UI 代码后置里不放业务逻辑；超过 200 行的页面抽 ViewModel |
| **S3** | 同一逻辑不要写第二遍（当前 `ReadConnected` 在 `BleBatteryWatcher` 与 `BluetoothClassicWatcher` 各一份） |
| **S4** | `async void` 只允许出现在事件处理器 |
| **S5** | UI 线程不做同步 IO（SQLite 查询、文件读写一律 `Task.Run`） |
| **S6** | 死代码及时移除，不要靠"以后可能有用"留着 |

## 4. 审查三层

| 层 | 谁做 | 内容 | 成本 |
|---|---|---|---|
| **L0 机械层** | 改代码的人（或 agent），每次改动后 | ① 构建 `0 警告 0 错误`；② 相关测试类绿（`dotnet test --no-build --filter "FullyQualifiedName~<类名>"`，**全量 `dotnet test` 在本机会卡住**，见 `docs/dev/build.md`）；③ 注释配额；④ 未碰 `AGENTS.md §5` 禁区 | 秒级 |
| **L1 自查清单** | 同上，提交前 | 按 §4.1 的改动类型逐条过 | 分钟级 |
| **L2 专家审查** | 用户发起的专家会话 | 按 §5 场景触发，产出报告 + BACKLOG 条目 | 小时级 |

### 4.1 L1 自查清单（按改动类型对号入座）

| 改了什么 | 必查 |
|---|---|
| 硬件写入 / 新插件 | R1、R2 + `docs/hardware/safety.md` 全部规则；发帧必须有出处（官方抓包或公开文档），禁止命令枚举 |
| 状态模型 / 设备列表 | R3；**重点验"变化判定还能不能成立"**——若上报方与存储方是同一个对象，`changed` 会恒假 |
| 线程 / 后台任务 | R3；事件回调是否回 UI 线程；后台循环里的异常会不会把循环静默杀掉 |
| 资源 / 生命周期 | R4；谁订阅谁解绑；句柄、Timer、`CancellationTokenSource` 的归宿 |
| 异常处理 | R5；`catch` 是否留痕；UI 兜底是否有计数熔断 |
| UI / XAML | S2、S5；初始化期事件时序（`docs/dev/runtime-pitfalls.md` §4） |
| 读数过滤 / 帧解析 / 历史库 | R7，必须带回归用例 |

## 5. 按场景的流程

| 场景 | 触发条件 | 做什么 |
|---|---|---|
| **A · 日常小改** | 单文件、< 200 行 | L0 + L1 → 提交 |
| **B · 功能轮次** | 跨模块的常规开发轮次 | L0 + L1 + **收工前 L2 轻审查**（限定改动面）→ 结论进 `docs/project/changelog.md` + BACKLOG |
| **C · 里程碑 / 大重构前** | 用户发起 | **L2 全仓静态审查**（按 §6 格式） |
| **D · 硬件协议改动** | 任何写帧的改动 | L0 + L1 + `safety.md` 闸门 + 真机验证计划（用户知情后另行安排） |

场景 B 是默认节奏：**每个功能轮次收工前审一次改动面**，而不是攒着做全仓审查。

## 6. L2 审查报告的格式

沿用 `docs/reviews/archive/` 那两份的优点，固定五段：

1. **基线与边界声明** — 基线 commit；本次是否启动应用、是否向设备发帧；哪些结论是**代码推断**而非实测。
2. **逐条发现** — 编号 / 严重级 / 证据（`文件:符号`）/ 影响。**证据不写行号**：行号会漂移，这正是上一批报告被迫归档的原因。
3. **需要修正或遗漏的部分** — 复核视角：哪条定级错了、哪条把轻重说反了、原报告漏了什么。
4. **未能证实的部分** — 单独列出，注明验证方式。**不得把静态推断包装成实测结论**。
5. **结论去向** — 条目写进 `docs/reviews/BACKLOG.md`，报告本体进 `docs/reviews/archive/`。

## 7. 发现闭环

**规则：先更新 BACKLOG，再写报告。** 报告只承载"证据与推理"，条目本体留在 BACKLOG。

- 载体：`docs/reviews/BACKLOG.md` 只留 `open` / `doing`。字段：编号 / 一句话 / 证据（`文件:符号`）/ 状态。每条一行。
- 状态机：`open` → `doing` → `done` / `wontfix`。标成后两者时，把该行挪到 `docs/reviews/archive/BACKLOG-CLOSED.md`。编号关闭后不复用。
- `wontfix` 在归档里写一句理由。调查过程留在审查报告或专题文档，不留在 BACKLOG。全文超过 120 行就先挪已关闭的。
- 与 `docs/project/handoff.md` §4 的分工（写死，别混）：
  - **BACKLOG = 审查发现的未修项全集**；
  - **handoff §4 = 当前这一轮在做的那几条**。
  - 同一件事只在 BACKLOG 留条目，handoff 只放指针。项目已经在 `experience.md` 抄待办上踩过一次，不再抄第二份。
- 条目关闭时，把"怎么验证的"一句话写进 `docs/project/changelog.md`（历史归 changelog，状态归 BACKLOG）。

## 8. 与其他文档的关系

| 想知道 | 去哪 |
|---|---|
| 审查发现还剩什么没修 | `docs/reviews/BACKLOG.md` |
| 注释该怎么写 | `docs/dev/comment-style.md` |
| 硬件写入的红线 | `docs/hardware/safety.md` |
| 构建 / 测试 / 环境坑 | `docs/dev/build.md` |
| 某轮改动为什么这么做 | `docs/project/changelog.md` |
| 一次性历史审查报告 | `docs/reviews/archive/` |
