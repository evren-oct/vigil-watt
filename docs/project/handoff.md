# 会话交接（给下一个 AI / 未来的自己）

> 快照时间：2026-10-14。**先读本文，再按需查其他文档**；内容冲突时以专题文档为准（本文是快照）。
> 逐轮原因在 **[changelog.md](changelog.md)**。本文只留当前状态、设备、未完成的待办、注意事项、文件速查。
> **维护约定**：收工后本轮原因追加进 `changelog.md`；本文件只改 §1 状态与 §4 未完成待办。已完成的待办删掉，不划掉留着。全文超过 120 行先删历史再写（`AGENTS.md` §3）。

## 1. 当前状态

当前安装包 **1.2.2**（`dist/`，本机试装用；尚未上传正式 Release）。正式线上以 GitHub 最新 Release 为准，Gitee 是回退源；更新源 GitHub 优先、8 秒不通回退 Gitee。发新版时保留上一版 `*-full.nupkg` 作为 delta 基准；规则见 `dev/build.md` §8。

本机已打通：迈从 G87（0x13）、雷柏 VT3（status-report7，纯被动）、北通 T9（被动流）、雷蛇 Kraken V4（dongle `1532:056C`，report `0x02`）、经典蓝牙耳机（HFP 网关节点 `DEVPKEY_Device_BatteryLevel`）。罗技 Bolt 接收器（C548）+ MX Master 3S 已真机验证。本机电量可选，默认关。弱水时砂 2.4G 无电量通道，不再投入。

任务栏贴片是原生子窗口 `Services/TaskbarPillWindow.cs`（`SetParent` 进 `Shell_TrayWnd`），不是 WinUI 覆盖窗。小组件仍用 `TransparentWindowSurface`；Win10 上因此没有 DWM 投影（BACKLOG **CR-57**，未做）。旧覆盖窗的白框、裁字、抢 Z 序在新贴片上已不存在。原理见 `dev/runtime-pitfalls.md` §16；§17 只对小组件。

页面已缓存（`NavigationCacheMode = Required`），订阅跟 `Loaded`/`Unloaded`。设置页可搜索。日志按日：`logs\app-日期.log`。插件开关即写入授权，没有第二套「写入许可」。

设备列表支持外设右键自定义名称与图标，按设备 ID 持久化；实时展示读取展示快照，历史页面用当前自定义名，已有采样不改写，后续采样保留自定义名称。

设备列表与任务栏悬停支持剩余时间估算；至少 3 个不同时间点且有净下降才显示，无法估算时显示原因；设置页全局开关默认开启。实现与限制见 `docs/project/code-flow.md` §5。

审查标准在 `docs/dev/review-standard.md`，未修项全集在 `docs/reviews/BACKLOG.md`（不要在本文再抄一份）。

社区分支 `integrate/community-contributions` 已收 6 个提交（HECATE → AULA/DELUX → DualSense → 迈从鼠标 V2 → GANSS → V9 Pro 耳机），2026-10-19 以 `--no-ff` 合并进 `main`（合并提交 `90cf94b`），该分支按用户要求**留在本地，未推送也未删除**。设备接入到此为止；设备管理类改动（#14 双通道去重、#16 设备管理）、#15 的 BLE 大改、issue #12/#11 都按 BACKLOG CR-68 ~ CR-72 排队，本轮不做。

2026-10-19 设备扩展：CHERRY MX 8.2、迈从 A5 与 Haute + DualSense 均已真机读取；A5 归迈从品牌，托盘图标可独立关闭，Haute 使用芯片 ID 并支持读取恢复。CHERRY / Mchose / Razer 默认关闭，Haute 仅读取默认开启。协议与历史兼容边界见 `hardware/protocols.md` §13–§16，长期观察见 CR-74，审查见 `reviews/archive/reconnect-2026-10-19.md`。

更新说明是用户向的 `docs/project/release-notes.md`，随包分发：关于页「更新日志」离线可看全部历史，更新确认框显示目标版本的内容（来自更新源 notes，缺失时只显示版本行）。规则见 `dev/build.md` §8。

下结论前先跑 `cmd /c ver`。**本机现在是 `10.0.26200.9445`（Win11 25H2）**——文档曾记成 Win11，2026-10-12 复核为 Win10 19045.6456，2026-10-14 再查已升到 26200。所以「哪一层背景可见」「Win10 上透明与 DWM 投影互斥」（CR-57）这类基于 Win10 的判断都要按新系统重判，别照抄。

## 2. 用户设备清单（勿对号入座错）

| 设备 | 通道 | 状态 |
|---|---|---|
| 迈从 G87（VID_41E4&PID_2001） | 0x13 会话 | 已打通 |
| 迈从 G87 V2（3837:201B）/ A7 V2 Pro（3837:100A） | 前者走 0x13 会话；后者走 E2 被动帧 + `0x11/0x06` 触发 | 代码已接入，默认关。贡献者真机验证，本机未复测（protocols.md §1.6） |
| 罗技 Bolt（C548 + MX Master 3S） | HID++ | 已真机验证 |
| 雷柏 VT3 二代（VID_24AE&PID_1412） | status-report7 被动心跳 | 已打通，零写入。雷柏不是 HID++，勿让 Logitech 插件认领 |
| 英菲克 ZHEN（VID_1D57&PID_FA65） | report 0x03 被动状态帧 | 已打通，零写入；放电 `0x01` / 充电 `0x03` 已校准（protocols.md §6） |
| HECATE G5M Pro（VID_35BB&PID_D00B） | report 0x04 被动状态帧 | 代码已接入，默认开、零写入。帧与充放电是贡献者声明，本机未复测（protocols.md §7） |
| AULA F99 Pro（VID_3554&PID_FA09） | `0x13` 会话后读推送 | 代码已接入，默认关。不进 15 秒发现。贡献者声明，本机未复测（protocols.md §9） |
| DELUX M900 Pro（VID_1D57&PID_FA65） | `0x000A` 被动 5 字节帧 | 代码已接入，默认关。与英菲克同 VID/PID；2026-10-19 本机没扫到 `1D57`（protocols.md §8） |
| DualSense（VID_054C&PID_0CE6） | 蓝牙 HID report `0x31` | 代码已接入，默认开、零写入。约 10% 一档。贡献者声明，本机未复测（dualsense.md） |
| GANSS GS3104T-Pro（VID_05AC&PID_024F） | 33 字节查询帧 `00 20 01 … 21` + 响应 `[3]` | 代码已接入，默认关。会写帧，不进 15 秒发现。贡献者声明，本机无该设备未复测（ganss.md） |
| MCHOSE V9 Pro 耳机（VID_291D&PID_385D） | 64 字节查询帧 `55 65 01 …` + 响应 `[2]` | 代码已接入（并入迈从插件，共用 `Mchose` 开关），默认关。会写帧。贡献者声明，本机无该设备未复测（mchose-v9-pro.md） |
| 雷蛇 Kraken V4（VID_1532&PID_056C） | MI_05 col04（`FF14/0001`，report `0x02`） | 已打通，`0x21`/`0x2A` 只读查询交替；充电中待接线校准（protocols.md §16） |
| 北通 T9（PID_5073）/ 鲲鹏20（PID_5191） | 厂商接口 0xFF00；T9 走 Col04 被动流 | T9 已真机验证；鲲鹏20 主动查询未验证 |
| 弱水时砂 2.4G（38FF:0001） | — | 无电量通道（protocols.md §3），不再投入 |
| EDIFIER W820NB | HFP 网关 `DEVPKEY_Device_BatteryLevel` | 已打通，与设置页一致 |
| 华硕 AURA、Virtual Multitouch、lifeme 音箱 | — | 与电量无关，勿探测 |
| 本机电池 | `GetSystemPowerStatus`，Id `sys:battery` | 已接入，默认关。托盘最低电量不统计本机 |

## 3. 历史

不在这里记。查「为什么这么改」看 [changelog.md](changelog.md)；查协议、坑、流程看对应专题文档。

## 4. 待办

审查未修项以 `docs/reviews/BACKLOG.md` 为准。这里只留还要人去做的事。

1. **迈从充电标志**：插上充电线，看日志里的状态字节，补全 `ParseKeyboardV1Charge`（已知 0x10 位 = 充电中）。
2. **雷柏充满**：放电 0x01、充电 0x02 已校准。充满时 `[7]` 还没采到，采到再补映射。
3. **迈从 QHW**（3837:3033）已实现，遇到再验证。
4. **北通鲲鹏20**（PID_5191）主动查询未真机验证。T9 的 `byte23` 低位仍只认 `0x50` = 未连接；验证方法见 protocols.md §4.4.1。
5. **贴片人手复看**：拖动与位置记忆（CR-48）、自动隐藏、竖向/多屏（CR-41）、深色对比（CR-42）、是否压住任务栏按钮（CR-39）、右键菜单（CR-58/CR-60）、悬停设备名（多台/底栏/深色任务栏）。分辨率往返已于 2026-10-13 通过。Win10 小组件投影（CR-57）等用户决定是否做。
6. **CR-53 真机**：迈从键盘睡够 3 分钟再唤醒，日志应在 1 分钟内出现重新上线。表在 `Plugins/WriteBackoff.cs`。
7. **页面缓存真机**：来回切换是否变快、离开再回来数据是否仍刷新、设置值是否与 `settings.json` 一致、搜索是否仍可用。坑见 `dev/runtime-pitfalls.md` §19。相关未修项 CR-61 / CR-62 / CR-63。
8. **启动更新确认框**：本轮只验到"托盘气泡 + 日志发现新版本"（`StartMinimized=true`，窗口没开）。打开主窗口应弹「发现新版本」确认框，未真机确认。
9. **Win11 26200 复核**：本机已升到 26200（见 §1），CR-57（小组件自绘投影）等基于 Win10 的结论需按新系统重判；任务栏贴片在 Win11 上的表现（CR-39/41/42/48）也值得重看。
10. **GANSS 认领判据（CR-66）**：需要有 GANSS 设备的人做三件事——记下接收器自报的产品名/厂商名；抓一次官方配置软件的包，看它怎么定位接口、有没有取型号的命令；换 USB 口与固件后复测 `&mi_03#` 是否稳定。清单在 `hardware/ganss.md` §6.1。
11. **V9 Pro 耳机（CR-67）**：需要有设备的人补三个电量点（满电/中间/低电）与充电时的样本，确认 `[3]` 是否充电标志；顺带确认接收器会不会把主机写入回显到输入队列。清单在 `hardware/mchose-v9-pro.md` §5。
12. **雷蛇充电中/充满**：插线看 `0x2A` 是否返回 `1`、充满时返回什么；蓝牙模式不在支持范围，要测需单开。

## 5. 给下一个 AI 的注意事项

1. **开工**：`docs/README.md` 症状速查 → 本文件 → 专题文档。文档比记忆新。
2. **硬件**：绝不盲扫；只发有出处的帧。插件开关就是写入授权（`PluginEnabled[Name]`）。熔断和退避在基类里，子类别绕过。详见 `hardware/safety.md`。
3. **dongle 静音**：本应用、浏览器里的 M HUB、官方 HUB 谁持有会话谁工作。崩溃进程会把会话粘死。先看 safety.md §2，别急着改协议。
4. **调试**：抓官方软件的包（debugging.md §1）；验证用 `pip install hidapi`（§2）。
5. **构建**：用 sln，从 `bin\x64\...\win-x64\` 启动。应用在跑时先结束进程，否则发布会锁文件。见 `dev/build.md`。
6. **启动方式**：工具拉起的进程会被进程树清掉。用 `explorer.exe "VigilWatt.App.exe"`。强杀会按 safety.md §2 占死 dongle 会话。
7. **用户偏好**：中文交流、文档中文；界面只中英文；个人自用，不做游戏内显示、HTTP API、账号。
8. **雷柏**：停手 = dongle 静音，静音不是离线。离线主判据是 `[18].bit7`，走约 6 秒确认，不要改成立刻离线。见 protocols.md §3。
9. **插件配置分三层**：协议事实和安全声明必须按品牌；离线策略集中在 `OfflinePolicy`。`CountPollFailures` 与 `SupportsPassiveListen` 是两件事，合成一个布尔会让迈从关机后永远不离线。
10. **发现节拍**：15 秒节拍只给 `SupportsFastDiscovery`（零写入）或 `SupportsDiscoveryProbe`（`ProbeCooldown` 兜住写入）。会写帧的新插件先看 safety.md。
11. **日志**：自研 `Log`，不引 Serilog。默认 Info；逐帧用 Debug。按日文件、默认留 14 天。设计见 `dev/logging.md`。
12. **脚本**：一次性探针放 `scripts/`，用完即删。长期 C# 项目放 `src/tests/`。仓库根不放临时文件。

## 6. 关键文件速查

| 要动什么 | 去哪 |
|---|---|
| 迈从 / 雷柏 / 北通协议 | `Plugins/Mchose/`、`Plugins/Rapoo/`、`Plugins/Betop/`（规格在 protocols.md） |
| 离线判定 | `Plugins/OfflinePolicy.cs` + `BatteryPluginBase` 的 `MarkOffline` / `RequestOffline` / `RequestConfirm` |
| 写入授权 | `AppSettings.PluginEnabled`；基类 `WritesEnabled`。设置页 `AddPluginRow` |
| 新增品牌 | 新建插件继承 `BatteryPluginBase`。会写帧的默认关 |
| 状态文案与充电 | `Models/DeviceStatusFormatter.cs`、`Models/ChargingStateMap.cs`；色值在 `Services/UiPalette.cs` |
| 变化判定 | `Devices/DeviceChangeDetector.cs`。`Upsert` 必须先 `Clone()`，不要存共享可变实例 |
| 轮询 / 采样 | `Devices/DeviceManager.cs`（code-flow.md） |
| 经典蓝牙 | `Ble/BluetoothClassicWatcher.cs`、`Ble/ClassicDevNodeReader.cs` |
| 本机电量 | `Power/SystemPowerReader.cs`、`Power/SystemBatteryWatcher.cs`。托盘在 `TrayService.Render` 里跳过 `DeviceSource.System` |
| 任务栏贴片 | `Services/TaskbarPillWindow.cs`、`TaskbarPillRenderer.cs`、`TaskbarLayout.cs`。原理 runtime-pitfalls.md §16 |
| 小组件透明 | `Services/TransparentWindowSurface.cs`。贴片不用它。见 §17 |
| 设备图标 | `Services/DeviceIconFactory.cs`（不要用字体图标） |
| 程序图标 | `scripts/make-app-icon.py` → `Assets/VigilWatt.ico`；csproj `ApplicationIcon` 和 `MainWindow.ApplyWindowIcon` 都要 |
| 历史库 | `Storage/HistoryStore.cs`；页面 `Views/HistoryPage`、`Views/StatsPage` |
| 日志 | `Utils/Log.cs`；设置页「日志」；设计 `dev/logging.md` |
| 语言包白名单 | `VigilWatt.App.csproj` 的 `MuiLanguagesToKeep`（build.md §6） |
| 更新说明（用户向） | `docs/project/release-notes.md`；解析 `Services/ReleaseNotes.cs`、渲染 `ReleaseNotesView.cs`、确认框 `UpdatePrompt.cs` |
| 某轮为什么这么做 | `docs/project/changelog.md` |
| 还有什么没修 | `docs/reviews/BACKLOG.md` |
| 审查红线 | `docs/dev/review-standard.md` |
