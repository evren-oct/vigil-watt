# 北通设备目录（2026-11-16）

> **JSON 是型号目录的唯一机器可读源**：`src/VigilWatt.Core/Plugins/Betop/betop-catalog.json`
> （逆向的原始产物 `betop-official-client.devices.json` 只是它的输入，不要当目录用）。
> 本文只解释结构与结论，逐条数据以 JSON 为准；两边不一致时改本文。
>
> 目录分**两层**，这是本阶段最重要的约定：
> - `devices[]`（33 条）——**运行时认领目录**，`ProtocolEngine` 只读这一段。认领、显示名、能力、`group` 全靠它。
> - `officialDevices[]`（66 PID / 118 身份）——**官方注册表**，BetopGame 2.6.2 逆向所得。**不参与认领**，
>   加载器会忽略这个键。它回答"官方认哪些 PID、叫什么、走哪套接口、电量在哪一字节"，不回答"VigilWatt 支持谁"。

## 1. 数据来源

| 层 | 来源 | 证据强度 |
|---|---|---|
| `devices[]` | Linux xpad KP 系列 PID 补丁（2026-07，只证明 XInput 身份）+ 项目真机记录 | 身份可信，电量仍需真机 |
| `officialDevices[]` | BetopGame 2.6.2 资源包（`20261111.asar`）里 65 个明文 JS 型号脚本的 `RegisterDev` / `RegisterDev2` / `RegisterAlterDev` 调用 | 厂商源码级；只读静态分析 |

逆向过程、帧格式、命令表、三代电量读法见 [betop-official-client.md](betop-official-client.md)；
118 条接口注册的机器可读原始版在 [betop-official-client.devices.json](betop-official-client.devices.json)（本目录由它派生）。

## 2. JSON 结构与状态定义

```
schemaVersion   2
source          devices[] 的来源（xpad + 真机）
officialSource  官方注册表的来源、版本、方法与局限
statusDefinitions  官方状态与 VigilWatt 状态的语义（本节口径）
evidenceSources    证据指针短名 → 文档位置（每条记录用短名引用）
protocolFamilies   六套接口族，按 (usagePage, usage, reportLength) 三元组
vidRelations / vidRules  三个 VID 的含义与不归并规则
devices[]       运行时认领目录（33 条）
officialDevices[]  官方注册表（66 条，一条 = 一个 PID）
```

`officialDevices[]` 一条 = 一个 PID，因为 **PID 是 USB 层的键**，而一个 PID 可能被厂商自己用多个脚本认领成多个型号
（`0x5125` = KP20A + KP40A + 西樵山项目）。所以型号是数组 `models[]`，`officialTitles` 保留该 PID 的全部官方标题原文；
只有"一个 PID 一个型号"时才写 `models[].modelName`，多型号时留空，**不按顺序硬配**。

### 2.1 状态定义

**官方状态**（`officialStatus`，描述注册事实）：

| 值 | 含义 |
|---|---|
| `OfficialRegistered` | 官方脚本里有明确 `RegisterDev` / `RegisterAlterDev` 注册（有 VID/PID） |
| `OfficialUnconfirmed` | 官方脚本存在但缺 PID 等注册证据，构不成设备记录 |

> 当前 66 条全是 `OfficialRegistered`，**没有一条 `OfficialUnconfirmed`**：老一代（阿修罗 / 斯巴达 / 蝙蝠）
> 确实有官方脚本，但不在 118 条注册里，没有 PID 可登记，只能记在 `protocolFamilies.legacy`。

**VigilWatt 状态**（`vigilWatt.verification`，描述我们自己的验证进度，**逐身份记**）：

| 值 | 含义 |
|---|---|
| `CatalogOnly` | 只在本目录里登记，VigilWatt 从未接触过该身份 |
| `Identified` | 本机枚举到过（HID 描述符 / 设备名），但没解析到电量帧 |
| `ProtocolVerified` | 收到过可解析的电量帧（真机或用户日志），未与官方界面百分比对照 |
| `BatteryVerified` | 真机多日样本且口径与官方源码一致；仍未做官方界面百分比对照 |

两条硬规矩：

1. **官方支持电量 ≠ `BatteryVerified`**。官方脚本里 66 个 PID 全都有电量解析代码，我们只把 3 个身份标到
   `BatteryVerified`（另有 1 个 `ProtocolVerified`、3 个 `Identified`）。
2. **目录里的 PID 不等于 VigilWatt 支持**。`inRuntimeCatalog=false` 的身份，插件连认领都不会发生。

### 2.2 与官方冲突时怎么裁（2026-11-16 定）

**名称与事实一律以官方客户端为准；只有 `role` 词表保留我们自己的定义。**

| 层 | 谁说了算 | 本次按官方改的例子 |
|---|---|---|
| 型号名（`devices[].model`） | 官方 | 29 条全部换成官方 `title` 去掉开头「北通」：`T9` → **宙斯2**、`鲲鹏20` → **鲲鹏20 绝尘白**、`鲲鹏70` → **鲲鹏70 AI旗舰** |
| 型号代码 / 官方标题 | 官方 | `515F`/`5160` 原写 `KP70A`，官方是 `BTP-KP70B` → 已改 |
| 一个 PID 属于哪些型号 | 官方 | `5125` 原写 KP20A/KP40A，官方还有 `KUNPThirty`（西樵山项目） → 已补 |
| 接口三元组、电量/充电字节 | 官方 | 充电掩码按型号分支，不再"一律 `& 0xf`"（betop.md §1.3） |
| 命令语义 | 官方 | `02 CD` = `cmd 0xD` 全局配置，不是电量帧（betop.md §1.8.4） |
| `role` 词表 | 我们 | 官方 `wired`/`dongle` 是**注册通道**标签（`RegisterDev2` / `RegisterAlterDev`），我们的 `controller`/`dongle`/`dock` 是**物理角色** |

**「去掉开头北通」是硬规则**：`DisplayNameOf` / `BetopDisplayName` 会再加一次品牌前缀，
存全称会显示成「北通 北通鲲鹏20 绝尘白」。官方 `title` 原文仍完整保留在 `officialDevices[].officialTitles`。

**唯一不照搬的是 `role`**：官方 `20BC:5158` 标 `wired`，按我们的词表它是 `controller`；官方 `20BC:5134`
也标 `wired`，但 xpad 产品串写的是 `KP50B XInput Dongle`，我们记 `dongle`。照搬会让"手柄"和"接收器"
都退化成 wired/dongle 二选一，物理角色就丢了。

**改名带来的两个副作用**（都已写进 betop.md §1.10）：

1. `20BC:5128` 与 `20DD:5128` 现在同名「北通 鲲鹏20智控游戏手柄」——官方不区分本体/接收器，后缀没了。
2. 多型号 PID（`5125`/`5127`/`5145`/`5146`）的 `model` 仍留空：官方对同一 PID 给了 2~3 个标题，
   挑一个就是猜；这几条继续走产品串分流。

## 3. 运行时认领目录（`devices[]`，33 条）

本阶段改了两类：**29 条 `model` 换成官方标题**（去掉开头「北通」，因为代码会再加品牌前缀）、
**6 条 `note` 文案**（上游验证说明、`02 CD` 定性、`KP70B`、`KUNPThirty`）。
VID/PID、协议、能力、`group`、`pairedPid` **一字未改**。33 条 = `20BC` 30 条 + `20DD` 3 条。

`role` 只描述这颗 PID 在目录里的角色：`controller`（手柄 / 承载电量的 XInput dongle）、`dongle`（接收器）、
`dock`（底座）。`group` 只有 5158/5159 写了 `kunpeng20`。

| VID:PID | 型号 | role | 协议 | 配对/组 | 能力 | 来源与验证 |
|---|---|---|---|---|---|---|
| `20BC:5073` | 宙斯2 | controller | `betop-zeus-keyevent` | — | `batteryRead=supported`、`chargingRead=unsupported` | 本机真机；Col04 被动状态流，无充电标志字节 |
| `20BC:5125` | —（歧义） | controller | `betop-status15` | — | 未声明 | xpad 标 KP20A/KP40A 共用；官方注册还含 KUNPThirty（西樵山项目）；型号由产品串判定，电量未验证 |
| `20BC:5126` | 鲲鹏20智控游戏手柄 | controller | `betop-status15` | — | 未声明 | xpad 标 KP20A；电量未验证 |
| `20BC:5127` | —（歧义） | controller | `betop-status15` | — | 未声明 | xpad 标 KP20A/KP40A 共用；型号由产品串判定，电量未验证 |
| `20BC:5128` | 鲲鹏20智控游戏手柄 | controller | `betop-status15` | — | 未声明 | xpad 标 KP20A；电量未验证 |
| `20BC:5158` | 鲲鹏20 绝尘白 | controller | `betop-status15` | `pairedPid=5159` / `group=kunpeng20` / `wired=true` | `batteryRead=supported`、`chargingRead=supported` | 贡献者真机，有线 `02 15` 查询 78% 充电中 |
| `20BC:5159` | 鲲鹏20 绝尘白 | controller | `betop-status15` | `pairedPid=5158` / `group=kunpeng20` / `wired=false` | `batteryRead=supported`、`chargingRead=supported` | 贡献者真机，2.4G 杂项页每秒广播 |
| `20BC:5191` | 鲲鹏20精英版-丁未 | controller | `betop-status15` | — | 未声明 | KP20EB；上游实现（oitsukiii/betop-battery）在该型号真机验证 `02 15` 查询，本机未复测 |
| `20BC:5145` | —（歧义） | controller | `betop-status15` | — | 未声明 | xpad 标 KP40A/KP40B 共用；型号由产品串判定，电量未验证 |
| `20BC:5146` | —（歧义） | controller | `betop-status15` | — | 未声明 | xpad 标 KP40A/KP40B 共用；型号由产品串判定，电量未验证 |
| `20BC:5154` | 鲲鹏40 明末联动款 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40D；电量未验证 |
| `20BC:5155` | 鲲鹏40 明末联动款 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40D；电量未验证 |
| `20BC:515B` | 鲲鹏40 御铠白 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40D White；电量未验证 |
| `20BC:515C` | 鲲鹏40 御铠白 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40D White；电量未验证 |
| `20BC:515D` | 鲲鹏40智控游戏手柄 风驰白 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40F White；电量未验证 |
| `20BC:515E` | 鲲鹏40智控游戏手柄 风驰白 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40F White；电量未验证 |
| `20BC:5169` | 鲲鹏40 电掣黑 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40F Black；电量未验证 |
| `20BC:516A` | 鲲鹏40 电掣黑 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40F Black；电量未验证 |
| `20BC:5133` | 鲲鹏50 云翼白 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50B；电量未验证 |
| `20BC:5149` | 鲲鹏50 星穹紫 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50C；电量未验证 |
| `20BC:5150` | 鲲鹏50智控游戏手柄 解限机联名款 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50D；电量未验证 |
| `20BC:5151` | 鲲鹏50智控游戏手柄 解限机联名款 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50D；电量未验证 |
| `20BC:5152` | 鲲鹏50智控游戏手柄 解限机联名款 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50E；电量未验证 |
| `20BC:5153` | 鲲鹏50智控游戏手柄 解限机联名款 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50E；电量未验证 |
| `20BC:5134` | 鲲鹏50 云翼白 | dongle | `betop-status15` | — | 未声明 | xpad 标 KP50B XInput Dongle；电量未验证 |
| `20BC:514A` | 鲲鹏50 星穹紫 | dongle | `betop-status15` | — | 未声明 | xpad 标 KP50C XInput Dongle；电量未验证 |
| `20BC:512F` | 鲲鹏70 AI旗舰 | controller | `betop-status15` | — | 未声明 | xpad 标 KP70A；电量未验证 |
| `20BC:5130` | 鲲鹏70 AI旗舰 | controller | `betop-status15` | — | 未声明 | xpad 标 KP70A XInput Dongle；用户日志广播 `02 15` 80% |
| `20BC:515F` | 鲲鹏70AI智控旗舰手柄 | controller | `betop-status15` | — | 未声明 | xpad 标 KP70A，官方注册为 BTP-KP70B（鲲鹏70AI智控旗舰手柄）；电量未验证 |
| `20BC:5160` | 鲲鹏70AI智控旗舰手柄 | controller | `betop-status15` | — | 未声明 | xpad 标 KP70A，官方注册为 BTP-KP70B（鲲鹏70AI智控旗舰手柄）；电量未验证 |
| `20DD:5128` | 鲲鹏20智控游戏手柄 | dongle | `betop-status15` | — | 未声明 | KP20 DONGLE；只回 `02 CD`（cmd 0xD 全局配置，不是电量帧；CR-106 待结案） |
| `20DD:5130` | 鲲鹏70 AI旗舰 | dock | `betop-status15` | — | 未声明 | BTP-KP70A DONGLE 底座；只回 `02 CD`（cmd 0xD 全局配置，不是电量帧；CR-106 待结案） |
| `20DD:515C` | 鲲鹏40 御铠白 | dongle | `betop-status15` | — | 未声明 | xpad 的 KP40D HID receiver；电量未验证 |

> `02 CD` 的定性见 [betop-official-client.md](betop-official-client.md) §5：`cmd 0xD` 是**全局配置报告**，
> 不是电量帧，所以 `20DD` 那几个接口不该当电量口。`docs/reviews/BACKLOG.md` 的 CR-106 仍是 `open`，
> 本阶段不动它（结案要人工确认）。## 4. VID/PID 关系与身份规则

### 4.1 三个 VID

| VID | 含义 | 注册 PID 数 | 当前是否认领 |
|---|---|---|---|
| `0x20BC` | 本体 / 直插身份（`RegisterDev2`） | 65 | 是（`betop.protocols.json` 的 `vendors`） |
| `0x20DD` | **同一 PID 的第二套 USB 身份**（`RegisterAlterDev`：接收器 / 底座 / 配置口） | 52 | 是 |
| `0x0517` | 鲲鹏50 第三套身份，只注册了 `0x5350` | 1 | **否**（不在 `vendors` 里） |

### 4.2 `0x20BC` 与 `0x20DD` 是同一设备的两种身份

官方 `kp20.js` 里同一个 `JOYPAD_PID` 被注册两次：

```js
RegisterDev2(0x20bc, JOYPAD_PID, 0xff, 0x3, -1, 64, ...);     // 本体
RegisterAlterDev(0x20bc, DONGLE_PID, 0xff, 0x3, -1, 64, ..., 0);  // 备选身份
RegisterDev2(0x20dd, JOYPAD_PID, ...);                         // 同一 PID 的第二个 VID
```

所以 `20BC:5128` 与 `20DD:5128` **不是两个型号**。但这**不等于**可以在 VigilWatt 里合并：
- 目录里它们是两条独立条目（`20DD:5128` 叫「鲲鹏20 接收器」）；
- 归一只认显式 `group`，不按 PID 数值跨 VID 推导；
- 这与 betop.md §1.8.4 的实测吻合：`20BC:5130` 正常广播、`20DD:5130` 只回 `02 CD`。

### 4.3 一个型号对应多个 PID（28 个型号代码里 26 个如此）

绝大多数型号代码至少两个 PID（有线 / 无线、本体 / 接收器），鲲鹏50 还是三个（多一个 `0x0517:0x5350`）。
只有两个例外各占一个 PID：`HONGHThirty`（`2104`）与 `KUNPThirty`（`5125` 里的西樵山项目）。

| 型号代码 | 官方名称 | PID |
|---|---|---|
| `BTP-KP20A` | （多型号 PID，见 §8） | `5125` `5126` `5127` `5128` |
| `BTP-KP20D` | 北通鲲鹏20 绝尘白 | `5158` `5159` |
| `BTP-KP20DL` | 北通鲲鹏20战双帕弥什联名款 | `516F` `5170` |
| `BTP-KP20EB` | 北通鲲鹏20精英版-丁未 | `5190` `5191` |
| `BTP-KP40A` / `BTP-KP40B` | （多型号 PID） | `5125` `5127` / `5145` `5146` |
| `BTP-KP40D` | 明末联动款 / 御铠白 | `5154` `5155` `515B` `515C` |
| `BTP-KP40E` | 北通鲲鹏40 精英版 | `5194` `5195` |
| `BTP-KP40F` | 风驰白 / 电掣黑 | `515D` `515E` `5169` `516A` |
| `BTP-KP40MCK` / `MCF` | 北通鲲鹏40鸣潮联名款 | `5163` `5164` / `5165` `5166` |
| `BTP-KP50B` | 北通鲲鹏50 云翼白 | `5133` `5134` **`5350`（VID 0x0517）** |
| `BTP-KP50C` | 北通鲲鹏50 星穹紫 | `5149` `514A` **`5350`** |
| `BTP-KP50D` / `KP50E` | 鲲鹏50 解限机联名款 | `5150` `5151` / `5152` `5153`（+`5350`） |
| `BTP-KP70A` | 北通鲲鹏70 AI旗舰 | `512F` `5130` |
| `BTP-KP70A1` | 北通鲲鹏70 午马限定款 | `516D` `516E` |
| `BTP-KP70B` | 北通鲲鹏70AI智控旗舰手柄 | `515F` `5160` |
| `BTP-KP70C` | 北通鲲鹏70 6D升级款 | `5173` `5174` |
| `BTP-KP70C1`~`C4` | 北通鲲鹏70 异环联名款 | `518A/B` `518C/D` `519B/C` `519D/E` |
| `BTP-PG01A` | 北通盘古智控游戏手柄 | `5161` `5162` |
| `T6Pro` | 宙斯 Pro | `500B` `502C` `502F` `5074` |
| `zs2` | 宙斯2 / 永劫无间 | `501E` `5073` `5074` `5076` `5077` `5104`~`5107` |
| `HONGHThirty` / `KUNPThirty` | 海心桥 / 西樵山项目机 | `2104` / `5125` |

### 4.4 同一 PID 对应多个型号（厂商自己如此）

| PID | 型号代码 | 说明 |
|---|---|---|
| `0x5125` | KP20A、KP40A、KUNPThirty | 5 个脚本认领（`hxc` `kp30` `kp30_app` `kp40` `kp401`），写加密 0x0/0x1 都有 |
| `0x5127` | KP20A、KP40A | `kp30` `kp40` `kp401` |
| `0x5145` / `0x5146` | KP40A、KP40B | `kp40b` `kp40v1r` |
| `0x5074` | T6Pro、zs2 | `t6p2a` `t9` |
| `0x5350` | KP50B、KP50C、KP50D、KP50E | `kp50p` `kp50c` `kp50d1` `kp50d2`（4 个型号共用一套 0x0517 身份） |

**这不是我们判错**——厂商自己也是两个脚本认领同一 PID。所以 `devices[]` 里 `5125/5127/5145/5146` 的 `model` 留空，
由产品串分流（§8）。**任何"PID → 型号"的查表都必须允许一个 PID 多个型号。**

### 4.5 不归并规则（与代码一致，本阶段未改）

1. **只有显式 `group` 才归一**：当前只有 `20BC:5158` / `20BC:5159` 的 `group=kunpeng20`，归一到 `betop:kunpeng20`。
2. **不按系列合并**：同属鲲鹏40 的多个 PID 没有配对证据，各自走路径哈希。
3. **不跨 VID 合并**：`20BC:5128` 与 `20DD:5128` 即使 PID 相同也是两条独立身份。
4. **`0x0517:0x5350` 不并入鲲鹏50 的 `5133/5134`**：型号名重叠但官方没给配对证据，只记关系不归并。
5. **同型号两只并行仍会并成一台**：`group` 是型号粒度，dongle 与本体没有配对关系数据（betop.md §1.8.2）。

## 5. 官方注册表（`officialDevices[]`，66 PID / 118 身份）

表由 JSON 生成，列含义：**族** = `protocolFamilies` 键；**身份** = 该 PID 注册在哪些 VID 下及 `role`；
**电量 / 充电** = 官方脚本解析的字节下标（0 起，不含 report id）；**VigilWatt** = 我们的验证状态。

| PID | 官方型号（型号代码（名称）） | 族 | 接口页/usage/长 | 电量 | 充电 | 身份 | VigilWatt |
|---|---|---|---|---|---|---|---|
| `0x2104` | HONGHThirty（海心桥项目） | 海心桥 | 0xff7a/0x01/64 | byte5 | — | 0x20bc wired | 仅目录 |
| `0x500b` | T6Pro（宙斯 Pro） | 宙斯 | 0x00ff/0x03/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc wired | 仅目录 |
| `0x501e` | zs2（宙斯2） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc wired | 仅目录 |
| `0x502c` | T6Pro（宙斯 Pro） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc dongle | 仅目录 |
| `0x502f` | T6Pro（宙斯 Pro） | 宙斯 | 0x00ff/0x03/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc dongle | 仅目录 |
| `0x5073` | zs2（宙斯2） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc dongle | 电量已验证（运行目录：宙斯2） |
| `0x5074` | T6Pro（宙斯 Pro）、zs2（宙斯2） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc dongle | 仅目录 |
| `0x5076` | zs2（宙斯2） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc wired | 仅目录 |
| `0x5077` | zs2（宙斯2） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc dongle | 仅目录 |
| `0x5104` | zs2（永劫无间） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc wired | 仅目录 |
| `0x5105` | zs2（永劫无间） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc dongle | 仅目录 |
| `0x5106` | zs2（永劫无间） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc dongle | 仅目录 |
| `0x5107` | zs2（永劫无间） | 宙斯 | 0xff00/0x01/24 + 0xff00/0x02/24 + 0xff00/0x03/24 | raw[1]&0x07 | — | 0x20bc dongle | 仅目录 |
| `0x5125` | BTP-KP20A（鲲鹏20）、BTP-KP40A（鲲鹏40）、KUNPThirty（项目机（西樵山）） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | — | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：型号留空） |
| `0x5126` | BTP-KP20A（北通鲲鹏20智控游戏手柄） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | — | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏20智控游戏手柄） |
| `0x5127` | BTP-KP20A（鲲鹏20）、BTP-KP40A（鲲鹏40） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | — | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：型号留空） |
| `0x5128` | BTP-KP20A（北通鲲鹏20智控游戏手柄） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | — | 0x20bc wired / 0x20dd dongle | 已识别（运行目录：鲲鹏20智控游戏手柄） |
| `0x512f` | BTP-KP70A（北通鲲鹏70 AI旗舰） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏70 AI旗舰） |
| `0x5130` | BTP-KP70A（北通鲲鹏70 AI旗舰） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 协议已验证（运行目录：鲲鹏70 AI旗舰） |
| `0x5133` | BTP-KP50B（北通鲲鹏50 云翼白） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏50 云翼白） |
| `0x5134` | BTP-KP50B（北通鲲鹏50 云翼白） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏50 云翼白） |
| `0x5145` | BTP-KP40A（鲲鹏40）、BTP-KP40B（鲲鹏40） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 混合 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：型号留空） |
| `0x5146` | BTP-KP40A（鲲鹏40）、BTP-KP40B（鲲鹏40） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 混合 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：型号留空） |
| `0x5149` | BTP-KP50C（北通鲲鹏50 星穹紫） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏50 星穹紫） |
| `0x514a` | BTP-KP50C（北通鲲鹏50 星穹紫） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏50 星穹紫） |
| `0x5150` | BTP-KP50D（鲲鹏50智控游戏手柄 解限机联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏50智控游戏手柄 解限机联名款） |
| `0x5151` | BTP-KP50D（鲲鹏50智控游戏手柄 解限机联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏50智控游戏手柄 解限机联名款） |
| `0x5152` | BTP-KP50E（鲲鹏50智控游戏手柄 解限机联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏50智控游戏手柄 解限机联名款） |
| `0x5153` | BTP-KP50E（鲲鹏50智控游戏手柄 解限机联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏50智控游戏手柄 解限机联名款） |
| `0x5154` | BTP-KP40D（北通鲲鹏40 明末联动款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏40 明末联动款） |
| `0x5155` | BTP-KP40D（北通鲲鹏40 明末联动款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏40 明末联动款） |
| `0x5158` | BTP-KP20D（北通鲲鹏20 绝尘白） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 电量已验证（运行目录：鲲鹏20 绝尘白） |
| `0x5159` | BTP-KP20D（北通鲲鹏20 绝尘白） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 电量已验证（运行目录：鲲鹏20 绝尘白） |
| `0x515b` | BTP-KP40D（北通鲲鹏40 御铠白） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏40 御铠白） |
| `0x515c` | BTP-KP40D（北通鲲鹏40 御铠白） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏40 御铠白） |
| `0x515d` | BTP-KP40F（北通鲲鹏40智控游戏手柄 风驰白） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏40智控游戏手柄 风驰白） |
| `0x515e` | BTP-KP40F（北通鲲鹏40智控游戏手柄 风驰白） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏40智控游戏手柄 风驰白） |
| `0x515f` | BTP-KP70B（北通鲲鹏70AI智控旗舰手柄） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏70AI智控旗舰手柄） |
| `0x5160` | BTP-KP70B（北通鲲鹏70AI智控旗舰手柄） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏70AI智控旗舰手柄） |
| `0x5161` | BTP-PG01A（北通盘古智控游戏手柄） | 盘古 | 0xff80/0x01/64 + 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x5162` | BTP-PG01A（北通盘古智控游戏手柄） | 盘古 | 0xff80/0x01/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x5163` | BTP-KP40MCK（北通鲲鹏40鸣潮联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x5164` | BTP-KP40MCK（北通鲲鹏40鸣潮联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x5165` | BTP-KP40MCF（北通鲲鹏40鸣潮联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x5166` | BTP-KP40MCF（北通鲲鹏40鸣潮联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x5169` | BTP-KP40F（北通鲲鹏40 电掣黑） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录（运行目录：鲲鹏40 电掣黑） |
| `0x516a` | BTP-KP40F（北通鲲鹏40 电掣黑） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏40 电掣黑） |
| `0x516d` | BTP-KP70A1（北通鲲鹏70 午马限定款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x516e` | BTP-KP70A1（北通鲲鹏70 午马限定款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x516f` | BTP-KP20DL（北通鲲鹏20战双帕弥什联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x5170` | BTP-KP20DL（北通鲲鹏20战双帕弥什联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x5173` | BTP-KP70C（北通鲲鹏70 6D升级款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x5174` | BTP-KP70C（北通鲲鹏70 6D升级款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x518a` | BTP-KP70C1（北通鲲鹏70 异环联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x518b` | BTP-KP70C1（北通鲲鹏70 异环联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x518c` | BTP-KP70C2（北通鲲鹏70 异环联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x518d` | BTP-KP70C2（北通鲲鹏70 异环联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x5190` | BTP-KP20EB（北通鲲鹏20精英版-丁未） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x5191` | BTP-KP20EB（北通鲲鹏20精英版-丁未） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录（运行目录：鲲鹏20精英版-丁未） |
| `0x5194` | BTP-KP40E（北通鲲鹏40 精英版） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x5195` | BTP-KP40E（北通鲲鹏40 精英版） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 掩码未记录 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x519b` | BTP-KP70C3（北通鲲鹏70 异环联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x519c` | BTP-KP70C3（北通鲲鹏70 异环联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x519d` | BTP-KP70C4（北通鲲鹏70 异环联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd wired | 仅目录 |
| `0x519e` | BTP-KP70C4（北通鲲鹏70 异环联名款） | 鲲鹏 | 0x00ff/0x03/64 | byte2 | byte4 低4位 | 0x20bc wired / 0x20dd dongle | 仅目录 |
| `0x5350` | BTP-KP50B（鲲鹏50）、BTP-KP50C（鲲鹏50）、BTP-KP50D（鲲鹏50）、BTP-KP50E（鲲鹏50） | 鲲鹏50 副 | 0xff00/0x01/64 | byte2 | byte4 掩码未记录 | 0x0517 wired | 仅目录 |

> 说明：
> - 充电列 `byte4 掩码未记录` = 官方脚本确实读 byte4，但逆向报告只记录了部分脚本的掩码（`& 0xf` / 整字节），
>   这些型号的掩码没记录，**不推定**。`混合` = 同一 PID 的多个脚本读法不同。
> - `0x5350` 的四个型号共用一套身份，官方没有一对一的标题，`models[].modelName` 留空，标题见 `officialTitles`。
> - 宙斯系 12 个 PID 的充电列是 `—`：位域里**没有充电位**，不是"未解析"。## 6. 协议族分布（按三元组，不按页）

| 族 | 接口页 / usage / 报文长 | PID 数 | 电量读法 |
|---|---|---|---|
| `kunpeng` 鲲鹏 / 盘古主路 | `0x00FF` / `0x03` / 64 | 50 | 整帧 byte2 = 百分比 |
| `zeus` 宙斯系 | `0xFF00` / `0x01`~`0x03` / **24** | 12 | 位域：`raw[1] & 0x07` → `[0,50,80,100,100]` |
| `pangu` 盘古另一路 | `0xFF80` / `0x01` / 64 | 2 | 整帧 byte2 |
| `kunpeng50-alt` 鲲鹏50 副身份 | `0xFF00` / `0x01` / **64** | 1 | 整帧 byte2 |
| `hh30` 海心桥 | `0xFF7A` / `0x01` / 64 | 1 | 整帧 byte5 |
| `legacy` 老一代 | `0x0003` / `0x02` / 64 | 0（无 PID） | byte5 或 byte6，无充电标志 |

两条必须记住的：

- **页与报文长不是一一对应**：`0x00FF` 有 64 也有 24（`0x500B`/`0x502F` 同时注册 `0x00FF/0x03/24`），
  `0xFF00` 有 24（宙斯）也有 64（鲲鹏50 副身份）。判据必须按**三元组**。
- **老一代没有 PID 注册**：`a1_ns` / `axl2_new` / `axl2prow_new` / `bd2w` / `sbd2` / `sbd3*` 这些脚本
  不在 118 条注册里，只能记族不记设备；不纳入接入范围（多为有线无电池）。

## 7. 官方电量支持 vs VigilWatt 验证

**官方电量支持（66 个 PID 全部有解析代码）**：

| 读法 | PID 数 | 说明 |
|---|---|---|
| byte2 = 百分比 | 53 | 鲲鹏 / 盘古 / 鸣潮 / 联名款全系 |
| byte5 = 百分比 | 1 | 海心桥项目机 |
| 位域 `raw[1] & 0x07` | 12 | 宙斯系（含我们的 `0x5073`，官方名「宙斯2」） |

充电：49 个 PID 有 byte4、17 个 PID 无充电字段（宙斯 12 + `5125`/`5126`/`5127`/`5128` 4 个 + 海心桥 1）。

**VigilWatt 实际验证（118 个身份里只有 7 个有本地证据）**：

| 身份 | 状态 | 依据 |
|---|---|---|
| `20BC:5073`（官方 zs2「宙斯2」，产品串 T9） | `BatteryVerified` | 本机真机 248 条样本；按官方位域改读 `raw[1]` 低 3 位 |
| `20BC:5158`（鲲鹏20 绝尘白，有线） | `BatteryVerified` | 贡献者真机：`02 15` 查询回 78% 充电中 |
| `20BC:5159`（鲲鹏20 绝尘白，2.4G） | `BatteryVerified` | 贡献者真机：每秒广播 `02 15 27 00 50` = 39% |
| `20BC:5130`（鲲鹏70 AI旗舰 dongle） | `ProtocolVerified` | 用户日志广播 `02 15 50 00 50` = 80%，未做界面对照 |
| `20BC:5128`（鲲鹏20智控游戏手柄，`20BC` 身份） | `Identified` | 设备变更日志里枚举到（两个口），无电量帧记录 |
| `20DD:5128` / `20DD:5130` | `Identified` | 本机枚举到，只回 `02 CD`（cmd 0xD 全局配置），无电量帧 |

其余 111 个身份是 `CatalogOnly`。**`BatteryVerified` 也不等于与官方界面一致**——三个已验身份都还没做
「官方客户端界面百分比 vs 我们的读数」同刻对照，这是 betop-official-client.md §9 的待办 1。

## 8. 型号歧义

以下 PID 在官方资料里同时对应多个型号，**不能按 PID 数值猜型号**：

- `5125` / `5127`：KP20A / KP40A（`5125` 还有西樵山项目）。
- `5145` / `5146`：KP40A / KP40B。
- `5074`：宙斯 Pro / 宙斯2。

`devices[]` 里这些条目的 `model` 留空；显示名由产品串分流（`KP20` → 鲲鹏20，`KP40` → 鲲鹏40，
两者同时出现 → 鲲鹏20/40）。产品串也分不出时使用「北通手柄 (VID:PID)」，不冒充已确认型号。

**T9 就是宙斯2**：官方把 `0x5073` 归到型号代码 `zs2`、标题「宙斯2」，所以目录与界面现在都显示
「北通 宙斯2」。产品串仍是 `BEITONG T9 For Keyboard & Mouse`——文档里的 T9 指同一只设备，
betop.md §1.4 的 248 条真机样本也是它，别当成两个型号。

## 9. 未确认项

| # | 事项 | 为什么没定 |
|---|---|---|
| 1 | `0x5161` 两套身份接口页不同（`20BC` = `0xFF80`，`20DD` = `0x00FF/0x03`） | 官方注册如此，逆向报告没有解释；同族的 `0x5162` 两边都是 `0xFF80` |
| 2 | 21 个 PID 的充电掩码 | 逆向报告 §4.1 只列了部分脚本的读法（`& 0xf` / 整字节），其余未记录，不推定 |
| 3 | `kp20.js` 的充电读法自相矛盾 | §4.1 同一张表里既写「整字节」又写「无 `charge_state` 解析」；`5126`/`5128` 的附录列本身是 `—`，暂不影响结论 |
| 4 | `0x0517:0x5350` 与 `0x20BC:0x5133/0x5134` 是否同一只鲲鹏50 | 型号名重叠，但官方没给配对证据 |
| 5 | 宙斯系是否有充电位 | 位域里没有，但可能藏在 `cmd 0x35`/`0x45` 等扩展状态报告里，未查 |
| 6 | 118 条注册里 29 个脚本没有 PID | 官方有 65 个型号脚本、只有 36 个调用了 `RegisterDev*`，其余（含老一代）注册方式未查 |

## 10. P2 数据化迁移（2026-11-18）

> §3 表的「协议」列写于 P1，P2 起以本节为准；其余字段（型号/role/能力）不变。

运行目录从 75 条 / 51 PID 扩到 **110 条 / 86 个 `VID:PID`**，协议 3 套：

| 协议 | 用途 |
|---|---|
| `betop-status15` | 鲲鹏基础帧（`0x00FF/0x03/64`），本身是 low4 充电口径 |
| `betop-status15-nocharge` | 同帧布局，但不解析充电 |
| `betop-zeus-keyevent` | 宙斯族（`0xFF00/24`）被动位域，替代原 `betop-t9` |

### 11.1 逐型号充电口径（50 个鲲鹏 20BC）

| 口径 | PID | 数量 |
|---|---|---|
| low4（`betop-status15`） | 512f, 5130, 5158, 5159, 515b, 515c, 515f, 5160, 516d, 516e, 516f, 5170, 5173, 5174, 518a, 518b, 518c, 518d, 5190, 5191, 519b, 519c, 519d, 519e | 24 |
| nocharge（`betop-status15-nocharge`） | 5125, 5126, 5127, 5128（none-documented）；5145, 5146（mixed）；5133, 5134, 5149, 514a, 5150~5153, 5154, 5155, 515d, 515e, 5163~5166, 5169, 516a, 5194, 5195（unknown） | 26 |

`5161/5162`（盘古）官方也是 low4，但不在运行目录（`0xFF80` 页），本批不列。
能力声明不因解析器支持而升级：只有 `5158/5159` 保持 `chargingRead=supported`。

### 11.2 宙斯 9 个新 PID

`500b, 502f, 5074, 5076, 5077, 5104~5107` 追加进 `devices[]`，指向 `betop-zeus-keyevent`，
`role=controller`、无 `capabilities`；`5074` 多型号，`model` 留空。

`501E` / `502C` 在当前 2.6.2 包内的注册调用是注释掉的，**不追加**；它们仍留在
`officialDevices[]` 的 `CatalogOnly`，见 BACKLOG CR-169。

### 11.3 `20DD` 回填 26 条

对“只有 20BC、官方有 20DD 身份”的 26 个 PID 追加 20DD 条目：

```
5125 5126 5127 512f 5133 5134 5145 5146 5149 514a 5150 5151 5152 5153
5154 5155 5158 5159 515b 515d 515e 515f 5160 5169 516a 5191
```

- `kind=unknown`、无 `capabilities`、无 `group` / `pairedPid`；
- `role` 按官方 20DD 身份映射：`wired → controller`、`dongle → dongle`；`5130` 有底座证据，保持 `dock`；
- P1-B2 已加的 21 条 20DD 也按同一映射修正 role（不在原 33 条冻结快照内）；
- 原有 3 条 20DD（5128/5130/515c）字段不动；
- 回填后 20DD 覆盖 50 / 52 个官方身份；缺的 2 个是盘古 `5161/5162`（不在运行目录）。

### 11.4 解析路径

`BetopCodec` 已删除；鲲鹏 `0x15` 与宙斯 `0x04` 都由 `FrameReader` / `ProtocolEngine` 解析。
宙斯 `<100%` 的 `Discharging` 由 JSON 的 `chargeWhenMissing: "discharging"` 表达；
`100%` 的满电收尾在 `BetopPlugin.FinalizeCharging`。`02 CD`（cmd 0xD）仍不解析。

## 11. 相关文档

- 逆向报告与证据等级：[betop-official-client.md](betop-official-client.md)。
- 协议与真机时间线：[betop.md](../brands/betop.md) §1（T9 口径见 §1.4、鲲鹏20 见 §1.8、目录见 §1.10）。
- 目录 JSON 写法：[protocol-json.md](../../dev/protocol-json.md) §2 / §2.2。
- 未决项：[BACKLOG.md](../../reviews/BACKLOG.md) CR-103 / CR-104 / CR-106。
