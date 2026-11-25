# BACKLOG 分诊表（2026-11-23）

> 依据：代码核对 + 现有证据。2026-11-23 已按本表执行：7 条 WONTFIX 移入归档，11 条 SOMEDAY 降为 P3；其余保持。
> 编号已按 2026-11-23 重编号：CR-172~CR-179。
> 明细的“现状”列是执行前快照；WONTFIX 已移入 `archive/BACKLOG-CLOSED.md`，SOMEDAY 已在 `BACKLOG.md` 改为 P3。

## 汇总

| 分类 | 数量 | 处理建议 |
|---|---|---|
| VERIFY | 13 | 保持 doing |
| BUG | 23 | 保留 open，修代码 |
| EVID | 33 | 保留 open，先取证据 |
| WONTFIX | 7 | 建议 wontfix |
| SOMEDAY | 11 | 建议 P3/someday |
| REFACTOR | 12 | 保留 open，重构 |

## 明细

| 编号 | P | 现状 | 建议 | 一句话 |
|---|---|---|---|---|
| CR-163 | P1 | doing | VERIFY：保持 doing | 罗技 `0x0005` 设备名读不到导致 G 系显示占位名「罗技设备 N」（2026-11-13 C53F/G304 日志）。已修两处：名字… |
| CR-164 | P1 | doing | VERIFY：保持 doing | 罗技电量级联两处口径与参考实现（OpenMouse `hidpp.js`）不一致：① `0x1004` func 0 是 GetCapabi… |
| CR-165 | P1 | doing | VERIFY：保持 doing | 罗技 feature 下标只在发现时解析一次并永久缓存（0 表示"不支持"）：一次超时就把该电量路径永久禁用，直到接收器重插或重启；参考实现… |
| CR-178 | P1 | doing | VERIFY：保持 doing | 迈从 CZ 状态块没有独立的"已充满"字段：`[0x20]` 只报充电中 + 电量百分比，"满电"靠 `MchoseSpecs.Resolv… |
| CR-66 | P1 | doing | VERIFY：保持 doing | GANSS 认领把 `&mi_03#` 当硬条件，但 VID/PID 是借用号、usage 是 VIA 标准页；换固件会静默认领失败，同号 … |
| CR-67 | P1 | doing | VERIFY：保持 doing | V9 Pro 只在一个电量点（100%）验证过：非满电是否线性、`[3]` 是否充电标志、低电量有无占位帧都不知道；接收器是否回显主机写入也… |
| CR-107 | P1 | doing | VERIFY：保持 doing | 贴片窗口残留的**单点成因未定论**：已按四条缺口全堵（去抖、清扫残留、单实例约束、`Dispose` 走 finally），但没在复现时抓… |
| CR-112 | P1 | doing | VERIFY：保持 doing | **已修（#64 `a0917b3`），只差真机验**：`HidDeviceScanner` 补调 `HidD_GetSerialNumbe… |
| CR-119 | P0 | doing | VERIFY：保持 doing | **罗技插件在 G102 Lightsync 上无限递归 → 栈溢出 → 进程直接退出**（1.2.1 / 1.2.3 均复现，`0xc00… |
| CR-121 | P1 | doing | VERIFY：保持 doing | **便携版的数据和日志仍落在 `%LOCALAPPDATA%`**，应在 exe 所在目录。**2026-11-01 已修**：`AppPa… |
| CR-124 | P1 | doing | VERIFY：保持 doing | **离线期间读数过滤器基准不重置**：`MarkOffline` 只翻 `IsOnline`，`TrackedDevice.Filter` … |
| CR-132 | P1 | doing | VERIFY：保持 doing | **ATK A9 Ultra Max 2.0（`0x11FE`）只需登记 PID**：VID `0x373B` / FF02 口 / 17 … |
| CR-133 | P1 | doing | VERIFY：保持 doing | **狼途 T98 零写入被动帧已落地**：`1A2C:8FFF` + `0xFF02/0x02` + 65 字节，帧 `00 AA 55 C… |
| CR-166 | P2 | open | BUG：保留 open，修代码 | 罗技直连（有线）与蓝牙 HID++ 都没有链路标签：`LogitechConnectionPolicy` 对 Direct 恒 Unknow… |
| CR-167 | P2 | open | BUG：保留 open，修代码 | 罗技 HID++ 错误帧（`0x8F`/`0xFF`）与超时都返回 null：分不清"功能不支持"与"设备没应答"，所有识别失败都成黑盒——… |
| CR-161 | P2 | open | BUG：保留 open，修代码 | `DeviceImageCatalog.ColorDto` 只解析 `source`，而迈从 manifest 写的是 `thumbnail… |
| CR-35 | P2 | open | BUG：保留 open，修代码 | 罗技打开成功后清了 `Io`，没清 `OpenFailed`。当前无实际后果 |
| CR-36 | P2 | open | BUG：保留 open，修代码 | `TryEnqueue` 失败时 `_refreshQueued` 不会复位。窗口关掉后不再收请求，当前无实际后果 |
| CR-52 | P2 | open | BUG：保留 open，修代码 | `ShutdownLogging` 的注释仍说没人调用 `Dispose`，实际退出路径会调 |
| CR-80 | P1 | open | BUG：保留 open，修代码 | BLE GATT 直读只 `Dispose` 了 `BluetoothLEDevice`，`svcResult.Services[0]` 与… |
| CR-81 | P1 | open | BUG：保留 open，修代码 | 查询型插件休眠即判离线：CHERRY 深度休眠块通道静音 → 3 轮 `ReportPollFailure` 转离线 + 熔断，唤醒后最长等… |
| CR-91 | P1 | open | BUG：保留 open，修代码 | Centurion 无应答时最坏 4×1500ms 读 + 500ms 写 ≈ 6.5 秒堵在 `Poll` 里（正常 8–13ms），休眠… |
| CR-93 | P1 | open | BUG：保留 open，修代码 | 基类 `AttachAsync` 已改 public virtual；`Poll` / `ReconcileRemovals` 仍是 pub… |
| CR-94 | P1 | open | BUG：保留 open，修代码 | 迈从 G3 V2 的 `mi_01&col05`（`0xFF06/0x0002`，64/64）不是命令通道，却按"≥60 字节只走鼠标"落 … |
| CR-174 | P2 | open | BUG：保留 open，修代码 | 采集的 `ReadFailed` 是死枚举：`ReadInputAsync` 把读异常吞成 `null`，USB/HID 来源分不清「超时」… |
| CR-175 | P2 | open | BUG：保留 open，修代码 | 采集句柄的释放不保证：`UsbHidCaptureSource.Dispose()` 是空实现，句柄在 `RunAsync` 的 `usin… |
| CR-100 | P1 | open | BUG：保留 open，修代码 | HECATE 主动查询最坏约 2.9 秒堵在 `Poll` 里（3 次 × (500ms 写超时 + 750ms 听) + 2 × 300m… |
| CR-103 | P1 | open | BUG：保留 open，修代码 | 鲲鹏20 双通道归一（`betop:kunpeng20`）后拔掉一侧：拔除事件立即把共享条目标离线，幸存通道下一帧要等主轮询拍（≤60 秒）… |
| CR-104 | P1 | open | BUG：保留 open，修代码 | 北通熔断不生效：`CountPollFailures=false` 且未接 `ReportPollFailure`，手柄关机静默时有线兜底查… |
| CR-110 | P1 | open | BUG：保留 open，修代码 | 半开探活在 15 秒发现线程上**同步**调 `PollDevice`（ATK 等应答 ≤700ms/台），同机多台设备同时熔断会把一轮发现… |
| CR-115 | P1 | open | BUG：保留 open，修代码 | `ExistingPathOfSameDevice` 去重**保留先认领的那条**，不是"更好"的那条。凡"两条链路里有一条更早常驻"的情形… |
| CR-116 | P1 | open | BUG：保留 open，修代码 | 墨将 GALE2 没做固定组 ID：`MojhonPlugin` 不覆写 `BuildDeviceId`，ID 是路径哈希（实测 `mojh… |
| CR-117 | P1 | open | BUG：保留 open，修代码 | 早期路径哈希 ID 的历史**不迁移**：CR-116 若落地，老库里 `mojhon:<路径哈希>` 的采样不会自动接到新 ID 上（先例… |
| CR-126 | P2 | open | BUG：保留 open，修代码 | **英菲克 IN9 的熔断不覆盖「帧无效」这一类失败**：`PollDevice` 在 `TryParse` 失败（读到帧但全 0 / 截断… |
| CR-151 | P1 | open | BUG：保留 open，修代码 | **`SkipPathsForThisPass` 跳过的接口不报离线，会留"在线幽灵行"**：基类把它从 `_devices` 删掉、标 `… |
| CR-154 | P1 | open | BUG：保留 open，修代码 | **Direct 0xFF 可能把接收器 secondary interface 认成外设**：当前只要求 Root.Ping 成功，没有 … |
| CR-169 | P2 | open | EVID：保留 open，先取证据 | **北通 `0x501E` / `0x502C` 未进运行目录**：BetopGame 2.6.2 包里这两个 PID 的 `Registe… |
| CR-170 | P2 | open | EVID：保留 open，先取证据 | **北通宙斯 `0x10` 占位帧现在无条件判离线**：旧判据要求 `byte1==0 && byte23==0x10`，`link.tab… |
| CR-171 | P2 | open | EVID：保留 open，先取证据 | **北通 5 个歧义 PID 没有专属产品图**：`5125/5127/5145/5146/5074` 的 `model` 为空，产品串只能… |
| CR-160 | P2 | open | EVID：保留 open，先取证据 | 罗技 G 系列产品图已用 OpenMouse 图床补 16 个型号，但 `displayNames` 是从它的名字正则**反推**的、未经真… |
| CR-149 | P2 | open | EVID：保留 open，先取证据 | 迈从 CZ 分块读只验证了第一段（`0x00..0x37`）：`0x38..0x3F` 那一段官方也读，但电量在 `0x20`，其内容语义未… |
| CR-84 | P1 | open | EVID：保留 open，先取证据 | AKG N9 Hybrid 已见 50% / 100% 读数，尚未核对官方 App、连续电量变化、关机与长期运行；用户观察充电时无法工作并取… |
| CR-60 | P1 | open | EVID：保留 open，先取证据 | 贴片右键菜单的 owner 是任务栏子窗口，弹出和关闭未真机看 |
| CR-63 | P1 | open | EVID：保留 open，先取证据 | 贴片与 explorer 输入队列串在一起，UI 线程长阻塞可能冻住任务栏。未实测 |
| CR-39 | P1 | open | EVID：保留 open，先取证据 | 贴片默认贴在托盘左边，左键被拖动吃掉，可能盖住任务栏按钮 |
| CR-41 | P1 | open | EVID：保留 open，先取证据 | 竖向任务栏、多屏、自动隐藏没有真机看过 |
| CR-42 | P2 | open | EVID：保留 open，先取证据 | 贴片颜色跟应用主题，不跟任务栏实际明暗 |
| CR-48 | P2 | open | EVID：保留 open，先取证据 | 贴片拖动手感没真机试过。留白靠 alpha=1 的命中层 |
| CR-72 | P1 | open | EVID：保留 open，先取证据 | 社区 issue #11 现象 2（断电后死锁）未处理：涉及基类读实现，风险高于收益，需单独排期并配复现手段 |
| CR-79 | P1 | open | EVID：保留 open，先取证据 | **未定级，只有一次观测**：启动后前 30 分钟提交按 ~0.47MB/min 上爬（122.6→136.6），窗口隐藏时照涨；3 小时端… |
| CR-83 | P1 | open | EVID：保留 open，先取证据 | DARKRIM 一条状态帧的尾部 CRC 与算法不符：同批另三条向量都能通过，这条实算 `0x5737`、帧里写 `0x3AEF`。疑似抓包… |
| CR-172 | P1 | open | EVID：保留 open，先取证据 | 飞智 APEX5 只测过接收器被拔，**手柄关机/休眠两态没测**；现在"关机"与"睡够 3 轮无应答"在界面上同为转离线。另：蓝牙模式 `… |
| CR-176 | P1 | open | EVID：保留 open，先取证据 | ATK 显示名依赖本体：接收器只报通用名 `Wireless mouse 8k dongle-M`，型号只能从插线时枚举出的本体取，`Mod… |
| CR-177 | P2 | open | EVID：保留 open，先取证据 | ATK 设备归一组是硬编码配对表（`DeviceGroups`：`101B`/`101A`/`1015` → promax、`1303`/`… |
| CR-106 | P1 | open | EVID：保留 open，先取证据 | 北通新批次 dongle（`20DD:5128` KP20 DONGLE、`20DD:5130` 底座）只回 `02 CD 09 0A 08… |
| CR-118 | P2 | open | EVID：保留 open，先取证据 | 墨将**跨模式百分比差异的成因未定**：2.4G 放电 97%（19:52）→ 换口 96%（20:03）↔ 有线充电 99%→100%（2… |
| CR-120 | P1 | open | EVID：保留 open，先取证据 | 接在**扩展坞**上的 2.4G 设备不识别（直插是否正常也未确认）。issue 只有一句话，无型号、无日志——先要一份设备诊断页的采集结果… |
| CR-122 | P1 | open | EVID：保留 open，先取证据 | **多显示器不同缩放比例下小组件缩放不对**。贴片是原生子窗口（`SetParent` 进 `Shell_TrayWnd`）、小组件走 `T… |
| CR-123 | P1 | open | EVID：保留 open，先取证据 | issue #41 **混了三个互不相关的问题**，都缺复现信息：① 貔貅75 键盘不识别；② 安装版装完打开即闪退但进程残留（完全卸载重装… |
| CR-134 | P1 | open | EVID：保留 open，先取证据 | **IQUNIX Q101（`369B:0184`）只有单点样本**：参考 36%，`byte[8]=0x24` 精确命中但 `byte[7… |
| CR-135 | P1 | open | EVID：保留 open，先取证据 | **西伯利亚 X7 Ultra Max（`36F9:AA20`）只有单点样本**：参考 20%，`byte[6]=0x14` 命中，但另有一… |
| CR-136 | P2 | open | EVID：保留 open，先取证据 | **飞智冰原狼3 采集采错接口**：`04B4:2412` 的 `MI_02` 上 7123 帧全是同一条 `04 FE 66 …`，0 变… |
| CR-137 | P2 | open | EVID：保留 open，先取证据 | **六个适配请求正文零证据**（#78 雷蛇 DeathAdder V2 X / #73 迈从 G75V2 / #69 前行者 X23+小鸡… |
| CR-138 | P1 | open | EVID：保留 open，先取证据 | **罗技 G502 LIGHTSPEED 插数据线时扫出 6 台设备**（拔掉接收器也一样）。用户附了完整日志与两份分析报告，未复核。与 C… |
| CR-152 | P2 | open | EVID：保留 open，先取证据 | **迈从 0x4D 的 DPI 原始值是否需换算未确认**：`dpiMagicNum` / `dpiSlider` 只在官方 `mouseD… |
| CR-153 | P2 | open | EVID：保留 open，先取证据 | **C52B/C532 的 Unifying 配对布局未裁决**：VigilWatt 用 `0x20+n-1` / `resp[5..6]`… |
| CR-155 | P2 | open | EVID：保留 open，先取证据 | **罗技回报率读不到**：MX Master 3S 对 `0x8060` / `0x8061` 都不应答，回报率只能留空。需要 G 系（有 … |
| CR-156 | P1 | open | EVID：保留 open，先取证据 | **罗技各族槽位连接推断缺真机验证**：2026-11-11 已按目录族把门全量放开（Bolt / Unifying / LightSpee… |
| CR-158 | P2 | open | EVID：保留 open，先取证据 | **C548 Bolt 配对信息无回包**：`0x83B5/0x51`、`0x61` 均 3 秒无应答，`TryReadPairingInf… |
| CR-57 | P2 | open | WONTFIX：建议 wontfix | 小组件自绘投影未做。原判据基于 Win10 19045，本机已升到 26200（Win11），需按新系统重判 |
| CR-70 | P2 | open | WONTFIX：建议 wontfix | 社区 PR #15 的 BLE 大改未纳入（`BleBatteryWatcher` +38/-22、`WatcherProps`、`Devi… |
| CR-71 | P1 | open | WONTFIX：建议 wontfix | 社区 issue #12（任务栏贴片避让托盘图标）只有根因与修法描述，没有代码。`TaskbarLayout.Place` 现在固定贴托盘左… |
| CR-102 | P2 | open | WONTFIX：建议 wontfix | 插件级策略无法按型号分化：`SupportsPassiveListen` / `SupportsFastDiscovery` / `Offl… |
| CR-113 | P1 | open | WONTFIX：建议 wontfix | ATK 系列的**充电读数虚高约 15 pp**（A9 Air 大师版+ 实测：有线充电 95% / 4276 mV ↔ 拔线放电 80% … |
| CR-114 | P2 | open | WONTFIX：建议 wontfix | 插件**学到的显示名只在进程内**（`AtkPlugin.ModelNames`）：无线态显示接收器通用串 `Wireless mouse … |
| CR-141 | P2 | open | WONTFIX：建议 wontfix | **插件按需下载（issue #63）作者已部分否决**：Windows 能枚举 USB/HID 但不保证认出具体型号，同 VID/PID … |
| CR-168 | P3 | open | SOMEDAY：建议 P3/someday | 罗技没读 `0x0003` GET_DEVICE_INFO（unitId / modelId / transportIds）与 `0x000… |
| CR-162 | P2 | open | SOMEDAY：建议 P3/someday | 产品图全内置不可持续：337 张 / 11.75 MB 随包分发，占全量安装包（36 MB）的 33%，用户为一台设备要背上全部型号。方案已… |
| CR-61 | P2 | open | SOMEDAY：建议 P3/someday | 日志在锁里同步刷盘，UI 线程可能等磁盘。改缓冲会丢掉崩溃前最后几行，所以先不改 |
| CR-74 | P1 | open | SOMEDAY：建议 P3/someday | 设备 ID 改端口无关键后的代价：同型号两只接收器同时插着会被并成一台（键相同）。需要时用「当前在位集合 + 端口哈希」做次级区分 |
| CR-82 | P1 | open | SOMEDAY：建议 P3/someday | CHERRY 2.4G 与有线是两台设备（`PIXIU75 Dongle` / `PIXIU 75`），显示名不同，历史库各占一行、互报离线… |
| CR-173 | P2 | open | SOMEDAY：建议 P3/someday | ATK N9 耳机协议已记录但**未实现**：电量走 CDC-ACM 串口，基类只有 HID 句柄；要落地需先给基类加传输抽象，且必须并进 … |
| CR-139 | P2 | open | SOMEDAY：建议 P3/someday | **任务栏贴片只能居中，没有左/中/右对齐设置**：贴片内设备全居中，调好位置后再新增设备就会往两边撑、和任务栏内容重合。需求明确、实现轻（… |
| CR-140 | P2 | open | SOMEDAY：建议 P3/someday | **桌面小组件不能轮播**：设备多时要么全显示占地方，要么只显示一台看不到其它。需求是「只显示一台 + 定时轮播」并带间隔设置。要先定轮播期… |
| CR-142 | P2 | open | SOMEDAY：建议 P3/someday | **可复用 OpenLogi 收集的外设 HID 特征库**：对方已积累大量 2.4G / 蓝牙设备的 VID/PID 与接口指纹。价值在「… |
| CR-157 | P2 | open | SOMEDAY：建议 P3/someday | **体系标签只在设备列表显示**：任务栏贴片与桌面小组件是 GDI 自绘、宽度按文案估算（`WidgetWindow.TagWidth`），… |
| CR-159 | P2 | open | SOMEDAY：建议 P3/someday | **主列表排序与小组件/贴片不一致**：`Devices` 按「在线 → 显示名」，小组件与贴片按「充电末尾 → 电量升序 → 名字」（注释… |
| CR-97 | P2 | open | REFACTOR：保留 open，重构 | HERO 68 Air 传输流程直接依赖 `HidDeviceHandle`，缺少传输层离线回归；维护者认可现有认领、分片及写入闸门，可合入… |
| CR-13 | P2 | open | REFACTOR：保留 open，重构 | 两个蓝牙 watcher 的发现循环和快照仍然重复。`ReadConnected` 已抽到 `WatcherProps` |
| CR-16 | P2 | open | REFACTOR：保留 open，重构 | 多个文件超过 400 行。拆分见下面的 D 项，不要先按行数硬拆。`MchosePlugin` 已从 1136 降到 1027：gmouse… |
| CR-50 | P2 | open | REFACTOR：保留 open，重构 | 日志跨午夜换文件没有单测。要测得注入时钟 |
| CR-88 | P2 | open | REFACTOR：保留 open，重构 | 写通道兜底没统一：`HidDeviceHandle.SetOutputReport` 只加在 `feat/atk`（ATK 那台 dongl… |
| CR-92 | P2 | open | REFACTOR：保留 open，重构 | 型号归一规则（`PRO X 2` → `PRO X 2 LIGHTSPEED`）写在通用 BLE 层；且只有蓝牙被归一，2.4G 仍取 `i… |
| CR-105 | P2 | open | REFACTOR：保留 open，重构 | `PidFromPath` 在 `BetopDeviceIdentity` 与 `AtkPlugin` 各有一份（同正则同逻辑），可抽公共 … |
| D1 | P2 | open | REFACTOR：保留 open，重构 | 两个 watcher 的 `Start` / `HandleRemoved` / `Snapshot`。属性读取已经抽走 |
| D3 | P2 | open | REFACTOR：保留 open，重构 | 图表的坐标和命中，抽成纯函数 |
| D4 | P2 | open | REFACTOR：保留 open，重构 | 小组件的排序和宽度，抽出视图模型 |
| D5 | P2 | open | REFACTOR：保留 open，重构 | 设置页保存时的副作用，抽出视图模型 |
| D6 | P2 | open | REFACTOR：保留 open，重构 | 各插件「发帧再读到截止」的循环（现有 Cherry `RequestBlock`、Pulsar `Query`、迈从、GANSS 各一套） |

## 已拍板 / 执行结果

- **WONTFIX 7 条**：CR-57、CR-70、CR-71、CR-102、CR-113、CR-114、CR-141
- **SOMEDAY/P3 11 条**：CR-61、CR-74、CR-82、CR-139、CR-140、CR-142、CR-157、CR-159、CR-162、CR-168、CR-173
- 执行：WONTFIX 已移入 `archive/BACKLOG-CLOSED.md` 的 `wontfix`；SOMEDAY 已在 `BACKLOG.md` 降为 P3；其余 BUG / EVID / REFACTOR 保持 open，VERIFY 保持 doing。
