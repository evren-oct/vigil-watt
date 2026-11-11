# 雷柏设备目录与协议族（2026-11-01 逆向 AHUB 2.0）

> 来源：官方网页驱动 **RAPOO HUB / AHUB 2.0** 主包
> `hid.rapoo.cn/AHUB_WEB/dist/assets/js/index-BNoOd8Jm.js`（3.25 MB，设备 manifest 全部编译在内）。
> **除 §6 标注「真机验证」的条目外，全部是静态分析**——本机只有 VT3（`24AE:1412`）和
> VT2S Air MAX V2（`24AE:4B13` / `1B13`）。
> 协议帧细节见 [rapoo.md](../brands/rapoo.md)；本文只放**设备清单、协议族划分、取数口径**。
> **已落地**：195 条 PID 收进 `Plugins/Rapoo/rapoo-catalog.json`，12 套协议收进
> `Plugins/Rapoo/specs/rapoo.protocols.json`（都是内嵌资源），由 `Plugins/Protocol/` 的解释器统一解析（§2.1）。
> 加新机型 = 改目录一行；加形态固定的协议 = 改 protocols 一段 JSON，代码不动。

## 1. 数据来源与提取口径

雷柏**没有**迈从那样的远程配置中心（`configCenter/cardList.json`）：设备表是**编译期常量**，
散在主包的 `hardware:{...}` 对象里。因此新机型上市必须重抓主包，拿不到增量接口。

主包里是 **108 个字面量 manifest + 1 个 `Ga()` 工厂函数（生成 5 个 V700diy 变体）**，
共 **109 台设备**；一个 manifest 可带 1~2 个 PID（有线 + 无线），展开后 **195 个 VID:PID**。
其中 4 个 V700diy 在字面量与工厂里各出现一次（PID 相同、型号相同），去重后计 1。

manifest 的字段形状：

```js
{ channel:["china_game","global"], model, mainControl, dongleModel,
  type:"mouse" | "keyboard" | "base",
  hardware:{ vendorId:9390, productId:[有线, 无线], currentPid, transport:"HID",
             filters:[{ usagePage:65280, usage:2, description, type:"default"|"update"|"updateWCH" }] },
  driver:"VT_nrf54L",                        // ← 协议族
  MCU:"N54L15", sensor:"PAW3398", microSwitch, encoder, battery:800,   // 电池 mAh
  ui:{ assetsDir:"manifests/VT3", colors:{ series:[{ name, value, key }], current } },
  capabilities:[ ... ] }
```

`channel` 是销售区域（`china_game` / `china_office` / `china_cybercafe` / `global` / `global_aesco` /
`china_aesco`），同一硬件在不同区域可能换型号名（`MT550L` / `MT550` 就是同一个 `productId` 的两个名字）。

## 2. 协议族：12 套

`driver` 字段即协议族，注册表在主包的 `Cs.register(...)`。**不是只有 `status-report7` 一套**，
但鼠标高度集中在 `VT_nrf54L`：

| 官方 driver | 本项目 slug | 台数 | 代表机型 | 控制接口 usagePage:usage | 电量来源 |
|---|---|---|---|---|---|
| `VT_nrf54L` | `status-report7` | **77** | VT3、VT2S Air MAX V2、VT0/VT9 全系 | `0xFF00:0x02` | 被动 report `0x07` ✅ 已实现 |
| `VT_nrf54L_Base` | `status-report7-base` | 7 | ESPR5/7/8、VPR8 | `0xFF00:0x0E` | 底座无电量，转发子鼠标的 `0x07` |
| `TelinkKeyboard` | `telink-keyboard` | 6 | A83 Air、V700diy 系列 | `0xFF00:0x0E` | 被动，载荷 `[0]==0x01` |
| `Telink8273_Mouse` | `telink-8273` | 6 | MT550/560/750/760/770 | `0xFF00:0x0E` | 被动 report `0xBB`（`[0]==0xB0`） |
| `CH585M` | `ch585m` | 3 | ESK900-75RT / 83RT / 98RT | `0xFF00:0x02` | 被动，载荷 `[0]==0x01` |
| `GCOME_AP` | `gcome-ap` | 2 | Ace 68 Turbo、VT68 | `0x0001:0x00` | 被动，自有解码器 |
| `Telink_Base` | `telink-base` | 2 | P5 PLUS、P6 PLUS | `0xFF00:0x0E` | 无电量通道 |
| `VT_CH584` | `vt-ch584` | 2 | VT1 SE、VT7 SE | `0xFF00:0x0F` | 被动 report `0x08` |
| `LM20RT` | `lm20rt` | 2 | VT2 RT V2、VT2 RT MASTER V2 | `0xFF00:0x02` | 被动 report `0x07` |
| `OHID` | `ohid` | 1 | ESK800-75RT | `0xFFA0:0x01` | 无电量通道（网吧专供） |
| `Telink3228C_Mouse` | `telink-3228c` | 1 | VT2 ULTRA V2 | `0xFF1C:0x92` | **需查询帧**（唯一一个） |
| `CH315` | —（无设备） | 0 | 已注册但当前没有 manifest 用它 | — | — |

**关键：每一套族在主包里都有 `handleReport`，电量全部来自被动上报。**
12 套里只有 `telink-3228c` 那一台（VT2 ULTRA V2）必须发查询帧，其余 98 台都能零写入覆盖。
`Sx`（`VT_nrf54L`）、`XE`（`LM20RT`）、`AV`（`VT_CH584`）三个族的 `parseUserCustomData` 布局逐字节相同，
只差 report ID（`7` / `7` / `8`）——所以 12 个 driver 实际上只有 **5 种帧结构**：鼠标 user-data、
`0xBB`、键盘 user-data（CH585M 与 TelinkKeyboard 各一套）、`GCOME_AP` 自有。

**已实现 5 个族**：`status-report7`（一并吃下 `lm20rt` / `vt-ch584`）、`telink-8273`、`gcome-ap`、
`telink-keyboard`、`ch585m`，覆盖 **98/109 台**。
**不实现**：`telink-3228c`（1 台，唯一要写帧的）、两个底座族（9 台，无电量或只转发子鼠标）、
`ohid`（1 台，网吧专供、无电量通道）。

> `GCOME_AP` 与 `OHID` 在主包里先后被 `Cs.register` 注册到同一个实现类（`XC`），
> 但 `OHID` 随后又被重新注册成 `cA`，所以两者行为不同，**slug 不要合并**。

### 2.1 插件结构：协议 = JSON，插件只派发

迈从把 8 套协议塞进 1136 行的 `MchosePlugin`，加一套要同时改枚举、`Classify`、`MatchesShape`、
`Probe`、`TryParsePassiveInput`、`PollDevice` 六处。雷柏有 12 套，照抄会失控，所以走**数据化**：

| 文件 | 职责 |
|---|---|
| `Plugins/Protocol/` | 品牌无关的解释器：规格加载（`extends` 继承 + 设备级 `override` 深合并 + 加载期校验）、帧读取、查询执行、认领派发 |
| `Plugins/Rapoo/specs/rapoo.protocols.json` | **12 套协议的全部数据**：接口指纹、字段偏移与类型、充电码映射、查询帧模板 |
| `Plugins/Rapoo/rapoo-catalog.json` | 195 条 VID:PID → 型号 / kind / 协议 / pairedPid；`override` 处理偏移差一位的机型（V700diy 六个 PID） |
| `RapooPlugin.cs` | 只剩认领与转交：`IsCandidate` 问解释器、`Probe` 交给解释器、被动帧交给解释器解析 |
| `RapooDisplayName.cs` | 未收录设备的显示名启发式（展示逻辑，不是协议数据，所以留代码） |

四条设计要点：

1. **派发键用 `TrackedDevice.Protocol`**（基类已存），不需要迈从那种 `KnownProtocolByPath` 旁路字典。
2. **一套协议可以覆盖多个官方 driver**：`lm20rt` / `vt-ch584` 用 `extends: status-report7`，
   因为三者帧布局逐字节相同、只差 report ID；`DeviceStatus.Protocol` 仍保留官方名。
3. **目录优先 + 形态兜底**：收录过的设备只试它那一套协议（省探测窗口、防误认），未收录的才按注册顺序逐个试。
   底座与升级口用 `implementation: "none"` 显式排除，不会每 5 秒白等一个窗口。
4. **读数与链路位分开返回**（`PassiveOutcome`）：电量越界的休眠瞬态帧没有读数，但链路位仍然可信，
   必须继续驱动离线候选（rapoo.md §3），所以不能用一个可空读数把两者绑在一起。

接口指纹必须校验形状，不能只看 PID——同型号常有旁路接口（升级口、底座），
这是迈从 `MchoseInterfaceClassifier.MatchesShape` 踩过的坑，直接沿用。写法见 `docs/dev/protocol-json.md`。

## 3. 设备清单

`PID` 列按 **`有线 / 无线`** 顺序（官方 `productId:[有线, 无线]`，代码里用 `productId === pids[1]` 判无线）。

### 鼠标（88 款）

| 型号 | PID（有线 / 无线） | 传感器 | 电池 | DPI 上限 | 回报率档位 | 协议族 |
|---|---|---|---|---|---|---|
| ESM612ACE | `0x464d` | PAW3955U | 800 mAh | 45000 | 1000/2000/4000/8000 | `status-report7` |
| ESM612MAX | `0x4681` | PAW3950U | 800 mAh | 45000 | 1000/2000/4000/8000 | `status-report7` |
| ESM612PRO | `0x4631` | PAW3398 | 800 mAh | 26000 | 1000/2000/4000/8000 | `status-report7` |
| ESM622MAX | `0x4637` | PAW3950U | 800 mAh | 45000 | 1000/2000/4000/8000 | `status-report7` |
| ESM622PRO | `0x4632` | PAW3398 | 800 mAh | 26000 | 1000/2000/4000/8000 | `status-report7` |
| ESM652PRO | `0x4633` | PAW3398 | 800 mAh | 26000 | 1000/2000/4000/8000 | `status-report7` |
| ESM672MAX | `0x4640` | PAW3950U | 800 mAh | 45000 | 1000/2000/4000/8000 | `status-report7` |
| ESM672PRO | `0x4635` | PAW3398 | 800 mAh | 26000 | 1000/2000/4000/8000 | `status-report7` |
| MT550L | `0x4506` / `0x1874` | — | — | 4000 | — | `telink-8273` |
| MT560 | `0x4504` / `0x1872` | — | — | 4000 | — | `telink-8273` |
| MT750 PLUS | `0x4507` / `0x1876` | — | — | 4000 | — | `telink-8273` |
| MT760L | `0x4500` / `0x1870` | — | — | 4000 | — | `telink-8273` |
| MT760M | `0x4503` / `0x1871` | — | — | 4000 | — | `telink-8273` |
| MT770L | `0x4c00` / `0x1c00` | — | — | 4000 | — | `telink-8273` |
| VT0 | `0x4610` / `0x1410` | PAW3398 | 800 mAh | 26000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT0 Air MAX | `0x46a1` / `0x14a1` | PAW3950U | 300 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT0 MAX | `0x4620` / `0x1420` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT0 MAX MASTER | `0x464a` / `0x144a` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT1 | `0x4615` / `0x1415` | PAW3398 | 800 mAh | 26000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT1 MAX | `0x4644` / `0x1444` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT1 SE | `0x4a11` / `0x1a11` | PAW3311 | 300 mAh | 12000 | 125/250/500/1000 | `vt-ch584` |
| VT1S MAX V2 | `0x4b0a` / `0x1b0a` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT1S V2 | `0x466a` / `0x146a` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2 | `0x4622` / `0x1422` | PAW3398 | 800 mAh | 26000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2 Air MAX V2 | `0x4b12` / `0x1b12` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2 MAX | `0x4623` / `0x1423` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2 MAX MASTER V2 | `0x4675` / `0x1475` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2 MAX V2 | `0x4b09` / `0x1b09` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2 RT MASTER V2 | `0x4b21` / `0x1b21` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `lm20rt` |
| VT2 RT V2 | `0x4b20` / `0x1b20` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `lm20rt` |
| VT2 ULTRA V2 | `0x4672` / `0x1472` | PAW3950U | 800 mAh | 40000 | 125/250/500/1000/2000/4000/8000 | `telink-3228c` |
| VT2 V2 | `0x4668` / `0x1468` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2K MAX | `0x46a4` / `0x14a4` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2S Air MAX V2 | `0x4b13` / `0x1b13` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2S MAX MASTER V2 | `0x4676` / `0x1476` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2S MAX V2 | `0x4b04` / `0x1b04` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT2S V2 | `0x465d` / `0x145d` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3 | `0x4612` / `0x1412` | PAW3398 | 800 mAh | 26000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3 Air MAX V2 | `0x4b0c` / `0x1b0c` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3 MAX | `0x4617` / `0x1417` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3 MAX MASTER V2 | `0x466d` / `0x146d` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3 MAX V2 | `0x4b06` / `0x1b06` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3 PRO V2 | `0x46a5` / `0x14a5` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3 V2 | `0x4662` / `0x1462` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S | `0x4611` / `0x1411` | PAW3398 | 800 mAh | 26000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S Air MAX V2 | `0x4b0d` / `0x1b0d` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S Air Max | `0x46a2` / `0x14a2` | PAW3950U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S MAX | `0x4621` / `0x1421` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S MAX MASTER | `0x464b` / `0x144b` | PAW3950U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S MAX MASTER V2 | `0x466e` / `0x146e` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S MAX V2 | `0x4b07` / `0x1b07` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S PRO V2 | `0x46a6` / `0x14a6` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT3S V2 | `0x4664` / `0x1464` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT4 Air MAX V2 | `0x4b14` / `0x1b14` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT4 MAX MASTER V2 | `0x4677` / `0x1477` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT4 MAX V2 | `0x4b03` / `0x1b03` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT4 V2 | `0x465b` / `0x145b` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT6 | `0x4651` / `0x1451` | PAW3398 | 800 mAh | 26000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT6 MAX | `0x4652` / `0x1452` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7 | `0x4613` / `0x1413` | PAW3398 | 800 mAh | 26000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7 Air MAX V2 | `0x4b0e` / `0x1b0e` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7 MAX | `0x4616` / `0x1416` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7 MAX MASTER V2 | `0x4670` / `0x1470` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7 MAX V2 | `0x4b05` / `0x1b05` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7 PRO V2 | `0x46a7` / `0x14a7` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7 SE | `0x4a00` / `0x1a00` | PAW3311 | 300 mAh | 12000 | 125/250/500/1000 | `vt-ch584` |
| VT7 V2 | `0x4660` / `0x1460` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7S Air MAX V2 | `0x4b0f` / `0x1b0f` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7S MAX MASTER V2 | `0x4671` / `0x1471` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7S MAX V2 | `0x4b02` / `0x1b02` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7S PRO V2 | `0x46a8` / `0x14a8` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT7S V2 | `0x4659` / `0x1459` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT8 Air MAX V2 | `0x4b15` / `0x1b15` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT8 MAX MASTER V2 | `0x4678` / `0x1478` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT8 MAX V2 | `0x4b0b` / `0x1b0b` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT8 V2 | `0x466c` / `0x146c` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9 | `0x4614` / `0x1414` | PAW3398 | 800 mAh | 26000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9 Air MAX V2 | `0x4b10` / `0x1b10` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9 MAX | `0x4618` / `0x1418` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9 MAX MASTER V2 | `0x4673` / `0x1473` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9 MAX V2 | `0x4b08` / `0x1b08` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9 PRO V2 | `0x46a9` / `0x14a9` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9 V2 | `0x4666` / `0x1466` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9S Air MAX V2 | `0x4b11` / `0x1b11` | PAW3955U | 300 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9S MAX MASTER V2 | `0x4674` / `0x1474` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9S MAX V2 | `0x4b01` / `0x1b01` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9S PRO V2 | `0x46aa` / `0x14aa` | PAW3955U | 800 mAh | 60000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |
| VT9S V2 | `0x4653` / `0x1453` | PAW3950U | 800 mAh | 45000 | 125/250/500/1000/2000/4000/8000 | `status-report7` |

### 键盘与底座（21 款）

| 型号 | PID | 类型 | 协议族 |
|---|---|---|---|
| A83 Air | `0x4814` / `0x1914` | keyboard | `telink-keyboard` |
| Ace 68 Turbo | `0x3026` | keyboard | `gcome-ap` |
| ESK800-75RT | `0x5745` | keyboard | `ohid` |
| ESK900-75RT | `0x482a` | keyboard | `ch585m` |
| ESK900-83RT | `0x481b` | keyboard | `ch585m` |
| ESK900-98RT | `0x481a` | keyboard | `ch585m` |
| ESPR5 | `0x1445` | base | `status-report7-base` |
| ESPR7 | `0x1458` | base | `status-report7-base` |
| ESPR7PRO | `0x1450` | base | `status-report7-base` |
| ESPR8 | `0x144e` | base | `status-report7-base` |
| ESPR8PRO | `0x1441` | base | `status-report7-base` |
| P5 PLUS | `0x4827` | base | `telink-base` |
| P6 PLUS | `0x4825` | base | `telink-base` |
| V700diy-75 | `0x4807` / `0x1907` | keyboard | `telink-keyboard` |
| V700diy-75 LRV | `0x6204` / `0x7204` | keyboard | `telink-keyboard` |
| V700diy-87 | `0x4806` / `0x1906` | keyboard | `telink-keyboard` |
| V700diy-98 LRV | `0x4824` / `0x1924` | keyboard | `telink-keyboard` |
| V700diy-98 | `0x4805` / `0x1905` | keyboard | `telink-keyboard` |
| VPR8 PLUS | `0x144d` | base | `status-report7-base` |
| VPR8 RT MASTER | `0x145a` | base | `status-report7-base` |
| VT68 | `0x4832` | keyboard | `gcome-ap` |

## 4. DPI 上限与回报率档位

DPI 上限来自 `capabilities` 里的 `dpi` 项，四个具名常量 + 内联对象：

| 形式 | min | max | 出现在 |
|---|---|---|---|
| `$1` | 1 | 60000 | VT1S MAX V2 / VT2·VT3 MAX V2 / PRO V2 / RT 系列 |
| `xt` | 10 | 45000 | VT0/VT1/VT2 MAX、VT2S V2 等 PAW3950U 机型 |
| `On` | 50 | 26000 | VT0/VT1/VT2/VT3/VT6/VT7/VT9 基础款、ESM6x2PRO |
| `ca` | 50 | 4000 | MT 系列（办公鼠标） |
| 内联 | 50 | 12000 | VT1 SE / VT7 SE |

**回报率档位**按 `t1({tier:...})` 分三档：

| tier | 可选档位 |
|---|---|
| `retail` | 125 / 250 / 500 / 1000 / 2000 / 4000 / 8000 Hz |
| `cybercafe` | 1000 / 2000 / 4000 / 8000 Hz |
| `paw3311` | 125 / 250 / 500 / 1000 Hz |

心跳里的 `[11]` 直接是 **Hz 值**（`8`=125、`4`=250、`2`=500、`1`=1000、`132`=2000、`130`=4000、`129`=8000），
不是索引。EEPROM 里存的也是同一个值（`VT_nrf54L` 的 `getReportRate()` 不解码直返），
只有 Telink AP 族（`KP`/`GP` 表）才用 0..6 的索引。见 [rapoo.md](../brands/rapoo.md) §1。

## 5. 图标与配色 URL

`ui.assetsDir` 是相对路径，拼到资源根 `https://hid.rapoo.cn/AHUB_WEB/dist/` 上：

| 文件 | 用途 | 已验证 |
|---|---|---|
| `<assetsDir>/thumb_<colorKey>.png` | 设备卡片缩略图 | ✅ 200，75~130 KB |
| `<assetsDir>/func_<colorKey>.png` | 功能页大图 | ✅ 200，218 KB |
| `<assetsDir>/rt_<colorKey>.png` | RT 页图 | 未验证 |
| `<assetsDir>/frame-dark.png` / `frame-light.png` | 键位布局底框 | 未验证（VT3 目录下 404） |

`<colorKey>` 取 `ui.colors.series[].key`（如 `VT3` 的 `Raspberry_Pink` / `Black` / `White`）。
**路径含空格时必须转义**（`manifests/VT2SAir%20MAX%20V2/thumb_Morning_Mist_Blue.png`）。
配色色值在 `colors.series[].value`，个别是渐变色（数组，如 `Metallic_Iridescent`）。

## 6. 能力：旧 `caps` 与新 `capabilities`

目录里**两个字段并存**，语义完全不同，别混：

| 字段 | 是什么 | 谁读它 |
|---|---|---|
| `caps` | 2026-11 建目录时的**历史验证记录**（`["battery"]` / `["battery","dpi"]`），只写本机真机插过机的能力 | **代码不读**，纯档案 |
| `capabilities` | PR-C 起的**正式能力声明**，三态、按型号逐项写，见 [dev/protocol-json.md](../../dev/protocol-json.md) §2.1 | `ProtocolSpecLoader` → `DeviceEntry.Capabilities` |

两条规则：

1. **旧 `caps` 全部保留，不删空数组，也不自动映射成 `capabilities`。**
   `caps` 里的 `dpi` 分不出新的 `dpiRead` 与 `dpiLevelRead`，自动映射只能靠猜；
   而 `[]` 是"没验证"，不是"不支持"——映射成 `unsupported` 会造出假结论。
2. **只有 `capabilities` 参与判断。** 以后新增型号直接写 `capabilities`，`caps` 不再追加。

### 6.1 旧 `caps` 记录（历史，勿据此推断能力）

`caps` **只写真机验证过的能力**，官方声明但没插机读过的**一律不写**：

| PID | 型号 | caps | 依据 |
|---|---|---|---|
| `0x1412` | VT3（2.4G） | `["battery","dpi"]` | 2026-11-01 本机实测，见 [rapoo.md](../brands/rapoo.md) §1.1 |
| `0x1B13` | VT2S Air MAX V2（2.4G） | `["battery"]` | rapoo.md §5 实测表 |
| `0x4B13` | VT2S Air MAX V2（有线） | `["battery"]` | rapoo.md §5 实测表 |

其余 192 条 `caps` 为空数组。**不是「不支持」，是「没验证」**——官方 manifest 对 88 只鼠标
全部声明了 `dpi` 与 `mousePerf` 能力，但只有 VT3 真的读过档位变化。

`rate` 至今没有一台填：VT3 读到 `[11]=1`（→1000Hz，在合法域内），但改回报率必须开官方 HUB，
会与本应用抢 dongle 会话（[safety.md](../safety.md) §2），所以「读到合法值」不算「验证过」。

### 6.2 新 `capabilities` 声明（当前唯一有效来源）

| PID | 型号 | 声明 | 依据 |
|---|---|---|---|
| `0x1412` | VT3（2.4G） | `batteryRead` / `dpiRead` / `dpiLevelRead` = supported；`rateRead` = **unverified** | 真机电量 + `[2]` 档位 + `[3..6]` DPI（[rapoo.md](../brands/rapoo.md) §1.1）；回报率证据不足，见下条 |
| `0x1B13` | VT2S Air MAX V2（2.4G） | `batteryRead` = supported | rapoo.md §5 实测表 |
| `0x4B13` | VT2S Air MAX V2（有线） | `batteryRead` = supported | rapoo.md §5 实测表 |

- **VT3 的 `rateRead` 是 `unverified`，不是 `supported`**（2026-11-04 复核降级）：
  `[11]` 只有**单点观测**——2026-11-01 那次采集读到 `1`（→1000Hz），而当时设备的回报率设置本来就是 1000Hz，
  没有对照组。**尚未直接验证**「在设备上改变回报率设置后 `[11]` 是否随之变化」。
  `[17]`（`sensorMode`=`firepower`）只印证帧偏移对齐（1000Hz 下允许的模式集含 `2`），
  **不能**当成回报率的间接验证。
  升回 `supported` 需要一次对照实验：改变设备回报率设置 → 观察并确认 `[11]` 随设置变化
  （至少两个点，其中一个是非 1000Hz 档位）。注意这只是**证据等级**问题，
  不代表 `[11]` 的偏移与码表有问题——那部分仍是真机逐字节校准过的。
- **`dpiWrite` / `rateWrite` 一条都没写**：本应用没有任何写入实现，写能力全是 `unverified`。
  不是 `unsupported`——没做不等于确认设备不支持。
- **VT3 有线 `0x4612` 没声明**：它与 `0x1412` 是同一只鼠标的两条枚举，但 `caps` 为空，
  没有独立真机记录，按"没有证据"处理（`unverified`）。要用就单独插线验一次再补。
- 其余 192 条设备一律不填：不按共享协议、不按品牌、不按厂商宣传推断能力。

## 7. 有线 / 无线是同一个设备的两条 PID

官方 manifest 的 `productId` 是二元数组，**`[0]` 有线、`[1]` 无线（接收器）**。判定逻辑是
`productId === pids[1]` → 无线，或产品串含 `Receiver`/`Dongle`。

目录里**两条 PID 都收**，并互相记 `pairedPid`，这样上层既能按 PID 认领、也能把同一只鼠标的两条
记录合并。本机实测印证：VT2S Air MAX V2 有线 `4B13` / 无线 `1B13` 序列号相同（[rapoo.md](../brands/rapoo.md) §4.3）。

底座（`kind: "base"`）是独立设备，只有一条 PID，不参与配对。

## 8. 未验证边界（别当成已知）

1. **除 §6 三条外全部是静态分析**。88 只鼠标里 87 只没插过机。
2. **`kind` 的取值多了个 `base`**：目标结构只列了 `keyboard`/`mouse`/`headset`/`gamepad`，
   但雷柏有 9 台底座/接收器（P5 PLUS、ESPR 系列），归到哪一类都会失真，故作扩展值。合并总表时要拍板。
3. **`protocol` 的 slug 是本项目新起的**（除 `status-report7` 沿用 rapoo.md 的旧称）。
   12 套里已实现 5 套（§2），覆盖 98 台；其余按 §2 末段的理由不实现。
4. **新族的 `MatchesShape` 长度下限是估的**：`0xBB`、两套键盘、`GCOME_AP` 的 HID 报告长度只能从帧结构反推
   （`>=7` / `>=10` / `>=6`），没有真机描述符可核。若某台设备认不上来，先怀疑这里，
   用 `scripts/probe-hid-caps.py 24AE` 打一份真实接口形状再收紧。
5. **`telink-8273` 的电量不走 `0x07`**：它用 `0xBB` 上报（载荷 `[0]==0xB0`，`[5]` 电量、`[2..3]` CPI），
   官方代码层面已确认是纯被动；本机无 MT 系列设备，**未真机验证**。
6. **键盘族的偏移只经过合成帧验证**：`RapooKeyboardCodec` / `RapooGcomeApCodec` 的偏移是从官方解析器
   反推的，单测用合成帧锁住，**没有一份真机帧**。错一位时靠"电量必须落在 1..100"这道闸挡住脏值，
   代价是可能整族读不出电量而不是读错。
7. **ESM6xx 的 cybercafe 变体没处理**：官方 `Zx()` 会先剥掉一个 `0xB0` 前缀标记，现有
   解释器（`specs/rapoo.protocols.json` 的 `status-report7.passive`）只在偏移 0/1 找 report ID，8 台网吧专供机可能错位。
8. **区域差异**：17 台标 `china_cybercafe` 专供（ESM6xx 系列），5 台 MT 系列型号名随区域变。
   目录里统一用**国内零售名**。
9. **主包会变**：文件名带 hash（`index-BNoOd8Jm.js`），发版即换。重新提取要重新抓 HTML 找入口。
