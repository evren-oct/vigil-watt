# 会话交接（给下一个 AI / 未来的自己）

> 快照时间：2026-11-23。**先读本文，再按需查其他文档**；内容冲突时以专题文档为准（本文是快照）。
> 逐轮原因在 **[changelog.md](changelog.md)**。本文只留当前状态、环境、未完成的待办、注意事项、文件速查。
> **维护约定**：收工后本轮原因追加进 `changelog.md`；本文件只改 §1 状态与 §4 未完成待办。已完成的待办删掉，不划掉留着。全文超过 120 行先删历史再写（`AGENTS.md` §3）。

## 1. 当前状态

- 当前正式安装包 **1.3.1**（`dist/`，本机试装；尚未上传正式 Release）；Beta **1.4.0-beta.1** 已本机打包到 `dist-beta/`（`beta` 通道，未上传）。发版规则见 `dev/build.md` §8；更新源 GitHub 优先、8 秒不通回退 Gitee。**上传由维护者手动完成（GitHub 主下载、Gitee 镜像），AI 只做本机打包。**
- **本机已验证**：迈从 G87、迈从通用 dongle `3837:3033`、罗技 Bolt + MX Master 3S、罗技 PRO X 2、雷柏 VT3 / VT2S Air Max、北通 T9、英菲克 ZHEN / IN9、HECATE G3M Pro 升级版、ATK F1 Pro Max、VXE R1、EDIFIER W820NB、本机电池。逐台状态、默认开关与文档入口见 [support-matrix.md](../hardware/support-matrix.md)。
- 其余机型均为贡献者真机或仅实现待验，本机未复测；不要把贡献者读数当成维护者结论。协议与验证边界见各品牌文档。
- 任务栏贴片是原生子窗口 `Services/TaskbarPillWindow.cs`（`SetParent` 进 `Shell_TrayWnd`），拖动走 16ms 轮询；小组件仍用 `TransparentWindowSurface`，Win10 上因此没有 DWM 投影（CR-57）。选屏见 `dev/monitor-identity.md`。
- 页面已缓存（`NavigationCacheMode = Required`），订阅跟 `Loaded`/`Unloaded`；设置页可搜索；日志按日 `logs\app-日期.log`；插件开关即写入授权，没有第二套「写入许可」。
- 罗技 ReceiverIdentity 已全量开放，连接推断覆盖 Bolt / Unifying / LIGHTSPEED / Nano；C548 的 `0xB5/0x03` 真机无回包，容量走目录 6 槽；`0x0005` 设备名改按块读、上限 64，待真机复验（CR-163）。细节见 `hardware/logitech.md` §1。
- 迈从主动查询已加目录能力白名单：未登记/未验证接口零写入；G87、G87 V2、CZ、0x4D、Gmouse、V9/X9、G75 Pro 保持查询。见 `hardware/safety.md` §3 与 `reviews/beta-1-release-readiness-audit.md`。
- 设备列表支持右键改名/图标、忽略、删除离线设备与恢复；剩余时间估算至少 3 个时间点且有净下降才显示；贴片可按显示名筛选设备。见 `code-flow.md` §5。
- 详情页 Hero 已接产品图（迈从 / 罗技 / 雷柏 / 北通），列表 / 贴片 / 小组件不显示产品图；接入规则见 `project/device-images.md`。
- 设备诊断页（主窗口第 4 项）是未适配设备的 USB/HID 只读采集；流程与边界见 `project/capture.md`。
- 开发版数据目录在 `bin\x64\Debug\...\win-x64\data\`（`#if DEBUG` 分流，`VIGILWATT_DATA_DIR` 可覆盖）；exe 必须保持 Medium IL，见 `dev/runtime-pitfalls.md` §20。
- 本机是 Win11 25H2（`10.0.26200.9445`）；基于 Win10 的判断要按新系统重判。

## 2. 设备清单

逐台的验证状态、默认开关与文档入口见 [support-matrix.md](../hardware/support-matrix.md)。本机已验证的只有 §1 第 2 条列出的那些；其余条目不要把「代码已接入」当成「已验证」。

## 4. 待办

审查未修项以 `docs/reviews/BACKLOG.md` 为准。PR #44 待验证项见 CR-84，HERO 非阻塞传输层测试见 CR-97；充电测试已取消，不等待插拔充电操作。其它人工待办如下。

1. **插线校准**：迈从 `ParseKeyboardV1Charge` 补全充电位（已知 0x10 = 充电中）。雷柏满电一律判「充满」已在 `hardware/rapoo.md` §5.2 落地，不再列待办。
2. **北通 P3 真机验证**：P2 已落（3 套 data 协议、110 条目录、`BetopCodec` 删除）。下一步：20DD `02 CD` 与 KP40/50/70 电量真机抓包（CR-106）；9 个新宙斯 PID 的被动帧；501E/502C 等官方下一版（CR-169）；`5191` 主动查询；T9 `0x10` 占位帧观察（CR-170，betop.md §1.4.2）。
3. **贴片人手复看**：连续拖 5 次、上下甩出贴片、拖到两端再回拖、松手后不自跳（CR-48）；自动隐藏、竖向/多屏（CR-41）、深色对比（CR-42）、是否压住任务栏按钮（CR-39）、右键菜单（CR-60）、悬停设备名（多台/底栏/深色任务栏）。分辨率往返已于 2026-10-13 通过。Win10 小组件投影（CR-57）等用户决定是否做。
4. **页面缓存真机**：来回切换是否变快、离开再回来数据是否仍刷新、设置值是否与 `settings.json` 一致、搜索是否仍可用。坑见 `dev/runtime-pitfalls.md` §19。相关未修项 CR-61 / CR-63。
5. **启动更新确认框**：本轮只验到"托盘气泡 + 日志发现新版本"（当时 `StartMinimized=true`，窗口没开）。**2026-10-20 起 `StartMinimized` 默认 `false`，所以「发现新版本」确认框成了默认路径，更需要在真机上确认一次**——打开主窗口时应弹确认框，未真机验证。
6. **贴片选屏真机复看**：本机只有「仅第二屏」一种接法实测过（配置屏不在 → 降级主屏，日志已确认）。还需切「仅电脑屏幕 / 扩展 / 复制」各一次，看贴片是否 2 秒内跟到新任务栏（重建去抖 2 拍，见 `dev/runtime-pitfalls.md` §16.12）、设置页刷新按钮后列表是否反映当前接法、`<名字>（未连接）` 是否显示正确。清单在 `dev/monitor-identity.md` §7。
7. **设备页 / 诊断页真机复核**：诊断页的 Feature 读取（勾选后实际 GET_REPORT）、采集中拔设备、记录上限截断、另存为对话框与剪贴板（非打包 WinUI）——本机只验到被动监听拿到真实报文，清单在 `capture.md` §7；设备页「忽略此设备」要复看托盘/贴片/小组件是否同步消失、恢复是否立刻回来、重启后是否仍被忽略；**卡片上那行 DPI / 档位 / 回报率（PR-D）**要看是否显示、拔掉与休眠后整行是否消失、用官方软件改 DPI 后是否跟着变（`pointer-telemetry.md` §8）；小组件同一行还要看 1~5 台混排（有指标的行更高）时最后一行有没有被切、贴片悬停提示三行是否不折行。罗技 C548 的离线行真实截图与深色主题对比未做（用户 2026-11-11 跳过深色；离线文案/取色已有单测）。
8. **本机无设备的真机复核**：墨将 GALE2 应用内验证（开关默认关、须用本仓库源码构建，看列表条目与电量/充电标志是否与官方助手一致，边看边插拔接收器确认有线↔2.4G 切换那 0.5 秒是否闪离线，mojhon.md §7.5、§9）；狼途 T98 看默认开、插线电量爬升，`Silence = 300 秒` 需一份真实休眠样本确认不误判离线（langtu.md §2）；ATK `11FE` 要补本体 PID，否则插数据线会多一条记录（atk.md §10.3）；ilovbee 要一份**插上充电器后静置 3 分钟**的采集，确认 `[7] bit0` 是否保持、`[6]` 是否爬升（ilovbee.md §3）。另：雷柏 VT3 有线 `0x4612` 没声明 `capabilities`（无独立真机记录），要用就插线验一次再补（`hardware/rapoo-catalog.md` §6.2）。 迈从 0x4D 的 DPI / 回报率代码与审计已完成、**零真机样本**，`DpiRead` / `DpiLevelRead` / `RateRead` 仍 `Unverified`；插上 0x4D 鼠标后看日志里 `迈从 0x4D 回报率 / DPI(X) / DPI(Y)` 三行即可取证（`hardware/mchose.md` §3.9）。
9. **雷柏本轮真机复核**：插线后只留一条且优先有线（同 SN 归一）；VT3 之外至少一台看 DPI/档位/回报率；详情页 Hero 看雷柏产品图与配色切换（96 款 / 296 色）。旧历史/自定义名/黑名单会按新 ID 重记，未做迁移。
10. **协议引擎治理**：M1 文档收尾完成，M2 主动查询安全治理进行中；写入点盘点、自动门控方案与手动目标探测设计稿已完成，见 `docs/reviews/2026-11-23-active-query-write-audit.md`、`docs/reviews/2026-11-23-active-query-gate-design.md` 与 `docs/reviews/2026-11-24-manual-target-probing-design.md`。北通、迈从已完成设备级先行门控；全面实施先补能力声明，避免误停现有设备。

## 5. 给下一个 AI 的注意事项

1. **dongle 静音**：本应用、浏览器里的 M HUB、官方 HUB 谁持有会话谁工作。崩溃进程会把会话粘死。先看 safety.md §2，别急着改协议。
2. **启动方式**：工具拉起的进程会被进程树清掉。用 `explorer.exe "VigilWatt.App.exe"`。强杀会按 safety.md §2 占死 dongle 会话。
3. **雷柏**：`[18].bit7` 只在 **2.4G 模式**下作断链判据（有线时恒 0，属正常）；离线走约 6 秒断链确认或 90 秒静默兜底，不要改成立刻离线。见 rapoo.md §4.1、§5.1。
4. **插件配置分三层**：协议事实和安全声明必须按品牌；离线策略集中在 `OfflinePolicy`。`CountPollFailures` 与 `SupportsPassiveListen` 是两件事，合成一个布尔会让迈从关机后永远不离线。

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
| 设备产品图 | `Core/Media/DeviceImageCatalog.cs`、`App/Services/DeviceImageService.cs`；**新品牌接入手册见 `device-images.md`** |
| 程序图标 | `scripts/make-app-icon.py` → `Assets/VigilWatt.ico`；csproj `ApplicationIcon` 和 `MainWindow.ApplyWindowIcon` 都要 |
| 历史库 | `Storage/HistoryStore.cs`；页面 `Views/HistoryPage`、`Views/StatsPage` |
| 日志 | `Utils/Log.cs`；设置页「日志」；设计 `dev/logging.md` |
| 语言包白名单 | `VigilWatt.App.csproj` 的 `MuiLanguagesToKeep`（build.md §6） |
| 更新说明（用户向） | `docs/project/release-notes.md`；解析 `Services/ReleaseNotes.cs`、渲染 `ReleaseNotesView.cs`、确认框 `UpdatePrompt.cs` |
| 某轮为什么这么做 | `docs/project/changelog.md` |
| 还有什么没修 | `docs/reviews/BACKLOG.md` |
| 审查红线 | `docs/dev/review-standard.md` |
