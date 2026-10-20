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
| MCHOSE G87 旧代 dongle（report `0x13`） | 官方 M HUB 的会话与电量交互；通过官方软件抓包和 dongle 实机观测确认帧结构、校验、查询和推送。 | 按抓包与实机样本实现；本机 G87 已验证。 | 官方软件交互是协议依据，不表示引用其代码。具体帧与未定语义见 [protocols.md](protocols.md)。 |
| MCHOSE QHW（新代 dongle） | [MCHOSE M HUB WEB](https://www.mchose.com.cn/#/home) 前端 JS 中的 `QhwCmdQueueItem`、命令表及 CZ 相关脚本。 | 按官方前端协议实现备用路径；适用于 `0x3837` 世代 dongle。当前用户设备 `41E4:2001` 不适用。 | 官方资料，非开源代码复用。入口与设备范围见 [protocols.md](protocols.md)。 |
| Logitech HID++ 电量 | Logitech HID++ 1.0 / 2.0 的设备发现与电池特征；实时电量通知行为与 Logi Battery Dashboard 同源。 | 本项目自行实现 HID++ 报文与特征解析；Bolt + MX Master 3S 已真机验证。 | 现有协议手册未记录外部代码复用；Logi Battery Dashboard 只作为行为 / 协议线索。实现细节见 [protocols.md](protocols.md)。 |
| Rapoo V2 `status-report7` | [pedro3z0/rapoo-software-linux](https://github.com/pedro3z0/rapoo-software-linux)（基于 A HUB 1.0.19 逆向及官方 Web 驱动抓包）；[D3m0nZOnFire/mousectl](https://github.com/D3m0nZOnFire/mousectl)（VT3 PRO 一代实测）。 | 参考协议分析后自行实现被动心跳解析；本机 VT3 二代 `24AE:1412` 已验证，部分状态以本机样本为准。 | `rapoo-software-linux` 标注 MIT；`mousectl` 的许可需以其仓库当前声明为准。两来源设备型号与本机不同，不能直接替代本机验证。 |
| Betop / 北通厂商接口 | [oitsukiii/betop-battery](https://github.com/oitsukiii/betop-battery) 的 `docs/protocol.md`：对北通官方 Electron 客户端做互操作性分析，不含厂商代码。 | 参考协议文档实现；本机 T9 被动流已验证。 | 项目标注 MIT；本项目验证情况与限制见 [protocols.md](protocols.md)。 |
| Inphic ZHEN `report 0x03` | 英菲克官方 ZHEN 网页驱动的 WebHID 行为、驱动显示与本机被动输入样本。 | 只读解析输入 report `0x03`，充放电状态经实机接线验证；不发送网页驱动的 report `0x04`。 | 基于官方行为和本机样本自行实现；未记录外部代码复用。见 [protocols.md](protocols.md)。 |
| HECATE G5M Pro `report 0x04` | 贡献者 dotisland 对官方 HECATE Connect 与接收器 `35BB:D00B` 的被动抓包（PR #13）。 | 只读解析 report `0x04`，不发送命令。帧与充放电对照是贡献者声明，本机未复测。 | 未复用官方软件代码。见 [protocols.md](protocols.md) §7。 |
| DELUX M900 Pro `0x000A` 状态帧 | 贡献者 Baron77777 对官方驱动与接收器 `1D57:FA65` 的被动抓包（PR #16）。 | 只读解析 5 字节状态帧。与英菲克同 VID/PID，本机这次没有扫到该接收器，插件默认关。 | 未复用官方软件代码。见 [protocols.md](protocols.md) §8。 |
| AULA F99 Pro `0x13` 会话/推送 | 贡献者 Baron77777 对 `3554:FA09` 实发会话帧并收到推送（PR #16）。 | 复用 `MchoseV1Codec`，只发 `13 05 01`；不发无应答的 `13 4A`，不进 15 秒发现。默认关。 | 未抓 AULA 官方驱动，未复用外部代码。见 [protocols.md](protocols.md) §9。 |
| DualSense 蓝牙 `0x31` | Linux `hid-playstation` 的帧格式与 CRC；贡献者 luze258 的蓝牙样本（PR #15）。 | 只读 78 字节输入报告，不发送命令。电量是约 10% 档位。贡献者声明只实测过一个放电档，本机未复测。 | 未复制 Linux 驱动代码。见 [dualsense.md](dualsense.md)。 |
| GANSS GS3104T-Pro 33 字节查询帧 | 贡献者 luze258 对官方软件通信的 USB 抓包（PR #15），只有固定查询帧与响应样本。 | 只发这一条固定查询帧，响应 `[3]` 为电量，校验和按"除末字节求和取低字节"；会写帧故默认关。判据与风险见 §6。 | 未复用厂商代码，按抓包事实自行实现。本机无该设备，未复测。见 [ganss.md](ganss.md)。 |
| MCHOSE V9 Pro 64 字节查询帧 | 贡献者 luze258 在官方 MCHOSE HUB 显示 100% 时的 USB 抓包（PR #15），单条请求与响应。 | 只发 `55 65 01 …`，响应 `[2]` 为电量；并入迈从插件共用品牌级开关。充电状态未知，恒为 Unknown。 | 未复用厂商代码，按抓包事实自行实现。本机无该设备，未复测。见 [mchose-v9-pro.md](mchose-v9-pro.md)。 |
| 弱水时砂炽天使 2.4G | 官方 RoseLink（Flutter + BLE 自定义协议）及官方升级助手逆向；同时检查 2.4G dongle 的 HID 通道。 | 用于确认 RoseLink 电量协议不含电量命令、正常 2.4G 模式无可用电量通道；本项目不接入该设备电量。 | 逆向结论用于能力边界判断，不包含外部代码复用。见 [protocols.md](protocols.md)。 |
| Classic Bluetooth 耳机电量 | Windows HFP 网关设备节点的 `DEVPKEY_Device_BatteryLevel` 属性；与 Windows 设置显示同源。 | 通过 PnP 设备节点读取；本机 EDIFIER W820NB 已验证。 | Windows 系统属性读取方案；不是 BLE AEP 属性。实现背景见 [runtime-pitfalls.md](../dev/runtime-pitfalls.md)。 |

## 新增设备协议依据

2026-10-19 新增设备的协议依据：

- **CHERRY MX BOARD 8.2 Xaga**：本机 CHERRY Utility Software 的 `CherryDll.dll` 1.1.0.0，SHA-256 `369964336C35D1DA37FBACA132DBB9F6308119DF66A4AF2A0DBA6D37927F2EA5`。静态核对 `0x100D3530` 的 `0x1A` 读取帧构造及调用方（长度 6、偏移 0），与官方运行时被动应答、独立查询结果交叉验证。仅记录互操作协议，自行实现；不分发厂商二进制或代码。
- **MCHOSE A5 V2 Ultra**：[官方 M HUB WEB](https://www.mchose.com.cn/#/home) 的 `assets/purify.es-CjCZz1i7.js`（2026-10-19）。`Oat` / `jat` 定义 `11 06` 查询及设备信息解析；`LG` 对 payload 取反，等待 Input 通知再 GET Feature。设备表将 `3837:100C` 识别为 A5 V2 Ultra。本项目独立实现，未复制厂商代码；真机验证了 21 字节传输及 100% 响应。
- **Haute Adapter 4DS**：[官方配置工具](https://engine.haute42.cn/) 的 `assets/index-CgADd3rA.js`（2026-10-19），`readFirmwareVersion` / `readSignalStrength` / `readDeviceInfo` 及 `Uc` / `Wc` 定义 GET `0xF8` / `0xF9` / `0xFA`、电量字段与 8 字节芯片标识。仅读取，不涉及配置、保存、固件更新或 GIP 命令；本机 Haute42 3.23 + DualSense 已验证。仅参考协议，自行实现。
- **Razer Kraken V4**：本机 Razer Synapse 4（`RazerAppEngine` 4.0.827）的运行日志 `%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Logs\products_1387_mw*.log`（`protocolAudio.getBatteryStatus` / `getDeviceChargingStatus` 的收发帧与命令表）与前端缓存模块 `AudioBillieT2Dongle` / `AudioMxicDevice` / `protocolAudio`（`_createDataSendGeneral`、`_getElectronTransferInResult`）。据此自行实现 `0x21` / `0x2A` 两条只读查询；真机 `1532:056C` 读到 63% 与未充电。仅记录互操作协议，不分发厂商二进制或代码。

精确字段、样本、硬件验证范围见 [protocols.md](protocols.md) §13–§16。网页资源文件名会随官方更新改变，需从各自 HTML 的脚本引用重新定位。

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
