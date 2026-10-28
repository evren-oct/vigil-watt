# 协议与实现来源索引

本文记录 VigilWatt 的协议依据、公开参考项目和功能实现参考，方便追溯方案从哪里来。这里是来源索引，不替代 [protocols.md](protocols.md) 中的帧格式、实机样本和安全边界。

## 记录口径

- **协议依据**：官方软件 / 网页、公开逆向资料，或本项目取得的实机抓包与被动样本。
- **实现参考**：借鉴外部项目公开的协议分析、交互方式或功能行为；不代表复制其代码。
- **本机验证**：列出本项目实际验证到的设备 / 场景；外部项目的设备结果不自动视为本项目验证。
- **许可**：仅记录来源明确标注的许可。参考协议说明不等于可复制其代码或资源；引入外部代码前须单独核对许可与归属。

## 设备协议与电量读取

| 设备 / 方案 | 来源与参考内容 | 本项目采用及验证 | 许可 / 边界 |
|---|---|---|---|
| AKG N9 Hybrid 接收器 | [HeadsetControl Airoha 协议](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/audeze_maxwell_device.hpp) 的 `06/07` 封装与 `0CD6` 查询；N9 真机控制输入和查询响应。 | 自行实现固定查询，限定 `0ECB:20D9` MI03 Col02；贡献者真机读取 50%，默认关闭。 | 未复制外部代码；只确认单点电量，充电状态未验证。见 [akg.md](akg.md)。 |
| ASUS Scope II 96 / Omni | G-Helper 键盘电量协议、rigstats Omni 配对表及华硕官方兼容表。 | 独立实现身份校验与 12/01 查询；本机 95%、未充电，见 [asus-rog.md](asus-rog.md)。 | 仅使用协议事实，未复制实现代码。 |
| Razer BlackShark V2 Pro (2023) | OpenRazer PR #2862 的 PA 格式和会话序列、HaloBattery 同型号实现。 | 独立实现；本机 79%、未充电，见 [razer-headset.md](razer-headset.md)。 | 上游驱动 GPL-2.0-or-later，PR 未合并；未复制代码。 |
| Razer Viper V3 Pro SE 无线 | [OpenRazer](https://github.com/openrazer/openrazer) 的公开型号表、90 字节控制协议、电量与充电查询。 | 按协议事实自行编写 C#，本机 1532:00DF 短时读取成功；详见 [razer-viper-se.md](razer-viper-se.md)。 | 上游驱动 GPL-2.0-or-later；未复制驱动代码；其他型号未验证。 |
| MCHOSE G87 旧代 dongle（report `0x13`） | 官方 M HUB 的会话与电量交互；通过官方软件抓包和 dongle 实机观测确认帧结构、校验、查询和推送。 | 按抓包与实机样本实现；本机 G87 已验证。 | 官方软件交互是协议依据，不表示引用其代码。具体帧与未定语义见 [protocols.md](protocols.md)。 |
| MCHOSE QHW（新代 dongle） | [MCHOSE M HUB WEB](https://www.mchose.com.cn/#/home) 前端 JS 中的 `QhwCmdQueueItem`、命令表及 CZ 相关脚本。 | 按官方前端协议实现备用路径；适用于 `0x3837` 世代 dongle。当前用户设备 `41E4:2001` 不适用。 | 官方资料，非开源代码复用。入口与设备范围见 [protocols.md](protocols.md)。 |
| Logitech HID++ 电量 | Logitech HID++ 1.0 / 2.0 的设备发现与电池特征；实时电量通知行为与 Logi Battery Dashboard 同源。 | 本项目自行实现 HID++ 报文与特征解析；Bolt + MX Master 3S 已真机验证。 | 现有协议手册未记录外部代码复用；Logi Battery Dashboard 只作为行为 / 协议线索。实现细节见 [protocols.md](protocols.md)。 |
| Logitech PRO X 2 LIGHTSPEED Centurion | [HeadsetControl](https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_gpro_x2_lightspeed.hpp)（设备/PID、报文形态与 legacy 电池查询）；[LogitechBatteryMonitor](https://github.com/Recopec/LogitechBatteryMonitor)（独立协议与充电字段实机报告）。 | 按公开协议事实自行实现 64 字节只读电池查询；2026-10-23 用户真机日志与截图确认 `046D:0AF7` 查询读数、插拔充电状态、状态不变时列表更新时间同步。 | 两参考仓库标注 GPL-2.0；未复制代码。充电字段状态存在来源差异，`0x03` 暂不解释；测试未留原始响应字节；2026-10-23 诊断版关闭罗技开关后确认插件卸载、设备与会话释放，约两分钟内没有新的 Centurion 查询记录，代码写入闸门同时核验通过（未做 USB 总线级抓包）。蓝牙模式由经典蓝牙监视器读取 Windows HFP 设备节点电量，2026-10-23 真机读数 60% 与 Windows 设置一致；该路径不属于 Centurion 协议。 |
| Rapoo V2 `status-report7` | [pedro3z0/rapoo-software-linux](https://github.com/pedro3z0/rapoo-software-linux)（基于 A HUB 1.0.19 逆向及官方 Web 驱动抓包）；[D3m0nZOnFire/mousectl](https://github.com/D3m0nZOnFire/mousectl)（VT3 PRO 一代实测）。 | 参考协议分析后自行实现被动心跳解析；本机 VT3 二代 `24AE:1412` 已验证，部分状态以本机样本为准。 | `rapoo-software-linux` 标注 MIT；`mousectl` 的许可需以其仓库当前声明为准。两来源设备型号与本机不同，不能直接替代本机验证。 |
| Betop / 北通厂商接口 | [oitsukiii/betop-battery](https://github.com/oitsukiii/betop-battery) 的 `docs/protocol.md`：对北通官方 Electron 客户端做互操作性分析，不含厂商代码。 | 参考协议文档实现；本机 T9 被动流已验证。 | 项目标注 MIT；本项目验证情况与限制见 [protocols.md](protocols.md)。 |
| Inphic ZHEN `report 0x03` | 英菲克官方 ZHEN 网页驱动的 WebHID 行为、驱动显示与本机被动输入样本。 | 只读解析输入 report `0x03`，充放电状态经实机接线验证；不发送网页驱动的 report `0x04`。 | 基于官方行为和本机样本自行实现；未记录外部代码复用。见 [protocols.md](protocols.md)。 |
| HECATE G5M Pro `report 0x04` | 贡献者 dotisland 对官方 HECATE Connect 与接收器 `35BB:D00B` 的被动抓包（PR #13）。 | 只读解析 report `0x04`，不发送命令。帧与充放电对照是贡献者声明，本机未复测。 | 未复用官方软件代码。见 [hecate.md](hecate.md)。 |
| DELUX M900 Pro `0x000A` 状态帧 | 贡献者 Baron77777 对官方驱动与接收器 `1D57:FA65` 的被动抓包（PR #16）。 | 只读解析 5 字节状态帧。与英菲克同 VID/PID，本机这次没有扫到该接收器，插件默认关。 | 未复用官方软件代码。见 [delux.md](delux.md)。 |
| AULA F99 Pro `0x13` 会话/推送 | 贡献者 Baron77777 对 `3554:FA09` 实发会话帧并收到推送（PR #16）。 | 复用 `MchoseV1Codec`，只发 `13 05 01`；不发无应答的 `13 4A`，不进 15 秒发现。默认关。 | 未抓 AULA 官方驱动，未复用外部代码。见 [aula.md](aula.md)。 |
| AULA HERO 68 Air · Bytech 8K | [官方 A HUB](https://hub.aulacn.com/checkDevice) 的 `index-GW9ckk1o.js` / `vendor-DTzSSMIf.js`：UUID、电量命令及 8K 分片/ACK。 | 仅 `372E:106C` 的 `FF60/0061`、20/20、report `09`；UUID `12000000000A` 才认领。贡献者真机验证 10% 放电/充电，默认关，与 F99 Pro 共用 `Aula`。 | 独立实现协议兼容，没有复制厂商源码或资源；具体来源、帧和验证范围见 [aula.md](aula.md) §2。 |
| DualSense 蓝牙 `0x31` | Linux `hid-playstation` 的帧格式与 CRC；贡献者 luze258 的蓝牙样本（PR #15）。 | 只读 78 字节输入报告，不发送命令。电量是约 10% 档位。贡献者声明只实测过一个放电档，本机未复测。 | 未复制 Linux 驱动代码。见 [dualsense.md](dualsense.md)。 |
| GANSS GS3104T-Pro 33 字节查询帧 | 贡献者 luze258 对官方软件通信的 USB 抓包（PR #15），只有固定查询帧与响应样本。 | 只发这一条固定查询帧，响应 `[3]` 为电量，校验和按"除末字节求和取低字节"；会写帧故默认关。判据与风险见 §6。 | 未复用厂商代码，按抓包事实自行实现。本机无该设备，未复测。见 [ganss.md](ganss.md)。 |
| MCHOSE V9 Pro 64 字节查询帧 | 贡献者 luze258 在官方 MCHOSE HUB 显示 100% 时的 USB 抓包（PR #15），单条请求与响应。 | 只发 `55 65 01 …`，响应 `[2]` 为电量；并入迈从插件共用品牌级开关。充电状态未知，恒为 Unknown。 | 未复用厂商代码，按抓包事实自行实现。本机无该设备，未复测。见 [mchose-v9-pro.md](mchose-v9-pro.md)。 |
| MCHOSE K7 V2 Ultra+ `0x4D` / `0x0900` / `0x0600` | 官方 M HUB WEB 运行时加载的打包 JS（`purify.es-*.js`）中的帧构造器 `i1e()`、设备信息解析 `_K()`、事件解析 `LA()`；用 CDP hook WebHID 取得原始报文后按命令码定位。 | 按其字段表自行实现 `MchoseK7Codec`；只发 `0x0900` 查询，被动解析 `0x0600` 事件。本机 K7 V2 Ultra+ 真机验证（连续 6.5 小时零告警），充电/断连两态未留证。 | 官方前端是协议依据，不表示引用其代码；未复制任何厂商源码。接口地图、帧格式与失败应答见 [mchose.md](mchose.md) §3。 |
| MCHOSE Gmouse 主动查询（`3837:4245`） | 官方 M HUB 网页端 `/gmouse` 打包 JS 与社区项目 [aliezzzz/mchose-battery](https://github.com/aliezzzz/mchose-battery) 的 `src/protocols/g3.rs`（Rust）。 | 只发一条 `00 55 30 A5 0B 2E 01 01 01…`（65 字节）查询帧，应答前缀 `AA 30`，电量 `[off+8]`、充电 `[off+9]`；并入迈从插件共用品牌级开关，默认关。 | 未复用厂商/社区代码，按公开协议事实自行实现；社区实现与 [mchose.md](mchose.md) §4 逐字节一致（官方 JS 未逐字节复核）。本机 G3 V2 真机验证（读到 86% 未充电）。见 [mchose.md](mchose.md) §4。 |
| MCHOSE G75 Pro（`3554:FA09`）与 AULA 的认领分流 | 同一条 M HUB `getPassWord` / `KB_PW2MODEL` 型号密码表；本机会话 ACK 实测（密码 `03 00 00 00 01 15` = G75 Pro）。 | 迈从插件只认密码命中型号表的接收器，AULA 插件见到迈从型号主动让位；本机 `13 4A` 查询 31~36%。 | 官方前端作为型号表依据，未复制代码；判据与未收录型号的边界见 [mchose.md](mchose.md) §5。 |
| 凌豹 M5PRO Max 状态帧（`3151:402D`） | **运行时抓包**：Frida 注入官方驱动的 `iot_driver_v190.exe`，hook `hid_send_feature_report` / `hid_get_feature_report`，读到它每 10 秒发的 `00 f7 00…`（67 字节、无校验和）与 65 字节回帧；设备表条目来自官方驱动 JS（`pid:16429`、usage `0xFFFF`/2、feat 65）。 | 自行实现 `LingbaoCodec` + 插件；本机与官方驱动同读数（88%），重插接收器后首帧即可读。早期按 JS 的 `0x83`+`0xFF-sum` 是错的，只拿到缓存/全 0。 | 未复制厂商代码；抓包脚本见工作区 `tools/capture_hid.js`。未闭合点见 [lingbao.md](lingbao.md) §1.4。 |
| VGN 蜻蜓 F1 V5 / F2 大师+（`391D:1A23`/`1026`、`3554:FB56`/`FB57`） | 官方网页驱动 `hub.vgnlab.com.cn` 前端 bundle（`vgn-kc2`，reportId 179）与 VGN VHUB（Electron）`app.asar` 的 `JmServe` 类（`vgn-y2`，reportId 8）；本机真机抓帧核对。 | kc2 发一条配置帧 `0x06`（电量在 `payload[19]` 的 bit0-6），y2 发 `BatteryLevel` 查询（应答 `[5]` 电量 / `[6]` 充电）。 | 未复制厂商代码，按公开前端协议事实自行实现；帧与真机样本见 [vgn.md](vgn.md)。 |
| ATK A98 Pro 键盘（VIA `02 96 24`）与迈从 X9 耳机（`3837:6045`） | 键盘：标准 VIA 协议 + ATK 自定义通道，贡献者 Python 探针实测；X9：官方 M HUB 前端未混淆的 Vue bundle。 | 键盘发 4 字节 `02 96 24`（回包 `[4]`/`[5]`）；X9 发 64 字节 `55 65 01`（回包 `[2]` 电量 / `[3]` 状态）。 | 未复制厂商代码，按公开协议事实自行实现；见 [atk.md](atk.md) §7、[mchose-x9.md](mchose-x9.md)。 |
| 弱水时砂炽天使 2.4G | 官方 RoseLink（Flutter + BLE 自定义协议）及官方升级助手逆向；同时检查 2.4G dongle 的 HID 通道。 | 用于确认 RoseLink 电量协议不含电量命令、正常 2.4G 模式无可用电量通道；本项目不接入该设备电量。 | 逆向结论用于能力边界判断，不包含外部代码复用。见 [protocols.md](protocols.md)。 |
| Classic Bluetooth 耳机电量 | Windows HFP 网关设备节点的 `DEVPKEY_Device_BatteryLevel` 属性；与 Windows 设置显示同源。 | 通过 PnP 设备节点读取；本机 EDIFIER W820NB 已验证。 | Windows 系统属性读取方案；不是 BLE AEP 属性。实现背景见 [runtime-pitfalls.md](../dev/runtime-pitfalls.md)。 |
| ATK / VXE 无线鼠标 17 字节查询帧 | 公开逆向项目 [0x44oge-ATK](https://github.com/ReformedDoge/0x44oge-ATK)（含 `calculateAtkChecksum` 与 "Only one that works" 的写入方式说明），本项目另做真机复核。 | 自行实现帧编解码（`AtkCodec`）；本机 F1 Pro Max `373B:101B` 已验证 95% / 4092 mV，与 ATK HUB 共存无冲突。 | 参考公开的协议分析自行实现，未复制其代码。休眠占位帧与设备归一见 [atk.md](atk.md)。VXE R1 Nearlink `10C9` / `10C7` 另以本机 HID caps、固定查询应答及官方 ATK Hub 显示复核；本机 Hub 前端仅用于核对设备目录和协议事实，未复制厂商代码，验证边界见该文 §9。 |
| HyperX CF2 2.4G 推送帧 | 本机对 `FF90` 口的被动抓包（脚本独立于本项目），充电/放电状态与官方 NGENUITY 3.1.0 显示逐次对照。 | 零写入被动解析（`Cf2Codec`）；本机 CF2 飓风炫彩版 `03F0:0AC1` 已验证（放电 40%、充电 94→96%）。 | 全部由本机抓包事实得出，未复用厂商代码。字段推断被实机证伪两次的过程见 [hyperx-cf2.md](hyperx-cf2.md) §4。 |
| 墨将 GALE2 `A5` 读帧 | 官方「墨将精英助手」2.1.5.0 操作手柄时的 **USBPcap 内核抓包**（`captures/mojhon-vendor-frames.jsonl`，厂商通道 70 条记录，35/35 帧校验通过）；型号代号取自官方助手本体 `MojhonAssistant.exe` 内嵌的 devices 目录字符串。 | 只重放实录里出现过的读帧：`A5 04 04`（电量+充电标志）与认领时一次的 `A5 04 E2`（型号标识）；未做任何命令枚举。本机 `E2B7:122C` 2.4G 与有线两种形态均已验证（放电 96/97%、充电 99→100%）。 | 仅使用抓包得到的协议事实与设备自报字符，未复用厂商代码或资源。原始 pcap 3.1 MB 未入库，需要时可另附；字段口径与未定论项见 [mojhon.md](mojhon.md) §8。 |

## 应用功能与实现方案

| 功能 | 来源 / 设计依据 | 本项目实现与边界 |
|---|---|---|
| 任务栏电量贴片 | 基于 Windows 任务栏窗口层级与 Win32 子窗口行为的本项目探索；现有资料未确认采用外部应用项目的代码或设计。 | `SetParent` 将原生子窗口挂入 `Shell_TrayWnd`，自行处理布局、绘制、拖动和自动隐藏。实现坑与依据见 [runtime-pitfalls.md](../dev/runtime-pitfalls.md)。 |
| 桌面小组件 | 基于 WinUI / Win32 窗口能力的本项目实现；现有资料未记录外部项目参考。 | 使用独立透明窗口表面；平台差异和限制见 [runtime-pitfalls.md](../dev/runtime-pitfalls.md)。 |
| 历史统计与管理 | 本项目需求与 SQLite 能力，没有已记录的外部实现参考。 | 历史存储、曲线、指标、按设备删除与 VACUUM 由本项目实现；数据流程见 [architecture.md](../project/architecture.md)。 |
| 插件发现与写入安全 | 本项目自身架构与硬件安全事故经验，不以第三方协议实现作为写入许可。 | 新插件遵守基类、显式授权、被动优先和“只发有出处的帧”；见 [safety.md](safety.md)。 |

## 维护规则

- 新增协议时，记录最初依据、参考项目链接、许可声明、与本机验证的区别，并链接到 [protocols.md](protocols.md) 的详细规格。
- 若只参考公开协议分析，明确写“参考协议说明，自行实现”；只有确认复用了代码时才写代码来源，并记录许可与归属。
- 发现来源许可、链接或型号信息变化时更新本文；不确定的许可标为“需核对”，不要推断。
