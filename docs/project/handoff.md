# 会话交接（给下一个 AI / 未来的自己）

> 快照时间：2026-10-14。**先读本文，再按需查其他文档**；内容冲突时以专题文档为准（本文是快照）。
> 逐轮原因在 **[changelog.md](changelog.md)**。本文只留当前状态、设备、未完成的待办、注意事项、文件速查。
> **维护约定**：收工后本轮原因追加进 `changelog.md`；本文件只改 §1 状态与 §4 未完成待办。已完成的待办删掉，不划掉留着。全文超过 120 行先删历史再写（`AGENTS.md` §3）。

## 1. 当前状态

新增适配：9 个新品牌（华硕 ROG、雷蛇、AKG、ATK、HyperX、凌豹、VGN、Haute42、墨将 MOJHON）与北通鲲鹏20 三通道、狼蛛 G9 / L9 / F108 Pro、VGN V98 Pro、迈从 G75 Pro / A5 V2 Ultra / X9、凌豹 K98、ATK A98 Pro / A9 Air 大师版+ / VXE R1 Nearlink、AULA HERO 68 Air 等，合计约 32 台；新品牌默认关。逐台状态见 §2 表，协议与验证边界见对应 `hardware/*.md`。VXE R1 Nearlink（`373B:10C9` 接收器 / `10C7` 本体）共用 ATK 开关，贡献者真机验证无线与插线充电、插线时由本体接管，**本机未复测**（`hardware/atk.md` §9）。

当前安装包 **1.3.1**（`dist/`，本机试装用；尚未上传正式 Release）。正式线上以 GitHub 最新 Release 为准，Gitee 是回退源；更新源 GitHub 优先、8 秒不通回退 Gitee。发新版时保留上一版 `*-full.nupkg` 作为 delta 基准；规则见 `dev/build.md` §8。

本机已打通：迈从 G87（0x13）、雷柏 VT3 + VT2S Air Max（status-report7，纯被动）、北通 T9（被动流）、经典蓝牙耳机（HFP 网关节点 `DEVPKEY_Device_BatteryLevel`）、罗技 Bolt 接收器（C548）+ MX Master 3S。本机电量可选，默认关。**其余机型均为贡献者真机、本机未复测，逐台状态见 §2 表**——别把 §2 里「贡献者真机」当成我们验过。

任务栏贴片是原生子窗口 `Services/TaskbarPillWindow.cs`（`SetParent` 进 `Shell_TrayWnd`），不是 WinUI 覆盖窗；小组件仍用 `TransparentWindowSurface`，Win10 上因此没有 DWM 投影（CR-57）。**拖动走 16ms 轮询，不靠鼠标捕获**——捕获对非前台窗口无效（`dev/runtime-pitfalls.md` §16、§16.5.1；§17 只对小组件）。选屏由 `Services/TaskbarPillManager.cs` 每秒按 `monitorDevicePath` 匹配，配置的屏不在就降级贴主屏，设置页显示真实屏幕名——`\\.\DISPLAYn` 是配置序号不是身份，见 `dev/monitor-identity.md`。

页面已缓存（`NavigationCacheMode = Required`），订阅跟 `Loaded`/`Unloaded`。设置页可搜索。日志按日：`logs\app-日期.log`。插件开关即写入授权，没有第二套「写入许可」。

设备列表支持：右键自定义名称与图标（按设备 ID 持久化，历史页用当前名、已有采样不改写）；**右键「忽略此设备」在线也能用**——只从列表/托盘/贴片/小组件隐藏、不再写历史，插件照常轮询，恢复入口在底部「已忽略 N 台设备」；**右键删除离线设备**——同时清掉该设备历史（VACUUM），勾「永久移除」写本地黑名单、插件认领阶段即跳过，恢复入口同为底部那行（三份名单见 `code-flow.md` §5.2，真机验收清单见 §4 第 9 条）；剩余时间估算（至少 3 个不同时间点且有净下降才显示，否则说明原因，见 `code-flow.md` §5）。**贴片可指定只显示哪几台设备**（设置页勾选，按**显示名**匹配——不用设备 ID，ID 由路径哈希而来、插线/换口会变；一个都不勾则显示全部在线设备）。

**开发版数据目录在 `bin\x64\Debug\...\win-x64\data\`**（`AppPaths` 用 `#if DEBUG` 分流，Release 才用 `%LOCALAPPDATA%\VigilWatt`），所以从 `bin` 双击即可运行、不碰用户目录；`VIGILWATT_DATA_DIR` 可覆盖。为什么必须这么改见 `dev/runtime-pitfalls.md` §20，用法见 `dev/build.md` §3.1。

设备接入已收六批，逐台状态见 §2 表；设备管理类改动（#14 双通道去重、#16 设备管理）、#15 的 BLE 大改、issue #12/#11 按 BACKLOG CR-68 ~ CR-72 排队。**设备诊断页（2026-10-24 新增，主窗口第 5 项）**= 未适配设备的 USB/HID 只读采集，流程与边界见 `capture.md`；审查标准见 `dev/review-standard.md`，未修项见 `reviews/BACKLOG.md`。会写帧的（HERO、N9、ATK、华硕、雷蛇、墨将）默认关；15 秒发现节拍按插件各自声明（`SupportsDiscoveryProbe`）——ATK、樱桃、派世、VGN、雷蛇、凌豹、HECATE 无线、迈从、前行者、索尼 H9 开了，HERO、N9、高斯、北通、墨将没开。**ATK 是唯一另开"熔断期半开探活"的插件**（`BreakerProbeInterval` 30s，`safety.md` §3 规则 3 ③；别的品牌要开按 `runtime-pitfalls.md` §23.1 逐个评估）。纯被动的 CF2 默认开。ATK 认领按「配对组」判、两只鼠标各自一条记录，响应须过运行时校验和；HyperX 充电态只认插拔事件帧；雷蛇 Viper V3 Pro SE 只开零权限 Feature 句柄。HECATE 自 #47 改主动查询、开关默认关。**#72 之后 ATK 双连接改留本体**（`SkipPathsForThisPass` 按组摘掉接收器，CR-115）。全部为贡献者真机、本机未复测。HERO 闲置静音见 CR-81，N9 未核对官方 App 见 CR-84，ATK 显示名与配对表见 CR-98 / CR-99，HECATE 查询阻塞见 CR-100；**1.3.0 的组合识别失效已修**（扫描器补读序列号，CR-112）——VGN 配对由此**首次真正生效**，待真机验蜻蜓 F2 / F1 V5 插线场景（`runtime-pitfalls.md` §24）；墨将的设备 ID 仍是路径哈希（CR-116 ~ CR-118）；协议与验证边界见 `hardware/aula.md` §2、`akg.md`、`atk.md`、`hyperx-cf2.md`、`asus-rog.md`、`razer*.md`、`hecate.md` §3、`mojhon.md`。

更新说明是用户向的 `docs/project/release-notes.md`，随包分发：关于页「更新日志」离线可看全部历史，更新确认框显示目标版本的内容（来自更新源 notes，缺失时只显示版本行）。规则见 `dev/build.md` §8。

下结论前先跑 `cmd /c ver`。**本机现在是 `10.0.26200.9445`（Win11 25H2）**；「哪一层背景可见」「Win10 上透明与 DWM 投影互斥」（CR-57）这类基于 Win10 的判断都要按新系统重判，别照抄。

## 2. 用户设备清单（勿对号入座错）

| 设备 | 通道 | 状态 |
|---|---|---|
| 迈从 G87（VID_41E4&PID_2001） | 0x13 会话 | 已打通 |
| 迈从 G87 V2（3837:201B）/ A7 V2 Pro（3837:100A） | 前者走 0x13 会话；后者走 E2 被动帧 + `0x11/0x06` 触发 | 代码已接入，默认关。贡献者真机验证，本机未复测（mchose.md §1.6） |
| 罗技 Bolt（C548 + MX Master 3S） | HID++ | 已真机验证 |
| 罗技 PRO X 2 LIGHTSPEED 耳机（046D:0AF7） | LIGHTSPEED 2.4G / Centurion | 2.4G 真机验证放电、充电、休眠唤醒与关闭开关后停查；蓝牙走 Windows HFP 节点，USB 有线模式未验证（logitech.md §2）。046D:C54D 的 PRO X 2 是另一台鼠标 |
| 雷柏 VT3 二代（VID_24AE&PID_1412） | status-report7 被动心跳 | 已打通，零写入。雷柏不是 HID++，勿让 Logitech 插件认领 |
| 英菲克 ZHEN（`1D57:FA65`）/ IN9（`248A:5B2F`） | ZHEN 被动 report `0x03`；IN9 走 feature `0x0C` 快照 + 每轮一条刷新帧（撞重复值换号重试一次，单轮最多 2 条） | ZHEN 已打通、零写入（放电 `0x01` / 充电 `0x03` 已校准）；IN9 2026-10-31 维护者真机抓包复测，会写帧，两型共用一行开关故默认关（inphic.md §1 §2） |
| HECATE G5M Pro（VID_35BB&PID_D00B） | 被动 report 0x04 状态帧 + 主动查询 `04 00 00 1A` | 代码已接入，**默认关**（会写帧）。主动查询后电量不再依赖 HECATE Connect，离线按连续 3 次无应答判。贡献者真机、本机未复测（hecate.md §3） |
| HECATE G3M Pro 升级版（接收器 VID_35BB&PID_D3FB） | 17 字节 `0xFF02` 通道，STATUS `08 03` / POWER `08 04` | 本机实测 50% / 3848 mV，默认关、会写两条只读帧。充电位未验证（hecate.md §2） |
| 狼蛛 AULA：F99 Pro（`3554:FA09`）/ G9 耳机（`38A6:0030`）/ L9 Ultra（`38A6:28FD`） | `0x13` 会话推送 / 被动 `AB 47 4D 86` / `0x81` 查询 | 三个协议类共用一个「狼蛛」开关（设置页一行）。F99/L9 默认关、会写帧；G9 纯被动。贡献者声明，本机未复测（aula.md §1 §3 §4） |
| DELUX M900 Pro（VID_1D57&PID_FA65） | `0x000A` 被动 5 字节帧 | 代码已接入，默认关。与英菲克同 VID/PID；2026-10-19 本机没扫到 `1D57`（delux.md §1） |
| DualSense（VID_054C&PID_0CE6） | 蓝牙 HID report `0x31` | 代码已接入，默认开、零写入。约 10% 一档。贡献者声明，本机未复测（dualsense.md） |
| Haute42 Adapter 4DS（`054C:0CE6`，USB 侧） | 只读 feature：`0xF8` 固件串认领、`0xFA` 芯片 ID 当设备 ID、`0xF9` 电量/充电 | 代码已接入，默认关、**零写入**，与 DualSense 插件按 USB/蓝牙互斥。贡献者声明，本机未复测（haute42.md） |
| GANSS GS3104T-Pro / 狼蛛 F108 Pro（VID_05AC&PID_024F） | 33 字节查询帧 `00 20 01 … 21` + 响应 `[3]`；两品牌同指纹，按产品名分 | 代码已接入，默认关。会写帧，不进 15 秒发现。设置页一行「高斯 / 狼蛛」。贡献者真机，本机未复测（ganss.md §7） |
| 迈从耳机：V9 Pro（`291D:385D`）/ X9（`3837:6045`） | 64 字节查询帧 `55 65 01 …`，X9 与键鼠同号段、靠 `mi_00&col05` 分流 | 代码已接入（并入迈从插件，共用 `Mchose` 开关），默认关、会写帧。贡献者声明，本机未复测（mchose-v9-pro.md、mchose-x9.md） |
| 北通 T9（PID_5073）/ 鲲鹏20 2.4G（PID_5159，BTP-KP20D）/ 鲲鹏20 有线（PID_5158，BTP-KP20D XINPUT WIRED）/ 鲲鹏20（PID_5191）/ 新批次 `20DD:5128`（KP20 DONGLE）、`20DD:5130`（KP70 底座） | T9 走 Col04 被动流；2.4G 走杂项页广播（`usage_page=0x00FF`/`0x0003`，纯听零写入）；有线 COL01 同页但静默、须 `02 15` 查询（betop.md §1.8.1）。5158/5159 归一 `betop:kunpeng20` 一个设备位 | T9、鲲鹏20 2.4G 与有线（78% 充电中）已真机验证；PID_5191 主动查询未验证；蓝牙模式固件无电量通道、不识别（§1.8.3）；`20DD` 只回 `02 CD`、电量格式未知（§1.8.4） |
| 寸渊影弑 SHADOW（3654:6901） | 0x11/0x01 通知；须发官方 `GetBasicInfo(0x09)` | 已打通（90% / 充电均核对过），默认关（§13） |
| 盖世小鸡 G7 Pro CE（3537，usage 0xFFF0） | 心跳 `0F F2 00` 开 0x12 状态流，60 秒突发读一次 | 已打通，不依赖官方软件，**默认关**（会写心跳帧，按主动通信处理；gamesir.md §1.4） |
| 前行者 X87 键盘（VID_0C45&PID_FEFE&MI_03） | 33 字节查询帧（0x20/0x01，校验和 [32]） | 代码已接入，默认关。会写帧。贡献者真机 100%，**本机未复测**。两型共用「前行者」开关。MI_04 是 65 字节同 usage 陷阱，只能写 MI_03（eweadn.md §1.1） |
| 前行者 S9 鼠标（VID_372E&PID_1026） | feature report 0x03，贝盈 beiyin_uuid | 代码已接入，默认关。会写帧。贡献者真机 72~73%，**本机未复测**。`0xFF06` 输出通道是死路（§18.2） |
| CHERRY PIXIU75（dongle 046A:01E4 / 有线 046A:01E2） | 64 字节块通道 0xFF1C，读块 0x1A（电量 + 充电位） | 已打通，真机 48%↔官方软件一致；插线 55% + 充电中（cherry.md §1.5） |
| 派世 Pulsar X2 V2（VID_3554&PID_F508） | 17 字节帧 `0x08`，STATUS `0x03` / POWER `0x04` | 已打通，真机 20% + 3566 mV（pulsar.md §1） |
| 迈从鼠标：K7 V2 Ultra+（`3837:1018`）/ G3 V2（`3837:4245`）/ A5 V2 Ultra（`3837:100C`） | 分别是 `0x4D` 通道 `0x0900` 查询、65 字节 `AA 30` 主动查询、反码 Feature `0x11` | 代码已接入，默认关、会写帧。K7 与 G3 V2 贡献者真机，A5 贡献者声明，本机均未复测（mchose.md §3 §4 §6） |
| 迈从 G75 Pro（VID_3554&PID_FA09） | 与 AULA F99 Pro 同指纹，靠会话 ACK 密码分流到迈从 | 代码已接入（并入迈从插件），默认关。会写帧。贡献者真机 ACK 型号 G75 Pro、`13 4A` = 36%，本机未复测（mchose.md §5） |
| 凌豹 M5PRO Max（VID_3151&PID_402D）/ K98（VID_3554&PID_FA09） | 前者 feature 65 字节、官方驱动同款 `F7` 查询，`[5]` = 充电位；后者 `0x13` 会话，靠型号码 `0x0212` 与 AULA `0x0158` 分流 | 一个插件管两台（`ResolveKind` 按协议给图标），默认关、会写帧。贡献者真机 88%/95%（M5PRO Max）与 100%（K98），有线 `4026` 不认领，本机未复测（lingbao.md §1 §2） |
| VGN：蜻蜓 F1 V5（`391D:1A23`/`1026`）/ F2 大师+（`3554:FB56`/`FB57`）/ V98 Pro（`320F:5088`） | kc2 配置帧 `0x06`（电量在 `payload[19]`）/ y2 `BatteryLevel` / V98 `0x1A` 查询 | 一个插件管键鼠，默认关、会写帧。插线后按序列号只留一条接口；贡献者真机，本机未复测（vgn.md） |
| 黑爵 AK870（`3151:4011`）/ 雷神 ML903 NearLink Se（`369B:0234`） | 分别是 feature `F7` 状态查询（`[2]` 电量 + `[4]` 在线）与 `0xA0` 设备信息 TLV `0x0225` | 代码已接入，默认关、会写帧。贡献者实机确认，本机未复测（ak870-ml903.md） |
| EDIFIER W820NB | HFP 网关 `DEVPKEY_Device_BatteryLevel` | 已打通，与设置页一致 |
| 华硕 AURA、Virtual Multitouch、lifeme 音箱、弱水时砂 2.4G（38FF:0001） | — | 与电量无关 / 无电量通道（rose.md），勿探测 |
| 雷蛇（毒蝰 V4 Pro `1532:00E6`/`00E5`；BlackShark V2 Pro `1532:0555`；Viper V3 Pro SE `1532:00DF`） | 经典 90 字节 feature 查询；耳机独立 PA 协议；SE 只开零权限 Feature 句柄 | 代码已接入，默认关、会写帧。均贡献者真机、本机未复测；V4 Pro 有线 `0x84` 充电查询无应答，充电态 Unknown（razer.md、razer-headset.md、razer-viper-se.md） |
| 雷蛇 Kraken V4 耳机（接收器 `1532:056C`） | 64 字节 `0x02` 报告，`0x21` 电量 / `0x2A` 充电，两条命令每轮交替 | 独立插件 `RazerKrakenPlugin` 但共用雷蛇开关，默认关、会写帧。贡献者声明，本机未复测（razer-kraken.md） |
| 华硕 ROG Strix Scope II 96（Omni 接收器 `0B05:1ACE`） | 按物理接收器分组、核实通道 2 配对型号后才查询电量 | 代码已接入，默认关、会写帧。贡献者真机 95%，本机未复测（asus-rog.md） |
| 索尼 INZONE H9 II（054C:0FA8） | `0xFF04` 控制传输 SET_REPORT 查询 | 代码已接入，默认关、会写帧。贡献者真机 78→94%（充电中），关掉 INZONE Hub 仍可读；本机未复测（inzone-h9.md） |
| AULA HERO 68 Air（`372E:106C`，UUID `12000000000A`） | Bytech 8K 分片查询 + ACK | 代码已接入，默认关、会写帧，与 F99 Pro 共用 Aula 开关。贡献者真机 10% 放电/充电，本机未复测（aula.md §2） |
| AKG N9 Hybrid（接收器 `0ECB:20D9`） | 固定 RACE 电量查询，控制输入读取 | 代码已接入，默认关、会写帧。贡献者真机 50% / 100%，充电态 Unknown，本机未复测（akg.md） |
| ATK：F1 Pro Max（`373B:101B`/`101A`，本体 `1015`）/ F1 Ultimate 2.0（`11D9`/`11E4`）/ A98 Pro 键盘（`1152`，VIA） | 鼠标 `FF02` 口 17 字节 `08 04 …49`；键盘 VIA `02 96 24` | 代码已接入，默认关、会写帧，一套开关管键鼠。F1 Ultimate 2.0 两个 PID 归一到 `atk:atk-f1ult`、本体可独立认领（插线时 dongle 读不到电量，atk.md §4.3）。本机真机的是 F1 Pro Max，F1 Ultimate 2.0 与 A98 Pro 为贡献者真机、**本机未复测**（atk.md §2 §7 §8） |
| ATK F1 V3 ULTIMATE+（接收器 `373B:1303`，本体 `12A9`） | 同 F1 Pro Max 一条查询帧 | 代码已接入，默认关、会写帧。与 F1 Pro Max 同机各一条记录、互不串台；显示名要插一次线才拿到，本机未复测（atk.md §4） |
| ATK A9 Air 大师版+（GEM 8K 接收器 `373B:1278`，本体 `1293`） | 同一条查询帧（`FF02` / 17 字节） | 代码已接入，默认关、会写帧。**贡献者**真机打通：无线 70%/80% 放电、插线 85%/95% 充电均全应答，两种形态归一条记录；**本机无此鼠标、未复测**。双连接（接收器没拔就插线）改留本体——实测此时只有本体应答、接收器已沉默（atk.md §2.2 §3.3）。⚠ **充电读数虚高约 15 pp**（有线 95% ↔ 拔线 80%，电压仅差 63 mV），插拔瞬间会跳变，属固件电压法电量计行为、codec 不修正（CR-113） |
| HyperX Cloud Flight 2（`03F0:0AC1`） | `FF90` 口被动推送帧 | 代码已接入，默认开、零写入。贡献者真机放电 40% / 充电 94→96%，与 NGENUITY 3.1.0 对照；充电态只认插拔事件帧，本机未复测（hyperx-cf2.md） |
| 墨将烈风2 GALE2（`E2B7:122C`，2.4G 接收器 / 有线直连同 PID） | 厂商页 `0xFFAB` 的 `A5 04 04` 读电量 + 充电标志 | 代码已接入，默认关、会写帧。**贡献者**真机：放电 96%/97%、充电 99%→100%，充放电标志 A/B 已验；**本机无此手柄、未复测**。两形态设备路径不同、目前各占一条记录（CR-116），跨模式 3~4 pp 差异成因未定（CR-118）（mojhon.md §7.4） |
| 本机电池 | `GetSystemPowerStatus`，Id `sys:battery` | 已接入，默认关。托盘最低电量不统计本机 |

## 3. 历史（不在这里记：查「为什么这么改」看 changelog.md，查协议与坑看对应专题文档）

## 4. 待办

审查未修项以 `docs/reviews/BACKLOG.md` 为准。PR #44 待验证项见 CR-84，HERO 非阻塞传输层测试见 CR-97；充电测试已取消，不等待插拔充电操作。其它人工待办如下。

1. **插线校准两处**：迈从 `ParseKeyboardV1Charge` 补全充电位（已知 0x10 = 充电中）；雷柏满电时 `[7]` 在 0/1/2 间横跳（底座涓流），采不到稳定值 → 100% 一律判「充满」，不再等这个标志（rapoo.md §5.2）。
2. **北通鲲鹏20 有线变体**（PID_5191/KP20EB）主动查询仍未真机验证（PID_5159 广播 / PID_5158 有线 COL01 查询已打通，见 betop.md §1.8）；手柄关机后广播帧是否有占位值无样本，待观察。T9 的 `byte23` 低位仍只认 `0x50` = 未连接；验证方法见 betop.md §1.4.1。
3. **贴片人手复看**：连续拖 5 次、上下甩出贴片、拖到两端再回拖、松手后不自跳（CR-48）；自动隐藏、竖向/多屏（CR-41）、深色对比（CR-42）、是否压住任务栏按钮（CR-39）、右键菜单（CR-60）、悬停设备名（多台/底栏/深色任务栏）。分辨率往返已于 2026-10-13 通过。Win10 小组件投影（CR-57）等用户决定是否做。
4. **页面缓存真机**：来回切换是否变快、离开再回来数据是否仍刷新、设置值是否与 `settings.json` 一致、搜索是否仍可用。坑见 `dev/runtime-pitfalls.md` §19。相关未修项 CR-61 / CR-63。
5. **启动更新确认框**：本轮只验到"托盘气泡 + 日志发现新版本"（当时 `StartMinimized=true`，窗口没开）。**2026-10-20 起 `StartMinimized` 默认 `false`，所以「发现新版本」确认框成了默认路径，更需要在真机上确认一次**——打开主窗口时应弹确认框，未真机验证。
6. **贴片选屏真机复看**：本机只有「仅第二屏」一种接法实测过（配置屏不在 → 降级主屏，日志已确认）。还需切「仅电脑屏幕 / 扩展 / 复制」各一次，看贴片是否 2 秒内跟到新任务栏（重建去抖 2 拍，见 §16.12）、设置页刷新按钮后列表是否反映当前接法、`<名字>（未连接）` 是否显示正确。清单在 `dev/monitor-identity.md` §7。
7. **设备页 / 诊断页真机复核**：诊断页的 Feature 读取（勾选后实际 GET_REPORT）、采集中拔设备、记录上限截断、另存为对话框与剪贴板（非打包 WinUI）——本机只验到被动监听拿到真实报文，清单在 `capture.md` §7；设备页「忽略此设备」要复看托盘/贴片/小组件是否同步消失、恢复是否立刻回来、重启后是否仍被忽略。
8. **墨将 GALE2 应用内验证**（本机无此手柄）：设置页打开「墨将 MOJHON」开关（默认关，作者所装 v1.2.3 不含本插件，必须用本仓库源码构建），确认列表出现条目、电量与充电标志与官方助手一致；再边看界面边插拔接收器，确认有线↔2.4G 切换那 0.5 秒列表是否闪离线（mojhon.md §7.5、§9）。

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
| 程序图标 | `scripts/make-app-icon.py` → `Assets/VigilWatt.ico`；csproj `ApplicationIcon` 和 `MainWindow.ApplyWindowIcon` 都要 |
| 历史库 | `Storage/HistoryStore.cs`；页面 `Views/HistoryPage`、`Views/StatsPage` |
| 日志 | `Utils/Log.cs`；设置页「日志」；设计 `dev/logging.md` |
| 语言包白名单 | `VigilWatt.App.csproj` 的 `MuiLanguagesToKeep`（build.md §6） |
| 更新说明（用户向） | `docs/project/release-notes.md`；解析 `Services/ReleaseNotes.cs`、渲染 `ReleaseNotesView.cs`、确认框 `UpdatePrompt.cs` |
| 某轮为什么这么做 | `docs/project/changelog.md` |
| 还有什么没修 | `docs/reviews/BACKLOG.md` |
| 审查红线 | `docs/dev/review-standard.md` |
