# MCHOSE 0x4D：DPI / 回报率只读协议证据审计（M2-4A）

> 目的：为 M2-4B 的只读遥测实现提供可追溯的协议依据。**本文只做证据审计，不含任何实现改动。**
> 结论全部来自官方前端运行时 JS 的实际代码，逐条标注证据等级；等级定义见 §7。
> 审计时间：2026-11-08。基线 `dev` @ `596b7cf`（含 M2-1 / M2-2 / M2-3）。

## 1. 审计范围与基线

| 项 | 值 |
|---|---|
| 分支 / HEAD | `dev` @ `596b7cf`，工作区干净 |
| M2-1（`0x0900` 自报身份） | 已提交，`MchoseDeviceIdentity` |
| M2-2（`qhw`+`mouse-k7` 合并） | 已提交，`Mchose0x4DCodec` |
| M2-3（连接方式 / 充电 / 能力声明） | 已提交，`ToConnectionKind` / `ToChargingState` |
| 官方代码 | `%TEMP%\mch\main-CRYCuU75.js`，7,670,191 B，mtime 2026-11-07 21:57，md5 `33bbbae270f5f39cb3dd82b490d9d893` |
| 交叉核对 | `%TEMP%\tmp.iwA8gnMYHG\mchose-purify.js`（2026-11-01，7,765,857 B，旧版打包，仅用于结构比对） |

**文件定位方式**：该 JS 是压缩单行文件，本文所有位置都是**字节偏移**，可用
`dd if=main-CRYCuU75.js bs=1 skip=<offset> count=600` 复现。

审计对象三个命令：`0x0003`（DPI 读）、`0x0002`（性能/回报率读）、`0x0600`（设备主动上报）。

## 2. `0x0003` DPI 读取

### 2.1 请求

`readDpiInfo` @1883644：

```js
mcReadWithRetry(() => mcReadCommand(dev, {commandId:"0x0003", data:[ea.profileIndex, 0]}))
// dpiHasY==1 时再发一次：
mcReadWithRetry(() => mcReadCommand(dev, {commandId:"0x0003", data:[ea.profileIndex, 1]}))
```

- **有参数**，payload 固定 2 字节：`[0] = profileIndex`、`[1] = dpiDirection`（`0` = X 轴，`1` = Y 轴）。
- 帧构造走 `getCommandBuffer` @1868959：`4D 01 flags len cmdLo cmdHi biz seq payload… chk`，
  `flags=1` 时 `chk = XOR(frame[2 .. 7+len])`，总长 64。**与 VigilWatt 现有 `BuildFrame` 逐字节一致**（A）。
- 响应识别走 `waitCommand` @1871857：按 `"0x"+data[4]+data[3]` 匹配命令回显，取 `data.slice(7)` 为 payload；
  **不校验校验和**，超时 3 s（A）。这与 `Mchose0x4DCodec.TryLocate` 的"不校验 chk"一致。

### 2.2 响应

`dpiToObj` @1871165（`readMap["0x0003"]` @1871587）：

| payload | 含义 | 备注 |
|---|---|---|
| `[0]` | `profileIndex` | 回显请求里的 profile |
| `[1]` | `dpiDirection` | 回显 0/1 |
| `[2]` | `dpiSum` | 档位数量（见 §5.3，B） |
| `[3]` | `usbDpiIndex` | **当前活动档位索引（0 基）** |
| `[4]` | `dpiHasY` | 1 = X/Y 可分别设置 |
| `[5..16]` | `dpi0..dpi5`（方向 0）或 `dpiY0..dpiY5`（方向 1） | 每档 **2 字节、小端**，共 6 档 |

```js
for (let sa=0; sa<6; sa++)
  ia[`dpi${na===0?"":"Y"}${sa}`] = parseInt(ea[5+sa*2+1] + ea[5+sa*2], 16);
```

- **单值还是多 profile**：一次只读**一个 profile 的 6 个档位**；profile 由请求参数指定（A）。
- **X/Y 两轴**：同一个命令读两次（direction 0/1），不是一帧带两轴（A）。
- **单位 / 字节序 / 范围**：原始 DPI 整数，小端 16 位；上限由型号决定（§6 的 `dpiMax`：12000～50000）（A）。
- **型号差异**：`dpiMax` 与档位预设按型号不同（§6），但**帧结构不变**（A）。
- **`profileIndex` 是否 = 当前活动 profile**：**不是**。官方调用链里它来自
  `readPerformanceInfo`（`0x0002` 的 `payload[0]`），是"要读哪个配置档"，不是"当前生效档"（A）。

## 3. `0x0600` DPI 档位事件

### 3.1 用途与方向

`mcMonitor` @1880715 是 **inputreport 监听器**，`0x0600` **只由设备主动上报，没有读请求**
（`readMap` 里没有 `0x0600` 条目，也没有任何 `mcReadCommand` 用这个命令）（A）。

### 3.2 结构

`payload[0]`、`payload[1]` 是"本次带了哪些字段"的位图（逐位判断，A）：

| 位 | 判定 | 值位置 | 官方处理 |
|---|---|---|---|
| `payload[0]` bit0 | `oa[7]==="1"` | `payload[2]` | `setNewTypeValue("dpiIndex", …)` + `handleNewMouseDpiChange` → **活动档位变化** |
| `payload[0]` bit1 | `oa[6]==="1"` | `payload[3]` | 电量 |
| `payload[0]` bit3 | `oa[4]==="1"` | `payload[6]` | "上报配置索引" → `updateDeviceBoardIndex` |
| `payload[1]` bit1 | `ia[6]==="1"` | `payload[12]` | 充电状态 |

（`oa = payload[0].toString(2).padStart(8,"0")`，故 `oa[0]` 是 bit7、`oa[7]` 是 bit0。）

### 3.3 回答

- **返回什么**：只有**活动档位索引**（0 基），不是档位数量、不是档位列表、不含 DPI 数值（A）。
- **与 `0x0003` 同一个 `profileIndex` 吗**：`0x0600` 不带 profileIndex 参数，`payload[6]` 被官方称为
  "配置索引"并用于 `updateDeviceBoardIndex`，**是否等于 `0x0003` 的 profileIndex 未在代码中显式关联**（B）。
- **能否与 `0x0003` 关联**：能，且官方就是这么做的——`handleNewMouseDpiChange` @1909941 收到索引后
  **重新读 `0x0003`**，再用 `dpiX[usbDpiIndex]` 取当前 DPI（A）：

```js
na = await readDpiInfo({profileIndex: ea}, ta);
const {usbDpiIndex: ra} = na;
oa = na["dpi"+ra]; ia = na["dpiY"+ra];
// 提示文案：档位 = ra+1，DPI 值 = oa
```

- **型号差异**：`0x0600` 位图结构在代码中无型号分支（A）；但"哪些型号会上报 bit0"没有证据（Unknown）。

## 4. `0x0002` 回报率读取

### 4.1 请求

`readPerformanceInfo` @1883073：`mcReadCommand(dev, {commandId:"0x0002"})` —— **无参数**（A）。

### 4.2 响应

`allInfoToObj` @1870429（`readMap["0x0002"]`）：

| payload | 解析 | 含义 |
|---|---|---|
| `[0]` | 整字节 | `profileIndex` |
| `[1]` 高半字节 | `>1 ? v-1 : v` | `usbRateIndex`（有线/本体回报率档位索引） |
| `[1]` 低半字节 | 原值 | `usbDpiIndex`（当前活动 DPI 档位） |
| `[2]` 高半字节 | `>1 ? v-1 : v` | `gRateIndex`（2.4G 回报率档位索引） |
| `[3]` | 整字节 | `sleep` |
| `[4]` | `& 1` | `sleepMode` |
| `[5]` | 整字节 | `sensor` |
| `[6]` | 整字节 | `rotateVal` |
| `[7]` | 整字节 | `leftKeyDebounce` |
| `[8]` | 整字节 | `rightKeyDebounce` |

- **返回 index 还是 Hz**：**只返回 index**，协议里没有任何 Hz 数值（A）。
- **index → Hz 靠表**：`rateMap` @1901116，两张（A）：

  | 表 | 档位 |
  |---|---|
  | `rateMap[1e3]` | 125 / 500 / 1000（3 档） |
  | `rateMap[8e3]` | 125 / 500 / 1000 / 2000 / 4000 / 8000（6 档） |

- **按型号 / 按 transport PID 区分**：**两者都区分**（A）。`mouseDiffConfig` @1903461 每条型号配置是
  `rate:[三槽], pids:[本体PID, donglePID, donglePID]`，选择规则在 `getReportRateList` @1863xxx：

```js
if (newMouseWirelessPids.includes(device.productId)) {
  const oa = mouseDiffConfig[name].pids.findIndex(ia => ia == device.productId);
  return oa >= 0 && rate[oa] != null ? rate[oa] : rate[0];
}
return rate[0];   // 本体直连
```

  `newMouseWirelessPids` @1900485 = `[4116, 4120, 4124, 4118, 4125]`
  = `0x1014 / 0x1018 / 0x101C / 0x1016 / 0x101D`，正是目录里登记的五颗 dongle（A）。
- **`>1 ? v-1 : v` 的偏移**：写侧 `allInfoToArr` 用 `index>0 ? index+1 : index`，读侧反向减 1。
  即线上编码 `1` 与 `0` 都表示索引 0（A，两侧代码互证）。
- **是否代表"当前设备配置"**：是"设备保存的设置值"，不是实测吞吐（A）。官方无任何实测回报率路径。

## 5. DPI profile / stage / active DPI 的关系

### 5.1 三层区分（本次审计的核心结论）

| 概念 | 来源 | 说明 |
|---|---|---|
| 配置里的 DPI 数值 | `0x0003` `dpi0..5`（按 profile） | 每 profile 6 个档位，可 X/Y 分开 |
| 当前活动档位（stage） | `0x0003` `payload[3]`；`0x0002` `payload[1]` 低半字节；`0x0600` bit0 | 0 基索引，三处同源 |
| 当前实际生效 DPI | `dpiX[usbDpiIndex]`（官方 `handleNewMouseDpiChange`） | **不是 `dpi0`** |

**不能把 `dpi0` 当当前 DPI**：官方明确用活动索引去取数组元素（A）。

### 5.2 profile

`readPerformanceInfo` 里 `[0,1,2].includes(profileIndex) || (profileIndex = 0)` → 官方按 **3 个 profile** 处理（A）。
`0x0900` 的 `maxConfigNum` 在真机样本里是 `4`，两者口径不同（B，未确认 `maxConfigNum` 是否就是 profile 数）。

### 5.3 `dpiSum`

UI 侧有两处直接赋值：`dpiSum: 6`（构造 6 档数组时）与 `dpiSum: sa.value`（档位计数 ref）@4538950。
**推断为"档位数量"**，但官方没有对该字段做显式说明，故记 **B**，M2-4B 若要展示"共 N 档"需真机核对。

## 6. 型号与 transport PID 的 rate table

`mouseDiffConfig` @1903461 共 **23 条**，与目录里 0x4D 鼠标的本体条目 **23/23 一一对应**（A）。逐条如下
（PID 十进制 → 十六进制；`rate` 三槽对应 `pids` 三槽）：

| 型号 | pids（本体 / dongle1 / dongle2） | rate 三槽 | dpiMax |
|---|---|---|---|
| K7 V2 Pro | 16443 (0x403B) / 4116 (0x1014) / 4120 (0x1018) | 8e3 ×3 | 26000 |
| K7 V2 Pro+ | 16423 (0x4027) / 4116 / 4120 | 8e3 ×3 | 42000 |
| K7 V2 Ultra | 16444 (0x403C) / 4116 / 4120 | 8e3 ×3 | 50000 |
| K7 V2 Ultra+ | 16424 (0x4028) / 4116 / 4120 | 8e3 ×3 | 50000 |
| A7 V3 | 16432 (0x4030) / 4116 / 4120 | 8e3 ×3 | 26000 |
| A7 V3 Pro | 16433 (0x4031) / 4116 / 4120 | 8e3 ×3 | 42000 |
| A7 V3 Pro+ | 16434 (0x4032) / 4116 / 4120 | 8e3 ×3 | 42000 |
| A7 V3 Ultra | 16445 (0x403D) / 4116 / 4120 | 8e3 ×3 | 50000 |
| A7 V3 Ultra+ | 16435 (0x4033) / 4116 / 4120 | 8e3 ×3 | 50000 |
| A7 V3-X | 16442 (0x403A) / 4116 / 4120 | 8e3 ×3 | 50000 |
| A7 V3 Magnetic | 16447 (0x403F) / 4116 / 4120 | 8e3 ×3 | 50000 |
| K5 Pro | 16439 (0x4037) / 4116 / 4120 | 8e3 ×3 | 26000 |
| K5 Ultra | 16440 (0x4038) / 4116 / 4120 | 8e3 ×3 | 50000 |
| R7 Ultra | 16438 (0x4036) / 4116 / 4120 | 8e3 ×3 | 50000 |
| K5 V2 | 16448 (0x4040) / 4124 (0x101C) | 8e3 ×2 | 42000 |
| **V7** | 16426 (0x402A) / 4118 (0x1016) | **1e3 ×2** | 26000 |
| **V7 Pro** | 16446 (0x403E) / 4125 (0x101D) | **1e3 ×2** | 42000 |
| **G3 V3** | 16425 (0x4029) / 4125 (0x101D) | **1e3 ×2** | 12000 |
| **G3 S** | 16449 (0x4041) / 4125 (0x101D) | **1e3 ×2** | 18000 |
| A5 V3 Pro | 16437 (0x4035) / 4116 / 4120 | 8e3 ×3 | 26000 |
| A5 V3 Ultra | 16450 (0x4042) / 4116 / 4120 | 8e3 ×3 | 50000 |
| A5 V3 Ultra+ | 16422 (0x4026) / 4116 / 4120 | 8e3 ×3 | 42000 |
| A5 V3 Ultra+(3955) | 16436 (0x4034) / 4116 / 4120 | 8e3 ×3 | 50000 |

**要点**

1. **1K 与 8K 是两套表**，不是同一张表的子集：1K 只有 3 档（125/500/1000），8K 有 6 档（含 2000/4000/8000）。
2. 用 1K 表的四款（V7 / V7 Pro / G3 V3 / G3 S）**正是需要 `AA 55` 握手的 WCH 四款**
   （`newMouseWCH` @1900xxx 同名单），两处证据独立指向同一分组（A）。
3. 官方给**同一型号的三个 transport 分别配表**；本表里同型号三槽一致，但结构上允许不同
   （旧版打包 `mouseDiffConfig$1` 里 M7 就是 `[1e3,1e3,8e3]`，说明该机制确实在用，B）。
4. `K5 V2` 只有 2 槽（本体 + 0x101C），说明槽数与型号有关（A）。
5. 四条 A5 型号在源码里用**混淆键**（`otMQfdZe("…")`），本文按 PID 与目录对应取型号名（C）；
   其中 `16437 ↔ "MCHOSE A5 V3 Pro"` 已用 16 字节 XOR 密钥解出（A）。

### 6.1 与当前 Catalog 的核对

| 项 | 结果 |
|---|---|
| 23 款覆盖 | 目录 `devices[]` **23/23 全部有对应本体 PID**，无缺口 |
| 协议标签 | 4 条标 `mchose-0x4d`（K7 V2 四条），**19 条仍标 `qhw`**——插件自 M2-2 起不再产出该协议名（`Claim0x4D` 恒返回 `mchose-0x4d`），认领不受影响（`KindFromProtocol("qhw")` → `Qhw` 仍试 0x4D）。属**配置口径不一致**，本次不修 |
| transport 登记 | 目录 `transports[]` 有 `0x1014/0x1016/0x1018/0x101C/0x101D`（+ `0x100C`/`0x3033`）；官方 `newMouseWirelessPids` 恰为前五颗，顺序不同（A） |
| 本体 PID vs dongle PID | 官方选表用 **USB 枚举到的 PID**（dongle 或本体），**不是** `0x0900` 自报的配对 PID（A）。所以 2.4G 下走 `pids[1]`/`pids[2]`，插线走 `pids[0]` |
| 真机验证 | 只有 `0x0900` 的电量 / 连接 / 充电（M2-3）；**DPI 与回报率零验证** |
| 四颗未验证 dongle | `0x1014/0x1016/0x101C/0x101D`：官方确认属无线集合，VigilWatt **未真机验证**，不得因此声称支持 |
| `0x3837:0x4026` | 官方表把 16422 配给 **A5 V3 Ultra+**（8e3 表、dpiMax 42000），与目录一致、与 M2-1 真机自报一致；但与用户当时记录的 K7 V2 Ultra+ 仍矛盾，**未解决**（超出本任务） |

## 7. 证据等级与官方代码定位

| 等级 | 含义 |
|---|---|
| **A** | 官方代码有明确实现，调用链完整 |
| **B** | 有字段或表格，但语义 / 型号关联 / 调用条件不完整 |
| **C** | 由其他证据推断，官方代码未明确确认 |
| **Unknown** | 现有证据无法判断 |

| 结论 | 等级 | 定位 |
|---|---|---|
| 帧构造 / 校验 / 64 字节 | A | `getCommandBuffer` @1868959 |
| `0x0003` 请求带 `[profileIndex, direction]` | A | `readDpiInfo` @1883644 |
| `0x0003` 响应字段与偏移 | A | `dpiToObj` @1871165 |
| 活动档位 = `usbDpiIndex`，当前 DPI = `dpiX[usbDpiIndex]` | A | `handleNewMouseDpiChange` @1909941 |
| `0x0600` 仅设备上报、无读请求 | A | `mcMonitor` @1880715；`readMap` @1871587 |
| `0x0600` bit0 → `payload[2]` 档位 | A | `mcMonitor` @1880715 |
| `0x0002` 无参数、返回 index | A | `readPerformanceInfo` @1883073、`allInfoToObj` @1870429 |
| 两张 rate 表与档位值 | A | `rateMap` @1901116 |
| rate 表按型号 + transport PID 选槽 | A | `mouseDiffConfig` @1903461、`getReportRateList` @1863xxx |
| 五颗 dongle PID 的官方集合 | A | `newMouseWirelessPids` @1900485 |
| `dpiSum` = 档位数量 | B | UI 赋值 @4538950（`dpiSum:6`） |
| `0x0600 payload[6]` = profileIndex | B | 日志"上报配置索引" + `updateDeviceBoardIndex` |
| `maxConfigNum` 与 3 个 profile 的关系 | B | `readPerformanceInfo` 的 `[0,1,2]` 白名单 |
| 四条混淆键型号名 | C | 按 PID 对应目录；仅 `16437` 解出明文 |
| `dpiMagicNum` / `dpiSlider` 是否参与读数换算 | **Unknown** | 两表只有定义（@1902158 / @1902415），**两个打包文件里都没有消费方** |
| 哪些型号真的会上报 `0x0600` bit0 | **Unknown** | 代码无型号分支，也无样本 |

## 8. 当前真机验证情况

| 项 | 状态 |
|---|---|
| `0x0900` 电量 / 连接 / 充电 | 真机验证（`3837:1018`，M2-3） |
| `0x0003` DPI | **未验证**——本机无鼠标，代码路径从未跑过 |
| `0x0002` 回报率 / 档位 | **未验证** |
| `0x0600` bit0（档位事件） | **未验证**；现有真机样本只有电量位与充电位 |
| `dpiSum` 实际取值 | **未验证**（只有 UI 里的常量 6） |
| 1K 四款（V7 / V7 Pro / G3 V3 / G3 S） | 未验证，且需先解决 `AA 55` 握手 |

## 9. 未解决问题

1. **`dpiMagicNum` / `dpiSlider` 的用途**：定义在 `mouseDiffConfig`，但两个可用打包文件里**没有任何读取方**。
   若 UI 用它们把原始值换算成显示值，则 VigilWatt 直接展示 `dpiX[i]` 会与官方显示不一致。**必须先解决**。
2. **`0x0600 payload[6]` 是否等于 profileIndex**：官方只记"配置索引"。
3. **`maxConfigNum`（`0x0900` `payload[4]`）与 3 个 profile 的关系**：真机样本是 4，代码按 0..2 处理。
4. **`0x0002` 的 `usbDpiIndex` 与 `0x0003` 的是否恒等**：代码里两处同名字段，未见比较逻辑。
5. **`gRateIndex` 在 2.4G 下的实际生效性**：官方只用 `usbRateIndex` 做提示文案，`gRateIndex` 无消费点。
6. **四条混淆键的完整型号名**：只解出一条。
7. **`0x4026` 身份矛盾**：官方表把 16422 配给 A5 V3 Ultra+，与 M2-1 真机自报一致，但用户当时记录的是 K7 V2 Ultra+。
   本次审计**未处理**该问题（超出范围），但它直接决定"用哪张 rate 表"。

## 10. M2-4B 推荐实施方案

### 10.1 数据来源与映射

| 目标字段 | 来源 | 来源标记 |
|---|---|---|
| `DpiX` | `0x0003` → `dpiX[usbDpiIndex]` | `DeviceReported` |
| `DpiY` | `0x0003`（direction=1）→ `dpiY[usbDpiIndex]`，仅 `dpiHasY==1` | `DeviceReported` |
| `DpiLevel` | 同帧 `usbDpiIndex` | `DeviceReported` |
| `DpiLevelCount` | 同帧 `dpiSum` | **`Inferred`**（B 级语义，真机核对前不升） |
| `ReportRateSettingHz` | `0x0002` 的 index → 型号+transport 表 | **`Inferred`**（表来自官方静态分析，本机未验证） |

`ReportRateMeasuredHz` **不实现**：官方无实测路径，本项目也无系统级估算接入口。

### 10.2 必须遵守的约束

1. **只读**：只发 `0x0002` / `0x0003`，绝不发 `0x0102` / `0x0103` / `0x0104`（写方向）。
2. **rate 表进 JSON**（`AGENTS.md` §10）：新增到 `mchose-catalog.json`，按"型号 → 三槽 pids + 三槽 rate"，
   与官方结构一一对应；插件里不许出现 PID 或档位字面量。
3. **型号未知就不出 Hz**：`0x4026` 这类身份未闭环的设备，只能出 index（日志）或什么都不出，
   **不能猜一张表**。这与 M2-3 "不从协议族泛化" 同一条原则。
4. **轮询预算**：一次完整读 = `0x0002`（1 帧）+ `0x0003` ×2（X/Y，1～2 帧）。建议**不放进 60 秒主轮询**，
   改为"认领后读一次 + `0x0600` bit0 事件触发重读"，避免给睡着/唤醒中的设备增加无谓写入。
5. **不新增能力声明**，直到有真机读数（本次任务明令禁止）。

### 10.3 与现有架构的冲突

1. **`TelemetryValue<int>` 有 `ReportRateSettingHz`，没有 `ReportRateIndex`**：协议只给 index。
   建议**不加字段**，把 index 换算成 Hz 后按 `Inferred` 落库；index 只进日志。
2. **能力枚举缺"能读档位索引"的区分**：`DpiLevelRead` 已存在，可直接用，无需扩展。
3. **rate 表选择依赖"型号 + 当前 transport PID"**，而 `CapabilitiesOf` / 遥测都是按**接口**维度调用；
   当前 `MchoseDeviceIdentity` 已能给出自报型号，可复用，无需新模型。
4. **`0x0600` 事件目前只解析电量/充电位**（`TryParseStatusEvent` 要求"至少带一个电源位"），
   **DPI 档位事件会被丢弃**。M2-4B 需要扩展该判定，但**不能改变现有电量/充电语义**。

### 10.4 是否足以开始 M2-4B

**结论：结构层面足够，语义层面不足。**

- 请求/响应结构、偏移、字节序、活动档位取法：**A 级，可直接实现**。
- rate 表内容与选槽规则：**A 级，但只能按官方静态表实现**，本机未验证 → 输出必须标 `Inferred`。
- 两个必须先解决的前置问题：`dpiMagicNum`/`dpiSlider` 是否影响读数（§9.1）、型号身份是否闭环（§9.7）。

**建议的 M2-4B 边界**：先实现 `DpiX` / `DpiY` / `DpiLevel`（A 级证据，无表依赖），
`ReportRateSettingHz` 与 `DpiLevelCount` 留到拿到真机样本、确认表与语义之后。

## 11. M2-4B1 落地情况（2026-11-08）

| 项 | 状态 |
|---|---|
| `0x0003` 请求 | 已实现 `BuildDpiQuery(profile, direction)`；方向非 0/1 直接抛，不发帧 |
| `0x0003` 解析 | 已实现 `TryParseDpi`：长度 ≥17、方向 0/1、活动索引 <6、hasY 0/1、活动档值非 0 |
| ActiveDpi 关系 | 已实现 `TryGetActiveDpi(reading, activeProfile)`；**当前没有任何调用方发布它** |
| `0x0600` bit0 | 已解析为 `DpiIndex` 并落到 `DpiLevel`；长度改为**按字段判**，纯档位事件不再被丢 |
| 回报率 `0x0002` | **已实现**（M2-4B2）：`TryParsePerformance` 只取 index，Hz 由 `rateTables` / `rateProfiles` 换算 |
| 能力声明 | **未改**：`DpiRead` / `DpiLevelRead` / `RateRead` 在 `3837:1018` 上仍是 `Unverified`，UI 不显示这些行 |
| 真机样本 | **仍无**。插件在认领后读一次 `0x0002` 与 `0x0003`，拿到设备即可直接取证 |

### 11.1 M2-4B2 的具体取舍

- 选表**按"本体型号 PID + USB 枚举 PID"两级查**：`rateProfiles[型号][USB PID] → 表名 → rateTables[表名]`。
  型号来自 `0x0900` 自报（M2-1），USB PID 是枚举到的那颗 dongle/本体——两者不互相替代。
- 发布用 **`usbRateIndex`**：官方全仓唯一的下标使用点是 `this.rateList[na.usbRateIndex]`，
  且改档位时 `{gRateIndex: vr, usbRateIndex: vr}` 一起写，正常设备两者本就同值。
  `gRateIndex` 只进日志，不发布——**没有证据说它才是 2.4G 下生效的那套**（§9.5 仍未关闭）。
- `ReportRateMeasuredHz` 不填：协议里不存在实测吞吐路径。

### 11.2 M2-4C-P1 取证准备修正（2026-11-09）

| 项 | 变更 |
|---|---|
| index 一致性 | **两个 index 不一致时不发布**，保持 Unknown、不顶替、不清旧值；判定集中在 `MchoseRatePolicy` |
| 读取重试 | 原来"试一次就永久放弃"改为**最多 3 轮**（每轮主轮询一次），两项都成功才收工 |
| X / Y 轴 | X 成功后**只有 `dpiHasY==1` 才读 Y**；Y 失败只丢 Y，不影响 X |
| 日志 | `0x0002` / `0x0003` / 档位事件各一条 Info，带原始帧、解析值、表名与发布结论；失败最后一轮记 Info |
| 能力声明 | **未改**：`DpiRead` / `DpiLevelRead` / `RateRead` 仍是 `Unverified` |

仍然**没有真机样本**：§9 的未解决项一条都没关闭，本阶段只把取证手段补齐。

## 12. M2-4 归档状态（2026-11-09）

| 类别 | 状态 |
|---|---|
| 官方源码支持 | 已审计（本文 §2–§7，逐条 A/B/C/Unknown + 字节偏移） |
| VigilWatt 代码实现 | 已完成（`0x0003` / `0x0600` bit0 / `0x0002`，全部只读） |
| 自动化测试 | 已完成（合成帧；`Mchose0x4DDpiTests` + `Mchose0x4DRateTests`） |
| 真机验证 | **Unverified — 当前无可验证的 MCHOSE 0x4D 鼠标** |
| 能力声明开放 | 未完成（`DpiRead` / `DpiLevelRead` / `RateRead` 仍 `Unverified`） |

- 本机设备只有 G87 键盘与 `3837:3033` dongle，两条链路都不走 0x4D，**不构成 0x4D 证据**。
- 真机验证转入独立阶段（M2-5）；在此之前 §9 的未解决项保持原状，不做推断性收口。

§9 的未解决项一条都没关闭，尤其 §9.1（`dpiMagicNum` / `dpiSlider`）与 §9.7（`0x4026` 型号身份）——
两者都直接决定 M2-4B2 能否发布数值。
