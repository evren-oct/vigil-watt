# 运行时坑 · 构建与运行环境

> 正文分册。索引与其余分册见 [runtime-pitfalls.md](../runtime-pitfalls.md)；**§N 是稳定编号**，代码与文档一律按 `runtime-pitfalls.md §N` 引用。

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

**怎么办**：

1. **先确认是不是这个坑**：`icacls <exe> | findstr Mandatory`。没有 `Mandatory` 行 = 默认 Medium，
   症状另有原因，别顺着这条查；
2. **确认是 Low 后，在管理员终端提回 Medium**：`icacls "<exe 路径>" /setintegritylevel Medium`。
   **必须管理员上下文**——该操作写 SACL 的强制标签 ACE，需要 `WRITE_OWNER`；普通终端只有
   `Authenticated Users:(I)(M)`（Modify，不含 `WRITE_OWNER`），必然 `拒绝访问`（退出码 5）。
   每次重建 exe 都会重新继承 Low，所以每次都要重做；
3. **不要改工作区目录本身的标签**（`icacls <dir> /setintegritylevel Medium`）：那是沙箱的隔离边界，
   可能被重置，而且会让沙箱侧的进程写不了源码；
4. 想彻底绕开：`run-app.ps1 -Copy` 复制到工作区之外再启动（新目录天然是 Medium）。

**已移除的自动化（2026-11-27）**：`VigilWatt.App.csproj` / `VigilWatt.Core.Tests.csproj` 的
`RestoreExeIntegrityLevel` / `RestoreTestHostIntegrityLevel` 两个构建目标，以及 `run-app.ps1` 启动前的
`icacls` 调用，全部删除。原因：它们在普通终端**从未成功过**（同上 `WRITE_OWNER` 限制），真出事时只留一条
`MSB3075` 警告而不能修复，是净负收益的噪音；且当前工作区路径（`D:\project-personal\vigil-watt`）全链路
无强制标签，前提本就不成立。**替代方式就是上面第 2 条：人工、管理员终端、按需执行。**

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
- `ProcessIdentity.Describe()`：启动日志打 `用户= 会话= 完整性=`。**`管理员=` 与 `沙箱组=` 只在
  `LogLevel=Debug` 时追加**——组名翻译要逐个问 LSASS/AD，域机上二三十个组近 1 秒，压启动路径不值当
  （2026-11-25 实测：这一行曾占启动关键路径 0.95 秒）。
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
