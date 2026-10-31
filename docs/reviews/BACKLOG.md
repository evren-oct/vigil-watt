# 审查发现 BACKLOG

> 只放 **`open` / `doing`**。`done` / `wontfix` 在 [archive/BACKLOG-CLOSED.md](archive/BACKLOG-CLOSED.md)，编号不复用：被未合并分支占用的号直接跳过，不回收（如 CR-84~90）。
> 每条一行：编号、一句话、`文件:符号`。调查过程写审查报告或专题文档，不写在这里。
> handoff §4 只放这一轮要人做的事，不抄本表。全文超过 120 行，先把已关闭的挪走（`AGENTS.md` §3）。

## 未修

| 编号 | 一句话 | 证据 | 状态 |
|---|---|---|---|
| **CR-84** | AKG N9 Hybrid 已见 50% / 100% 读数，尚未核对官方 App、连续电量变化、关机与长期运行；用户观察充电时无法工作并取消充电测试，不接入充电标志，状态保持 Unknown | `Plugins/Akg/AkgN9Codec.cs:TryParseBattery`、`hardware/akg.md` §2 | `open` |
| **CR-97** | HERO 68 Air 传输流程直接依赖 `HidDeviceHandle`，缺少传输层离线回归；维护者认可现有认领、分片及写入闸门，可合入，建议后续补通知穿插等传输流程测试，此项不阻塞合入 | `Plugins/Aula/AulaHero68Transport.cs`、PR #44 维护者反馈 | `open` |
| **CR-57** | 小组件自绘投影未做。原判据基于 Win10 19045，本机已升到 26200（Win11），需按新系统重判 | `WidgetWindow.xaml.cs:ApplyWin10Chrome` | `open` |
| **CR-60** | 贴片右键菜单的 owner 是任务栏子窗口，弹出和关闭未真机看 | `TaskbarPillWindow.cs:OnRightClick` | `open` |
| **CR-63** | 贴片与 explorer 输入队列串在一起，UI 线程长阻塞可能冻住任务栏。未实测 | `TaskbarPillWindow.cs` | `open` |
| **CR-13** | 两个蓝牙 watcher 的发现循环和快照仍然重复。`ReadConnected` 已抽到 `WatcherProps` | `Ble/BleBatteryWatcher.cs`、`Ble/BluetoothClassicWatcher.cs` | `open` |
| **CR-16** | 多个文件超过 400 行。拆分见下面的 D 项，不要先按行数硬拆。`MchosePlugin` 并入 V9 Pro 后已 766 行（四套协议），拆分建议等 D6 落地或有了第二台迈从设备再动——现在拆只能靠读代码验证 | `WidgetWindow.xaml.cs`、`LogitechPlugin.cs`、`BatteryPluginBase.cs`、`MchosePlugin.cs`、`DeviceManager.cs`、`BatteryChartControl.xaml.cs` | `open` |
| **CR-35** | 罗技打开成功后清了 `Io`，没清 `OpenFailed`。当前无实际后果 | `LogitechPlugin.cs:AttachCore` | `open` |
| **CR-36** | `TryEnqueue` 失败时 `_refreshQueued` 不会复位。窗口关掉后不再收请求，当前无实际后果 | `WidgetWindow.xaml.cs:RequestRefresh`、`DevicesPage.xaml.cs:RequestRefresh` | `open` |
| **CR-39** | 贴片默认贴在托盘左边，左键被拖动吃掉，可能盖住任务栏按钮 | `TaskbarPillWindow.cs:OnLButtonDown`、`TaskbarLayout.cs:Place` | `open` |
| **CR-41** | 竖向任务栏、多屏、自动隐藏没有真机看过 | `TaskbarPillWindow.cs:TryGetTaskbar`、`TaskbarLayout.cs:Place` | `open` |
| **CR-42** | 贴片颜色跟应用主题，不跟任务栏实际明暗 | `TaskbarPillRenderer.cs:SkinFor` | `open` |
| **CR-48** | 贴片拖动手感没真机试过。留白靠 alpha=1 的命中层 | `TaskbarPillWindow.cs:OnLButtonDown` | `open` |
| **CR-50** | 日志跨午夜换文件没有单测。要测得注入时钟 | `Utils/Log.cs:EnsureWriter` | `open` |
| **CR-52** | `ShutdownLogging` 的注释仍说没人调用 `Dispose`，实际退出路径会调 | `App.xaml.cs:ShutdownLogging` | `open` |
| **CR-61** | 日志在锁里同步刷盘，UI 线程可能等磁盘。改缓冲会丢掉崩溃前最后几行，所以先不改 | `Utils/Log.cs:Emit` | `open` |
| **CR-66** | GANSS 认领把 `&mi_03#` 当硬条件，但 VID/PID 是借用号、usage 是 VIA 标准页；换固件会静默认领失败，同号 VIA 键盘可能被误认领并写帧。已加产品名兜底，仍需有设备的人确认实际产品名并抓包，清单见 `hardware/ganss.md` §6.1 | `Plugins/Ganss/GanssCodec.cs:IsSupportedInterface` | `open` |
| **CR-67** | V9 Pro 只在一个电量点（100%）验证过：非满电是否线性、`[3]` 是否充电标志、低电量有无占位帧都不知道；接收器是否回显主机写入也未确认（已加"整帧等于查询帧即丢弃"的兜底）。需有设备的人补样本，清单见 `hardware/mchose-v9-pro.md` §5 | `Plugins/Mchose/MchoseV9ProCodec.cs:TryParse` | `open` |
| **CR-69** | 社区 PR #16（设备管理）未纳入。**删除离线设备已按本项目方案落地**（2026-10-20：删除默认连带清历史 + 永久移除黑名单，见 `code-flow.md` §5.2），PR 里剩「只显示在线」「电量条对齐」仍不做 | PR #16 提交 `6908997f` | `open` |
| **CR-70** | 社区 PR #15 的 BLE 大改未纳入（`BleBatteryWatcher` +38/-22、`WatcherProps`、`DeviceKindGuesser`、`DeviceSourceInfo`、`SettingsPage`）。通用 GATT 路径已能读标准电池服务，故本轮只摘设备插件；蓝牙通道要单独一轮 | PR #15 | `open` |
| **CR-71** | 社区 issue #12（任务栏贴片避让托盘图标）只有根因与修法描述，没有代码。`TaskbarLayout.Place` 现在固定贴托盘左侧，与 CR-39 同源 | issue #12、`Services/TaskbarLayout.cs:Place` | `open` |
| **CR-72** | 社区 issue #11 现象 2（断电后死锁）未处理：涉及基类读实现，风险高于收益，需单独排期并配复现手段 | issue #11 | `open` |
| **CR-74** | 设备 ID 改端口无关键后的代价：同型号两只接收器同时插着会被并成一台（键相同）。需要时用「当前在位集合 + 端口哈希」做次级区分 | `Plugins/PluginUtils.cs:StableDeviceKey` | `open` |
| **CR-79** | **未定级，只有一次观测**：启动后前 30 分钟提交按 ~0.47MB/min 上爬（122.6→136.6），窗口隐藏时照涨；3 小时端点 134.8 未再涨，但 144 分钟无中间采样。倾向高水位，需多次运行 + 中间采样 + 换方向（Release / Win11 / GPU / 设备数）才能定性 | `Services/TaskbarPillWindow.cs:UpdateCore`、`Services/TrayService.cs:Render` | `open` |
| **CR-80** | BLE GATT 直读只 `Dispose` 了 `BluetoothLEDevice`，`svcResult.Services[0]` 与 `chrResult.Characteristics[0]` 没 Dispose（每 2–3 分钟一次，句柄小漏） | `Ble/BleBatteryWatcher.cs:ReadBatteryViaGattAsync` | `open` |
| **CR-81** | 查询型插件休眠即判离线：CHERRY 深度休眠块通道静音 → 3 轮 `ReportPollFailure` 转离线 + 熔断，唤醒后最长等 5 分钟退避才恢复。GANSS / 雷神 ML903 / AULA HERO 68 Air 同类，需要一次跨插件的「休眠不等于失联」方案；HERO 真机闲置静音、按键唤醒后固定查询恢复 | `Plugins/Cherry/CherryPlugin.cs:PollDevice`、`Plugins/Ganss/GanssPlugin.cs:PollDevice`、`Plugins/Aula/AulaPlugin.cs:PollDevice`（cherry.md §1.6、aula.md §2.4） | `open` |
| **CR-82** | CHERRY 2.4G 与有线是两台设备（`PIXIU75 Dongle` / `PIXIU 75`），显示名不同，历史库各占一行、互报离线；要合并得改设备 ID 口径 | `Plugins/Cherry/CherryPlugin.cs:BuildDisplayName`（cherry.md §1.6） | `open` |
| **CR-83** | DARKRIM 一条状态帧的尾部 CRC 与算法不符：同批另三条向量都能通过，这条实算 `0x5737`、帧里写 `0x3AEF`。疑似抓包转录错误，也可能是另一种 CRC 口径。用例已拆成 `[Fact(Skip)]` 保留证据，**不得改帧里的 CRC 值来让它变绿**；待作者真机复核 | `Tests/DarkrimCodecTests.cs:RealFrames_CrcMismatch_PendingAuthorCheck`（darkrim.md §1.2） | `open` |
| **CR-86** | 飞智 APEX5 只测过接收器被拔，**手柄关机/休眠两态没测**；现在"关机"与"睡够 3 轮无应答"在界面上同为转离线。另：蓝牙模式 `0x1003` 心跳能通但电量字段未证实，按 PID 排除 | `Plugins/Flydigi/FlydigiPlugin.cs:PollDevice`（flydigi.md §6） | `open` |
| **CR-87** | ATK N9 耳机协议已记录但**未实现**：电量走 CDC-ACM 串口，基类只有 HID 句柄；要落地需先给基类加传输抽象，且必须并进 ATK 品牌插件（§9 一行开关） | `docs/hardware/atk-n9.md` §5 | `open` |
| **CR-88** | 写通道兜底没统一：`HidDeviceHandle.SetOutputReport` 只加在 `feat/atk`（ATK 那台 dongle 的 WriteFile 不通），飞智分支只用 `WriteOutput`。两支合并时统一成「先 `WriteOutput`、失败回落 `SetOutputReport`」，并确认飞智是否也需要 | `Hid/HidDeviceInfo.cs:SetOutputReport`、`Plugins/Flydigi/FlydigiPlugin.cs:Query`（flydigi.md §2） | `open` |
| **CR-91** | Centurion 无应答时最坏 4×1500ms 读 + 500ms 写 ≈ 6.5 秒堵在 `Poll` 里（正常 8–13ms），休眠/关机期间会拖长这一轮插件 pass；未实测对其它插件轮询节奏的影响 | `Plugins/Logitech/LogitechPlugin.cs:TryReadCenturionBattery`（logitech.md §2） | `open` |
| **CR-92** | 型号归一规则（`PRO X 2` → `PRO X 2 LIGHTSPEED`）写在通用 BLE 层；且只有蓝牙被归一，2.4G 仍取 `info.Product`，两条通道显示名可能不一致 | `Ble/ClassicDeviceName.cs:NormalizeBluetoothHeadsetName`、`Plugins/Logitech/LogitechPlugin.cs:BuildDisplayName` | `open` |
| **CR-93** | 基类 `AttachAsync` / `Poll` / `ReconcileRemovals` 是 public 非 virtual，`LogitechPlugin` 只能用 `new` 隐藏，靠类重新声明 `IBatteryPlugin` 做接口重实现才生效；以基类静态类型调用会静默跳过 HID++ 分支。建议基类这三个改 virtual + 子类 override | `Plugins/BatteryPluginBase.cs:AttachAsync`、`Plugins/Logitech/LogitechPlugin.cs`（review-standard.md §2 R2） | `open` |
| **CR-94** | 迈从 G3 V2 的 `mi_01&col05`（`0xFF06/0x0002`，64/64）不是命令通道，却按"≥60 字节只走鼠标"落 `MouseV2` 兜底：每 15 秒发现拍点被写 1 条 `0x11/0x06` 触发帧并阻塞约 6 秒（3 秒被动监听 ×2）。建议对已知 Gmouse PID 的非命令厂商接口返回 `None`，或只被动听不写 | `Plugins/Mchose/MchoseInterfaceClassifier.cs:Classify`、`Plugins/Mchose/MchosePlugin.cs:TryMouseV2`；接口地图见 `docs/hardware/mchose.md` §4.1、§4.5 | `open` |
| **CR-95** | 采集的 `ReadFailed` 是死枚举：`ReadInputAsync` 把读异常吞成 `null`，USB/HID 来源分不清「超时」和「读失败」，只剩 2 秒一次的 `IsPresent()` 兜底。要修得给 `HidDeviceHandle` 加错误回调或结果类型（会影响全部插件调用方），故单独一轮 | `Capture/UsbHidCaptureSource.cs:PumpAsync`、`Hid/HidDeviceInfo.cs:ReadInputAsync`（capture.md §8） | `open` |
| **CR-96** | 采集句柄的释放不保证：`UsbHidCaptureSource.Dispose()` 是空实现，句柄在 `RunAsync` 的 `using` 里；pump 卡在同步 `HidD_GetFeature` 时，「暂停 = 释放句柄」不成立，`StopSourceAsync` 3 秒超时后来源仍在跑 | `Capture/Hid/UsbHidCaptureSource.cs:RunAsync`、`Capture/CaptureSession.cs:StopSourceAsync` | `open` |
| **CR-98** | ATK 显示名依赖本体：接收器只报通用名 `Wireless mouse 8k dongle-M`，型号只能从插线时枚举出的本体取，`ModelNames` 是进程内缓存 → 重启后名字退回通用名，要再插一次线或用户自定义改名才恢复 | `Plugins/Atk/AtkPlugin.cs:BuildDisplayName`、`Plugins/Atk/AtkPlugin.cs:ModelNames`（atk.md §4.1） | `open` |
| **CR-99** | ATK 设备归一组是硬编码配对表（`DeviceGroups`：`101B`/`101A`/`1015` → promax、`1303`/`12A9` → v3、`11D9`/`11E4` → f1ult、`1278`/`1293` → a9air），新 PID 必须补一行；未登记 PID 在插充电线时会多出一条记录（上游路径哈希行为）。同机两只同型号鼠标会被并成一条，与 CR-74 同源（`SerialNumber` 为空时配对只能按型号组，见 **CR-112**）。补采 SN 后若要按 SN 变严，得在 `IsCandidate` 的"已登记分组"分支之前加一道同组同 SN 校验 | `Plugins/Atk/AtkPlugin.cs:DeviceGroups`（atk.md §4） | `open` |
| **CR-100** | HECATE 主动查询最坏约 2.9 秒堵在 `Poll` 里（3 次 × (500ms 写超时 + 750ms 听) + 2 × 300ms 间隔），鼠标关机/深度休眠时每轮都走满，与 CR-91 同源；未实测对其它插件轮询节奏的影响 | `Plugins/Hecate/HecatePlugin.cs:PollDevice`、`QueryAsync`（hecate.md §3） | `open` |
| **CR-102** | 插件级策略无法按型号分化：`SupportsPassiveListen` / `SupportsFastDiscovery` / `Offline` / `MaxPlausibleStepPercent` 都是插件级，狼蛛 G9（被动、进 15 秒档、沉默 10 秒离线、单步 100）与 F99 Pro/HERO（会话、不进档、沉默不判离线、单步 20）只能拆成两个插件类共用一个 `Name`。要做「一品牌一插件类」得先把这四个开关改成按协议可覆盖 | `Plugins/BatteryPluginBase.cs`、`Plugins/Aula/AulaG9Plugin.cs`（aula.md §3） | `open` |
| **CR-103** | 鲲鹏20 双通道归一（`betop:kunpeng20`）后拔掉一侧：拔除事件立即把共享条目标离线，幸存通道下一帧要等主轮询拍（≤60 秒）才写回在线，出现假离线窗口；修复方向是组内一通道被拆时立即核实幸存通道 | `Plugins/BatteryPluginBase.cs:ReconcileRemovals`、`docs/hardware/betop.md` §1.8.2 | `open`（原 PR #60 编号与 dev 冲突，合入时顺延） |
| **CR-104** | 北通熔断不生效：`CountPollFailures=false` 且未接 `ReportPollFailure`，手柄关机静默时有线兜底查询 1 帧/60 秒永不自动停写；等真机休眠样本决定是否接线 | `Plugins/Betop/BetopPlugin.cs:Offline`（betop.md §1.7） | `open`（原 PR #60 编号与 dev 冲突，合入时顺延） |
| **CR-105** | `PidFromPath` 在 `BetopDeviceIdentity` 与 `AtkPlugin` 各有一份（同正则同逻辑），可抽公共 util；不阻塞 | `Plugins/Betop/BetopDeviceIdentity.cs:PidFromPath`、`Plugins/Atk/AtkPlugin.cs:PidFromPath` | `open`（原 PR #60 编号与 dev 冲突，合入时顺延） |
| **CR-106** | 北通新批次 dongle（`20DD:5128` KP20 DONGLE、`20DD:5130` 底座）只回 `02 CD 09 0A 08 08 …`，header 不在已知命令表里、`ParseStatus` 不认 → 探测失败、设备不进列表。疑似「dongle 在位、手柄未连接」占位帧但未证实（`20BC:5130` 同场景回的是 `02 15`）；须用设备诊断页采手柄开机/关机两份原始报文才能定 byte 含义 | `Plugins/Betop/BetopCodec.cs:ParseStatus`（betop.md §1.8.4） | `open` |
| **CR-107** | 贴片窗口残留的**单点成因未定论**：已按四条缺口全堵（去抖、清扫残留、单实例约束、`Dispose` 走 finally），但没在复现时抓到到底是漏销毁还是实例被覆盖。下次出现"贴片多一个"先跑 `scripts/check-taskbar-pill.py` 数窗口，再看日志里的 `任务栏贴片：建窗` / `清掉残留窗口` 两行 | `Services/TaskbarPillWindow.cs:ReleaseWindow`、`Services/TaskbarPillManager.cs:Reconcile`（runtime-pitfalls.md §16.12） | `open` |
| **CR-110** | 半开探活在 15 秒发现线程上**同步**调 `PollDevice`（ATK 等应答 ≤700ms/台），同机多台设备同时熔断会把一轮发现叠加拖慢 N×0.7s；目前最多 2 台（鼠标+键盘）可接受。铺开到多设备插件前应改成时间预算——**不要用"一轮只探一台"**，那会把轮转周期乘上设备数（30s 间隔 + 15s 节拍，两台就变 60s 一轮） | `Plugins/BatteryPluginBase.cs:RunHalfOpenProbe`、`Devices/DeviceManager.cs:DiscoverOnlyAsync`（runtime-pitfalls.md §23） | `open` |
| **CR-112** | **已修（#64 `a0917b3`），只差真机验**：`HidDeviceScanner` 补调 `HidD_GetSerialNumberString`，`SerialNumberWiringTests` 4 条源码级断言把链路钉住（`runtime-pitfalls.md` §24）。**待验点**：VGN 的 `SkipPathsForThisPass` 由此**首次真正生效**（此前 `SameSerial` 恒 false、一次也没跑过），插线 + 接收器同时在场时不再出两行——属行为变更，需真机验蜻蜓 F2 / F1 V5 插线场景，验过即关闭 | `Hid/HidDeviceScanner.cs`、`Tests/SerialNumberWiringTests.cs`（vgn.md 配对、atk.md §4） | `doing` |
| **CR-113** | ATK 系列的**充电读数虚高约 15 pp**（A9 Air 大师版+ 实测：有线充电 95% / 4276 mV ↔ 拔线放电 80% / 4213 mV，电压只差 63 mV）。固件按电压估电量、充电端电压抬升导致虚高，**codec 不应修正**（会掩盖真实电压），但用户会在插拔瞬间看到 95% ↔ 80% 跳变，容易误判成插件读错。若要更平滑，应在展示层做"同一设备充电态读数上限夹到上次放电读数 + 一个迟滞"，属产品决策而非协议问题 | `Plugins/Atk/AtkCodec.cs`（atk.md §2 实测表） | `open` |
| **CR-114** | 插件**学到的显示名只在进程内**（`AtkPlugin.ModelNames`）：无线态显示接收器通用串 `Wireless mouse 8k dongle-L`，重启后要再插一次线才恢复真实机型。**不为此新增持久化机制**——项目当前没有"插件自动学习并持久化显示名"的先例（`DeviceCustomizations` 是用户手动设置），且已有替代方案：因为两种形态归一到同一个设备 ID，用户在设置页手动改名一次对插不插线同时生效（atk.md §4.1）。若将来要做，`AppSettings` 需加一个 `LearnedDeviceNames`（按设备 ID），并注意 `Load()` 要补 null、老配置缺键不报错 | `Plugins/Atk/AtkPlugin.cs:BuildDisplayName`（atk.md §4.1） | `open` |
| **CR-115** | `ExistingPathOfSameDevice` 去重**保留先认领的那条**，不是"更好"的那条。凡"两条链路里有一条更早常驻"的情形（接收器开机就插着、本体插线才枚举），去重必然选中错误的一条：ATK 实测表现为插线后设备页显示**离线**——能读的本体被释放、已沉默的接收器留着并熔断。修法是插件覆写 `SkipPathsForThisPass` 显式摘掉不该认领的那条，**不要指望去重顺序** | `Plugins/BatteryPluginBase.cs:ExistingPathOfSameDevice`（atk.md §4.4） | `open` |
| **CR-116** | 墨将 GALE2 没做固定组 ID：`MojhonPlugin` 不覆写 `BuildDeviceId`，ID 是路径哈希（实测 `mojhon:201ACABD9282B605`），而有线 `8&74fbec5&0&0000` 与接收器 `8&320bfad6&0&0000` **是两条路径** → 两种形态各占一条记录、历史分裂；同型号两台手柄也无法归并。ATK 用 `DeviceGroups` 固定 ID 解决了同一问题（CR-99），墨将未做。修法要按 `VID+PID`（两形态同为 `122C`）归一 | `Plugins/Mojhon/MojhonPlugin.cs`（未覆写 `BuildDeviceId`）、`Plugins/Atk/AtkPlugin.cs:DeviceGroups`（mojhon.md §7.4） | `open` |
| **CR-117** | 早期路径哈希 ID 的历史**不迁移**：CR-116 若落地，老库里 `mojhon:<路径哈希>` 的采样不会自动接到新 ID 上（先例 #65 是作者本机手动 `UPDATE samples SET device_id=...`，1497 条）。要么发版时给迁移脚本，要么在 release-notes 写明"历史断一次"。同一笔债在 CR-82（CHERRY 两形态）上也有 | `Storage/HistoryStore.cs`、`Plugins/PluginUtils.cs:StableDeviceKey`（mojhon.md §7.4） | `open` |
| **CR-118** | 墨将**跨模式百分比差异的成因未定**：2.4G 放电 97%（19:52）→ 换口 96%（20:03）↔ 有线充电 99%→100%（20:07 / 20:09），插线瞬间跳 3~4 pp。可归因于充电端电压抬升（与 CR-113 同类），但**没有"拔线回到 2.4G 后是否回落"的对照采样**，故"充电抬升"与"固件按连接形态报不同值"未能区分。不阻塞（方向正确、无虚报在线），要定论得补一次拔线后连采 | `Plugins/Mojhon/MojhonCodec.cs`（mojhon.md §7.4 时间线） | `open` |
| **CR-119** | **罗技插件在 G102 Lightsync 上无限递归 → 栈溢出 → 进程直接退出**（1.2.1 / 1.2.3 均复现，事件查看器 `0xc00000fd`）。默认最小化到托盘、窗口从不出现，用户看到的是「双击没反应 / 托盘一闪就没」。日志末尾反复 `[WARN] WriteOutput 异常 len=20: …参数错误`，崩溃转储在 `%LOCALAPPDATA%\CrashDumps`（1.2.1 从 10-23 起累积 10 个）。触发点在罗技的「跳过冗余接口（PreferLong）」路径，**递归环未定位**，需按转储栈复现。**这是目前 open issue 里唯一的进程级崩溃** | issue #55（`Plugins/Logitech/LogitechPlugin.cs`） | `open` |
| **CR-120** | 接在**扩展坞**上的 2.4G 设备不识别（直插是否正常也未确认）。issue 只有一句话，无型号、无日志——先要一份设备诊断页的采集结果，才能判断是枚举路径差异、还是认领判据把坞上的接口滤掉了 | issue #4（`Hid/HidDeviceScanner.cs`、`capture.md` §7） | `open` |
| **CR-121** | **便携版的数据和日志仍落在 `%LOCALAPPDATA%`**，应在 exe 所在目录。`AppPaths` 现在只按 `#if DEBUG` 分两档（Debug → bin 下 `data\`，Release → `%LOCALAPPDATA%\VigilWatt`），没有「便携模式」这一档。要做得多一个判定（exe 旁标记文件，或按 exe 目录可写性），且老用户升级时已有数据不能丢 | issue #81（`AppPaths`、`dev/build.md` §3.1、`runtime-pitfalls.md` §20） | `open` |
| **CR-122** | **多显示器不同缩放比例下小组件缩放不对**。贴片是原生子窗口（`SetParent` 进 `Shell_TrayWnd`）、小组件走 `TransparentWindowSurface`，两者对 DPI 的处理本来就不同。issue 没给截图和显示器配置，需先复现：跨屏拖动、各屏缩放不同、拖完再改缩放 | issue #70（`Services/WidgetWindow.xaml.cs`、`runtime-pitfalls.md` §17） | `open` |
| **CR-123** | issue #41 **混了三个互不相关的问题**，都缺复现信息：① 貔貅75 键盘不识别；② 安装版装完打开即闪退但进程残留（完全卸载重装才恢复）；③ 英菲克 2.4G 鼠标搜不到。②最像真 bug（配置损坏或单实例残留），需先要日志与 `settings.json` | issue #41 | `open` |

| **CR-124** | **离线期间读数过滤器基准不重置**：`MarkOffline` 只翻 `IsOnline`，`TrackedDevice.Filter` 的 `LastAcceptedPercent` 留着离线前的值 → 设备重新上线后若电量已大幅变化（充电、换电池、误采信的占位值），会被"变化超 `MaxPlausibleStepPercent` 需持续 `AnomalyPersistWindow`（5 分钟）"挡住，界面卡在旧值最多 5 分钟，且被动流每秒一帧时刷满 `读数异常已过滤`（2026-10-30 19:38 北通 T9 13%→73% 实测连刷 30 余条）。修法：`MarkOffline` 或 `TryApplyReading` 的"离线→在线"那一拍重新 `Seed`。**属基类行为、影响全部插件**，改前要按插件逐个过一遍"占位帧频繁闪断"的品牌（迈从、北通、雷柏）。北通 T9 已用 500 ms 稳定判定绕过这一条（`hardware/betop.md` §1.4.3），本条针对其余插件 | `Plugins/BatteryPluginBase.cs:MarkOffline` / `TryApplyReading`、`Plugins/BatteryReadingFilter.cs:Seed`（`hardware/betop.md` §1.4.2） | `open` |

| **CR-126** | **英菲克 IN9 的熔断不覆盖「帧无效」这一类失败**：`PollDevice` 在 `TryParse` 失败（读到帧但全 0 / 截断）时既不 `ReportPollFailure` 也不 `ReportPollAnswered`，而 `Offline.CountPollFailures = false` 又关掉了基类兜底（`BatteryPluginBase.cs:736`）→ `TripBreaker` 的两条触发路径（742 自动记账、903 `ReportPollFailure`）都不走，`CanWrite` 只看 `WritesDisabled`，于是永远为真。**触发条件很窄**：只有 `SetFeature` 持续成功、`GetFeature` 却持续回全 0 才会每轮照写 1~2 条（正常"序号重复→清寄存器"已被递增序号规避）；更常见的"写失败 + 读全 0"只是每轮 1 次只读。限频没破、写的是官方同款帧，故定 **P2**，不是安全违规。修法：解析失败也 `ReportPollFailure`，或单列"写成功但读无效"档位。**2026-10-31 用户决定保持现状**：每轮重试优先于熔断，改与不改都不影响读数正确性 | `Plugins/Inphic/InphicIn9Plugin.cs:PollDevice`、`BatteryPluginBase.cs:ReportPollFailure` / `CanWrite`（`hardware/inphic.md` §2.4） | `open` |

| **CR-132** | **ATK A9 Ultra Max 2.0（`0x11FE`）只需登记 PID**：VID `0x373B` / FF02 口 / 17 字节 `report 0x08` / 校验基数 0x4D，与 11D9、1303 完全同构，issue #71 的六条实测帧校验全通过。**本体 PID 未知**（用户只给了接收器），插数据线时会多出一条记录，与 CR-99 同源 | issue #71、`Plugins/Atk/AtkPlugin.cs:DeviceGroups`（atk.md §10） | `doing` |
| **CR-133** | **狼途 T98 零写入被动帧已落地**：`1A2C:8FFF` + `0xFF02/0x02` + 65 字节，帧 `00 AA 55 CC 33 0E <pct> 01`，`0xFF` 是插拔瞬间哨兵必须丢。**未验证项**：`Silence = 300 秒` 是按 issue #40 的「休眠静音 4 分钟」定的，没有第二份休眠样本；`[7]` 已证伪不是充电位，故充电状态恒 `Unknown` | issue #40、`Plugins/Langtu/LangtuT98Plugin.cs`（langtu.md §1） | `doing` |
| **CR-134** | **IQUNIX Q101（`369B:0184`）只有单点样本**：参考 36%，`byte[8]=0x24` 精确命中但 `byte[7]=0x25` 也接近，两个字节无法区分；充电窗口该字节变 `0xBC`(188)，语义未定。要有第二个电量点（70~80%）才能定稿 | issue #87、采集包 `20261031-184729-DBE2` | `open` |
| **CR-135** | **西伯利亚 X7 Ultra Max（`36F9:AA20`）只有单点样本**：参考 20%，`byte[6]=0x14` 命中，但另有一帧 `0x15`；`byte[5]` 在 01/00 间变化，作用未定。同样要第二个电量点 | issue #83、采集包 `vigilwatt-capture` | `open` |
| **CR-136** | **飞智冰原狼3 采集采错接口**：`04B4:2412` 的 `MI_02` 上 7123 帧全是同一条 `04 FE 66 …`，0 变化，是配置通道。且 VID 与已完成的 APEX5（`37D7:2501`）不同、帧头也不同（`04 FE 66` vs `5A A5`），**不能复用 `FlydigiCodec`**，需重采 + 独立判据 | issue #82、`Plugins/Flydigi/`（flydigi.md） | `open` |
| **CR-137** | **七个适配请求正文零证据**（#78 雷蛇 DeathAdder V2 X / #73 迈从 G75V2 / #69 前行者 X23+小鸡超新星 / #68 ilovbee b98 / #39 迈从K980+毒刺+冰原狼2 / #5 iKF / #4 扩展坞）：全部只有一句话或空正文，无 VID/PID、无采集包。先要数据，不做协议猜测 | issue #78 #73 #69 #68 #39 #5 #4 | `open` |

| **CR-138** | **罗技 G502 LIGHTSPEED 插数据线时扫出 6 台设备**（拔掉接收器也一样）。用户附了完整日志与两份分析报告，未复核。与 CR-112 / CR-115 同源：插线后接收器与本体两条路径同时在场，去重口径按「先认领者保留」而非「能读的那条」。**顺带能真机验证 CR-112 那个待验点**（VGN 配对首次生效后插线是否出两行） | issue #84、`Plugins/BatteryPluginBase.cs:ExistingPathOfSameDevice`、`Plugins/Logitech/LogitechPlugin.cs` | `open` |
| **CR-139** | **任务栏贴片只能居中，没有左/中/右对齐设置**：贴片内设备全居中，调好位置后再新增设备就会往两边撑、和任务栏内容重合。需求明确、实现轻（`TaskbarLayout.Place` 加一个对齐枚举 + 设置项），但属 UI 改动，按 `AGENTS.md` §3 要先出方案 | issue #85、`Services/TaskbarLayout.cs:Place`、`Services/TaskbarPillRenderer.cs` | `open` |
| **CR-140** | **桌面小组件不能轮播**：设备多时要么全显示占地方，要么只显示一台看不到其它。需求是「只显示一台 + 定时轮播」并带间隔设置。要先定轮播期间历史/图表怎么处理，属产品决策 | issue #66、`Services/WidgetWindow.xaml.cs` | `open` |
| **CR-141** | **插件按需下载（issue #63）作者已部分否决**：Windows 能枚举 USB/HID 但不保证认出具体型号，同 VID/PID 可对应多个型号、一个接收器可能暴露多个接口，认错会匹配到不兼容插件甚至影响设备使用。现有方案是「插件去认领设备」。**保留的唯一价值**是设备数量增多后的包体积/启动开销，是否做取决于用户 | issue #63、`Devices/DeviceManager.cs:DiscoverPlugins` | `open` |
| **CR-142** | **可复用 OpenLogi 收集的外设 HID 特征库**：对方已积累大量 2.4G / 蓝牙设备的 VID/PID 与接口指纹。价值在「认领判据的交叉校验」与「新设备快速定位接口」，**不是**直接抄协议。要先确认对方仓库许可与数据格式，再决定是人工比对还是写导入脚本 | issue #74、`docs/hardware/protocols.md`、`docs/hardware/sources.md` | `open` |

## 重构候选（未做）

纯结构调整，没有对应的界面自动化。顺序：D1 剩下的基类 → D3 → D4 / D5 → 再处理 CR-16。

| 编号 | 抽什么 | 状态 |
|---|---|---|
| **D1** | 两个 watcher 的 `Start` / `HandleRemoved` / `Snapshot`。属性读取已经抽走 | `open` |
| **D3** | 图表的坐标和命中，抽成纯函数 | `open` |
| **D4** | 小组件的排序和宽度，抽出视图模型 | `open` |
| **D5** | 设置页保存时的副作用，抽出视图模型 | `open` |
| **D6** | 各插件「发帧再读到截止」的循环（现有 Cherry `RequestBlock`、Pulsar `Query`、迈从、GANSS 各一套） | `open` |
