# BETOP 厂商级支持 · 第一批（协议 JSON 数据化）设计说明

> 状态：**已实现（2026-11-18）**。验证结果见最终报告；实现与本文的差异已回写。
> 基线：`dev` / `4101eab`（P1-B1B）。工作区已有的产品图与 `BetopPlugin.cs` 未提交改动与本批一起保留/提交
> （用户 2026-11-18 确认），实现时在其上继续改，不回退。
> 已确认：上一轮未提交改动与本批放在一起；宙斯只登记实际有效的 9 个 PID；20DD role 按官方映射；
> `0x10` 按表判离线；不保留 `betop-status15-low4` 别名。
> 宙斯 <100% 充电态保留旧口径 `Discharging`（用户确认，由 `chargeWhenMissing: "discharging"` 表达）。
> 通用字段只加 `requirePercent` + `chargeWhenMissing`；不新增
> `fullAtOnlyWhenNotCharging` / `pollListenMs` / `pollWindowMs`，满电收尾与轮询窗口放插件层。

## 0. 结论

- 运行目录从 2 套 `implementation=code` 协议改为 3 套 `implementation=data`：
  `betop-status15`、`betop-status15-nocharge`、`betop-zeus-keyevent`。
- 帧偏移、掩码、电量等级表、充电语义、链路表、查询模板全部进 JSON；`BetopCodec` 删除。
- 目录从 75 条 / 51 PID 扩到 **110 条 / 86 个 `VID:PID`**：
  50 个鲲鹏 PID 按官方充电证据分成 24 `low4` + 26 `nocharge`；9 个宙斯 PID 入目录；回填 26 个 `20DD` 身份。
- 能力声明不变：只有 `5158`/`5159` 有 `chargingRead=supported`，`5073` 有 `batteryRead=supported`；
  其余一律 `Unverified`。解析器能读不等于能力已验。
- 行为目标：广播探测 3s + 查询 2s、轮询先听 1.5s + 查询 1.5s、T9 500ms 稳定判定、占位帧离线，全部保持。
- 宙斯族只被动听，不再向 `0xFF00` 写 `0x15`。

## 1. 基线核对与两个新发现

基线数字与任务一致：`devices[]` 75 条（51 PID：50 鲲鹏 + 5073）、`officialDevices[]` 66 PID / 118 身份、
协议 2 套。官方脚本已从 `resources\app\20261111.asar` 重新抽取，只读，未改安装目录。

复抽时发现两件任务基线没写的事，需要一并裁决：

1. **`t9v2.js` 的 `extkeyevent1` 不是电量位域。**
   `t9v2.js` 注册的是 `0x20BC:0x5084`（本体）/ `0x5085`（dongle），这两个 PID 不在官方 66 个 PID 里；
   `extkeyevent1` 只在 `cmd 0x0E` 里读 `btn_fn` / `btn_sensor` / `btn_profile` / `btn_panel1` / `btn_panel2`，
  是按键字段，不是 `extkeyevent` 的 `batlevel`。所以本批 10 个宙斯 PID 不需要为它单开协议，
   `0x5084` / `0x5085` 也不登记。
2. **`0x501E` 与 `0x502C` 的注册调用在当前包内是注释掉的。**
   `t9_app.js:3305-3307` 的 `0x501E`、`t6p2a.js:3961-3963` 的 `0x502C` 都以 `//` 开头；
   当前实际有效的宙斯注册是 `0x500B`、`0x502F`、`0x5073`、`0x5074`、`0x5076`、`0x5077`、`0x5104`~`0x5107`。
   `officialDevices[]` 仍按 66 PID 收录了这两个 PID。已确认：本批**只登记当前实际有效的 9 个**，
   `0x501E` / `0x502C` 留在 `officialDevices[]` 的 `CatalogOnly`，另记 CR 等下一版官方客户端再补。

## 2. 通用 schema 扩展

### 2.1 `percent.map`

`PercentSpec` 增加 `Dictionary<int,int>? Map`。JSON 两种写法都收：

```jsonc
"percent": { "offset": 1, "mask": 7, "map": [0, 50, 80, 100, 100] }
"percent": { "offset": 1, "mask": 7, "map": { "0": 0, "1": 50, "2": 80, "3": 100, "4": 100 } }
```

读取顺序：

1. `raw = data[off + offset] & mask`；
2. `Map` 非空时必须命中，未命中 `percent=null`（不拿 0 冒充）；
3. 命中值再按 `validPercent.min/max` 校验。

数组写法是任务给的示例，`Dictionary` 是任务给的字段类型；两者语义一致，数组只是稠密表的简写。
加载期把数组转成 `0..n-1 → value` 的字典，对象写法按 `ParseKey` 解析键（支持 `0x50`）。

**注意**：任务示例写的是帧级 `"minPercent": 0`，但现有 schema 没有这个字段，只有
`"validPercent": { "min": 0 }`。未知字段会被静默忽略，写成 `minPercent` 会让 0% 被默认下限 1 丢掉。
本设计统一用 `validPercent`，不新增 `minPercent` 别名。

### 2.2 `link.table`

`LinkSpec` 增加 `Mask`（默认 `0xFF`）、`Dictionary<int, LinkState>? Table`、`LinkState OnMiss`（默认 `Unknown`）。

```jsonc
"link": { "offset": 23, "table": { "0x50": "down", "0x10": "down" }, "onMiss": "up" }
```

读取顺序：

1. `Table` 非空：读 `data[off + offset] & mask`，命中返回值，未命中返回 `OnMiss`；
2. `Table` 为空：走现有 `bit` / `connectedWhenSet` 逻辑；
3. `onlyWhenMode` 仍然先判，语义不变。

`Table` 存在时忽略 `bit`；加载期校验键在 `0..255`、值只能是 `up` / `down` / `unknown`。

### 2.3 查询模板的对象写法

保留现有固定写法（`length` + `hex` + `pad`），新增可选对象写法，两者加载期二选一：

```jsonc
"query": {
  "header": "15",
  "reportIdFromCaps": true,
  "padToInterfaceOutputLength": true,
  "fallbackLength": 64
}
```

运行时：

- `rid = handle.GetOutputReportId()`；`null` → 不发帧，`quiet` 返回 null；
- `0` → 无 ID 前缀，首字节就是 `header`；
- `>0` → `[rid, header, 0…]`；
- 长度取 `Info.OutputReportLength`；`< 2` 时用 `fallbackLength`；
- 写超时 500ms，失败 `quiet`；
- `header` 是 1 字节十六进制串，本批固定 `"15"`。

`ActiveSpec.Query` 从 `byte[]` 改为一个小联合类型（固定字节 / 动态模板）。
加载期规则：固定写法必须有 `length > 0`；动态写法必须有 `header` + `reportIdFromCaps: true`，
且不能同时出现 `hex` / `length` / `pad`。雷柏、迈从继续用固定写法，JSON 一行不改。
`rejectQueryEcho` 改为与本次实际写出的帧比较（动态模板每次可能不同），不再直接引用 `spec.Query`。
动态写法不参与现有 `outputLength.eq` 的查询长度上界校验——长度要到运行时才知道。

单测直接测纯函数 `QueryFrame.Build(template, rid, outputLength)`：有 ID / 无 ID / `rid=null` /
长度不足回退 / 写失败（用只读 `FileStream` 构造 `HidDeviceHandle`，`WriteOutput` 返回 false）。

### 2.4 两个通用字段；其余行为放插件层

| 字段 | 默认 | 作用 | 为什么留 |
|---|---|---|---|
| `requirePercent`（帧级） | `false` | `true` 时电量越界 / 为 0 的帧直接判“不是有效状态帧” | 通用：任何“只报状态”的帧都能用；不加会让 0% 占位帧被当成有效探测结果，探测提前认领 |
| `chargeWhenMissing`（帧级） | `unknown` | 协议没有 `charge` 字段时返回什么；`discharging` / `unknown` / `charging` / `full` | 通用：把“没有充电位时默认算什么”变成数据，而不是插件里的 `passive-only → Discharging` 硬编码；宙斯用 `discharging`，`status15-nocharge` 保持默认 `unknown` |

以下两个行为**不新增通用字段**，放在 `BetopPlugin`：

- 满电收尾：100% + 未充电 → `Full`；100% + 充电中 → `Charging`。
  宙斯 <100% 的 `Discharging` 已由 `chargeWhenMissing` 表达，不再写成插件规则。
- 轮询窗口：广播轮询“先听 1.5s、再查 1.5s”写在插件里；探测查询窗口仍用 JSON 的 `windowMs: 2000`。

理由：`fullAtOnlyWhenNotCharging` 和 `pollListenMs` / `pollWindowMs` 目前只有北通用，通用性低；
任务 §3 也把“听/查编排”划在代码侧，不值得为一个插件扩通用 schema。
`chargeWhenMissing` 不同：它替代的是一条会误伤未来 passive-only 设备的代码规则，且任何“没有充电位”的协议都能用。

## 3. 协议拆分

### 3.1 `betop-status15`（基础）

```jsonc
{
  "kind": "gamepad",
  "implementation": "data",
  "pollable": true,
  "probeWindowMs": 3000,
  "interface": {
    "inputLength": 64,
    "anyOf": [
      { "usagePage": 255, "usage": 3 },
      { "usagePage": 3, "usage": 255 }
    ]
  },
  "passive": {
    "reportId": [2],
    "tryOffsets": [0, 1],
    "length": { "min": 64 },
    "prefix": [ { "offset": 1, "value": 21 } ],
    "requirePercent": true,
    "fields": {
      "percent": { "offset": 2 },
      "charge":  { "offset": 4, "mask": 15, "nonZero": true }
    }
  },
  "active": {
    "query": { "header": "15", "reportIdFromCaps": true,
               "padToInterfaceOutputLength": true, "fallbackLength": 64 },
    "windowMs": 2000,
    "attempts": 1,
    "response": {
      "reportId": [2],
      "tryOffsets": [0, 1],
      "length": { "min": 64 },
      "prefix": [ { "offset": 1, "value": 21 } ],
      "requirePercent": true,
      "fields": {
        "percent": { "offset": 2 },
        "charge":  { "offset": 4, "mask": 15, "nonZero": true }
      }
    }
  }
}
```

`passive` 与 `active.response` 是同一套帧布局，必须各写一份：`ProbeAsync` 的“先听”只认 `passive`；
轮询的“先听”由插件自己的 Listen 循环调 `_engine.Parse` 完成；查询应答只认 `active.response`。
只写 `active` 会让广播探测直接跳过 3s 纯听、一上来就写 `0x15`，与现有行为和“广播零写入”都冲突。
`prefix` 必须保留：同一 report ID 2 下还有 `0x25` 按键/摇杆报告，不能把非状态帧当电量。

`anyOf` 保留 `BetopCodec.IsVendorInterface` 的“page/usage 可能被扫描器读颠倒”兼容；
不写 `anyOf` 会丢掉这条既有行为。

### 3.2 充电变体

```jsonc
"betop-status15-nocharge": {
  "extends": "betop-status15",
  "passive": { "fields": { "charge": null } },
  "active": { "response": { "fields": { "charge": null } } }
}
```

基础协议 `betop-status15` 本身就是 `low4`（`mask: 15`），不再单独设 `betop-status15-low4` 别名；
`low4` 的 24 个 PID 直接指向基础协议。
`nocharge` 用 `null` 同时覆盖 `passive` 与 `active.response` 里继承来的 `charge`，
加载器按“字段缺失”处理。只盖 `active` 会让广播帧仍然解析充电，这是错的。
满电收尾由插件统一处理：`nocharge` 的 100% 仍判 `Full`，`<100%` 保持 `Unknown`
（与现有“100% = 充满”的通用口径一致）。
`nocharge` 不写 `chargeWhenMissing`，走默认 `unknown`；宙斯才写 `discharging`。

逐 PID：

| 变体 | PID（均为运行目录已有的 20BC） | 数量 |
|---|---|---|
| `betop-status15`（low4） | 512f, 5130, 5158, 5159, 515b, 515c, 515f, 5160, 516d, 516e, 516f, 5170, 5173, 5174, 518a, 518b, 518c, 518d, 5190, 5191, 519b, 519c, 519d, 519e | 24 |
| `betop-status15-nocharge` | 5125, 5126, 5127, 5128（`none-documented`）；5145, 5146（`mixed`）；5133, 5134, 5149, 514a, 5150~5153, 5154, 5155, 515d, 515e, 5163~5166, 5169, 516a, 5194, 5195（`unknown`） | 26 |

`5161` / `5162`（盘古）官方也是 `low4`，但它们不在运行目录（`0xFF80` 页，本批不做），不列入。

### 3.3 `betop-zeus-keyevent`

```jsonc
{
  "kind": "gamepad",
  "implementation": "data",
  "pollable": false,
  "probeWindowMs": 2000,
  "interface": {
    "inputLength": 24,
    "anyOf": [
      { "usagePage": 65280, "usage": [1, 2, 3] },
      { "usagePage": [1, 2, 3], "usage": 65280 }
    ]
  },
  "passive": {
    "reportId": [4],
    "length": { "min": 24 },
    "validPercent": { "min": 0 },
    "chargeWhenMissing": "discharging",
    "fields": {
      "percent": { "offset": 1, "mask": 7, "map": [0, 50, 80, 100, 100] },
      "link":    { "offset": 23,
                   "table": { "0x50": "down", "0x10": "down" },
                   "onMiss": "up" }
    }
  }
}
```

- 不声明 `active`：宙斯族只被动听，不写 `0x15`。
- `probeWindowMs: 2000` 对齐现有 T9 探测的 2s 窗口；不写会落到默认 5s。
- `validPercent.min: 0` 是必需的：`map[0] = 0`，默认下限 1 会把 0% 丢掉。
- 协议本身不声明充电字段；`chargeWhenMissing: "discharging"` 表达旧口径（用户确认）。
  `100%` 的 `Full` 仍由插件 `FinalizeCharging` 收尾；JSON 里不写一个假的充电位或满电阈值。
- 命名：`betop-t9` **改名为** `betop-zeus-keyevent`，不保留别名。理由：协议现在覆盖 10 个 PID，不再只是 T9；
  协议名不持久化（历史库只存设备名/电量/充电/时间），没有兼容负担；同一接口挂两个协议名会让 `Resolve` 出现两个候选。
- 10 个 PID：5073（已登记）+ 500b, 502f, 5074, 5076, 5077, 5104, 5105, 5106, 5107。
  `5074` 官方是多型号（宙斯 Pro / 宙斯2），`model` 留空；其余按官方标题去「北通」。
  `501e` / `502c` 不登记（注册证据见 §1 第 2 条）。

## 4. 插件重构

### 4.1 `BetopPlugin`

保留：

- `IsCandidate`：`_engine.Resolve(info)` 命中后，若目录未登记且协议只有 `passive`、没有 `active`，
  只识别不探测（替换现有 `IsUnregisteredZeus` 的硬编码页/长度判据）。
- 探测的“先听 3s、再查 2s”由 `ProtocolEngine.ProbeAsync` 执行；
  轮询的“先听 1.5s、再查 1.5s”留在插件（任务 §3 把听/查编排划在代码侧，不为此扩通用 schema）。
- 插件保留 T9 稳定判定（`IsSettled`）、写开关（`CanWrite`）、显示名/身份归一。

改为：

- `ProbeAsync`：`_engine.ProbeAsync(handle, WritesEnabled)`；用新增的
  `_engine.ProbeLinkOf(info)` 取探测帧的链路；`Down` → 返回 `(protocol, null, Unknown)`
  （认领但离线，保持现有 T9 占位帧语义）；电量/充电走 `FinalizeCharging` 收尾。
- `PollDevice`：`pollable=true` 的协议先由插件 Listen 循环（1.5s）调 `_engine.Parse`；
  读到帧就按 `FrameReading` 处理（`Link=Down` → `RequestOffline`，电量/充电走 `FinalizeCharging`），
  读不到且 `CanWrite` 才
  `_engine.Poll(..., windowMs: 1500)`。宙斯 `pollable=false`，不轮询。
- `TryParsePassiveInput`：`_engine.Parse(RefOf(dev), dev.Protocol, input)`；
  `Link == Down` → `Offline: true`；`Percent == null` → 保留旧读数、不标离线；
  只有 passive-only 协议（宙斯）走 `IsSettled`，status15 广播帧不套 500ms 稳定判定
  （用 `_engine.SpecOf(dev.Protocol)?.Active is null` 判，不在代码里写协议名）。
  充电缺失时的默认值由 JSON 的 `chargeWhenMissing` 决定（宙斯 `discharging`，nocharge `unknown`），
  插件不再按“passive-only”猜。
  `FinalizeCharging` 只做满电收尾：100% + 未充电 → `Full`；100% + 充电中 → `Charging`。
- 删除 `Query` / `Listen` / `ResolvePlan` / 旧 `MapCharging` / `QueryPlan` / `PlanByPath`。
- `ProtocolEngine.Listen` 做成内部同步/异步共用入口；`QueryRunner.Run` 增加可选 `windowMs` 参数
  （不写进 JSON）。`ProbeLinkOf` 与现有 `ProbeConnectionOf` 同风格，按路径暂存。
- 原始帧日志不降级：被动路径保留“前几帧 + 状态变化”日志，探测/查询路径保留
  `北通 状态报告: 电量=… 充电=… 原始=…` 一行；日志格式沿用 `Log.Hex`。

### 4.2 `BetopCodec`

删除整个文件。仍被非解析代码使用的只有厂商判断：

- 用 `ProtocolEngine.IsVendor(ushort)`（新增一行，读 JSON 的 `vendors`）替换 `BetopCodec.IsBetopVendor`；
- `BetopPlugin.BetopVid` / `BetopVidNew` 常量删除（无外部引用）；
- `CleanProductName` / `EncodeHeader` / `DecodeHeader` / `BuildQuery*` 只被测试或旧解析路径使用，随文件删除；
  对应 `BetopCodecTests` 的 C1~C3、P1~P3 用例删除，其余解析用例改走 `ProtocolEngine.Parse`。
- `ProbeInferredConnection` 仍需要“宙斯通道 = 2.4G”的推断，保留在插件里；这是链路推断，不是帧解析，
  本批不改成 JSON（任务已把连接方式推断列为上一轮完成项）。

`src/tests/VigilWatt.Core.Tests/VigilWatt.Core.Tests.csproj` 删除 `BetopCodec.cs` 的 `<Compile Include>`。

### 4.3 行为对照

| 场景 | 现在 | 本设计 |
|---|---|---|
| 广播探测 | 听 3s → 查 2s | 同 |
| 有线/静默探测 | 听 3s → 查 2s | 同 |
| 宙斯探测 | 写 `0x15` 后听 2s | 纯听 2s，零写入 |
| 广播轮询 | 听 1.5s → 查 1.5s | 同 |
| 宙斯轮询 | 写 `0x15` 后听 1.5s | 不轮询（`pollable=false`），被动监听实时更新 |
| T9 稳定判定 | 500ms 同值 | 同 |
| T9 占位帧 | `Linked=false` → 认领但离线 / `RequestOffline` | `Link=Down` → 同 |
| status15 0% / 101% | `ParseStatus` 返回 null，继续等 | `requirePercent` → 同 |
| 100% + 充电位 | `Charging` | 插件 `FinalizeCharging` → 同 |
| 宙斯 <100% 充电态 | `Discharging`（`FromChargingFlag(false, p)`） | `Discharging`（`chargeWhenMissing: "discharging"`；JSON 不写假充电位） |

最后一行是用户确认的**产品口径**：宙斯没有充电位，但界面继续按“放电中”显示，避免因为充电状态未知
把放电状态也一起改掉。`status15-nocharge` 不受这条影响，仍然返回 `Unknown`。
满电收尾与轮询窗口都在插件层，不新增 `fullAtOnlyWhenNotCharging` / `pollListenMs` / `pollWindowMs`。

## 5. 目录迁移

### 5.1 鲲鹏 50 PID

- 24 个 `low4` → `betop-status15`（基础协议本身就是 low4）；
- 26 个 `nocharge` → `betop-status15-nocharge`；
- 只改 `protocol` 字段，`model` / `kind` / `role` / `group` / `pairedPid` / `capabilities` / `note` 不动。
- 现有 3 条 `20DD`（5128 / 5130 / 515c）**保持 `betop-status15` 不动**（任务要求原 33 条不改字段）。
  它们只是第二身份，`20DD:5128` 只回 `02 CD`、不会走 status15 解析；新增的 26 条才按 20BC 兄弟的变体写。
- 原有 33 条逐字段冻结测试继续保留；快照只把其中 30 条 20BC 的 `protocol` 改成新值
  （29 条鲲鹏换 low4/nocharge，5073 换 `betop-zeus-keyevent`），其余字段（含 3 条 20DD）一字不动。

### 5.2 宙斯 9 PID

追加到 `devices[]`，`vid=0x20bc`、`kind=gamepad`、`role=controller`、`protocol=betop-zeus-keyevent`、无 `capabilities`。

| PID | model | 备注 |
|---|---|---|
| 500b | 宙斯 Pro | 官方 `t6p2a` |
| 502f | 宙斯 Pro | 官方 `t6p2a` |
| 5074 | （空） | 官方多型号：宙斯 Pro / 宙斯2 |
| 5076 / 5077 | 宙斯2 | 官方 `t9` |
| 5104~5107 | 永劫无间 | 官方 `t9_yjwj` |

`501e` / `502c` 留在 `officialDevices[]` 的 `CatalogOnly`，不追加运行目录；另记 CR 等官方下一版恢复注册。

### 5.3 `20DD` 回填 26 条

对运行目录里“只有 20BC、官方有 20DD 身份”的 26 个 PID 追加 `vid=0x20dd` 条目：

```
5125 5126 5127 512f 5133 5134 5145 5146 5149 514a 5150 5151 5152 5153
5154 5155 5158 5159 515b 515d 515e 515f 5160 5169 516a 5191
```

- `model` / `protocol` 与同 PID 的 20BC 条目一致；
- `kind=unknown`；`role` 按官方 20DD 注册角色映射：官方 `dongle` → `dongle`，官方 `wired` → `controller`；
  无 `capabilities`、无 `group` / `pairedPid`；
- `note` 统一写“官方 RegisterAlterDev 第二身份；未单独验证，不声明能力”。
- 同一映射也修正 P1-B2 已加的 21 条 20DD `role`（它们不在原 33 条冻结快照里）；原有 3 条 20DD 不动。
- 不新增 `5161` / `5162`（盘古不在运行目录）；不新增 20BC 没有的官方 PID。
- 回填后 `20DD` 覆盖 50 / 52 个官方身份；`officialDevices[]` 不动。

### 5.4 计数

| 项 | 现在 | 之后 |
|---|---|---|
| `devices[]` | 75 | 110 |
| 唯一 `VID:PID` | 51 | 86 |
| `officialDevices[]` | 66 PID / 118 身份 | 不变 |
| 协议数 | 2 | 3 |
| `20DD` 条目 | 3 | 50（3 原有 + 21 P1-B2 + 26 本次） |

## 6. 测试矩阵

1. **schema**
   - `percent.map`：数组命中 / 对象命中 / 未命中 → null / 命中 0 且 `validPercent.min=0` 通过 / `min=1` 被拒。
   - `link.table`：`0x50` → down、`0x58` → up、`0x10` → down、未知值 → `onMiss`；`mask` 截位；`onlyWhenMode` 仍生效。
   - `chargeWhenMissing`：无 `charge` 字段时返回声明的默认值；未声明返回 `Unknown`。
   - `requirePercent`：0 / 101 帧返回 null；`validPercent.min=0` 时 0 通过。
   - 查询模板：`rid=2` → `[02 15 00…]`；`rid=0` → `[15 00…]`；`rid=null` → null；`outputLength=1` → 64；只读句柄写失败 → null。
2. **充电逐型号**
   - `low4`：byte4 `0x51` → `Charging`；`0x50` → `Discharging`；100% + `0x51` → `Charging`（不是 `Full`）。
   - `nocharge`：byte4 任意 → `Unknown`（100% 时 `Full` 由插件 `FinalizeCharging` 判，单独断言）；能力仍 `Unverified`。
   - 5125 / 5126 / 5127 / 5128 明确断言“不解析充电”。
   - `FinalizeCharging`：100% + `Charging` → `Charging`；100% + `Discharging`/`Unknown` → `Full`；
     宙斯 `<100%` + `Unknown` → `Discharging`。
3. **宙斯**
   - `04 02 … 58` → 80%；`04 03 … 58` → 100%；`04 00 … 10` → `Link=Down`；`04 02 … 50` → `Link=Down`。
   - 等级表外（`raw[1]&7 = 5..7`）→ `Percent=null`、`Link=Up`。
   - `<100%` → `Charging=Discharging`（保留旧口径）；100% → `Full`。
   - `0x5084` / `0x5085`（`t9v2`）不登记、不被认领；`extkeyevent1` 不参与电量。
4. **鲲鹏回归**
   - 现有 75 条全部仍可 `Resolve`；5158/5159 充电仍 `supported`；5073 能力不变。
   - 原有 33 条里 30 条 20BC 只改 `protocol`，其余字段不变；3 条 20DD 完全不变。
   - 50 条鲲鹏 20BC 的 `protocol` 按上表切换。
   - 新增 26 条 20DD 的 `role` 按官方映射：14 条 `controller`、12 条 `dongle`；`kind` 均为 `unknown`。
   - 现有 `BetopCodecTests` 的 16 字节合成帧要补零到 64（只补 0，不改有效字节）后再喂解释器；
     `length: {min:64}` 会先把短帧挡掉，否则测不到 `percent` / `charge` 本身。
5. **门控**
   - 已登记的 10 个宙斯 PID 在 `0xFF00/24` 上可被认领（被动）。
   - 未登记宙斯 PID（如 `0x9999`）仍不探测；未登记鲲鹏 `0x00FF/64` 的 fallback 保留。
   - `501E` / `502C` 仍匹配 `betop-zeus-keyevent`，但不在 `devices[]`，所以不认领、能力保持 `Unverified`。
6. **全量**
   - `dotnet build VigilWatt.sln -c Debug` 0 警告 0 错误。
   - BETOP 专项 + 全量测试；按 `docs/dev/build.md` §4.1 把测试产物复制到工作区外跑。
   - `git diff --check` 干净；`ProtocolEngine.Load` 成功。
7. **数据化回归**
   - 用 `ProtocolEngine.FromJson` 造一个“新 BETOP 设备 + 新协议”的最小 JSON：只加 `devices[]` 条目和
     `protocols` 条目，不改 C#，断言 `Resolve` 命中且能解析一帧。这是“新设备只加 JSON”的回归测试。

## 7. 落地顺序

1. 通用 schema：`PercentSpec.Map`、`LinkSpec.Table/Mask/OnMiss`、`FrameSpec.RequirePercent/ChargeWhenMissing`、
   `ActiveSpec` 查询联合类型、`ProtocolEngine.ProbeLinkOf/IsVendor/Listen`、
   `QueryRunner.Run` 的可选 `windowMs` 参数；先写单测。
2. `QueryRunner` + `QueryFrame.Build`，先跑雷柏 / 迈从全量，确认固定模板零变化。
3. 改 `betop.protocols.json`（3 套协议），跑 `ProtocolEngine.Load`。
4. 改 `BetopPlugin`，删除 `BetopCodec`，跑 BETOP 专项。
5. 改 `betop-catalog.json`（50 鲲鹏协议名、9 宙斯、26 个 20DD），更新计数与覆盖测试。
6. 文档：`protocol-json.md`、`betop-catalog.md`（含 §8.1 的“新设备只加 JSON”清单）、
   `betop.md` §1、`changelog.md`、`handoff.md` §1。
7. 全量构建 + 全量测试 + `git diff --check`。

## 8. 风险与待确认项

| # | 事项 | 建议 |
|---|---|---|
| R1 | `0x501E` / `0x502C` 在当前 2.6.2 包内的注册是注释掉的 | 已确认：只登记实际有效的 9 个；`501E/502C` 留 `CatalogOnly` + CR |
| R2 | 任务示例用帧级 `minPercent`，现有 schema 是 `validPercent` | 用 `validPercent`，不新增别名（未知字段会静默失效） |
| R3 | `fullAt: 100` 会把“100% + 充电中”改成“已充满” | 已确认：不新增字段；满电收尾放插件 `FinalizeCharging` |
| R4 | 26 个新 20DD 里有 14 个官方注册角色是 `wired`，不是 `dongle` | 已确认：按官方映射，`wired → controller`、`dongle → dongle` |
| R5 | 宙斯 <100% 的充电态 | 已确认：保留旧口径 `Discharging`；由 `chargeWhenMissing: "discharging"` 表达，`nocharge` 仍 `Unknown` |
| R6 | `0x10` 占位帧现在只认 `byte1==0 && byte23==0x10`，`link.table` 会无条件认 `0x10` | 已确认：按表接受，记一条观察 CR |
| R7 | 宙斯 `0x5084` / `0x5085`（`t9v2`）不登记 | 它们的 `extkeyevent1` 是按键字段，不是电量；若将来要接，另开协议 |

### 8.1 新 BETOP 设备“只加 JSON”清单

| 变化 | 只改 JSON | 需要 `codeFamily` |
|---|---|---|
| 新 PID / 型号 / 显示名 / role / group / capabilities | `devices[]` | — |
| 同帧布局，只换充电掩码 / 码表 | `devices[].override` 或新协议 | — |
| 新接口 page / usage / 报文长 | 新协议 `interface` | — |
| 新帧偏移 / 掩码 / 前缀 / xor / 长度 | 新协议 `passive` / `active.response` | — |
| 离散电量等级 | `percent.map` | — |
| 新链路码表 | `link.table` | — |
| 新查询帧 / report ID / 长度 / 探测窗口 / 重试 | `active.query` / `windowMs` / `attempts` | — |
| 没有充电位时的默认状态 | `chargeWhenMissing` | — |
| 握手 / 会话 / 密码门 | — | 是 |
| 校验和 / CRC / 加密 | — | 是 |
| 多帧组合 / 跨报告状态机 | — | 是 |
| 同一 PID 多接口、每口不同帧布局 | — | 是（或后续加 per-interface 协议表） |
| 同一接口多 report ID、每 ID 不同布局 | — | 是（现有 `reportId` 数组共用一套字段） |
| 电压 → 百分比 / 线性换算 | — | 是（未来可抽 `scale`） |
| 大端字段 / 有符号 / 跨字节位域 | — | 是（未来可抽 `endian` / `signed`） |
| 不同离线策略 / 轮询节拍 / 稳定窗口 | — | 插件级（本批不数据化） |

这张表就是“厂商级适配”的边界：**帧怎么读、码怎么映射、设备叫什么**全部 JSON；
**流程、校验、多帧状态机、换算**才写代码。新设备如果落在前 8 行，不需要改 C#。

### 8.2 本批保留的代码边界

- `FinalizeCharging`：100% 满电收尾（北通品牌口径）。
- `IsSettled`：500ms 被动流稳定判定（北通品牌口径）。
- 轮询窗口：先听 1.5s、查询等 1.5s（插件常量；探测窗口仍在 JSON）。
- `Offline`：北通离线策略（占位帧立即离线、不按轮询失败计数）。
- `ProbeInferredConnection`：`0xFF00/24` → 2.4G（连接方式推断）。
- `BetopDisplayName`：目录无型号时的产品串兜底。
- `BetopOfficialAppGuard` / `ProbeCooldown`：品牌级观测与探测冷却。

### 8.3 什么时候再抽通用字段

不提前抽，满足下面任一条件再抽：

- 第二个厂商也需要“100% + 充电中”与雷柏不同的满电口径 → 抽 `fullAtMode`；
- 第二个厂商需要不同的被动稳定窗口 → 抽 `settleMs`；
- 第二个厂商需要不同的离线策略 / 轮询节拍 → 抽 `offline` / `pollInterval`；
- 第二个厂商 / 接口家族需要不同的探测连接推断 → 抽 `connectionOnProbe`；
- 第二个厂商需要电压换算 → 抽 `scale` / `divisor`；
- 第二个厂商需要大端字段 → 抽 `endian`。

这样不会为北通一家扩一堆通用字段，但边界和触发条件都写清楚了。

## 9. 本批不做

- UI、产品图、连接方式推断、离线策略改造；
- 其他品牌插件 / 协议；
- `02 CD`（cmd 0xD）解析；
- 盘古 `0xFF80`、鲲鹏50 副身份 `0x0517:0x5350`、海心桥 `0xFF7A`；
- 写入类功能、能力升级（除已有真机证据的 5158/5159）。
