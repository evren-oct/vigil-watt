# 设备支持矩阵

> **唯一事实源**：某台设备是否已适配、验证到什么程度、默认开关、对应文档，只看本表。
> `README.md`、`handoff.md`、`protocols.md` 不再维护第二份状态表，只链接到这里。
> 最后核对：2026-11-23。改状态时同时改这一行。

## 状态口径

- **本机已验证**：维护者本机有该设备，读数与官方软件或系统设置对照过。
- **贡献者真机**：社区贡献者真机验证，维护者未复测。
- **仅实现待验**：代码已接入，但没有对应真机的电量读数。
- **未实现**：只有协议资料或采集包，没有插件。
- **不支持**：已确认主机侧无电量通道，不再投入。

> 2026-11-23 统一口径：旧 `README.md` 曾把部分贡献者设备写成「本机验证」，本表按 `handoff.md` 的保守口径改为「贡献者真机」；若维护者后来已复测，直接改这一列并注明日期。

默认开关来自 `src/VigilWatt.Core/Settings/AppSettings.cs` 的 `PluginEnabled` 默认表；会写帧的插件默认关，纯被动插件默认开。

## 1. 2.4G / 厂商 HID 设备

| 品牌 | 型号 / 设备 | VID:PID | 通道 / 协议 | 状态 | 默认 | 文档 |
|---|---|---|---|---|---|---|
| 迈从 MCHOSE | G87 键盘 | `41E4:2001` | 0x13 会话 | 本机已验证 | 关 | [mchose.md](brands/mchose.md) |
| 迈从 MCHOSE | 通用 2.4G dongle（G87 V2 / G75 V2 / K87S / K99 V3） | `3837:3033` | CZ 分块读 `cz-block` | 本机已验证 | 关 | [mchose.md](brands/mchose.md) §2.4 |
| 迈从 MCHOSE | G87 V2 / A7 V2 Pro | `3837:201B` / `100A` | 0x13 会话 / E2 被动帧 | 贡献者真机 | 关 | [mchose.md](brands/mchose.md) §1.6 |
| 迈从 MCHOSE | 0x4D 鼠标通道（K7 V2 Ultra+ 等） | `3837:1018` | `0x0900` 查询 | 贡献者真机 | 关 | [mchose.md](brands/mchose.md) §3 |
| 迈从 MCHOSE | G3 V2 | `3837:4245` | 65 字节 `AA 30` 查询 | 贡献者真机 | 关 | [mchose.md](brands/mchose.md) §4 |
| 迈从 MCHOSE | A5 V2 Ultra | `3837:100C` | 反码 Feature `0x11` | 仅实现待验 | 关 | [mchose.md](brands/mchose.md) §6 |
| 迈从 MCHOSE | G75 Pro | `3554:FA09` | 0x13 会话 + ACK 型号分流 | 贡献者真机 | 关 | [mchose.md](brands/mchose.md) §5 |
| 迈从 MCHOSE | V9 Pro 耳机 | `291D:385D` | 64 字节查询帧 | 贡献者真机 | 关 | [mchose-v9-pro.md](devices/mchose-v9-pro.md) |
| 迈从 MCHOSE | X9 耳机 | `3837:6045` | 64 字节查询帧 | 贡献者真机 | 关 | [mchose-x9.md](devices/mchose-x9.md) |
| 罗技 Logitech | Bolt C548 + MX Master 3S | `046D:C548` | HID++ | 本机已验证 | 关 | [logitech.md](brands/logitech.md) |
| 罗技 Logitech | PRO X 2 LIGHTSPEED | `046D:0AF7` | LIGHTSPEED / Centurion；蓝牙走 HFP | 本机已验证（USB 有线未验） | 关 | [logitech.md](brands/logitech.md) §2 |
| 雷柏 Rapoo | VT3 二代 | `24AE:1412` | `status-report7` 被动心跳 | 本机已验证 | 开 | [rapoo.md](brands/rapoo.md) |
| 雷柏 Rapoo | VT2S Air Max V2 及同族 | 见目录 | `status-report7` 被动心跳 | 本机已验证（其余型号未逐台复测） | 开 | [rapoo-catalog.md](reference/rapoo-catalog.md) |
| 北通 BETOP | T9 | `20BC:5073` | Col04 被动流 | 本机已验证 | 开 | [betop.md](brands/betop.md) §1.4 |
| 北通 BETOP | 鲲鹏20 2.4G | `20BC:5159` | 杂项页广播 | 贡献者真机 | 开 | [betop.md](brands/betop.md) §1.8 |
| 北通 BETOP | 鲲鹏20 有线 | `20BC:5158` | `02 15` 查询 | 贡献者真机 | 开 | [betop.md](brands/betop.md) §1.8 |
| 北通 BETOP | 鲲鹏20（`5191`） | `20BC:5191` | 主动查询 | 仅实现待验 | 开 | [betop.md](brands/betop.md) §1.8 |
| 北通 BETOP | 新批次 `20DD`（5128 / 5130） | `20DD:5128` / `5130` | 只回 `02 CD` | 未实现（电量格式未知） | 开 | [betop-catalog.md](reference/betop-catalog.md) §10 |
| 英菲克 Inphic | ZHEN | `1D57:FA65` | 被动 report `0x03` | 本机已验证 | 关 | [inphic.md](brands/inphic.md) §1 |
| 英菲克 Inphic | IN9 | `248A:5B2F` | Feature `0x0C` + 刷新帧 | 本机已验证 | 关 | [inphic.md](brands/inphic.md) §2 |
| HECATE | G5M Pro | `35BB:D00B` | 被动 `0x04` + 主动查询 | 贡献者真机 | 关 | [hecate.md](brands/hecate.md) §3 |
| HECATE | G3M Pro 升级版 | `35BB:D3FB` | 17 字节 `0xFF02` | 本机已验证（充电位未验） | 关 | [hecate.md](brands/hecate.md) §2 |
| 狼蛛 AULA | F99 / F99 Pro | `3554:FA09` | 0x13 会话推送 | 贡献者真机 | 关 | [aula.md](brands/aula.md) §1 |
| 狼蛛 AULA | G9 耳机 | `38A6:0030` | 被动 `AB 47 4D 86` | 仅实现待验 | 关 | [aula.md](brands/aula.md) §3 |
| 狼蛛 AULA | L9 Ultra | `38A6:28FD` | `0x81` 查询 | 仅实现待验 | 关 | [aula.md](brands/aula.md) §4 |
| 狼蛛 AULA | HERO 68 Air | `372E:106C` | Bytech 8K 分片查询 | 贡献者真机 | 关 | [aula.md](brands/aula.md) §2 |
| 狼蛛 AULA | F108 Pro | `05AC:024F` | 33 字节查询帧 | 贡献者真机 | 关 | [ganss.md](brands/ganss.md) §7 |
| 黑爵 AJAZZ | AK870 | `3151:4011` | Feature `F7` 状态查询 | 贡献者真机 | 关 | [ak870-ml903.md](devices/ak870-ml903.md) |
| 雷神 THUNDEROBOT | ML903 NearLink Se | `369B:0234` | `0xA0` 设备信息 TLV | 贡献者真机 | 关 | [ak870-ml903.md](devices/ak870-ml903.md) |
| DELUX | M900 Pro | `1D57:FA65` | 被动 `0x000A` | 贡献者真机 | 关 | [delux.md](brands/delux.md) |
| 索尼 SONY | DualSense（蓝牙） | `054C:0CE6` | 蓝牙 HID `0x31` | 贡献者真机 | 开 | [dualsense.md](devices/dualsense.md) |
| Haute42 | Adapter 4DS | `054C:0CE6`（USB） | 只读 Feature `0xF8` / `0xFA` / `0xF9` | 仅实现待验 | 关 | [haute42.md](devices/haute42.md) |
| 高斯 GANSS | GS3104T-Pro | `05AC:024F` | 33 字节查询帧 | 贡献者真机 | 关 | [ganss.md](brands/ganss.md) §7 |
| CHERRY | PIXIU75 | `046A:01E4` / `01E2` | 64 字节块 `0x1A` | 贡献者真机 | 关 | [cherry.md](brands/cherry.md) §1.5 |
| 派世 Pulsar | X2 V2 | `3554:F508` | 17 字节 STATUS/POWER | 贡献者真机 | 关 | [pulsar.md](brands/pulsar.md) |
| 寸渊影弑 Darkrim | SHADOW | `3654:6901` | `0x11/0x01` + GetBasicInfo | 贡献者真机 | 关 | [darkrim.md](brands/darkrim.md) §1.3 |
| 盖世小鸡 GameSir | G7 Pro CE | `3537` | 心跳 `0F F2 00` + 0x12 状态流 | 贡献者真机 | 关 | [gamesir.md](brands/gamesir.md) §1.4 |
| 前行者 EWEADN | X87 键盘 | `0C45:FEFE` | 33 字节查询帧 | 贡献者真机 | 关 | [eweadn.md](brands/eweadn.md) §1.1 |
| 前行者 EWEADN | S9 鼠标 | `372E:1026` | Feature `0x03` | 贡献者真机 | 关 | [eweadn.md](brands/eweadn.md) §1.2 |
| 凌豹 Lingbao | M5PRO Max | `3151:402D` | 65 字节 `F7` 查询 | 贡献者真机 | 关 | [lingbao.md](brands/lingbao.md) §1 |
| 凌豹 Lingbao | K98 | `3554:FA09` | 0x13 会话 | 贡献者真机 | 关 | [lingbao.md](brands/lingbao.md) §2 |
| VGN | 蜻蜓 F1 V5 | `391D:1A23` / `1026` | kc2 `0x06` | 贡献者真机 | 关 | [vgn.md](brands/vgn.md) §1 |
| VGN | 蜻蜓 F2 大师+ | `3554:FB56` / `FB57` | y2 BatteryLevel | 贡献者真机 | 关 | [vgn.md](brands/vgn.md) §2 |
| VGN | V98 Pro | `320F:5088` | `0x1A` 查询 | 仅实现待验 | 关 | [vgn.md](brands/vgn.md) §3 |
| AKG | N9 Hybrid | `0ECB:20D9` | RACE 查询 | 贡献者真机（充电态未知） | 关 | [akg.md](devices/akg.md) |
| ATK / VXE | VXE R1 Nearlink | `373B:10C9`（本体 `10C7`） | `FF02` 17 字节 | 本机已验证 | 关 | [atk.md](brands/atk.md) §9 |
| ATK | F1 Pro Max | `373B:101B` / `101A`（本体 `1015`） | `FF02` 17 字节 | 本机已验证 | 关 | [atk.md](brands/atk.md) §2 |
| ATK | F1 Ultimate 2.0 | `373B:11D9` / `11E4` | `FF02` 17 字节 | 贡献者真机 | 关 | [atk.md](brands/atk.md) §4.3 |
| ATK | F1 V3 ULTIMATE+ | `373B:1303`（本体 `12A9`） | `FF02` 17 字节 | 贡献者真机（上游真机，维护者未复测） | 关 | [atk.md](brands/atk.md) §4 |
| ATK | A9 Air 大师版+ | `373B:1278`（本体 `1293`） | `FF02` 17 字节 | 贡献者真机（充电读数虚高约 15pp） | 关 | [atk.md](brands/atk.md) §2.2 |
| ATK | A9 Ultra Max 2.0 | `373B:11FE` | `FF02` 17 字节 | 仅实现待验 | 关 | [atk.md](brands/atk.md) §10 |
| ATK | A98 Pro 键盘 | `373B:1152` | VIA `02 96 24` | 贡献者真机 | 关 | [atk.md](brands/atk.md) §7 |
| ATK | N9 系列耳机 | `373B:1204` | CDC-ACM 串口 | 未实现 | 关 | [atk-n9.md](devices/atk-n9.md) |
| HyperX | Cloud Flight 2 | `03F0:0AC1` | `FF90` 被动推送 | 贡献者真机 | 开 | [hyperx-cf2.md](brands/hyperx-cf2.md) |
| 雷蛇 RAZER | 毒蝰 V4 Pro | `1532:00E6` / `00E5` | 经典 90 字节 Feature | 贡献者真机（充电态未知） | 关 | [razer.md](brands/razer.md) |
| 雷蛇 RAZER | BlackShark V2 Pro | `1532:0555` | 独立 PA 协议 | 贡献者真机 | 关 | [razer-headset.md](devices/razer-headset.md) |
| 雷蛇 RAZER | Viper V3 Pro SE | `1532:00DF` | 零权限 Feature | 贡献者真机 | 关 | [razer-viper-se.md](devices/razer-viper-se.md) |
| 雷蛇 RAZER | Kraken V4 | `1532:056C` | 64 字节 `0x02` | 仅实现待验 | 关 | [razer-kraken.md](devices/razer-kraken.md) |
| 华硕 ASUS ROG | Strix Scope II 96 | `0B05:1ACE` | 接收器分组 + 通道 2 查询 | 贡献者真机 | 关 | [asus-rog.md](brands/asus-rog.md) |
| 索尼 SONY | INZONE H9 II | `054C:0FA8` | `0xFF04` SET_REPORT | 贡献者真机 | 关 | [inzone-h9.md](devices/inzone-h9.md) |
| 墨将 MOJHON | 烈风2 GALE2 | `E2B7:122C` | 厂商页 `A5 04 04` | 贡献者真机 | 关 | [mojhon.md](brands/mojhon.md) §7.4 |
| 飞智 FLYDIGI | APEX5 | `37D7:2501` | NewXInput 心跳 | 贡献者真机 | 关 | [flydigi.md](brands/flydigi.md) |
| 狼途 LANGTU | T98 | `1A2C:8FFF` | 被动状态帧 | 贡献者真机 | 开 | [langtu.md](devices/langtu.md) |
| ilovbee | b98 | `258A:01BB` | 被动 20 字节状态帧 | 仅实现待验（贡献者采集包） | 开 | [ilovbee.md](devices/ilovbee.md) |

## 2. 蓝牙 / 系统设备

| 来源 | 设备 | 标识 | 通道 | 状态 | 默认 | 文档 |
|---|---|---|---|---|---|---|
| EDIFIER | W820NB | HFP 网关 | `DEVPKEY_Device_BatteryLevel` | 本机已验证 | 开 | [code-flow.md](../project/code-flow.md) §3 |
| 经典蓝牙耳机 | 通用 HFP 网关 | — | `DEVPKEY_Device_BatteryLevel` | 本机已验证（EDIFIER 样本） | 开 | [code-flow.md](../project/code-flow.md) §3 |
| 本机电池 | `sys:battery` | — | `GetSystemPowerStatus` | 本机已验证 | 关 | [code-flow.md](../project/code-flow.md) §3.1 |

## 3. 不支持 / 无电量通道

| 品牌 | 设备 | 标识 | 原因 | 状态 | 默认 | 文档 |
|---|---|---|---|---|---|---|
| 弱水时砂 ROSE | FIRST ANGEL | `38FF:0001` | 2.4G 主机侧无电量通道 | 不支持 | — | [rose.md](brands/rose.md) |
| 北通 BETOP | 蓝牙模式 | — | 固件无电量通道 | 不支持 | — | [betop.md](brands/betop.md) §1.8.3 |
| 华硕 ASUS | AURA | — | 与电量无关 | 不支持 | — | — |
| 系统虚拟设备 | Virtual Multitouch | — | 与电量无关 | 不支持 | — | — |
| lifeme | 音箱 | — | 与电量无关 | 不支持 | — | — |

## 维护规则

- 新设备先改 `specs/*.protocols.json` 或目录 JSON，再在本表加一行；状态没有真机证据就写「仅实现待验」。
- 状态升级必须能指出证据（读数、对照截图、抓包记录），不接受「代码已接入」当成「已验证」。
- 本表只回答「这台设备现在怎样」；帧格式、偏移、时序仍在各品牌文档，不在这里复制。
