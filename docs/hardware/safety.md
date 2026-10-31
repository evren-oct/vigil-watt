# 硬件安全规则（必读）

无线外设的 dongle 是有状态的，写错命令不只读不到数据，**可能把用户的键盘/鼠标搞断连**。以下规则是用两次真实事故换来的，改任何涉及写入的代码前先读完。

## 1. 事故一（2026-10-04）：盲扫导致键盘断连

**经过**：为找电量协议，写了"诊断模式"：255 个邮箱命令全扫（经 Col06 feature 0x06 逐个 SetFeature）+ 输出 report ID 0x10-0x20 扫描 + feature ID 0x09-0xFF 全扫，且跑了两轮。之后用户键盘与 dongle 断连（"怎么回事？你做了什么？我现在键盘连不上了"）。

**恢复**：拔下 dongle 断电 10 秒 → 插回 → 键盘电源循环（关再开）→ 必要时重新配对（Fn+Esc 或插线测试）。

**根因**：dongle 固件对未知命令序列没有防护，大批量乱序写入会让它的会话/状态机错乱。**收益为负**：两轮扫描一条有效数据都没扫出来。

**用户明确要求：不要再出现这种问题。**

## 2. 事故二（2026-10-05）：崩溃进程把 dongle 会话占死

**经过**：用浏览器跑官方 M HUB WEB 时页面被刷新，会话没正常关闭。之后 dongle 进入"假死"：**所有人**（我们的探测、甚至官方 M HUB 自己）发开会话命令和电量查询全部零响应，持续数小时。

**误判教训**：期间把"零响应"误判为"协议不对/接口不对"，绕了很久。实际上协议和帧都是对的，是 dongle 状态被占死。

**判定方法**：官方软件发官方帧也静音 = 状态问题，不是协议问题。先排查"谁持有会话"（本例是 msedge.exe），kill 持有者或重新插拔 dongle 立即恢复。

**推论**：本应用是 dongle 会话的长期持有者，与官方 M HUB 同时连会互相抢会话（表现为 HUB 连不上设备），不要同时开。

## 3. 安全规则（写代码时逐条对照）

1. **只发有出处的帧**：官方软件抓包拿到的、或公开逆向文档记载的。禁止对任何接口做命令枚举/ID 扫描/字节猜测。
2. **插件开关本身就是写入授权，且会写帧的新插件默认关闭**。**唯一授权入口是 `AppSettings.PluginEnabled[<插件名>]`**——开插件即允许它发送工作所需的帧；**不再有独立的"写入许可"开关**（2026-10-10 取消，见 CR-37：对"不写帧就完全没功能"的插件，两个开关只会让人以为功能坏了）。判定入口唯一：`BatteryPluginBase.WritesEnabled`（= `IsWritesEnabled(Name)`，直读插件开关）与 `CanWrite`。**逐插件现状见 §3.1——改代码前对照那张表，别照抄旧说法。**
3. **自动熔断 + 递增退避**：连续 3 次查询失败 → 停止写入，只留被动监听；退避期**递增**（30 秒 → 1 分 → 2 分 → 4 分 → **封顶 5 分钟**，`Plugins/WriteBackoff.cs`），期满允许重试一轮，再失败则升档（`BatteryPluginBase.CanWrite` / `TripBreaker`）。
   退避是为对付键盘休眠（用户离开即睡，唤醒后若永久停写就再也读不到），不是给"失败后换命令"留口子——**重试的永远是同一条已知命令**。
   ⚠️ **固定 10 分钟是错的口径**（2026-10-11 改，CR-53）：设备休眠是常态，固定长退避会把"唤醒检测"一起挡死——实测唤醒后盲区达 **12 分 52 秒**。
   配套三条：① **事件驱动放行**——设备自己推通知触发的那次"核实查询"不受熔断限制（仍受插件开关约束），放行频率 ≤1 帧/30 秒；
   ② 退避期满由被动空闲 tick **立刻**发起半开重试，不再苦等 60 秒轮询拍。
   ③ **半开探活是第三条通道，默认关闭、按品牌逐个评估**（`BreakerProbeInterval`，2026-10-27 起只有 ATK 开）。
      它只在**熔断停写期间**发帧——设备本来就不应答，所以边际写入不落在正常使用时段；成功即解除熔断，
      失败不记账、不升档。**各厂商对"睡着的设备被定期戳一帧"的容忍度不一样，能不能开、间隔多长，
      必须按实际设备确认，不能照抄**（有会话占用史的协议、对同刻第二帧不应答的 dongle 尤其要小心）。
      **建议 30 秒起步**（= 每轮 60 秒主轮询之间最多一帧）；要更密先算清每小时帧数。判断清单见
      `dev/runtime-pitfalls.md` §23.1。
4. **限频**：每个轮询周期最多 1 条查询帧 + 每句柄一次的会话帧。重试最多 2 次、带 500ms 间隔。
   - **例外（2026-10-08 用户批准）**：迈从键盘参与 **15 秒发现节拍**（`SupportsDiscoveryProbe`），
     把"键盘开机后多久被认出"从最坏 ~60 秒压到 ~15 秒（起因：用户复报"键盘启动了但程序要一段时间
     才识别到在线"）。约束：**只在设备不响应时**才会重探（每次 ≈3 帧，最坏 ≈12 帧/分钟）；
     认领成功后 `Attach` 对已知设备直接返回，该路径**零写入**。
     发帧内容不变——仍然只有官方/实测的会话帧与 0x4A 电量查询，**没有引入任何新命令**。
     该属性是插件级的，所以同一插件的鼠标与 V9 Pro 耳机也按 15 秒节拍重探：鼠标最多 1 条 `0x11/0x06`
     触发帧、耳机最多 1 条 64 字节查询帧、G3 V2 的 gmouse 最多 2 条 65 字节查询帧（1.2 秒窗口 × 2 次尝试），
     都比键盘链路（1 条会话 + 最多 2 条查询）更省。
   - **例外（2026-10-27 用户批准）**：华硕 ROG 耳机（`0B05:1AFA`）每轮发 **2 条查询帧**——
     `12 07` 取电量、`12 08` 取充电位。POWER 帧剔除电量字节后两种充电状态完全相同，无法推断充电，必须单独问一次。
     两条是**先后等应答**而非同时发（`Query` 等上一帧回来才发下一帧），单轮最坏 2 帧、且不重试，低于迈从键盘的 3 帧。
     充电帧无应答时沿用上次值、**不计入熔断**，见 `asus-rog.md` §5。
   - **例外（2026-10-27，#80 合入）**：ATK 鼠标/键盘参与 **15 秒发现节拍**，并在熔断停写期按
     `BreakerProbeInterval` **30 秒**做半开探活（间隔取用户口径"建议 30 秒起步"）。写入由 `ProbeCooldown`
     10 秒与探活间隔双重兜住：认领成功后 `Attach` 零写入、熔断期探活**失败不记账不升档**，
     最坏 ≈1 帧/30 秒。见 `atk.md` §5、`dev/runtime-pitfalls.md` §23。
5. **被动优先**：能靠监听（input report / feature GET）解决的不写入。迈从 dongle 的自发推送（`13 0A` 帧）就是免费数据源。
6. **新设备接入流程**：先被动监听看日志 → 再查官方驱动是否支持（支持就抓官方的包）→ 拿到精确帧后才允许写入验证。全程用户知情。
7. **任何"诊断/扫描"代码不得留在正常执行路径上**（哪怕有开关）。要实验就在独立脚本（test-*.ps1 / python）里做，用户点头后再跑。
   - **2026-10-10 修复轮已清**：`HidWriteProbe` 写入变体矩阵已删除；曾留在正常路径的 Elan feature 扫描（CR-10）随后整插件已删（见 rose.md）。要排查，按本条在独立脚本里做。

### 3.1 各插件写入开关现状（2026-10-10 取消独立许可后逐条核对）

| 插件 | 写入开关（= 插件开关） | 默认 | 说明 |
|---|---|---|---|
| 雷蛇 | `PluginEnabled["Razer"]` | **false** | 鼠标/毒蝰 SE 走 feature 查询、耳机走 PA 或 64 字节 `0x02` 报告；Kraken V4 电量与充电两条命令每轮交替发一条，共用一个开关，见 `razer*.md` |
| 华硕 | `PluginEnabled["Asus"]` | **false** | Omni 只读配对身份核实与 Scope II 96 的 12/01 查询；耳机 `0x1AFA` 每轮 2 帧（`12 07` 电量 + `12 08` 充电位，2026-10-27 用户批准），见 [asus-rog.md](asus-rog.md) |
| 迈从（含 X9 耳机） | `PluginEnabled["Mchose"]` | **false** | dongle 协议未破解，探测写入可能干扰设备，故默认关；开插件即授权写帧。**一个开关管键盘、鼠标、V9 Pro / X9 耳机、K7 鼠标、G3 V2（gmouse）、A5 V2 Ultra（反码 Feature）七类设备**（品牌级，`AGENTS.md` §9） |
| 北通 | `PluginEnabled["Betop"]` | **true** | 鲲鹏20 的 2.4G 广播通道**纯听零写入**，只有有线静默接口才发 `02 15` 查询； 查询是官方客户端同款只读命令（cmd=0x5），风险低，故默认开 |
| 罗技 | `PluginEnabled["Logitech"]` | **false** | Centurion 新路径继承 `BatteryPluginBase`，复用公共写入闸门、读数过滤和熔断退避；HID++ 历史路径保留 `HidppIO.WritesAllowed` 与专用会话处理 |
| 雷柏 | 无（纯被动） | — | `WritesEnabled => false` + `ProbeWrites => false`，零写入 |
| 黑爵 | `PluginEnabled["Ajazz"]` | **false** | 3151:4011 已验证 F7 状态查询；见 `ak870-ml903.md` |
| 雷神 | `PluginEnabled["Thunderobot"]` | **false** | 369B:0234 官方 02 设备信息查询；见 `ak870-ml903.md` |
| 樱桃 | `PluginEnabled["Cherry"]` | **false** | 046A 块通道读 `0x1A` 必须发帧；`SupportsDiscoveryProbe` + `ProbeCooldown` 10s；见 `cherry.md` §1.7 |
| 派世 | `PluginEnabled["Pulsar"]` | **false** | 3554:F508 的 STATUS/POWER 两帧查询；同上节拍；见 `pulsar.md` §1.6 |
| 英菲克 | `PluginEnabled["Inphic"]` | **false** | ZHEN（`1D57:FA65`）纯被动读 report `0x03`、零写入（`WritesEnabled => false` + `ProbeWrites => false`）；IN9（`248A:5B2F`）每轮先发刷新帧（序号自增，首条被当重复请求时换号重试一次，单轮最多 2 条）再 GetFeature。两型共用一行开关，按"会写帧"默认关，见 [inphic.md](inphic.md) §2 |
| HECATE（查询型） | `PluginEnabled["HecateWireless"]` | **false** | 35BB:D3FB 只发 STATUS `08 03` / POWER `08 04` 两条只读查询；走 15 秒发现节拍，见 `hecate.md` §2 |
| HECATE | 无（纯被动） | — | `WritesEnabled => false` + `ProbeWrites => false`；仅读 `35BB:D00B` 的 report `0x04` |
| DELUX | 无（纯被动） | **false** | 与英菲克同 VID/PID，只读 `0x000A`；本机未扫到接收器，故默认关 |
| AULA | `PluginEnabled["Aula"]` | **false** | F99 Pro 发 `13 05 01`、HERO 68 Air 发固定 UUID 查询与分片 ACK、L9 发 `0B 81`；G9 耳机是同一开关下的纯被动插件（零写入）。见 `aula.md` §2 §3 §4 |
| AKG | `PluginEnabled["Akg"]` | **false** | N9 Hybrid 接收器只发固定 `0CD6` 电量查询，控制输入读取结果，不重发；见 `akg.md` |
| VGN | `PluginEnabled["Vgn"]` | **false** | 蜻蜓 F1 发配置帧 `0x06`（report ID `0xB3`/`0xB5`）、F2 发 `BatteryLevel` 查询、V98 Pro 发 7 字节 `0x1A` 查询；y2 靠 `ProbeCooldown` 兜住发现节拍，见 `vgn.md` §5 |
| 凌豹 | `PluginEnabled["Lingbao"]` | **false** | M5PRO Max 发官方驱动同款 `F7` feature 查询；K98 只发 `13 05 01` 会话帧，按型号码 `0x0212` 与 AULA 区分。**参与 15 秒发现节拍**（`SupportsDiscoveryProbe` + `ProbeCooldown` 10s，插件级、两条链路都算），见 `lingbao.md` §2.2 |
| GANSS / 狼蛛 F108 | `PluginEnabled["Ganss"]` | **false** | 发固定查询帧才能读电量；两个品牌同指纹共用这一行开关；不进 15 秒发现，见 `ganss.md` §7 |
| DualSense | 无（纯被动） | — | `WritesEnabled => false` + `ProbeWrites => false`；只读蓝牙 HID `054C:0CE6` |
| Haute42 | `PluginEnabled["Haute42Gaming"]` | **false** | 只发 `HidD_GetFeature`（`0xF8` 固件串 / `0xFA` 芯片 ID / `0xF9` 电量），零写入；与 DualSense 插件按「USB / 蓝牙」互斥，见 `haute42.md` §5 |
| ATK | `PluginEnabled["Atk"]` | **false** | 鼠标发 17 字节 op 帧、键盘发 VIA `02 96 24`；两条链路共用一个开关，见 `atk.md` §5 §7。**唯一开了半开探活的插件**：`SupportsDiscoveryProbe` + `ProbeCooldown` 10s + `BreakerProbeInterval` 30s（`safety.md` §3 规则 3 ③） |
| HyperX | 无（纯被动） | **true** | `WritesEnabled => false` + `ProbeWrites => false`；dongle 固件只实现 interrupt OUT，写帧一律 `ERROR_INVALID_FUNCTION`，只读 `FF90` 推送帧 |
| 墨将 | `PluginEnabled["Mojhon"]` | **false** | `E2B7:122C` 厂商页 `0xFFAB` 只发官方助手实录里的只读帧 `A5 04 04`（电量+充电标志）与认领时一次的 `A5 04 E2`（型号标识）；`SupportsDiscoveryProbe` + `ProbeCooldown` 10s，见 `mojhon.md` §7 |

三条硬约束：

1. **判定入口唯一**：`BatteryPluginBase.WritesEnabled => IsWritesEnabled(Name)`，直读 `AppSettings.PluginEnabled[Name]`——**插件关着就写不了**。写点各自保留这道检查作为纵深防御（R1：写入必须能被一个开关挡住）。
2. **会写帧的新插件必须默认关闭**（`PluginEnabled` 里默认 `false`），否则装上去就自动写帧。
3. **熔断降级仍在**：连续 3 次失败 → 停写（`WritesDisabled`）→ 只被动监听 + **递增退避**（30 秒起、封顶 5 分钟，见 §3 规则 3）。这是**自动**降级，与用户开关无关——用户那侧只有"插件开/关"两档。

## 4. 应急恢复（给用户的手册）

1. 键盘/鼠标断连：拔 dongle → 等 10 秒 → 插回；键盘关机再开机。
2. 仍不行：键盘插 USB 线直连测试（能识别 = 硬件没坏，是配对丢了）→ 重新配对（迈从一般是 Fn+Esc 长按，见型号说明书）。
3. dongle 假死（官方软件也连不上）：关掉所有可能持有会话的程序（本应用、浏览器里开着的 M HUB WEB、官方桌面版），还不行就重新插拔 dongle。
