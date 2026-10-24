# 会话交接（给下一个 AI / 未来的自己）

> 快照时间：2026-10-14。**先读本文，再按需查其他文档**；内容冲突时以专题文档为准（本文是快照）。
> 逐轮原因在 **[changelog.md](changelog.md)**。本文只留当前状态、设备、未完成的待办、注意事项、文件速查。
> **维护约定**：收工后本轮原因追加进 `changelog.md`；本文件只改 §1 状态与 §4 未完成待办。已完成的待办删掉，不划掉留着。全文超过 120 行先删历史再写（`AGENTS.md` §3）。

## 1. 当前状态

当前安装包 **1.2.3**（`dist/`，本机试装用；尚未上传正式 Release）。正式线上以 GitHub 最新 Release 为准，Gitee 是回退源；更新源 GitHub 优先、8 秒不通回退 Gitee。发新版时保留上一版 `*-full.nupkg` 作为 delta 基准；规则见 `dev/build.md` §8。

本机已打通：迈从 G87（0x13）、雷柏 VT3（status-report7，纯被动）、北通 T9（被动流）、经典蓝牙耳机（HFP 网关节点 `DEVPKEY_Device_BatteryLevel`）、罗技 Bolt 接收器（C548）+ MX Master 3S。本机电量可选，默认关。弱水时砂 2.4G 无电量通道，不再投入。**2026-10-24 新增**：迈从 G75 Pro（`3554:FA09`，会话 ACK 分流，36%）、凌豹 M5PRO Max（`3151:402D`，83%，电量字段待与官方界面对照，见 `hardware/lingbao.md` §1.4）。**其余机型均为贡献者真机、本机未复测，逐台状态见 §2 表**——别把 §2 里「贡献者真机」当成我们验过。

任务栏贴片是原生子窗口 `Services/TaskbarPillWindow.cs`（`SetParent` 进 `Shell_TrayWnd`），不是 WinUI 覆盖窗；小组件仍用 `TransparentWindowSurface`，Win10 上因此没有 DWM 投影（CR-57）。**拖动走 16ms 轮询，不靠鼠标捕获**——捕获对非前台窗口无效（`dev/runtime-pitfalls.md` §16、§16.5.1；§17 只对小组件）。选屏由 `Services/TaskbarPillManager.cs` 每秒按 `monitorDevicePath` 匹配，配置的屏不在就降级贴主屏，设置页显示真实屏幕名——`\\.\DISPLAYn` 是配置序号不是身份，见 `dev/monitor-identity.md`。

页面已缓存（`NavigationCacheMode = Required`），订阅跟 `Loaded`/`Unloaded`。设置页可搜索。日志按日：`logs\app-日期.log`。插件开关即写入授权，没有第二套「写入许可」。

设备列表支持：右键自定义名称与图标（按设备 ID 持久化，历史页用当前名、已有采样不改写）；**右键删除离线设备**——同时清掉该设备历史（VACUUM），勾「永久移除」写本地黑名单、插件认领阶段即跳过，恢复入口在设备页底部「已屏蔽 N 台设备」（两份名单见 `code-flow.md` §5.2，真机验收清单见 §4 第 13 条）；剩余时间估算（至少 3 个不同时间点且有净下降才显示，否则说明原因，见 `code-flow.md` §5）。

**开发版数据目录在 `bin\x64\Debug\...\win-x64\data\`**（`AppPaths` 用 `#if DEBUG` 分流，Release 才用 `%LOCALAPPDATA%\VigilWatt`），所以从 `bin` 双击即可运行、不碰用户目录；`VIGILWATT_DATA_DIR` 可覆盖。为什么必须这么改见 `dev/runtime-pitfalls.md` §20，用法见 `dev/build.md` §3.1。

审查标准在 `docs/dev/review-standard.md`，未修项全集在 `docs/reviews/BACKLOG.md`（不要在本文再抄一份）。

设备接入已收五批（#36 CHERRY/派世、#25 黑爵/雷神、#33 迈从 K7、#23 寸渊影弑/盖世小鸡、#18 前行者），逐台状态见 §2 表；两个 `integrate/*` 本地分支已删。设备管理类改动（#14 双通道去重、#16 设备管理）、#15 的 BLE 大改、issue #12/#11 按 BACKLOG CR-68 ~ CR-72 排队。

更新说明是用户向的 `docs/project/release-notes.md`，随包分发：关于页「更新日志」离线可看全部历史，更新确认框显示目标版本的内容（来自更新源 notes，缺失时只显示版本行）。规则见 `dev/build.md` §8。

下结论前先跑 `cmd /c ver`。**本机现在是 `10.0.26200.9445`（Win11 25H2）**；「哪一层背景可见」「Win10 上透明与 DWM 投影互斥」（CR-57）这类基于 Win10 的判断都要按新系统重判，别照抄。

## 2. 用户设备清单（勿对号入座错）

| 设备 | 通道 | 状态 |
|---|---|---|
| 迈从 G87（VID_41E4&PID_2001） | 0x13 会话 | 已打通 |
| 迈从 G87 V2（3837:201B）/ A7 V2 Pro（3837:100A） | 前者走 0x13 会话；后者走 E2 被动帧 + `0x11/0x06` 触发 | 代码已接入，默认关。贡献者真机验证，本机未复测（mchose.md §1.6） |
| 罗技 Bolt（C548 + MX Master 3S） | HID++ | 已真机验证 |
| 雷柏 VT3 二代（VID_24AE&PID_1412） | status-report7 被动心跳 | 已打通，零写入。雷柏不是 HID++，勿让 Logitech 插件认领 |
| 英菲克 ZHEN（VID_1D57&PID_FA65） | report 0x03 被动状态帧 | 已打通，零写入；放电 `0x01` / 充电 `0x03` 已校准（inphic.md §1） |
| HECATE G5M Pro（VID_35BB&PID_D00B） | report 0x04 被动状态帧 | 代码已接入，默认开、零写入。帧与充放电是贡献者声明，本机未复测（hecate.md §1） |
| AULA F99 Pro（VID_3554&PID_FA09） | `0x13` 会话后读推送 | 代码已接入，默认关。不进 15 秒发现。贡献者声明，本机未复测（aula.md §1） |
| DELUX M900 Pro（VID_1D57&PID_FA65） | `0x000A` 被动 5 字节帧 | 代码已接入，默认关。与英菲克同 VID/PID；2026-10-19 本机没扫到 `1D57`（delux.md §1） |
| DualSense（VID_054C&PID_0CE6） | 蓝牙 HID report `0x31` | 代码已接入，默认开、零写入。约 10% 一档。贡献者声明，本机未复测（dualsense.md） |
| GANSS GS3104T-Pro（VID_05AC&PID_024F） | 33 字节查询帧 `00 20 01 … 21` + 响应 `[3]` | 代码已接入，默认关。会写帧，不进 15 秒发现。贡献者声明，本机无该设备未复测（ganss.md） |
| MCHOSE V9 Pro 耳机（VID_291D&PID_385D） | 64 字节查询帧 `55 65 01 …` + 响应 `[2]` | 代码已接入（并入迈从插件，共用 `Mchose` 开关），默认关。会写帧。贡献者声明，本机无该设备未复测（mchose-v9-pro.md） |
| 北通 T9（PID_5073）/ 鲲鹏20（PID_5191） | 厂商接口 0xFF00；T9 走 Col04 被动流 | T9 已真机验证；鲲鹏20 主动查询未验证 |
| 寸渊影弑 SHADOW（3654:6901） | 0x11/0x01 通知；须发官方 `GetBasicInfo(0x09)` | 已打通（90% / 充电均核对过），默认关（§13） |
| 盖世小鸡 G7 Pro CE（3537，usage 0xFFF0） | 心跳 `0F F2 00` 开 0x12 状态流，60 秒突发读一次 | 已打通，不依赖官方软件，默认开（§14） |
| 前行者 X87 键盘（VID_0C45&PID_FEFE&MI_03） | 33 字节查询帧（0x20/0x01，校验和 [32]） | 代码已接入，默认关。会写帧。贡献者真机 100%，**本机未复测**。两型共用「前行者」开关。MI_04 是 65 字节同 usage 陷阱，只能写 MI_03（eweadn.md §1.1） |
| 前行者 S9 鼠标（VID_372E&PID_1026） | feature report 0x03，贝盈 beiyin_uuid | 代码已接入，默认关。会写帧。贡献者真机 72~73%，**本机未复测**。`0xFF06` 输出通道是死路（§18.2） |
| 弱水时砂 2.4G（38FF:0001） | — | 无电量通道（rose.md），不再投入 |
| CHERRY PIXIU75（dongle 046A:01E4 / 有线 046A:01E2） | 64 字节块通道 0xFF1C，读块 0x1A（电量 + 充电位） | 已打通，真机 48%↔官方软件一致；插线 55% + 充电中（cherry.md §1.5） |
| 派世 Pulsar X2 V2（VID_3554&PID_F508） | 17 字节帧 `0x08`，STATUS `0x03` / POWER `0x04` | 已打通，真机 20% + 3566 mV（pulsar.md §1） |
| 迈从 K7 V2 Ultra+（VID_3837&PID_1018） | `0x4D` 通道，`0x0900` 查询 + `0x0600` 被动事件 | 代码已接入（并入迈从插件），默认关。会写帧。贡献者真机连跑 6.5 小时（mchose.md §3） |
| 迈从 G75 Pro（VID_3554&PID_FA09） | 与 AULA 同指纹，会话 ACK 密码分流到迈从 | 本机实测：ACK 型号 G75 Pro，`13 4A` = 36%；与 AULA 的认领冲突已按 ACK 解决（mchose.md §4） |
| 凌豹 M5PRO Max（VID_3151&PID_402D） | feature 65 字节，`83` + `0xFF-sum` 校验 | 本机实测 83%，默认关；电量下标/状态字节待与官方界面对照（lingbao.md §1.4） |
| 黑爵 AK870（VID_3151&PID_4011） | feature `F7` 状态查询，`[2]` 电量 + `[4]` 在线 | 代码已接入，默认关。贡献者实机确认（ak870-ml903.md） |
| 雷神 ML903 NearLink Se（VID_369B&PID_0234） | `0xA0` 设备信息查询，TLV `0x0225` = 电量 | 代码已接入，默认关。贡献者实机确认（ak870-ml903.md） |
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
4. **北通鲲鹏20**（PID_5191）主动查询未真机验证。T9 的 `byte23` 低位仍只认 `0x50` = 未连接；验证方法见 betop.md §1.4.1。
5. **贴片人手复看**：连续拖 5 次、上下甩出贴片、拖到两端再回拖、松手后不自跳（CR-48）；自动隐藏、竖向/多屏（CR-41）、深色对比（CR-42）、是否压住任务栏按钮（CR-39）、右键菜单（CR-60）、悬停设备名（多台/底栏/深色任务栏）。分辨率往返已于 2026-10-13 通过。Win10 小组件投影（CR-57）等用户决定是否做。
6. **页面缓存真机**：来回切换是否变快、离开再回来数据是否仍刷新、设置值是否与 `settings.json` 一致、搜索是否仍可用。坑见 `dev/runtime-pitfalls.md` §19。相关未修项 CR-61 / CR-63。
7. **启动更新确认框**：本轮只验到"托盘气泡 + 日志发现新版本"（当时 `StartMinimized=true`，窗口没开）。**2026-10-20 起 `StartMinimized` 默认 `false`，所以「发现新版本」确认框成了默认路径，更需要在真机上确认一次**——打开主窗口时应弹确认框，未真机验证。
8. **Win11 26200 复核**：本机已升到 26200（见 §1），CR-57（小组件自绘投影）等基于 Win10 的结论需按新系统重判；任务栏贴片在 Win11 上的表现（CR-39/41/42/48）也值得重看。
9. **GANSS 认领判据（CR-66）**：需要有 GANSS 设备的人做三件事——记下接收器自报的产品名/厂商名；抓一次官方配置软件的包，看它怎么定位接口、有没有取型号的命令；换 USB 口与固件后复测 `&mi_03#` 是否稳定。清单在 `hardware/ganss.md` §6.1。
10. **V9 Pro 耳机（CR-67）**：需要有设备的人补三个电量点（满电/中间/低电）与充电时的样本，确认 `[3]` 是否充电标志；顺带确认接收器会不会把主机写入回显到输入队列。清单在 `hardware/mchose-v9-pro.md` §5。
11. **贴片选屏真机复看**：本机只有「仅第二屏」一种接法实测过（配置屏不在 → 降级主屏，日志已确认）。还需切「仅电脑屏幕 / 扩展 / 复制」各一次，看贴片是否 1 秒内跟到新任务栏、设置页刷新按钮后列表是否反映当前接法、`<名字>（未连接）` 是否显示正确。清单在 `dev/monitor-identity.md` §7。

## 5. 给下一个 AI 的注意事项

1. **开工**：`docs/README.md` 症状速查 → 本文件 → 专题文档。文档比记忆新。
2. **硬件**：绝不盲扫；只发有出处的帧。插件开关就是写入授权（`PluginEnabled[Name]`）。熔断和退避在基类里，子类别绕过。详见 `hardware/safety.md`。
3. **dongle 静音**：本应用、浏览器里的 M HUB、官方 HUB 谁持有会话谁工作。崩溃进程会把会话粘死。先看 safety.md §2，别急着改协议。
4. **调试**：抓官方软件的包（debugging.md §1）；验证用 `pip install hidapi`（§2）。
5. **构建**：用 sln，从 `bin\x64\...\win-x64\` 启动。应用在跑时先结束进程，否则发布会锁文件。见 `dev/build.md`。
6. **启动方式**：工具拉起的进程会被进程树清掉。用 `explorer.exe "VigilWatt.App.exe"`。强杀会按 safety.md §2 占死 dongle 会话。
7. **用户偏好**：中文交流、文档中文；界面只中英文；个人自用，不做游戏内显示、HTTP API、账号。
8. **雷柏**：停手 = dongle 静音，静音不是离线。离线主判据是 `[18].bit7`，走约 6 秒确认，不要改成立刻离线。见 rapoo.md。
9. **插件配置分三层**：协议事实和安全声明必须按品牌；离线策略集中在 `OfflinePolicy`。`CountPollFailures` 与 `SupportsPassiveListen` 是两件事，合成一个布尔会让迈从关机后永远不离线。
10. **发现节拍**：15 秒节拍只给 `SupportsFastDiscovery`（零写入）或 `SupportsDiscoveryProbe`（`ProbeCooldown` 兜住写入）。会写帧的新插件先看 safety.md。
11. **日志**：自研 `Log`，不引 Serilog。默认 Info；逐帧用 Debug。按日文件、默认留 14 天。设计见 `dev/logging.md`。
12. **脚本**：一次性探针放 `scripts/`，用完即删。长期 C# 项目放 `src/tests/`。仓库根不放临时文件。

## 6. 关键文件速查

| 要动什么 | 去哪 |
|---|---|
| 迈从 / 雷柏 / 北通 / 前行者协议 | `Plugins/Mchose/`、`Plugins/Rapoo/`、`Plugins/Betop/`、`Plugins/Eweadn/`（规格在各品牌文档） |
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
