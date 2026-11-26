# 变更历史（changelog）

> 只留「改了什么 + 为什么」。协议、坑、流程在专题文档，这里不抄第二份。
> 新会话不必读。每条一两句；单日新增超过 15 行就是写长了。发版只在文末版本表加一行。构建结果、打包耗时、文件清单不记（`AGENTS.md` §3）。
> **只保留当月条目**，跨月时跑 `python scripts/archive-changelog.py` 把整月搬进 `archive/`；全文上限 250 行（`check-doc-health.py` 会拦）。

> 更早的条目见 `docs/project/archive/`：`changelog-2026-10.md`

## 2026-11-26

- **剩余时间估算不再每台每分钟扫 90 天历史**：`HistoryStore.ScanDischarge` 流式读三列（不建 `HistorySample`、不跑 LINQ），`DeviceManager` 用「历史版本 + 电量」做缓存键，历史没变就不重算；顺带补上「关掉估算开关就不查」和「无读数先判后查」（后者此前对每台离线墓碑也照扫一遍）。改前每次要把整段窗口的采样物化成对象（每台每分钟 1~3 次，每次都触发 gen2），改后是零对象扫描。新老两条判定路径用 `H15` / `Evaluate_ScanComputesSameResultAsSampleList` 对拍，见 CR-79。
- **BLE GATT 会话归位**：`ReadBatteryViaGattAsync` 的 `svcResult.Services` 现在一定 `Dispose`（`finally`），`DataReader` 改 `using`，8 秒超时后晚到的 `BluetoothLEDevice` 由 `ContinueWith` 释放。`GattCharacteristic` 不实现 `IDisposable`，随 service 释放（CR-80 原描述有误）。
- **`StartTimers` 补 `_cleanupTimer?.Dispose()`**：每次保存设置都会新建一个清理定时器，旧的那个此前要等 GC 终结才停，期间可能多跑几轮历史清理。

## 2026-11-25

- **`0xB5` 长寄存器加会话级负缓存**：`ReceiverSession.ReceiverInfoUnavailable` —— 一次超时后本会话不再为它的其他子寄存器白等。C548 对 `0xB5/0x03`（槽位容量）和 `0xB5/0x51`（Bolt 配对）都不回包，此前每次启动白等 **1.73 秒**（877 + 856ms）；改后 `0x83B5` 超时 2 次→1 次，设备出现 5.72→**3.70s**。重开句柄时连同 `SlotCountProbed` 一起复位。回包但解析不出**不**记负缓存（说明接收器答了，只是布局不同）。
- **HID++ 被动监听不再独占 I/O 锁**：`HidppIO.PumpNotifications` 原来在 `_ioGate` 里读满整个 500ms 窗口、无间隔重入，等于长期占着锁，**每个 `Request` 都要排队约 500ms**（埋点实测 20+ 次，含 `0x83B5` 探测 1346ms、空槽 ping 910ms/400ms 预算）。改成 50ms 切片、每片放锁后再取。整轮发现扫描 **10713→5651ms**，设备出现 10.05s→5.72s，等锁记录归零。剩余项见 CR-181。
- **`进程身份` 日志不再拖慢启动**：`管理员=` / `沙箱组=` 两项改成只在 `LogLevel=Debug` 时计算。实测这一行曾占启动关键路径 **0.95 秒**（组名翻译逐个问 LSASS/AD，域账户二三十个组）；`用户=`/`会话=`/`完整性=` 保留常打——`完整性` 才是 §20 那次事故要看的东西，而且只要一次 `GetTokenInformation`。
- **删掉设置页预热**：`PrewarmSettingsPage` 整段移除，启动基线提交 127.7→100.1MB、专用工作集 ~120→74.7MB。**注意这是"推迟"不是"消除"**：第一次切页要一次性补回 ~30MB（图形/文字栈 + 设置页树），点完所有页面后与预热版持平（153.6 vs 153.3）。
- **页面常驻保留（试过拆，不划算）**：去掉 4 个页面的 `NavigationCacheMode.Required` 后重测，第一轮持平、**重复访问每轮 +28MB 且不回收**，手感却几乎没差别（用户实机判断）。数据与结论见 `dev/runtime-pitfalls.md` §27。
- **`AboutPage` 订阅归位**：原先是构造函数订阅 `App.UpdateStatusChanged` / `UpdateDownloadUi.ProgressChanged` 且从不解绑，靠"页面永不销毁"侥幸成立；改成 `Loaded` 订阅 + `Unloaded` 解绑 + `_hooked` 守卫（§19 的形态）。
- **设备图片清单改懒加载**：`DeviceImageService` 的两个清单改 `Lazy<>`，首次打开详情页才解析。315 个 `manifest.json` 常驻只有 ~0.6MB（667 配色 / 4129 条字符串），**这不是内存优化**，只是把启动期 315 次读盘挪走；日志行随之改成按需输出的「设备图片包（内置）: N 个型号」。
- **隐藏到托盘后修剪工作集**：`MainWindow.HideToTray()` 在 `AppWindow.Hide()` 之后延迟 1.2 秒调 `SetProcessWorkingSetSizeEx(-1,-1)`，`TrayService.ShowMainWindow()` 先取消。起因是用户实测"隐藏半小时仍占 148MB，要等系统内存紧张才掉到 29MB"——非专业用户看的就是任务管理器那一列，而主窗口通常只开一次配置。**只裁驻留页、不改提交**（同期提交 183.8→187.8），口径与取舍见 `dev/runtime-pitfalls.md` §26。

## 2026-11-24

- **Beta 打包通道**：`build-installer.ps1` 增加 `-Version` / `-Channel` / `-OutputDir`；预发布构建只查 `beta` 通道与 prerelease 源，正式版仍走 `win` 稳定通道并在运行时忽略预发布目标。本机已打 `1.4.0-beta.1` 到 `dist-beta/`，未上传。
- **迈从主动查询白名单**：未登记/未验证接口不再发 0x4D / 0x11 / 键盘会话帧；已验证 G87、G87 V2、CZ、0x4D、Gmouse、V9/X9、G75 Pro 保持查询。新增 `MchoseActiveQueryPolicy` 与 Fake Handle 零写入测试，见 `hardware/safety.md` §3。
- **手动目标探测设计**：补充 `TargetSelection` / `DeviceIdentity` / `ProbeResult` 的分离，明确插件预登记 `ProbePlan`、一次性只读授权、`ProbeVerified` 仅作实例级证据、失败保持 `unverified`；不新增通用 `ActiveQueryPolicy`。见 `reviews/2026-11-24-manual-target-probing-design.md`。
- **M2 方案修订**：自动查询与手动探测分离；自动门控按插件逐点补齐，手动探测复用现有插件/解析器/门控，不新增独立协议框架。能力覆盖审计见 `reviews/2026-11-24-active-query-capability-coverage.md`，当前未提交、待审查。

## 2026-11-23

- **协议引擎治理文档体系**：新增治理路线图与协议定义、主动查询安全、身份与能力、回归测试、统一遥测五份专项规范；M1 文档收尾完成，M2 主动查询安全治理列为下一阶段，现有 `protocol-json.md`、`plugin-standard.md` 与 `safety.md` 继续承担原职责。
- **M2 写入点盘点**：静态审计所有 `WriteOutput` / `SetFeature` / `SendOutputReport` 与 ATK 原生后备路径；确认目前只有北通有设备级能力门控，矩阵与风险见 `reviews/2026-11-23-active-query-write-audit.md`。未改代码、未向设备发帧。
- **M2 门控方案定稿**：统一授权顺序为插件开关 → 操作类型 → 设备能力 → 身份/接口 → 熔断；`ProtocolEngine.activeAllowed` 改为调用方显式传值，直接 HID 使用同一策略。因能力声明只覆盖少量型号，先补证据再逐插件启用，方案见 `reviews/2026-11-23-active-query-gate-design.md`。
- **设备状态收口**：新增 `docs/hardware/support-matrix.md` 作为逐台验证状态、默认开关与文档入口的唯一载体；`README.md` 只留本机已验证的 10 台，`handoff.md` §2 与 `protocols.md` 改为链接，不再维护第二份状态表。
- **文档归位与坏链修复**：`vendor-plugin-standard.md` 从 `docs/architecture/` 移到 `docs/dev/plugin-standard.md`；修掉 logitech 源码链接、`capture.md` §8 顺序、`atk.md` 缺失的 §3 与 4.1/4.2 顺序；补 `reviews/archive/README.md` 归档索引。
- **发布口径**：GitHub Releases 为主下载源，Gitee Releases 为镜像备份；上传由维护者手动完成，AI 只做本机打包、不代传。`AGENTS.md`、`dev/build.md`、两份 README 与打包/发布脚本同步标注。
- **文档一致性**：英文 README 缩成 10 台本机已验证 + 矩阵链接；设备图统计改为当前 315 型号 / 667 配色 / 25.8 MB；changelog 与 contributions 按日期排序合并；插件选型表/检查清单收口到 `plugin-standard.md`；新增 `docs/INDEX.md` 生成器与 `check-doc-health.py`。
- **BACKLOG 核查**：代码核对后把 CR-163/164/165/107/66/67/178 改为 `doing`，CR-69 关闭归档，CR-93 摘要按实际代码更新；撞号条目重编号为 CR-172~CR-179；按分诊表把 7 条 WONTFIX 归档、11 条 SOMEDAY 降为 P3。

## 2026-11-19

- **协议 JSON 严格加载**：`ProtocolSpecLoader` 对协议/设备/嵌套/`override` DTO 开启 `JsonUnmappedMemberHandling.Disallow`；设备反序列化错误带资源、条目序号、VID:PID 与 JSON Path。见 `dev/protocol-json.md` §2.1。
- **目录数据清理**：雷柏移除 195 条设备级 `brand` / `caps`（历史证据留在 `hardware/reference/rapoo-catalog.md` §6.1）；北通 110 条、迈从 2 条 `devices[].note` 迁到顶层 `deviceNotes`，`transports[].note` 不动。

## 2026-11-18

- **北通 P2 协议数据化**：鲲鹏 `0x15` 与宙斯 `0x04` 的偏移/掩码/五档表/充电/链路全部搬进 `betop.protocols.json`，`BetopCodec` 删除；通用 schema 新增 `percent.map`、`link.table`、`requirePercent`、`chargeWhenMissing` 与动态查询模板。目录扩到 110 条 / 86 个 `VID:PID`（24 low4 + 26 nocharge、9 个宙斯 PID、26 个 20DD），能力声明不变。见 `hardware/reference/betop-catalog.md` §10。
- **北通产品图补全**：从 BetopGame 2.6.2 的 `handle_kp*`、`t6pro_*`、`t9*` 抽 21 款 / 34 色缩略图，脚本 `scripts/import-betop-images.py`；`5125/5127/5145/5146/5074` 型号留空，继续回退几何图标。见 `project/device-images.md` §7.7。

## 2026-11-16

- **北通 P1 品牌目录与身份**：新增 `betop-catalog.json`（33 条 PID）与 `specs/betop.protocols.json`，接入现有 `ProtocolEngine`；身份归一只认目录显式 `group`（当前仅 5158/5159 → `betop:kunpeng20`），显示名目录优先、歧义 PID 由产品串分流。协议解析、轮询与离线策略未改；T9、鲲鹏20 2.4G/有线是唯一已验电量项，见 `hardware/reference/betop-catalog.md`。
- **罗技充电状态码拆成两套（CR-164）**：`0x1004` 与 `0x1000` 是两套枚举，此前共用一张表——`0x1004` 的 4（充电故障）被报成"充电中"，`0x1000` 的 5/6/7（无效电池/过热/错误）被报成"放电中"。改为 `TryParseUnifiedBattery` / `TryParseBatteryStatus` 两个解码器，未知码一律 Unknown。
- **去掉 `0x1004` func 0 电量回退（CR-164）**：func 0 是 GetCapabilities，func 1 超时一次就会锁到档位数上并长期报错值（注释自己记过 15% 误读）。只用 func 1，`UnifiedWorkingFunc` 状态随之删除。
- **feature 下标不再一次超时就存死（CR-165）**：解析回 0 先留空、下轮再试，连续 3 次才认定不支持（`HidppFeatureProbe`）。此前一次超时即永久禁用该电量路径，直到接收器重插。
- **罗技设备名读不到（G304 显示「罗技设备 1」）**：`0x0005` 的 func 1 是**按块**回正文，旧实现一帧取一个字符、还把字符数上限写成 32；G 系自报长营销名（G304 = 37 字符）整条被丢弃，列表只剩占位名、详情页无型号无图。改为按 16 字符分块读、上限放到 64（仅防坏字节），拼完拒不可见字符。37 次往返降到 3 次。见 `hardware/logitech.md` §1.2。
- **32 字节扩展集合不再抢会话**：C53F 同时暴露 7/20/32 字节三个 HID++ 集合，`ShouldSkipInterface` 只分短/长，20 与 32 都被认领成独立会话，而 32 字节那条零回包，每分钟重开一次句柄白等三次超时。改为 `PreferLong` 下扩展集合只在没有 20 字节同伴时才认领（C548 无此集合，此前未暴露）。
- **G304/G305 产品图补长名变体**：`DeviceImageCatalog` 是整串归一化精确比较，`G304` 匹配不到 `g304lightspeedwirelessgamingmouse`。其余 G 型号仍待真机核对（CR-160）。
- **雷柏插线双行已修（CR-150）**：同一只鼠标的有线/无线两条枚举按「目录型号 + 序列号」归一到同一 ID，同轮 `SkipPathsForThisPass` 优先保留有线；目录 172 条配对记录补 `wired`，加载期校验每组恰一条，黑名单也改用同一把 ID 钥匙。VT2S Air MAX V2 真机同 SN 已验证，其余配对组待真机复看。
- **雷柏全系 DPI/档位/回报率遥测**：`[2]`/`[3..6]`/`[11]` 从 `0x1412` 的设备级 override 提到 `status-report7` 协议本体，`lm20rt` / `vt-ch584` 一并继承；能力声明仍按设备三态，只有 VT3 / VT2S 有真机证据。DPI 上限放到 60000，未知回报率码不冒充有值。
- **雷柏产品图全量**：新增 `scripts/import-rapoo-images.py`，从官方 AHUB 2.0 主包按 `productId` 取 `assetsDir/colors`，下载 96 款 / 296 个配色的缩略图（11.1MB）。Ace 68 Turbo 官方缩略图 404、V700diy-75 LRV 无配色，均跳过；底座/OHID/telink-3228c 不产生设备条目不导。

## 2026-11-13

- **罗技办公线产品图全量**：新增 `scripts/import-logitech-images.py`，读本机 Options+ 的 `devices.json` 与 `depots/*/logioptionsplus/thumbnails`，按 `displayName` 归一化分组，生成 151 款 / 151 张缩略图（原始 22.1MB，300px 重采样后 6.7MB）。每型号只取一张官方主图，不做配色切换——罗技的 `derivatives` 只有 `ext1/ext2` 编号，且混了 for Mac/for Business SKU 与重复引用。`productIds` 写设备 WPID 并排除接收器 PID（C548 是所有 Bolt 设备共用的传输通道）。
- **修改名设备离线后丢产品图**：`DeviceCustomization` 加 `OriginalName`，改名时记下协议原名，`RestoreOfflineTombstones` 优先用它作 `Name`。在线设备本来就没事（详情页用 `Manager.Find` 的原始实例匹配，改名只写设置），只有墓碑从历史库取名字时才会拿到改名后的值，而墓碑不带 `Usb`/`Reported` 身份。顺带修列表改名时新建 `DeviceCustomization` 会把详情页选过的 `ImageKey` 清掉。
- **G 系列产品图补上（OpenMouse 图床）**：新增 `scripts/import-openmouse-images.py`，从 `img.openmouse.app` 取 16 个 G / PRO 型号（G502 / G502 X / G502 X Plus / G703 / G903 / G PRO / G PRO 2 / G203 / G303 / G305 / G309 / G402 / G403 / PRO X SUPERLIGHT / PRO X2·X3 SUPERSTRIKE），裁掉透明边距后统一 300 长边，共 0.63MB。`displayNames` 是从它的名字正则反推的，需真机核对 G 系列 `0x0005` 自报名后补变体；图不在其仓库内、不在 AGPL 范围，版权处理同其它产品图。
- **G 系列未覆盖**：Options+ 只管办公线（180 条里 0 条 G/PRO），G HUB 安装器是自研封装、payload 加密，7-Zip 解不出资源；开源侧 OpenLogi 只有动作图标。要做得先装一次 G HUB 再读它的 resources，见 `reviews/BACKLOG.md`。

## 2026-11-12

- **详情页 Hero 接入本地产品图**：新增 `DeviceImageCatalog` 读取 `device-images/<品牌>/<型号>/manifest.json`，按显示名或「自报型号 + VID/PID」匹配，不按设备 ID；无图/清单损坏回退 `DeviceIconFactory`。只改详情页，列表/托盘/贴片/小组件不动，格式见 `project/device-images.md`。
- **迈从 G87 图片包**：从 MCHOSE HUB 的 `webIndexList.colorList` 与官方 CDN 整理 11 个配色的缩略图/整机图，最初放在本机数据目录；随后缩略图改为项目资源（见下条）。新增清单匹配、路径越界与配色覆盖单测。
- **产品图配色可覆盖**：官方 HUB 只用 `checkIndex` 记录用户选择（本机默认 0 = 白），不从设备读颜色；因此详情页 Hero 悬停时显示调整按钮，选中的配色按设备 ID 存 `ImageKey`，详情页优先用户选择。新增配色覆盖与设置序列化单测。
- **迈从键盘产品图铺全量**：新增 `scripts/import-mchose-images.py`，从官方 `cardList.json` 按型号/PID 分组下载缩略图，生成 30 款 / 159 个配色；加上原有 G87 V1，共 31 款 / 170 个缩略图（压缩后约 4.5MB）。缩略图作为项目 `Assets/device-images` 随包分发，整机图可选写本机数据目录。
- **产品图接入手册**：`docs/project/device-images.md` 扩成完整流程（官方源、解包、分组、manifest、压缩、校验、已知坑），并新增 `scripts/check-device-images.py` 校验文件缺失与显示名冲突。未来接新品牌照此执行，不再从零设计。

## 2026-11-11

- **设备列表收窄为摘要 + 离线徽标改灰**：删掉 DPI/档位/回报率与「协议 · 状态 · 剩余时间 · 更新于」两行（行高 98→58，一屏 5→8 台），信息移交详情页；离线改用 `DeviceConnectionInfo.Badge` 统一灰色，不再把历史链路显示成当前连接。`PointerTelemetryFormatter` / `GetRemainingTimeEstimate` 没删——贴片提示、小组件、详情页还在用。
- **新增设备详情页（M4-4）**：Hero 加性能 / 连接 / 身份三卡，列表单击整卡或右键进入；离线仍展示最后已知值并逐项标注时刻。身份卡没有「品牌」——`DeviceStatus` 无此字段，按协议串前缀截取等于猜。
- **修详情页缓存复用残留**：页面常驻 + 指标指纹跳过重建，而空指标列表的指纹是空串、正好等于 `OnNavigatedTo` 复位值 → 新设备没指标时提前返回，上一台的 DPI 留在屏幕上；改空列表走显式 `ResetMetrics()`。
- **C548 真机验证**：`0xB5/0x03`、`0x51`、`0x61` 均无回包，确认设备不提供该寄存器读；容量走目录 6 槽，DPI=1000、回报率 Unknown。见 `hardware/logitech.md` §1.1。
- **文档修正**：`BACKLOG.md` 有 4 条记录前夹了空行（空行终止表格 → 后 4 段没表头）、「重构候选」表分隔行少一列；同轮给 `check-doc-links.py` 加表格完整性检查，扫出 `review-standard.md` 的 R 清单被引用块截断。另修 4 处既有错误（两处代码注释锚点、两份归档报告路径、README 导航项编号）。
- **文档目录整理**：`docs/hardware/` 43 份平铺拆成 `brands/`(23) / `devices/`(13) / `reference/`(3)，判据是「文件名 = 品牌名」而不是「型号数 ≥ 2」；全库 706 处路径引用用脚本机械改写。
- **补两份文档**：`ui-style.md`（视觉规则从 `UiPalette.cs` 类头捞出来）与 `user-guide.md`（逐页逐开关）；`AGENTS.md` 加 §0，`BACKLOG.md` 加 P0/P1/P2 优先级列。
- **槽位容量改读接收器自报值**：`0xB5/0x03` 参数区 `[6]` 是配对槽位容量，成功就覆盖目录 `maxSlots`——目录是静态表、自报值是固件事实。只对目录登记过的接收器 PID 探。**局限**：`PreferShort` 的 Unifying / Nano 凑不出 9 字节响应，仍以目录值为准。
- **连接推断先收紧、再放开到全 2.4G 族**：先（M4-3B-2B2-R）改收 `HidppCatalog.FindReceiver` 的目录记录，只有 Bolt + 目录内有效槽位才 `Inferred(Wireless24G)`、未知 PID 不再借默认 6 槽；随后放开到 Unifying / LightSpeed / Nano——判据只有「非 Direct + 目录族属 2.4G + 槽位在该族 `MaxSlots` 内」，族本身不参与证据强度。原先「要真机确认配对布局才能开」把 `0xB5` 配对寄存器误当成链路前提，而这段代码从没读过它。门控测试 14→16 条；真机槽位枚举仍缺，见 CR-156。
- **接收器身份全量开放**：`LogitechReceiverIdentityPolicy` 不再只放行 Bolt，带 `familyDisplay` 的族都填体系名与图标。身份是「这颗 PID 属于哪一族」的目录事实、不是链路结论——原判定把两者混成了一件事。
- **设备列表显示接收器体系**：`ReceiverIdentity` 加 `DisplayName` / `IconKey`（来自目录 `familyDisplay` / `familyIcon`），列表在链路徽标右侧显示「Bolt」这类标签；展示层只读目录、不做品牌映射。迈从暂无体系名（与「2.4G」重复）故不显示。
- **接收器图标改用官方 path**：`bolt` / `unifying` 直接复用 OpenLogi 的 `action-icons/` path 原文（用户当日授权推翻「不复刻商标」旧决定），工厂内加只支持 M/L/H/V/C/Z 的私有解析器；实测 Unifying 星形镂空 11px 会塌成方块，图标边长 11→14 才立住。

## 2026-11-10

- **迈从键盘显示真实链路**：`41E4:2001` 是 G87 的 dongle（`mchose.md` §1），登记进目录 `transports` 后，认领到它即把链路推断为 2.4G，来源记 `Inferred`——主机读的是接收器通道，有线数据不会经过 dongle。键盘本体 PID 未登记，插线场景仍显示未知。`ConnectionOfProbe` 改为返回遥测值，来源由插件自报（探测帧 `DeviceReported` / 结构推断 `Inferred`），基类不再一律当成设备上报。
- **连接遥测的 Unknown 加固（M4-3B-1R）**：`TelemetryValue.HasValue` 只校验"有没有来源"，所以 `Reported(Unknown)` 也算有效，合并时能压掉已知链路。连接专用合并与徽标判据统一改成"Unknown 值不算链路"（`DeviceStatusMerger.MergeConnection`、`DeviceConnectionInfo.IsKnown`），`HasConnectionChanged` 同样忽略它。现有写入点（`SetConnection` / `SeedConnection` / 两个蓝牙 watcher）本来就有守卫，这次是堵住模型层可被误用的口子，未改通用 `TelemetryValue.Merge`，Battery / Pointer 行为不变。测试 +7（`M13`~`M15`、`C16`、`D8`、认领落地两条）。
- **罗技 C548 ReceiverIdentity（M4-3B-2B1）**：`AddDevice` 只在非 Direct、目录 Family 为 Bolt 时填 `ReceiverIdentity(null, "bolt")`；当前目录只有 C548 为 Bolt，C52B/C532 等其它接收器保持 null。`Connection`、DirectDevice、认领/去重与配对读取均未改；新增纯策略与身份回归测试 9 例。
- **罗技接收器槽位连接推断（M4-3B-2B2）**：`AddDevice` 只在非 Direct、Ping 成功且槽位在 `1..MaxSlots` 时填 `Inferred(Wireless24G)`；Direct `0xFF`、空槽和越界槽位保持 Unknown。接收器存在不等于设备当前连接，Slot Ping 不升级成 `DeviceReported`。
- **罗技 DPI / 回报率只读接入（M4-3B-2C）**：`0x2201`（不支持时退回 `0x2202`）读 DPI、`0x8060`（退回 `0x8061`）读回报率，2 分钟一次，来源记 `DeviceReported`；读不到就留空，不按型号补默认值。C548 + MX Master 3S 真机读回 1000 DPI；回报率两个 feature 都不应答，故仍为未知。新增纯解析测试 15 例。

## 2026-11-09

- **设备身份快照（M4-2，补记）**：`DeviceStatus` 增加 `Usb` / `Reported` / `Receiver` 三个可选身份，分别表达"被认领的 HID 接口""协议自报的本体""接收器或传输通道"。三者都是 `readonly record struct`，`Clone` 按值复制，`DeviceStatusMerger` 按"空值不清洗"合并（轮询应答与离线墓碑都不带身份）；合并时先判身份再补子字段，VID+PID 变了就整体替换，不继承旧序列号 / 型号。填充边界：基类在认领构造处统一填 `Usb`（34 个子类零改动），迈从额外用 0x0900 自报身份填 `Reported`、用目录 `transports` 的 role 填 `Receiver`，罗技在 `AddDevice` 里补 `Usb`（会话型插件走不到基类路径）；罗技的 `Receiver` 仍为空，`HidppCatalog.Family()` 未接入。身份不参与认领、去重、设备 ID 与历史，也不进 `HasChanged`。
- **连接方式改成带来源与时刻的遥测（M4-3B-1）**：`DeviceStatus.Connection` 从裸枚举改为 `TelemetryValue<ConnectionKind>`，来源复用 `TelemetrySource`，新增 `MergePreferTrusted` 按证据强度合并（设备上报压过推断、反向不覆盖、无值不清洗）；`ConnectionKind` 补 `Bluetooth`，BLE 与经典蓝牙两个 watcher 直接声明链路，不再依赖来源兜底。见 `project/architecture.md` §6。
- **修徽标把接入栈当链路**：删掉 `BadgeLabel` 的 `Hid24G + Unknown → 2.4G` 回落（M4-3A 审计 P1/P3），改由新的 `DeviceConnectionInfo` 统一出文案与取色档——只有 BLE / 经典蓝牙 / 本机允许按来源兜底，未知单独一档用中性灰、不再借 2.4G 紫；离线设备显示"离线 · 最后已知：X"或"离线 · 未知"。设备列表、桌面小组件、删除确认框三处共用同一出口。
- **测试**：新增 `DeviceConnectionInfoTests`（7 例）与合并优先级 / 来源升级不触发变化 / Clone 保留遥测 / 离线保留最后已知用例，`DeviceSourceInfoTests.S4` 改为钉"来源不再冒充连接方式"；全量 1821 通过 / 1 跳过。
- **迈从 0x4D 取证准备修正（M2-4C-P1）**：两个 rate index **不一致时不发布**（语义未确认，不顶替也不清旧值，判定集中在 `MchoseRatePolicy`）；`0x0002`/`0x0003` 从"试一次就永久放弃"改为**最多 3 轮**重试，两项都成功才收工；DPI 在 `dpiHasY==1` 时补读 Y 轴（Y 失败不影响 X）；三条取证日志升到 Info 并带原始帧。仍无真机样本，DPI / Rate 能力保持 `Unverified`。见 `hardware/mchose.md` §3.4.1、§3.4.2。
- **M2-4 归档**：DPI / 回报率只读遥测阶段收尾，官方审计、代码实现、自动化测试三项完成，真机验证与能力开放转 M2-5。归档状态表见 `hardware/mchose.md` §3.9。

## 2026-11-08

- **迈从 0x4D 协议合并（M2-2）**：`qhw` 与 `mouse-k7` 原本是两套并行实现，帧构造、校验范围、应答定位各写一遍，且 QHW 那套的 `0x0904` 命令与 `flags=0x03` + 求和校验都是错的（设备只回显、不执行）。合并为唯一执行器 `Mchose0x4DCodec`，协议名统一 `mchose-0x4d`，删掉 `MchoseK7Codec` 及其测试；分类器 `MouseK7` 改名 `Mouse0x4D`，并为目录登记的通用 dongle（`0x1014/0x1016/0x101C/0x101D`）加「厂商页 + 双向 64 字节」的宽松判据，`0x1018` 仍只认精确指纹。见 `hardware/mchose.md` §2、§3。
- **迈从 `0x0900` 自报身份**：`payload[0..3]` 是配对**本体**的 VID/PID（不是 dongle 的），新增 `MchoseDeviceIdentity` 把 USB 身份与协议身份分开存，只用于显示名、**不参与认领**——dongle PID 被多款共用，不能反推型号。真机样本自报 `0x4026`（A5 V3 Ultra+），与当时记录的 K7 V2 Ultra+ 不符，型号身份仍未闭环（`hardware/mchose.md` §3.8）。
- **迈从 0x4D 接入连接方式与统一充电状态（M2-3）**：`0x0900` 的 `payload[10]` 一直是"解析后丢弃"，现在映射到 `ConnectionKind`（`1` 有线 / `2` 2.4G，其余 `Unknown`——官方只给了这两档语义）；`payload[11]` 的充电码表从插件搬进 `Mchose0x4DCodec`，与连接映射同处一个纯函数层，单测可直接钉住。连接方式**只刷徽标**：`Unknown` 不覆盖已知值、不参与在线判定、也不触发重新认领（认领只看 VID/PID 与接口描述符）。见 `hardware/mchose.md` §3.3。
- **能力声明补两项、只给有真机证据的通道**：`DeviceCapability` 增加 `ConnectionRead` / `ChargingRead`（原六项表达不了这两件事），`mchose-catalog.json` 的 `transports[]` 支持 `capabilities`；只有 `3837:1018` 声明三项 `supported`，四颗未验证的共用 dongle 与四条本体 PID 一律不声明——不从协议族外推。见 `dev/protocol-json.md` §2.1。
- **补测试**：`Mchose0x4DTelemetryTests`（连接/充电映射、未知值不落确定态、三字段索引隔离、能力声明与目录一致）+ `ConnectionTelemetryTests`（连接变化不碰在线状态/电量、失败轮询不重置遥测、不触发重新认领）。
- **迈从 0x4D 解析 DPI 档位事件与 `0x0003` 配置读取（M2-4B1）**：`TryParseStatusEvent` 原先要求 payload ≥13 字节，纯档位事件（3 字节）会被整条丢掉；改为**按实际带的字段判长度**，并把档位事件落到 `PointerTelemetry.DpiLevel`（存协议原值，不 +1）。新增 `TryParseDpi` / `TryGetActiveDpi`：`ActiveDpi = dpiX[usbDpiIndex]` 只在"读的配置档确实是活动档"时成立，而活动档来源（`0x0600` 的 `payload[6]`）只有 B 级证据，所以**不发布任何 DPI 值**，插件只在认领后读一次 profile 0 留样本。能力声明未改（仍无真机）。见 `hardware/mchose.md` §3.4.1、`hardware/mchose-dpi-report-rate-audit.md`。
- **迈从 0x4D 接入回报率只读遥测（M2-4B2）**：`0x0002` 只回 index（`[1]` 高半字节 USB / `[2]` 高半字节 2.4G），Hz 靠官方两张表换算，表与槽位进了 `mchose-catalog.json` 的 `rateTables` / `rateProfiles`（按"型号 + USB 枚举 PID"选槽，本体直连用槽 0）。发布 `ReportRateSettingHz` 并标 `Inferred`；`ReportRateMeasuredHz` 不填；型号不明 / 槽位缺失 / index 越界一律保持 Unknown，不默认 125Hz。见 `hardware/mchose.md` §3.4.2。

## 2026-11-07

- **设备卡片展示 DPI / 档位 / 回报率（PR-D）**：模型与解析在 PR-A/PR-B 就绪，界面此前一处都没展示。新增 `PointerTelemetryFormatter`（Core 纯函数）产出分段与单行文案，设备页只多一个 TextBlock；`DeviceStatus.Capabilities` 在认领落地时由插件写入，用来区分"能读但还没读到（未获取）"与"不支持（该字段不出现）"。离线与过期一律不渲染，实测回报率带 `≈` 与来源标记。见 `project/pointer-telemetry.md` §8。
- **指标行配色**：标签按类别上色（DPI 青绿 / 档位 琥珀 / 回报率 靛蓝，色值进 `UiPalette`），数值保持主题前景色；未获取的项整段灰显。三个色相避开来源徽标与电量状态色，免得卡片上三套颜色互相干扰。
- **修设备卡片刷新时整行闪一下**：`RefreshCore` 原来在内容指纹变化时执行 `_rows[i] = fresh[i]`，那是 `ObservableCollection` 的 Replace 通知，`ListView` 会回收容器、重建整行；而指纹里带着 `更新于 HH:mm:ss`，等于每次轮询全表重建。行模型改成可变 + `INotifyPropertyChanged` 原地更新，`x:Bind` 显式 `Mode=OneWay`，集合只在设备增删 / 换位时变动。见 `dev/runtime-pitfalls.md` §25。
- **修"在线设备百分比显示成灰色"**：原地更新用"旧值 == 新值就 return"省通知，而 `PercentBrush` 的字段默认值 `null` 恰好是正常态的合法值，首帧被判成"没变化"，刷子停在构造函数那个灰色——只有充电或低于 20% 才会自愈。改用透明色当哨兵。见 `dev/runtime-pitfalls.md` §25 规则 5。
- **小组件与任务栏贴片也展示指针遥测（PR-E）**：小组件在电量条下加第三行（有指标的行高 62 DIP，窗口高度改为按设备累加）；贴片横向塞不下（单格会从 60px 涨到 120px），只加进悬停提示。小组件顺带按内容指纹跳过无谓重建——心跳每 30 秒补推一次，原来每次都清空重建整棵元素树；缩放比例因此也进了指纹，并单独监听 `XamlRoot.Changed`，否则拖到不同 DPI 的屏幕后窗口尺寸不再修正。见 `project/pointer-telemetry.md` §8.1。
- **修 Capability 查询的设备匹配边界（PR-C1）**：`ProtocolEngine.CapabilityOf` 原本只按 VID/PID 取目录条目，同一只鼠标的鼠标集合、底座集合都会拿到 `supported`。现在判据与认领同源——先过设备 `interface` 指纹，接口不符 / `implementation: none` / 未收录一律 `unverified`，`data` 与 `code` 两种实现的正常行为不变。见 `dev/protocol-json.md` §2.1。

## 2026-11-04

- **新增设备能力声明（PR-C）**：`DeviceCapability` 六项 × `CapabilityState` 三态，声明挂在**设备目录**的 `capabilities` 上而不是协议上——同一套 `status-report7` 被 77 个型号（146 个 PID）共用，放协议层会让 VT3 的验证结果漏给同族。`CapabilitySet` 不可变、未声明一律 `unverified`、非法枚举值直接抛；加载期对未知能力名 / 非法状态 / 非法类型报错（带 `VID:PID` 与 `capabilities.<字段>`），不静默。查询入口 `ProtocolEngine.CapabilityOf` 是纯配置查询，不开句柄、不发帧、不动解析状态。见 `dev/protocol-json.md` §2.1。
- **只迁移有真机证据的三条设备记录**：VT3 `0x1412` 声明三项读取能力，VT2S Air MAX V2 两个 PID 声明 `batteryRead`；写能力一条没写（本应用没有写入实现）。旧 `caps` 全部保留、不自动映射——`dpi` 分不出 `dpiRead`/`dpiLevelRead`，`[]` 是"没验证"不是"不支持"。见 `hardware/rapoo-catalog.md` §6。
- **修 PR-C 审查两项（修复批次 A）**：VT3 `0x1412` 的 `rateRead` 由 `supported` 降为 `unverified`——`[11]` 只有单点观测，没验证过"改回报率设置后 `[11]` 随之变化"，`[17]` 的 sensor 模式只印证帧偏移对齐、不是回报率的直接证据；升回 `supported` 需先做这项对照实验（`hardware/rapoo.md` §1.1）。`ProtocolSpecLoader` 改为拒收协议对象（含嵌套层级）里的 `capabilities`：`ProtocolDto` 没有该字段、未知字段默认被忽略，写错层就是"加载成功但声明丢失"，现在加载期报错并指明必须写进设备条目；其余未知字段的忽略策略不变，设备条目的 `capabilities` 照常加载（`dev/protocol-json.md` §2.1）。
- **补 CR-148（批次 A 补充修复）**：`capabilities` 写进设备 `override` 原本会被静默丢弃——它先深合并进协议对象，再在 `ProtocolDto` 反序列化时丢掉。现在 `ReadDevices` 在合并前拒绝，报错带 `VID:PID` 与完整字段路径（含嵌套对象与数组元素），`map` / `mapValue` 的取值表不参与扫描。调查结论：拒绝逻辑确实被绕过，但能力到不了最终对象（`ProtocolSpec` 无该成员），危害是"声明静默丢失"而非注入；现有 7 条 `passive.fields` override 与设备顶层 `capabilities` 零影响。见 `dev/protocol-json.md` §2.1。
- **迈从通用 2.4G dongle（`0x3837:0x3033`，G87 V2 / G75 V2 / K87S / K99 V3 共用）打通电量**：这颗 dongle 不认 QHW `0x4D`（本机三条 65 字节通道全静默），官方 web 驱动走的是 **CZ 分块读**——`55 05 00 [off+size] size offLo offHi` 读 64 字节状态块的 `0..0x37` 段，应答 `AA 05 …` 的数据段从下标 9 起。协议写成 JSON 规格 `cz-block`（`specs/mchose.protocols.json`），协议名进 `MchosePlugin` 的探测/轮询/已知协议三处分派；电量与充电同在 `[0x20]`（bit7 充电、bit0..6 百分比）；规格只声明这两个位域，**不写 `fullAt`**——满电改由 `MchoseSpecs.ResolveFull` 在插件层按官方口径补判（100% 且设备不再报充电才是 Full，充电中不覆盖）。本机真机验证 90% / 98% / 100% 与充电三态，见 `hardware/mchose.md` §2.4。
- **协议 JSON 补位域 `mask`**：`percent` / `charge` 支持 `mask`（1..255，缺省整字节），用于"电量与标志位同处一字节"的设备；`mask: 0` 加载期直接拒。文档 `docs/dev/protocol-json.md` §3.0。
- **两条已证伪的路写进文档别再试**：QHW `0x4D`（`0x0904`）在本机全静默；官方 `audio.mc` 的单帧电量命令（`0xAA` 帧头 + `subCmd 11`）被 CZ 通道的真 NAK 拒掉（`00 AB AA … cserr`）。

## 2026-11-01

- **协议数据化落地**：`Plugins/Protocol/` 解释器接住雷柏 12 套协议，删掉 `RapooCodec` / `RapooFamily` 等 7 个文件（约 500 行），判断与状态仍留代码；同轮新增 `dev/protocol-json.md` 与 `AGENTS.md` §10 红线（帧偏移 / VID:PID / 充电码 / 协议名只许出现在规格与 JSON）。真机帧用例 R1–R26 全部改走解释器并通过。
- **迈从迁进解释器**：Gmouse / V9 / X9 的帧模板与重试进 JSON，三个 codec 改薄壳，既有 35 条断言一条没删、钉的变成 JSON 偏移；顺带删掉 `IsCandidate` 里 7 行与 VID 白名单等价的品牌排除，并把 `KnownProtocolByPath` / `_seq` 从 `static` 改实例（`AttachAsync` 每轮清失效路径）。
- **修 CR-145 同名双插件认领互斥失效**：owner 从插件名改成 `ClaimOwner(Id, PluginName)`，A5 覆写 `ClaimId`，主插件按身份让路而不是猜接口形态。
- **雷柏补 5 个协议族（覆盖 98/109 台，全部零写入）**：官方族的电量全来自被动上报，**不为键盘写查询帧**；认领改「目录优先 + 形态兜底」。除 VT3 外只有合成帧单测，边界见 `hardware/rapoo-catalog.md` §8。
- **雷柏插件改「目录 + 协议族注册表」**：`RapooPlugin` 211 行降到只管认领与派发，显示名与图标改由目录给，行为零变化、64 项单测全过。见 `hardware/rapoo-catalog.md` §2.1。
- **雷柏目录与心跳字段（真机 VT3）**：逆向官方主包拿到 109 台 / 195 个 VID:PID / 12 套族，19 字节 `0x07` 的 `[9]`~`[17]` 逐字节核对通过；目录先只落 JSON、没接进 csproj。见 `hardware/rapoo-catalog.md`、`hardware/rapoo.md` §1。
- **新设备两台**：狼蛛 F99 非 Pro（`0x00A4` 与 Pro 共用 `0x13` 帧，编解码零改动）；ilovbee b98（`258A:01BB`，零写入被动插件，关机帧判离线而不是 0%）。见 `hardware/aula.md` §1.2、`hardware/ilovbee.md`。
- **修罗技栈溢出（issue #55，唯一进程级崩溃）**：HID++ 通知不再在等包循环里同步回调，改入队、出锁后派发（200ms 预算 + 256 条上限），写失败停写 30 秒。见 `hardware/logitech.md` §3。
- **罗技设备类型改用 `0x0005` func 2 的营销类型**：判定顺序改 营销类型 → 配对寄存器 → 按名猜，触控板 / 轨迹球不再被报成键鼠。见 `hardware/logitech.md` §1。
- **两处修复**：CR-124「重新上线后电量卡旧值 5 分钟」——离线→在线那一拍用 `Reseed` 重建过滤器基准，无效值仍挡住；issue #81 / CR-121「便携版数据落 C 盘」——`AppPaths` 加便携版一档（解压根目录带 `.portable` → `<root>\data`），不迁移旧数据（见 `dev/build.md` §3.1）。
- **贴片加位置锁定**：拖动判据只有「光标在矩形内 + 左键按下」，单击手抖就改位置并落盘；新增锁定开关（默认关）+ `SM_CXDRAG` 阈值，锁定时停掉 20Hz 轮询。见 `dev/runtime-pitfalls.md` §16.5.2。
- **迈从改目录驱动 + 修 QHW 通道不可达**：分类器先查 62 条目录、未命中才回落形态判定；`Probe` 的 `MouseV2` 分支原先无条件 `return`，`0x3033` 这类 dongle 永远走不到 `TryQhw`。另补 97 条设备全景与 DPI/回报率命令（纯文档）。见 `hardware/mchose-catalog.md`。

## 版本

发新版留着上一版全量包才有 delta；清空 `dist/` 再打同版本就没有。见 `dev/build.md` §8。GitHub Releases 为主下载源，Gitee Releases 为镜像备份；上传由维护者手动完成，AI 不代传。**1.0.11 是首个上传 Gitee Release 的版本**（1.0.10 及以前只在本地 `dist/`，故 1.0.11 的 delta 是拿本地留存的 1.0.10 全量包做基准算出来的）。

| 版本 | 日期 | 相对上一包多了什么 |
|---|---|---|
| 1.3.1 | 10-28 | 5 款新设备（墨将烈风2、ATK A9 Air、VXE R1 星闪、华硕棱镜2 / Delta II）；设置页改弹窗、贴片选设备；组合识别与雷柏充电态等 8 处修复；本机试装 |
| 1.3.0 | 10-25 | 8 个新品牌、约 29 台设备；设备诊断页、忽略设备、列表崩溃修复；本机试装 |
| 1.2.3 | 10-21 | 9 款社区设备适配；删除离线设备与永久屏蔽、贴片拖动/选屏/尺寸三处修复、失败不再静默；本机试装 |
| 1.2.2 | 10-19 | 8 款社区设备适配（会写帧的默认关）、更新说明与贡献者页；本机试装 |
| 1.2.1 | 10-18 | 剩余时间估算及原因提示、设备右键重命名简介；本机打包 |
| 1.2.0 | 10-16 | 关于页（介绍 + 检查更新从设置迁出）；本机打包 |
| 1.0.12 | 10-15 | 任务栏贴片悬停显示设备名 |
| 1.0.11 | 10-14 | 更新源换内置 GiteaSource；启动弹窗与进度修复；**首个 Gitee Release**；相对 1.0.10 的 delta 约 68KB |
| 1.0.10 | 10-14 | 历史曲线断档不连线；相对 1.0.9 有 delta |
| 1.0.9 | 10-14 | 历史设备名取最新非占位；曲线悬停气泡稳定 |
| 1.0.8 | 10-14 | Win10 任务栏耳机图标扶正；相对 1.0.7 的 delta 约 80KB |
| 1.0.7 | 10-14 | 版权信息与文件头；相对 1.0.6 的 delta 约 97KB |
| 1.0.6 | 10-13 | 分辨率往返后的贴片位置；有 delta |
| 1.0.5 | 10-12 | 右键菜单 Win32 化。当日又重发两次（真子窗口、CR-65），那两次 dist 是空的，无 delta |
| 1.0.4 | 10-12 | Win10 透明修复；留下 1.0.3 全量包，delta 约 97KB |
| 1.0.3 | 10-11 | 清空后重打，无 delta；同日重发含 CR-53 |
| 1.0.2 | 10-08 | 本机电量。Release 必须 `AllowUnsafeBlocks`（build.md §4） |

### 本机试装（非发布）

| 版本 | 日期 | 说明 |
|---|---|---|
| 1.0.1 | 10-16 | 本机试装包（含更新进度 UI）；版本回落便于测升级，非正式发版 |
| 1.0.2 | 10-16 | 更新进度高水位（多文件不倒退）；本机试装，非正式发版 |
