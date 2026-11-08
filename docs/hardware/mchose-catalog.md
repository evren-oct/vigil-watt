# 迈从设备目录与鼠标 0x4D 规格（2026-11-01 逆向）

> 来源：M HUB WEB 运行时 JS（`main-CRYCuU75.js`，2026-11-07 抓取；旧版 `purify.es-*.js`）+ `theme-*.js` + 配置中心
> `configCenter/custom/cardList.json`。**除注明外全部是静态分析，本机未真机验证**——
> 本机只有 G87 V1（`41E4:2001`），没有鼠标、CZ 族键盘和耳机。
> 协议实现细节见 [mchose.md](mchose.md)；本文只放**设备清单、协议族判定、取数口径**。
> **已落地**：本体 63 条进 `Plugins/Mchose/mchose-catalog.json` 的 `devices[]`；**共用 dongle 不进 `devices[]`**
> （会被当成型号），另记在 `transports[]`——只描述"这颗 PID 是哪种通道、承载哪个协议"，不带型号。
> 2026-11-08 起 `transports[]` 还承载能力声明：只有 `3837:1018` 写了 `batteryRead / connectionRead /
> chargingRead = supported`，其余通道一律不写（= `Unverified`）。认领仍走 `Probe` 的「先试 0x4D 再试 V2」。

## 1. 设备全景

官方把设备分四类，靠 `webdriverEnum` 判定（`theme.js`）：

```js
deviceType:  {KEYBOARD:1, MOUSE:2, HEADSET:3, SPEAKER:4, MOUSE_DOCK:5, HEADSET_DOCK:6}
keyboardType:{BY:1, CZ:2}          // 键盘只有两族
mouseType:   {RY:1, FRK:2, NDK:3}
```

`productKey` 统一格式：**`型号名$$$VID十进制$$$PID十进制`**，例如
`MCHOSE K7 Ultra$$$14391$$$4097` = `3837:1001`。

### 1.1 CZ 族键盘（30 款，`cardList.json`）

厂商接口是 **`0xFF00/0x0001`**（V3 款是 `0xFF80/0x01`），不是 BY 族的 `0xFF02/0x0002`。

| 型号 | 厂商接口 VID:PID | 2.4G dongle | usagePage |
|---|---|---|---|
| Ace 60 | 0x41e4:0x2102, 0x19f5:0xff1b | - | 0xff00 |
| Ace 60 Pro | 0x41e4:0x2104, 0x19f5:0xff0f | - | 0xff00 |
| Ace 60 Pro ISO FR | 0x3837:0x3041 | - | 0xff00 |
| Ace 60 Pro Nordic | 0x3837:0x3022 | - | 0xff00 |
| Ace 60X I | 0x41e4:0x2127 | - | 0xff00 |
| Ace 60X II | 0x41e4:0x2113 | - | 0xff00 |
| Ace 68 I | 0x41e4:0x2115 | - | 0xff00 |
| Ace 68 Pro | 0x41e4:0x2117 | - | 0xff00 |
| Ace 68 III | 0x3837:0x302a | - | 0xff00 |
| Ace 68 Air II | 0x41e4:0x2121 | - | 0xff00 |
| Ace 68 Air III | 0x41e4:0x2133 | - | 0xff00 |
| Ace 68 Air 2 | 0x3837:0x300b | - | 0xff00 |
| Ace 68 GT | 0x3837:0x3009 | - | 0xff00 |
| Ace 68 Turbo 16K | 0x3837:0x3027 | - | 0xff00 |
| Ace 68 Turbo 8K | 0x3837:0x3029 | - | 0xff00 |
| Ace 68 V2 III | 0x3837:0x3025 | - | 0xff00 |
| Ace 75 8K | 0x3837:0x303d | - | 0xff00 |
| G75 V2 | 0x3837:0x2022 | 0x3837:0x3033 + 0x2010 | 0xff00 |
| G87 V2 | 0x3837:0x2019 | 0x3837:0x3033 + 0x2010 | 0xff00 |
| G98 V3 | 0x3837:0x2025, 0x2026 | 0x3837:0x1015 + 0x1017 | 0xff80 |
| God 60 | 0x3837:0x301a | - | 0xff00 |
| GT 75 | 0x3837:0x302F | - | 0xff00 |
| Jet 75 I | 0x41e4:0x2119 | - | 0xff00 |
| Jet 75 II | 0x41e4:0x211b | - | 0xff00 |
| K87 V3 | 0x3837:0x2023, 0x2024 | 0x3837:0x1015 + 0x1017 | 0xff80 |
| K87S | 0x3837:0x200f | 0x3837:0x3033 + 0x2010 | 0xff00 |
| K99 V3 | 0x3837:0x201f | 0x3837:0x3033 + 0x2010 | 0xff00 |
| Mix 87 I | 0x41e4:0x2123 | - | 0xff00 |
| Mix 87 III | 0x3837:0x302c | - | 0xff00 |
| Zero75X | 0x41e4:0x211d | - | 0xff00 |

> `0x19F5` 是 Ace 60/60 Pro 的**贴牌 VID**，与 `0x41E4` 是同一台设备的两套 ID，不是误收。

### 1.2 BY 族键盘（18 款，会话密码表 `Pat`）

| 型号键 | 密码（会话 ACK `[5..10]`） |
|---|---|
| G87 | `03 00 00 00 02 1c` |
| K99 | `03 00 00 00 00 77` |
| K99 V2 | `03 00 00 00 01 df` |
| G98 | `03 00 00 00 01 32` |
| X75 | `03 00 00 00 00 d6` |
| G98 Pro | `0a 00 00 00 00 03` |
| X75 V2 | `03 00 00 00 01 c6` |
| G75 Pro | `03 00 00 00 01 15` |
| G98 V2 | `0a 00 00 00 00 18` |
| UT98 | `0a 00 00 00 00 2b` |
| Z75 | `0a 00 00 00 00 01` |
| Z75S | `0a 00 00 00 00 09` |
| KX75 | `03 00 00 00 01 c7` |
| GX87 | `03 00 00 00 01 74` |
| GX87 V2 | `03 00 00 00 01 ed` |
| K87 | `03 00 00 00 01 35` |
| G75 V2 | `03 00 00 00 03 35` |
| G87 V2 | `0a 00 00 00 00 34` |

与 `MchoseMouseCodec.ModelFromPassword` 现有 18 条**逐条一致**，无新增。

**BY 族判定**（官方按产品名硬编码，不是按接口）：

```js
["MCHOSE G87","MCHOSE K99 Gaming Keyboard","K99 V2","MCHOSE G98","MCHOSE G98 Pro",
 "MCHOSE G98 V2","MCHOSE UT98","Z75","MCHOSE Z75 S","MCHOSE Z75S","MCHOSE G87 V2 (1K)"].includes(a)
 || a.includes("X75") || a.includes("GX87")
 || a.includes("K87") && !a.includes("K87S")
 || a.includes("G75_Pro")
 || a.includes("G75 V2") && ![12339,8225,8226].includes(p)
```

### 1.3 鼠标（23 款，规格表 `Vb`）

键名用 `mchose_secret_2024` 做 base64+XOR 混淆，可还原。

| 型号 | PID（本体 / 1K dongle / 8K dongle） | DPI 上限 | LOD | 回报率档位 |
|---|---|---|---|---|
| A5 V3 Pro | 0x4035 / 0x1014 / 0x1018 | 26000 | 1 | 8000 |
| A5 V3 Ultra | 0x4042 / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| A5 V3 Ultra+ | 0x4026 / 0x1014 / 0x1018 | 42000 | 0.7 | 8000 |
| A5 V3 Ultra+(3955) | 0x4034 / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| K7 V2 Pro | 0x403B / 0x1014 / 0x1018 | 26000 | 1 | 8000 |
| K7 V2 Pro+ | 0x4027 / 0x1014 / 0x1018 | 42000 | 0.7 | 8000 |
| K7 V2 Ultra | 0x403C / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| K7 V2 Ultra+ | 0x4028 / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| A7 V3 | 0x4030 / 0x1014 / 0x1018 | 26000 | 1 | 8000 |
| A7 V3 Pro | 0x4031 / 0x1014 / 0x1018 | 42000 | 0.7 | 8000 |
| A7 V3 Pro+ | 0x4032 / 0x1014 / 0x1018 | 42000 | 0.7 | 8000 |
| A7 V3 Ultra | 0x403D / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| A7 V3 Ultra+ | 0x4033 / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| A7 V3-X | 0x403A / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| A7 V3 Magnetic | 0x403F / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| K5 V2 | 0x4040 / 0x101C | 42000 | 0.7 | 8000 |
| K5 Pro | 0x4037 / 0x1014 / 0x1018 | 26000 | 1 | 8000 |
| K5 Ultra | 0x4038 / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| R7 Ultra | 0x4036 / 0x1014 / 0x1018 | 50000 | 5 | 8000 |
| V7 | 0x402A / 0x1016 | 26000 | 1 | 1000 |
| V7 Pro | 0x403E / 0x101D | 42000 | 0.7 | 1000 |
| G3 V3 | 0x4029 / 0x101D | 18000 | 1 | 1000 |
| G3 S | 0x4041 / 0x101D | 18000 | 1 | 1000 |

> **`0x1014` / `0x1016` / `0x1018` / `0x101C` / `0x101D` 是通用 dongle PID，多型号共用**；
> `0x4762` 是 4K dongle（不是 Realtek 设备）。所以**不能按 PID 反推型号**，只能按本体 PID 或 `0x0900` 回执。

### 1.4 耳机 / 音箱（26 条 productKey）

| 型号 | VID:PID |
|---|---|
| S9 PRO | 0x291D:0x385D, 0x41E4:0x3402 |
| G9 PRO | 0x291D:0x385D, 0x41E4:0x3502 |
| V9 | 0x291D:0x385D, 0x41E4:0x3201 |
| V9 PRO | 0x291D:0x385D, 0x41E4:0x3202 |
| X9 | 0x3837:0x6045 |
| K9 | 0x3837:0x6047 |
| G9 V2 | 0x3837:0x6089 |
| G9 WIRED HEADSET | 0x3837:0x8080 |
| G20 / G20 PRO | 0x0E6A:0x0111 |
| K20 | 0x3837:0x60C4 |
| K20 PRO | 0x3837:0x6003 |
| K9 ULTRA | 0x3837:0x6002 |
| K9 ULTRA HEADSET | 0x3837:0x6001 |
| V9 TURBO+ | 0x3837:0x600A |
| V9 TURBO HEADSET | 0x3837:0x6009 |
| V9 TURBO | 0x3837:0x600D |
| V9 PRO 2 / G9 V2-II | 0x3837:0x6011 |
| K20 GT | 0x3837:0x60C6 |
| R9 | 0x3837:0x6013 |

**协议未拿到**：耳机的实现在客户端 `libs/<型号>/`（USB Audio 驱动 + APO，如 X9 是
`MCHOSE_F9.sys` + `CmApOIce.dll`），不走 HID 命令表。本应用目前只认 `291D:0x385D`（V9 Pro）
和 `3837:0x6045`（X9）。

## 2. 0x4D 命令表（鼠标，`dgt`）

帧构造与校验同 [mchose.md](mchose.md) §2.1。命令 → 解析器映射：

| 命令 | 用途 | 关键字段 |
|---|---|---|
| `0x0001` | 按键配置 | 6 键的 `Type`/`Val` |
| `0x0002` | 基础配置 | `usbRateIndex`、`gRateIndex`、`sleep`、`sensor`、左右键去抖 |
| **`0x0003`** | **DPI** | `dpiSum`、`usbDpiIndex`、`dpiHasY`、`dpi0..dpi5` |
| `0x0009` | LOD | `lodIndex` |
| `0x000a` | 回报模式 | `activeReportMode`、`adaptivePowerRateIndex`、`sensorMode` |
| `0x0900` | 设备信息 | vid/pid/connectStatus/chargeStatus/batteryLevel/gameMode |
| `0x0901` | 固件版本 | 拼接字符串 |

### 2.1 DPI 解析（`0x0003`）

**入参是十六进制字符串数组**（`Array.from(bytes, b => b.toString(16).padStart(2,"0"))`），
不是原始字节：

```js
profileIndex = n[0]
dpiDirection = n[1]        // 0 = X 轴，非 0 = Y 轴
dpiSum       = n[2]        // 档位总数
usbDpiIndex  = n[3]        // 当前档位
dpiHasY      = n[4]
dpi{a}       = parseInt(n[5+a*2+1] + n[5+a*2], 16)   // 小端 16 位，a = 0..5
```

### 2.2 回报率（`0x0002`）

```js
usbRateIndex = n[1][0]     // 有线/2.4G 回报率索引
gRateIndex   = n[2][0]     // 2.4G 回报率索引
```

**拿到的是索引，必须再查表**（见 §3.1），否则会把索引当 Hz 用。

## 3. 三张映射表

```js
Ca = { 1000: ["125","500","1000"],
       8000: ["125","500","1000","2000","4000","8000"] }     // 回报率
ar = { 12000:["200","1200","2200","3200","4200","12000"],
       18000:[...], 26000:[...], 42000:[...], 50000:[...] }   // DPI 六档预设
hr = { 0.7:[0.7/1/2mm], 1:[1/2mm], 5:[0.7/0.9/1.2/1.4/1.7mm] }// LOD 选项
```

取数路径：`Vb[型号].rate` → 得到该通道的档位表 → `[usbRateIndex]` 得到 Hz。
`Vb[型号].dpiMax` → `ar[dpiMax]` 得到六档预设。

## 4. 设备名称与图标

`cardList.json` 每个设备带 `colorList[]`，每项：

```json
{ "className": "color-white", "theme": "light",
  "image": "https://cdn.mchose.com.cn/configCenter/static/images/Ace60White_06e6d57a9a2f.png",
  "deviceImage": ".../Ace60WhiteFull_f196a7f973f8.png" }
```

`image` 是缩略图、`deviceImage` 是整机图，`className` 是配色、`theme` 是明暗主题。
**直接下载即可**，不需要逆向。BY 族键盘的配色表在 `purify.es.js` 里按型号硬编码（`G87`/`K99`/
`G98`… 各一组 `pic`/`deviceImage`）。

## 5. 未验证边界（别当成已知）

1. **全部静态分析**。本机只有 G87 V1，鼠标/CZ 键盘/耳机一台都没有。
2. **DPI 的 `n[5+a*2]` 偏移未真机校准**——`mchose.md` §1.3 的教训是官方解析器与真机常差 1 字节
   （电量 `[5]` 就是例子），照抄前必须插鼠标抓一次。
3. **`0xFF00/0x0001` 接口是否认 `0x4D` 帧未验证**。CZ 族厂商接口形态与 0x4D 通道（`0xFF01`）不同，
   0x4D 帧能否被接受未知。
4. **耳机协议未拿到**，走音频驱动，不是 HID。
5. **`0x0900` 的 `chargeStatus` 是整字节**（0/1/2），与键盘 V1 的「高 4 位 fullStatus + 低 4 位
   chargeStatus」不是一套，别混用。
