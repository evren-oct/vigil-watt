# 2.4G 协议手册（索引）

> 所有结论都是真机验证或官方软件抓包所得，**改协议代码前先读对应品牌文档，不要重新猜**。
> 2026-10-21 按品牌拆分：本文只留**索引 + 通用陷阱 + 抓包资料**，协议细节在各品牌文档里。
> 零写入原则与写入闸门见 [safety.md](safety.md)；来源与许可边界见 [sources.md](sources.md)。

## 1. 品牌索引

| 品牌 | 型号 | VID:PID | 文档 | 状态 |
|---|---|---|---|---|
| 迈从 MCHOSE | G87 键盘 | `41E4:2001` | [mchose.md](mchose.md) | 本机验证 |
| 迈从 MCHOSE | QHW 新一代 dongle | `3837:3033` | [mchose.md](mchose.md) | 已实现，备用 |
| 迈从 MCHOSE | K7 V2 Ultra+ 鼠标 | `3837:1018` | [mchose.md](mchose.md) | 贡献者真机 |
| 迈从 MCHOSE | V9 Pro 耳机 | `291D:385D` | [mchose-v9-pro.md](mchose-v9-pro.md) | 贡献者真机 |
| 迈从 MCHOSE | G75 Pro 键盘（与 AULA 共用 `3554:FA09`） | `3554:FA09` | [mchose.md](mchose.md) §4 | 本机实测 |
| 凌豹 LINGBAO | M5PRO Max 鼠标 | `3151:402D` | [lingbao.md](lingbao.md) | 本机实测，电量字段待官方对照 |
| 罗技 Logitech | Bolt C548 + MX Master 3S | `046D` | [logitech.md](logitech.md) | 本机验证 |
| 雷柏 Rapoo | VT3 二代 | `24AE:1412` | [rapoo.md](rapoo.md) | 本机验证 |
| 北通 BETOP | T9 / 鲲鹏20 | `20BC:5073` / `5191` | [betop.md](betop.md) | T9 本机验证；鲲鹏20 未验 |
| 英菲克 ZHEN | 接收器 | `1D57:FA65` | [inphic.md](inphic.md) | 本机验证 |
| HECATE | G5M Pro | `35BB:D00B` | [hecate.md](hecate.md) | 贡献者真机 |
| DELUX | M900 Pro | `1D57:FA65` | [delux.md](delux.md) | 贡献者真机 |
| AULA | F99 Pro | `3554:FA09` | [aula.md](aula.md) | 贡献者真机 |
| 索尼 | DualSense 蓝牙 | `054C:0CE6` | [dualsense.md](dualsense.md) | 贡献者真机 |
| GANSS | GS3104T-Pro | `05AC:024F` | [ganss.md](ganss.md) | 贡献者真机 |
| CHERRY | PIXIU75（2.4G / 有线） | `046A:01E4` / `01E2` | [cherry.md](cherry.md) | 贡献者真机 |
| 派世 PULSAR | X2 V2 | `3554:F508` | [pulsar.md](pulsar.md) | 贡献者真机 |
| 寸渊影弑 DARKRIM | SHADOW | `3654:6901` | [darkrim.md](darkrim.md) | 贡献者真机 |
| 盖世小鸡 GameSir | G7 Pro CE | `3537` | [gamesir.md](gamesir.md) | 贡献者真机 |
| 前行者 EWEADN | X87 键盘 / S9 鼠标 | `0C45:FEFE` / `372E:1026` | [eweadn.md](eweadn.md) | 贡献者真机 |
| 黑爵 AJAZZ | AK870 | `3151:4011` | [ak870-ml903.md](ak870-ml903.md) | 贡献者真机 |
| 雷神 THUNDEROBOT | ML903 NearLink Se | `369B:0234` | [ak870-ml903.md](ak870-ml903.md) | 贡献者真机 |
| 弱水时砂 ROSE | FIRST ANGEL | `38FF:0001` | [rose.md](rose.md) | ❌ 2.4G 无电量通道，不再投入 |

## 2. 共用号段的陷阱（认领前必读）

- **`3554` 是 Compx 共用号段**：AULA F99 Pro（`FA09`）、派世 X2 V2（`F508`）、VGN 蜻蜓 F2（`FB56`/`FB57`）都在这里。
  按 VID 猜协议族一定错，判据必须带 usage + 报文长度；`3554:FA09` 上还叠了 AULA F99 Pro / 凌豹 K98 / 迈从 G75，
  三者指纹完全一致，只能靠会话 ACK 里的型号码分流（[aula.md](aula.md) §1.1、[mchose.md](mchose.md) §4）。
- **`05AC:024F` 是借用苹果号段**：GANSS GS3104T-Pro 与狼蛛 F108 Pro 的五条接口判据逐条一致，HID 层分不出品牌，
  唯一可用证据是产品名。
- **`1D57:FA65` 被两个品牌共用**：英菲克 ZHEN 读被动 `0x03` 帧，DELUX M900 Pro 读 `0x000A`，靠长度与块结构区分。
- **会写帧的插件默认关闭**，开关本身就是写入授权；别在认领判据里放宽到「同 VID 就试」。


## 2. 官方资料地图（抓包/查协议用）

- M HUB WEB（网页驱动）：https://www.mchose.com.cn/#/home → `#/connectDevice`，支持列表明确含 **G87 Series**
- CZ 键盘驱动 SPA：`https://www.mchose.com.cn/cizhou/CZ_SHARED_DATA/asset-manifest.json`（内含 layout-g87_v2.js）
- 配置中心版本清单：`https://cdn.mchose.com.cn/configCenter/config/global.json`（base64 zip，内含 keyboardConfig 哈希）
- 桌面版 MCHOSE HUB：`https://cdn.mchose.com.cn/MCHOSE_HUB_installer.zip`（Electron，未验证是否支持 41E4 老款）
- 官方设备表（打包在主 JS 里）：G87 V2 有线 = 0x3837:0x2015，官方通用 dongle = 0x3837:0x3033；0x41E4 世代 = Ace60/Ace68/Jet75/Mix87/Zero75 等有线型号 + 老款 dongle（0x41E4:0x2001 不在官方表里，但协议同代）
