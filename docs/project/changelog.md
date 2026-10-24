# 变更历史（changelog）

> 只留「改了什么 + 为什么」。协议、坑、流程在专题文档，这里不抄第二份。
> 新会话不必读。每条一两句；单日新增超过 15 行就是写长了。发版只在文末版本表加一行。构建结果、打包耗时、文件清单不记（`AGENTS.md` §3）。

## 2026-10-21

- **MCHOSE K7 V2 Ultra+（`3837:1018`）**：接入 `mouse-k7` 协议——`0x0900` 查询设备信息、被动解析 `0x0600` 电源事件。这颗 dongle 的 feature 通道写通读不通（`GetFeature` 恒 `ERROR_GEN_FAILURE`），原有的 feature 读族与 E2 被动路径一个都不成立，这才是它原先完全读不到电量的根因。帧取自官方 M HUB 运行时 JS 的 `i1e()` / `_K()` / `LA()`，不是猜的。新增 `MchoseK7Codec`（纯函数 + 16 个真机帧向量用例），分类器加 `MouseK7` 并排在 `MouseV2` 之前。见 `hardware/protocols.md` §15。
- **K7 的 `0xFF` 不能按失败记账**：真机常态应答是 `4D 01 01 01 … 00 FF`（鼠标在睡）。按普通失败记，睡着的鼠标三轮就被判离线并熔断停写、醒了也不再被问，所以 `PollMouseK7` 自己记账——收到任何 `0x4D` 帧就算应答。
- **新增 CHERRY 插件（PIXIU75 蔚蓝档案 圣园未花联动款）**：64 字节块协议（report `0x04`，块 `0x1A` = 电量 + 充电位），真机 48% 与官方软件一致，插线后 55% 判充电中；有线（`046A:01E2`）与 2.4G（`046A:01E4`）共用同一块通道。见 `hardware/protocols.md` §13。
- **新增派世 Pulsar 插件（X2 V2 DJMAX 联动款）**：按 Linux `hid-pulsar` 的 17 字节帧（report `0x08`，STATUS/POWER）实现，真机 20% + 3566 mV。见 `hardware/protocols.md` §14。
- 两个插件都写帧，`PluginEnabled` 默认关；休眠无应答走 `ReportPollFailure` 记账，3 轮转离线并熔断停写，唤醒后最长等 5 分钟退避恢复（与 GANSS 同）。
- **设备 ID 改为端口无关**：HID 实例段前两段（`7&15dc4901`）由 Windows 按物理端口生成，接收器不报序列号时换 USB 口就变；旧实现哈希整条路径，于是同一个接收器换口后变成一台新设备（历史分裂、黑名单与自定义名失效）。`PluginUtils.StableDeviceKey` 只抹前两段、保留集合序号，`IdFor` 与罗技 `SlotId` 都改走它。代价是同型号两只接收器同时插着会被并成一台（CR-74）。见 `code-flow.md` §2.2。
- **拔出即时下线，插入不做即时检测**：托盘窗口收 `WM_DEVICECHANGE` 后跑 `RequestReconcileAsync`（只做消失检测，不认领不写帧），约 0.3 秒下线；15 秒节拍用同一个 `ReconcileRemovals` 兜底。**插入方向试过 1 秒接口路径检查 + 30 秒重试阶梯，当天回退**——插入广播实测不可靠、USB 枚举有固有延迟，这段延时是硬件成本，不值得为它叠机制；插入回到原节拍，要立即发现就点主界面「刷新」（`RequestPollAsync` 会认领）。顺带修掉罗技拔掉后不上报离线（`Upsert` 存快照副本，改本地 `IsOnline` 传不到 UI）、刚插上整轮 ping 超时后 5 分钟不再探（丢句柄前必须 `StopListening`，否则监听任务在已关闭句柄上空转、且此后不再重启）。`ReconcileRemovals` 遇到空扫描直接返回——那是 CM 枚举失败，不是"设备全拔了"。见 `code-flow.md` §2.3。
- **被动监听空转修掉**：句柄失效时 `ReadInput` 立即返回 null，`StartPassiveListen` / `EnsureListening` 的外层 `while` 没有退避 → 实测 7.9 秒刷 22.4 万行、烧满一个核。现在按"连续 3 次在 100ms 内返回 null"判定句柄失效，退避 200ms；正常静音仍等满 500ms，离线确认节拍不受影响。见 BACKLOG CR-75/76（已关闭）。

## 2026-10-20

- **启动默认显示主界面**：`AppSettings.StartMinimized` 默认值由 `true` 改为 `false`。之前双击后直接缩到托盘，看不到窗口，很容易被当成"没启动起来"。设置页的「启动时最小化」开关仍在，需要托盘常驻的自己打开。注意 `AutostartService` 写注册表时带 `--minimized` 参数，但应用从不读它（只记日志）——所以自启现在也会显示窗口。
- **开发版数据目录改为 exe 旁边的 `data\`**：`AppPaths.ResolveRootDir()` 用 `#if DEBUG` 分流——Debug 取 `AppContext.BaseDirectory\data`，Release 才取 `%LOCALAPPDATA%\VigilWatt`。这样从 `bin` 双击就能跑，不往用户目录写东西，开发版与正式版的 settings/history/logs 天然隔离。`VIGILWATT_DATA_DIR` 仍可显式覆盖（优先级最高，非法路径自动退回）。`run-app.ps1` 简化为"结束实例 + 启动"，`-Copy` 作为环境受限时的兜底。单实例互斥量仍是全局同名，两边同时只能跑一个（避免抢 HID）。见 `dev/build.md` §3.1。
- **补上"写盘失败"的盲区 + 新增 `scripts/run-app.ps1`**：`Log` 打开日志失败时会把所有行静默丢弃，用户只看到"一行日志都没有"。新增 `Log.TryProbeWriter`，启动时试开当天日志，失败就弹窗报出路径与真实异常。**根因已验证：从工作区目录启动的 exe 拿不到 `%LOCALAPPDATA%` 写权限**（该目录 ACL 里有沙箱组 `CodexSandboxUsers`），复制到工作区之外即正常；`run-app.ps1` 把这个复制+启动固化成一条命令。定位完成后一次性探针已删除，只留 `TryProbeWriter` / `LastSaveError` / `StartupGuard` 三处。见 `dev/runtime-pitfalls.md` §20。
- **设置页保存失败不再无声**：`Save_Click` 以前把 `AppSettings.Save()` 的返回值直接丢掉，写盘失败时界面毫无反馈，用户以为"点了没反应"（实测在受限环境下必现）。现在失败会弹原生窗说明路径、真实异常（`AppSettings.LastSaveError`）与常见原因，并直接返回、不再继续应用内存里的改动。
- **启动失败不再静默**：`OnLaunched` 里的异常以前会直接逸出，被 XAML 包成 stowed exception——事件日志只留一条 `Microsoft.UI.Xaml.dll / 0xc000027b`，应用日志一行写不出来（`Log` 可能还没初始化），用户只看到"点了没反应"。现在启动流程抽成 `StartCore` 由 `try/catch` 包住，失败时 `StartupGuard.Fatal` 落日志 + 原生 `MessageBoxW` 显示异常类型、消息和调用栈，再退出。兜底自身全程 `try/catch`，不允许它自己抛。
- **设备列表支持删除离线设备**：右键「删除设备…」，确认框默认按钮是取消；删除条目时**一并清掉该设备的历史**并 VACUUM，勾「永久移除」则写本地黑名单，插件认领阶段就不再探测、不发帧。顺序是**名单先落盘再摘条目**——插件那一轮重报会插进空档把条目塞回来。恢复入口在设备页底部「已屏蔽 N 台设备」。见 `code-flow.md` §5.2。
- **贴片尺寸改为每拍按当前缩放重算**：换线（HDMI → USB-C / 扩展坞）后缩放变了、任务栏像素尺寸却没变时，贴片会一直沿用旧尺寸（重启才恢复）；`GetDpiForWindow` 返回 0 的瞬间还会按 100% 算错。现在高度每拍按 `rect + dpi/96` 重算，高度或缩放任一变就重画。见 `dev/runtime-pitfalls.md` §16.11。
- **贴片选屏改用显示器身份，修两个 bug**：① 设置页不再显示「主显示器 / 显示器 2」，改为真实名字（`DELL S2719DC · 2560×1440（主显示器）`）；② 切到「仅电脑屏幕」后贴片不再消失——配置的屏不在时降级贴主屏，1 秒内跟到新任务栏。根因是拿 `\\.\DISPLAYn`（当前配置里的序号）当身份与名字，改存 `monitorDevicePath` + 名字快照。见 `dev/monitor-identity.md`。
- 设置页屏幕行加刷新按钮（页面缓存复用，插拔后不自动重扫）；配置的屏没插时仍列出 `<名字>（未连接）`，保存不会静默改回主屏。
- **修复任务栏贴片拖动时灵时不灵**：根因是 `SetCapture` 对非前台窗口无效（贴片带 `WS_EX_NOACTIVATE`，光标离开 114×28 的贴片就没有 `WM_MOUSEMOVE`）。拖动改为 16ms 轮询驱动、松手靠 `GetAsyncKeyState` 判定，捕获降级成加速器；顺带修掉"在贴片外松手导致 `_dragging` 卡死、之后悬停就自己跳"的第二个 bug。见 `dev/runtime-pitfalls.md` §16.5.1、BACKLOG CR-73。
- 单击不再触发设置写盘（偏移没变就不保存）。真机复看清单在 CR-73。
- **ATK 鼠标 / 键盘（新插件）**：接入 `AtkPlugin`，一个品牌级开关管两条链路。鼠标走 16 字节 op 帧（`0x04` + `(0x4D - sum)` 校验和），键盘走 VIA `0x02/0x96/0x24`。键盘对 op 帧只回显、`data[0]` 恒 41%，故解析强制校验和。会写帧，默认关。见 `hardware/atk.md`。
- **MCHOSE X9 耳机**：接入 64 字节查询帧，响应 `[2]` 电量、`[3]` 状态（放电/充电/满电/休眠）。与 V9 Pro 同帧结构但 VID 与键鼠同号段，分类器必须前置分流，否则会往耳机发 `0x11/0x06`。并入迈从插件，共用 `Mchose` 开关，默认关。见 `hardware/mchose-x9.md`。
- **AI 参与编写**：本轮三台设备的代码由 AI（WorkBuddy 智能体）编写，贡献者完成抓包、真机验证与审阅；已在 `CONTRIBUTORS.md` 与 `contributions.md` 注明。
- **文档与 BACKLOG**：新增两篇设备文档，README / sources / safety / handoff / docs 首页同步；未复测项登记 CR-84、CR-85。

## 2026-10-19

- **新增寸渊影弑 DARKRIM 与盖世小鸡 GameSir 支持**：杰理 dongle（`3654:6901`）帧格式与 CRC-16/CCITT-FALSE 逐字节确认，电量须发官方同款 `GetBasicInfo(0x09)`（纯被动拿不到），默认关；盖世小鸡状态流由心跳 `0F F2 00` 开启（发一帧约 250 帧/秒、只维持约 2 秒），插件每 60 秒发一帧心跳读 2 秒，不依赖官方软件，默认开。详见 protocols.md §16 / §17。

- **黑爵 / 雷神电量**：接入 AK870 与 ML903 NearLink Se 的已验证接收器查询，品牌开关沿用中英文命名；补充帧解析与迈从接口隔离测试，详见 `hardware/ak870-ml903.md`。

- **HECATE G5M Pro**：接入只读电量插件，认领限定 `35BB:D00B` 的厂商集合；拔出设备时把离线状态交回设备列表。帧样本来自贡献者，本机未复测。见 `hardware/protocols.md` §7。
- **AULA F99 Pro / DELUX M900 Pro**：接入两个插件。AULA 会写会话帧，默认关，且不进 15 秒发现。DELUX 只读，但与英菲克同 VID/PID，本机没扫到接收器，也默认关。见 `hardware/protocols.md` §8、§9。
- **DualSense**：接入蓝牙 HID 只读电量。只认 `054C:0CE6` 的 78 字节报告，CRC 失败丢弃。不拿同一 PR 里的 GANSS 和 V9 Pro。见 `hardware/dualsense.md`。
- **迈从鼠标 V2 / G87 V2（#17）**：接入 E2 被动状态帧与 `0x11/0x06` 触发帧，认领加 `0x3837`/`0x41E4` 白名单并把 64 字节等值判据放宽到 ≥60（65 字节接口原先永远命不中）。新增 `ResolveKind` 让同一插件按协议区分键鼠图标。鼠标链路为本贡献者真机验证，本机只有 G87 V1，未复测。见 `hardware/protocols.md` §1.6。
- **GANSS GS3104T-Pro（#15 裁剪版）**：接入 2.4G 电量插件，发固定 33 字节查询帧、响应 `[3]` 为电量。会写帧故默认关，不进 15 秒发现。未沿用贡献者把有效下界降到 1 的做法：0 由协议层拒、1 由公共下界拒，不为此改基类。判据不符时补一条节流诊断日志，避免认领失败无声。本机无该设备，未复测。见 `hardware/protocols.md` §11。
- **GANSS 认领加产品名兜底**：`&mi_03#` 与「产品名含 `GANSS` / `GS3104`」二选一，接口编号随固件漂移时仍能认领；usage、报告长度、reportId 仍是硬条件，产品名绕不过去。设备实际自报什么名字未确认，见 `hardware/ganss.md` §6.1。
- **设置页插件开关改为品牌级**：插件行只有两行字——中文主标题 + 英文副标题（北通 / BETOP），不带型号、品类与协议。型号由扫描识别、只出现在设备列表；「2.4G 设备插件」区块名维持原样。插件行按设置键稳定排序，避免反射顺序漂移导致条目换位。规则写入 `AGENTS.md` §9。
- **MCHOSE V9 Pro 耳机（#15 第三轮）**：接入 64 字节固定查询帧，响应 `[2]` 为电量。**并入现有迈从插件**而不是新开插件键——品牌级开关只能一行（`AGENTS.md` §9），协议名 `headset-v9`。会写帧故默认关；充电状态未知，一律 `Unknown`。加了一条回显保护（整帧等于查询帧时丢弃），否则回显会被读成 1%。本机无该设备，未复测。见 `hardware/protocols.md` §12。
- **本轮收口**：README 中英设备表补上迈从鼠标 V2 / GANSS / V9 Pro 三行，`hardware/sources.md` 补两个协议的依据与许可边界（这两处前几轮漏了）；社区 PR 里明确不做的 5 项登记为 BACKLOG CR-68 ~ CR-72，避免下一轮重新分析。设备接入到此为止。
- **贡献者名单**：新增根目录 `CONTRIBUTORS.md`，按人记录谁适配了哪些设备，README 设备支持表下方加入口。与 `docs/project/contributions.md` 分工写清：前者回答"谁做了什么"，后者回答"这个 PR 收了什么、没收到什么"，`AGENTS.md` §3 补了对应规则。

## 2026-10-18

- **更新说明**：新增用户向 `docs/project/release-notes.md`，打包时截当前版本段传给 `vpk pack --releaseNotes` 并作为 Gitee Release 正文；发现新版本的确认框显示本次更新内容，关于页新增「更新日志」可离线回看历史版本。格式契约与三处消费方见 `dev/build.md` §8。
- **前行者 X87 键盘**：新增插件，走官方驱动 `DeviceDriver.exe` 反编译出的 33 字节查询帧（`00 20 01 00 … 校验和`），电量在响应 `body[3]`。真机实测 100%。接口必须在 MI_03（33 字节）而非 MI_04（65 字节）。规格 `hardware/protocols.md` §7.1。
- **前行者 S9 鼠标**：新增插件，走官方网页驱动 `hub.eweadn.cn` 的贝盈协议，feature report `0x03` 63 字节帧（`[0]=sum([1..62])`），响应 `[6]` bit7=充电、低 7 位=电量。真机实测 72→73%。`0xFF06` 输出通道是死路。规格 §7.2。
- **键盘发现节拍**：键盘休眠时查询无应答，只探一次会在启动时永久漏掉；`SupportsDiscoveryProbe = true` + `ProbeCooldown = 12s` 参与 15 秒快速发现节拍。
- **鼠标休眠 0%**：dongle 在鼠标休眠时回结构合法但电量为 0 的满长帧，codec 层直接拒收（真 0% 不存在），不再每轮刷读数过滤器 WARN。
- **协议与实现来源索引**：新增硬件来源文档，集中记录外部协议依据、参考项目、许可与本机验证边界，并从 docs 首页建立入口。
- **README 与文档首页整理**：确认 Logitech Bolt 已真机验证、QHW 待对应设备验证，移除易过期的包大小和测试数；根 README 和文档首页改为精简导航，专题索引避免堆叠长表。
- **剩余时间估算提示原因**：估算不可用时，设备列表和任务栏悬停分别说明当前原因，方便判断是离线、充电、采样不足还是近期未见电量下降。
- **关于页功能简介**：补充剩余时间估算与设备列表右键重命名的简短说明，方便用户了解这两项功能。
- **设备名称输入限制**：仅允许中文、英文、数字、空格和常见中英文标点，最多 20 个字符，避免名称过长或含不可预期字符。
- **本机打包保留增量基准**：发布新版本时保留上一版全量包，让 Velopack 生成 delta；本机更新流程可优先下载增量包。

## 2026-10-17

- **关于页**：去掉「打开发布页」按钮，换成「打开 GitHub」；项目行同时列出 Gitee / GitHub 两个地址。
- **GitHub 仓库统一到 `evren-oct/vigil-watt`**：`publish-github.ps1` 里的旧账号地址（`ACoolOctober/VigilWatt`）一并改掉，顺带修正脚本末尾过时的"设置页更新源"提示。
- **署名统一**：csproj 的 `Authors` / `Company` / `Copyright` 改为 `evren-oct`（旧 GitHub 账号 `ACoolOctober` 已停用）；README 新增「仓库与账号」一节，说明 GitHub `evren-oct` 与 Gitee `YGWG-LX` 是同一人。
- **README 版本徽章改成动态**：`shields.io/github/v/release/evren-oct/vigil-watt` 直接读 GitHub 最新 Release 的 tag，发版后不必再手改 README；徽章点击跳 Releases，下载节补上 GitHub 链接、Gitee 标为镜像。
- **更新源改为 GitHub 优先、Gitee 回退**：`UpdateService` 同时持有 `GithubSource` 与 `GiteaSource`，GitHub 8 秒内无响应或报错才换 Gitee；检查成功后源固定，delta 包继续从同一个源下。界面文案不再写死 "Gitee"，回退时状态栏会提示"GitHub 不可达"。两仓库需传同一份 Release 资产（`build.md` §8）。
- **README 补任务栏贴片截图**：截图从剪贴板默认名 `image-20261017…png` 改为 `assets/screenshot-taskbar.png`，与其它 `screenshot-*` 命名一致，避免相对路径下线上裂图。顺手删掉无任何引用的遗留图 `assets/image-20261009210019246.png`。

## 2026-10-16

- **扫描提速**：HID 路径未变时复用元数据，不再每拍 CreateFile；各插件 `AttachAsync` 并行探测（同句柄仍串行）。见 `code-flow.md` §2。
- **罗技空槽（CR-62）**：超时负缓存 5 分钟、每轮最多补探 1 槽；空接收器也启只读通知监听。
- **关于页**：导航底栏新增介绍页；检查更新、Gitee / 发布页跳转与联系邮箱从设置迁出，设置只留操作项。
- **删除 Elan 占位**：`04F3:026F` 是义隆另一台接收器（非炽天使），插件从不认领设备也不出电量；连同探针脚本一并去掉。身份结论留在 `protocols.md` §3。
- **更新下载进度**：确认后弹出进度对话框 + 关于页进度条；托盘提示开始/完成/失败。避免下载中无反馈。
- **更新进度倒退**：Velopack 多文件各自从 0% 重报，UI 改为高水位只升不降。

## 2026-10-15

- **任务栏贴片悬停名称**：鼠标停在某一格上显示设备名。跨进程子窗口改自绘分层提示 + `TME_HOVER`；样式类 Win11 圆角气泡。见 `runtime-pitfalls.md` §16.10。
- **英菲克 ZHEN**：应用内真机验证通过；README / handoff 标为已打通。

## 2026-10-04~05

- **G87 打通**：官方 M HUB 的 0x13 会话（`13 05 01` 开会话，`13 4A 01` 查电量）。规格 `hardware/protocols.md` §1。
- **MchosePlugin 重写**：只发有出处的帧，删掉盲扫。事故见 `hardware/safety.md`。
- **插件基类**：`BatteryPluginBase` + 中央认领 + 反射发现。见 `architecture.md` §3。
- **语言包**：自包含运行时只留 en-us / zh-CN / zh-TW。见 `dev/build.md` §6。
- **0% 电量**：当时把休眠归零帧当成无效。次日才确认真凶是类型位，见下。

## 2026-10-06

- **经典蓝牙电量**：设置页读的是 HFP 网关节点的 PnP 属性 `DEVPKEY_Device_BatteryLevel`，不是 AEP 属性。实测 W820NB 60%。见 `runtime-pitfalls.md` §3。
- **休眠伪读数**：类型位 `0x05` = 电量、`0x02` = 状态通知；旧代码把 `0x02` 帧的 `0x01` 当成 1%。`0x02` 现在立即标离线。见 `runtime-pitfalls.md` §12。
- **可测化**：帧解析抽成 `MchoseV1Codec` / `BatteryReadingFilter`，单测用真机帧。
- **首扫 27.6s → 约 5.6s**：Attach 的读数被丢掉又重读；雷柏放行了从不发心跳的接口；QHW 探测先空等 3 秒。并行探测已放弃（阻塞读会饿死线程池）。日志安静不等于卡死。
- **北通**：标准电池 API 把它报成有线满电，必须走厂商接口。T9 的 report ID 不是写死的 `0x02`，要从 caps 读。`HidP_GetReportDescriptor` 在 hid.dll 里不存在，P/Invoke 失败会被 catch 成「设备不支持」。T9 不回答查询，改解析 Col04 被动流。见 protocols.md §4。
- **显示名修成原始 ID**：WinRT 在友好名没出来时会把 AEP 设备 ID 当名字。占位名要验，注册表配对名只读兜底。
- **第二轮真机复测**：北通产品名收成「北通 T9」；图标匹配把手柄放在键盘/鼠标前面；已配对但断开的耳机不能凭 HFP 上残留的电量算在线。
- **第三轮：改显式声明设备类型**：真机显示名里没有 mouse/keyboard/head，按名字猜不成立。类型由插件声明。见 `architecture.md` §6。
- **第四轮：刷新按钮**：按钮禁用被自己立刻解开，BLE 轮询是 fire-and-forget，重入被静默丢掉。改成可等待，并复用进行中的那一轮。点了没反应要先分清「没做」还是「做了但看不见」。
- **历史统计 / 历史管理**：统计页加指标卡和悬停读数。删除按设备批量做，删后 VACUUM；历史页不订阅 `DevicesChanged`，避免采样打断勾选。
- **图标与托盘**：exe 图标要同时写 csproj 和 `AppWindow.SetIcon`。托盘提示只在启动时画过一次，所以永远是「暂无在线设备」；`DeviceUpdated` 和 `DevicesChanged` 都要订。
- **T9 的 `0x50`**：手柄关掉后 dongle 仍推流，只把电量字节换成 `0x50`。只认这个值，不把低位猜成标志（宁漏判、不误判）。验证口子见 handoff §4。

## 2026-10-08

- **本机电量**：进列表、历史和低电量提醒；不进托盘最低电量；无电池不显示；默认关。命名空间不能叫 `System`，否则遮蔽 BCL。
- **雷柏掉线**：心跳是活动驱动的，停手静音十几秒很常见。静音阈值 12s 改 90s，只做兜底。断链看 `[18].bit7`，延迟确认，不立刻判离线（bit7 一抖列表会闪）。
- **迈从发现太慢**：15 秒发现节拍原先只给零写入插件。`SupportsDiscoveryProbe` 允许写帧，但写入频率由 `ProbeCooldown` 兜住。见 safety.md。
- **迈从关机后永不离线**：`SupportsPassiveListen` 被当成「轮询空读不算失败」。机制和策略拆开；离线参数收进 `OfflinePolicy`。`0x00` 只当核实触发，不当离线证据。真机关机后迈从约 1.8s、雷柏约 6s 离线。

## 2026-10-09

- **日志分级**：默认 Info，逐帧下沉 Debug；写失败不再静默；文件首行写时间基准，好和 history.db 的 UTC 对齐。设计以 `dev/logging.md` 为准。按日分文件是次日才改的，见 10-10。
- **经典蓝牙类型**：watcher 把所有已配对设备的 `Kind` 写成耳机，错误类型还会被冻结。按设备类别 → 服务 UUID → 名字判定，猜不出就是 Unknown。类别在父节点，UUID 在子节点。见 `runtime-pitfalls.md` §3.1。

## 2026-10-10

- **第十七轮**：补上 `docs/dev/review-standard.md` 和 `docs/reviews/BACKLOG.md`。审查发现只进 BACKLOG；handoff 只留还要人做的事。当时核对：共享可变 `DeviceStatus` 让变化判定恒为假（CR-03）。
- **第十八轮**：上线前全检后修了当时的 P0/P1（快照后再比较、罗技写入闸门、北通探测闸门、小组件订阅泄漏、历史库 WAL、退出释放、异常熔断等）。报告在 `deliverables/gstack/pre-launch-check-2026-10-10.md`。纯拆分、无法点检的项留在 BACKLOG。
- **第十九轮**：独立「写入许可」让罗技在插件已开时仍不认领设备。改为插件开关即写入授权，删掉第二套开关。熔断仍在。迈从因此在开关打开时会写帧，用户知情。
- **任务栏贴片（覆盖窗，后被替换）**：deskband 在 Win11 已不可用，AppBar 会挤占屏幕。先做无边框窗口摆进任务栏矩形。本机电量不进贴片。用户不要底色。Win10/Win11 上「白框」换过两次定性：可见层一度判错；最终两层都清，见 `runtime-pitfalls.md` §17。点任务栏闪一下是 Z 序空档。截屏空白是因为截屏进程没声明 DPI 感知。这套覆盖窗后来整段删除，见 10-12。
- **日志按日**：推翻「不做按日分文件」。`logs\app-日期.log`，默认留 14 天，不再按大小滚动。旧 `app.log` 废弃。
- **文档目录** `doc/` 改为 `docs/`。

## 2026-10-11

- **设置页搜索**：按标题、说明、分区和插件名定位，点一下滚到目标并短暂高亮。搜索不改设置值。
- **协议采集工具**：`src/tests/VigilWatt.ProtocolCapture/`，USBPcap / WebHID / BLE 被动广告。用法 `hardware/debugging.md` §9。
- **通知区方案撤回**：曾把贴片改成托盘图标，用户要的是独立胶囊，已恢复。随后双击胶囊打开主窗口。
- **CR-53**：固定 10 分钟熔断把唤醒检测一起挡住（实测盲区 12 分 52 秒）。改为 30s→5 分钟封顶，设备自己的通知可以打破熔断，退避期满立刻半开。见 `Plugins/WriteBackoff.cs`。真机复看仍在 handoff §4。

## 2026-10-12

- **Win10 白框与裁字**：本机是 Win10 19045，不是文档里写过的 Win11。accent 透明通道实测无效。`TransparentWindowSurface` 两层都清、不分系统。贴片宽度改按客户区。Win10 上透明和 DWM 投影互斥，小组件取透明（CR-57 未做）。见 §17.8。
- **右键菜单不消失**：WinUI 菜单靠窗口「去激活」关闭，贴片是 `WS_EX_NOACTIVATE`，永远不会去激活。改成 Win32 `TrackPopupMenuEx`。见 §18。
- **贴片改为真子窗口**：WinUI 窗口不能 `SetParent`（内容岛会灰）。新建 `TaskbarPillWindow`：`SetParent` + `WS_EX_LAYERED` + `UpdateLayeredWindow`。16ms 抢 Z 序删掉。旧覆盖窗的白框和裁字随之消失。见 §16。右键、拖动、竖向/多屏、深色仍待人手看。
- **页面切换慢**：四个页面不缓存，设置页又在构造函数里建完整棵树。改为缓存，订阅改到 `Loaded`/`Unloaded`，设置页拆成只建一次和每次重读。见 §19。真机复看仍在 handoff §4。
- **内存爬升结案（CR-64 wontfix）**：不是泄漏。判泄漏只看 PRIVATE；任务管理器里掉下去的往往是工作集。同轮清掉导航 `BackStack`（CR-65）。报告 `docs/reviews/archive/memory-growth-2026-10-12.md` §6。

## 2026-10-13

- **贴片位置**：外接屏拔插时，过渡帧的夹紧结果写坏了记住的偏移。只在拖动中写回，并按记下时的 DPI 换算；父窗口或 DPI 变了就重新挂上。见 §16.5 / §16.8。真机通过。

## 2026-10-14

- 软件更新源切到公开 Gitee Release，应用启动后自动检查；用户确认后安装，启动到托盘时通过托盘通知延后确认。补充 Gitee Release 附件发布脚本与更新状态展示。
- 程序集补上作者、公司和版权；发布目录带上 `LICENSE`。`src` 下 `.cs` 的主要类型补了文件头。新建文件的格式在 `AGENTS.md` 与 `docs/dev/comment-style.md`。
- **任务栏耳机图标**：GDI+ `DrawArc` 在 Win10 上把半圆弧两端画得不在同一高度，头梁看起来是斜的。改为居中的对称贝塞尔；小组件补上右侧描边。见 §16.9。
- **历史设备名**：`MAX(name)` 按字典序，会把偶发写入的 `ble:BluetoothLE#…` 排到真实名前面。改为取最新非占位名。
- **历史曲线悬停**：命中层设在 Content 根上，气泡才稳定出现；后台刷新不再无谓重建曲线冲掉气泡。内容仍是时间 + 电量。
- **历史曲线断档**：相邻采样间隔过大时不再连线（默认 3×采样间隔），离线/关机区间不再画成假斜坡。
- **英菲克 ZHEN**：新增纯被动电量插件；按官方网页驱动抓包确认 85% 放电帧，不发送配置命令。
- **自动更新改用内置 GiteaSource**：Velopack 1.2.161 已内置 `GiteaSource`，而 Gitee 的 `/api/v1` 与 `/api/v5` 返回一致（实测同键同值），自写的 Gitee 适配器整段删除。旧适配器用 `/releases/latest` 定位 tag，仓库还没有 Release 时该端点返 404，`EnsureSuccessStatusCode()` 会把"没发过版"报成"检查失败"。见 `dev/build.md` §8。
- **启动更新弹窗**：`XamlRoot` 未就绪时改为等窗口 `Loaded` 再补弹，避免确认框永久丢失；下载进度节流刷新状态文本。发布 1.0.11（首个 Gitee Release）。
- **publish-gitee.ps1 三处 Gitee 特有坑**已修（PS 5.1 把 JSON `null` 读成字符串 `"null"`、建 Release 只收表单参数、强制要求 `target_commitish`），细节见 `dev/build.md` §8。

## 2026-10-18

- 设备列表右键可自定义外设名称和图标；覆盖值按稳定设备 ID 保存，应用到实时展示与历史页面，已有采样不改写、后续采样沿用自定义名称。
- 新增剩余使用时间估算，按近期放电历史计算并显示于设备列表和任务栏悬停；设置页提供默认开启的全局开关。
- 放宽剩余时间估算门槛：使用最近 90 天数据，3 个采样点且可测到 1 个百分点净下降即可估算，适配长续航设备。

## 2026-10-20

- 修「点几次保存后进程直接退出」：受限令牌下写 `HKCU\Run` 抛 `UnauthorizedAccessException`，异常从 `Save_Click` 逸出、被全局熔断器累计 5 次后放行退出。`AutostartService` 改为 `TrySetEnabled(out error)` 内部收口，失败只弹「开机自启未生效」，其余设置照常保存。
- **定案「没托盘 / 贴片跑到原点 / 保存失败」的真因**：工作区目录带 `Low` 完整性标签，构建出的 exe 继承它，进程以 Low IL 运行，写不了 Medium 的用户目录与 HKCU、也发不了 shell 消息。csproj 构建后自动 `icacls /setintegritylevel Medium`，`run-app.ps1` 再兜一次；启动日志新增 `进程身份`（含 `完整性=`）一行。见 `dev/runtime-pitfalls.md` §20。

## 版本

发新版留着上一版全量包才有 delta；清空 `dist/` 再打同版本就没有。见 `dev/build.md` §8。**1.0.11 是首个上传 Gitee Release 的版本**（1.0.10 及以前只在本地 `dist/`，故 1.0.11 的 delta 是拿本地留存的 1.0.10 全量包做基准算出来的）。

| 版本 | 日期 | 相对上一包多了什么 |
|---|---|---|
| 1.0.2 | 10-08 | 本机电量。Release 必须 `AllowUnsafeBlocks`（build.md §4） |
| 1.0.3 | 10-11 | 清空后重打，无 delta；同日重发含 CR-53 |
| 1.0.4 | 10-12 | Win10 透明修复；留下 1.0.3 全量包，delta 约 97KB |
| 1.0.5 | 10-12 | 右键菜单 Win32 化。当日又重发两次（真子窗口、CR-65），那两次 dist 是空的，无 delta |
| 1.0.6 | 10-13 | 分辨率往返后的贴片位置；有 delta |
| 1.0.7 | 10-14 | 版权信息与文件头；相对 1.0.6 的 delta 约 97KB |
| 1.0.8 | 10-14 | Win10 任务栏耳机图标扶正；相对 1.0.7 的 delta 约 80KB |
| 1.0.9 | 10-14 | 历史设备名取最新非占位；曲线悬停气泡稳定 |
| 1.0.10 | 10-14 | 历史曲线断档不连线；相对 1.0.9 有 delta |
| 1.0.11 | 10-14 | 更新源换内置 GiteaSource；启动弹窗与进度修复；**首个 Gitee Release**；相对 1.0.10 的 delta 约 68KB |
| 1.0.12 | 10-15 | 任务栏贴片悬停显示设备名 |
| 1.0.1 | 10-16 | 本机试装包（含更新进度 UI）；版本回落便于测升级，非正式发版 |
| 1.0.2 | 10-16 | 更新进度高水位（多文件不倒退）；本机试装，非正式发版 |
| 1.2.0 | 10-16 | 关于页（介绍 + 检查更新从设置迁出）；本机打包 |
| 1.2.1 | 10-18 | 剩余时间估算及原因提示、设备右键重命名简介；本机打包 |
| 1.2.3 | 10-21 | 9 款社区设备适配；删除离线设备与永久屏蔽、贴片拖动/选屏/尺寸三处修复、失败不再静默；本机试装 |
| 1.2.2 | 10-19 | 8 款社区设备适配（会写帧的默认关）、更新说明与贡献者页；本机试装 |

## 2026-10-23

- 为罗技 PRO X 2 LIGHTSPEED 增加 Centurion 查询路径、严格接口判据与解析单测，并让新路径复用 `BatteryPluginBase` 的读数过滤和熔断；真机覆盖放电、充电和休眠唤醒，并修正拔充电线时短暂超时导致的离线误判。
- 诊断版真机复核确认耳机按分钟轮询读到充电/放电变化、状态不变时列表更新时间与鼠标同步，并核验关闭罗技开关后插件卸载且不再查询；随后确认蓝牙模式通过 Windows HFP 设备节点读到与系统设置一致的 60%，无需加入 Centurion 协议。详见 `hardware/logitech.md` §2。
- 将经典蓝牙中识别为耳机且名称为 `PRO X 2` 的条目显示为 `PRO X 2 LIGHTSPEED`，避免与罗技 2.4G 鼠标混淆；依据用户提供的 G HUB 型号及电量截图。
- 合并 #45 后的收口：改正 `WritesEnabled` 注释（Centurion 是第二个写点，不再是「唯一写点」）、补回误删的 `IsHidppInterface` 摘要，R2 例外条款按「混合型插件」重写，`logitech.md` 补 §1 标题。
