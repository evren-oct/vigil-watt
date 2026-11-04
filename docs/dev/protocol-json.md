# 协议 JSON 怎么写（数据化的边界）

> 雷柏与迈从的帧解析已从 C# 搬到 JSON：解释器在 `src/VigilWatt.Core/Plugins/Protocol/`，
> 数据在 `Plugins/<品牌>/specs/*.protocols.json` 与 `Plugins/<品牌>/*-catalog.json`。
> **新增设备先想"能不能只改 JSON"**，改不动才写代码。

## 1. 切分线

| 进 JSON | 留代码 |
|---|---|
| vid / pid / 型号 / kind / pairedPid / capabilities | 去抖（`chargeDebounce` 只开开关，算法在解释器里） |
| 接口指纹：usagePage / usage / 长度 / pathContains / reportId | 无效帧怎么处理（丢读数 / 标离线 / 请求核实） |
| 查询帧模板（十六进制串 + 总长） | 链路位 → 离线候选 |
| 字段：偏移 / 类型 / 位宽 / mask / xor | 握手、会话、状态机、轮询策略 |
| 充电码 → 枚举 的映射表 | 校验和算法 |
| 有效取值范围 min / max / not | 显示名启发式 |

判断口径：**"这个值是什么意思"进 JSON，"知道意思之后做什么"留代码。**

## 2. 加一台设备（最常见，1 行）

`Plugins/<品牌>/*-catalog.json` 的 `devices` 数组加一条：

```jsonc
{ "vid": "0x24AE", "pid": "0x4B99", "model": "VT9", "kind": "mouse",
  "protocol": "status-report7", "pairedPid": "0x4B9A" }
```

同型号偏移差一位时，只覆盖那一个字段：

```jsonc
{ "vid": "0x24AE", "pid": "0x4805", "model": "V700diy-98", "kind": "keyboard",
  "protocol": "telink-keyboard", "override": { "passive": { "fields": { "percent": { "offset": 7 } } } } }
```

`override` 是**结构化深合并**，叠在"已展开 extends"的协议上；只写要改的叶子，其余继承。

### 2.1 设备能力声明（`capabilities`，PR-C 起）

`capabilities` 写在**设备条目**上，**不在协议里**：同一套 `status-report7` 被 98 台鼠标共用，
能力若挂在协议上，VT3 的真机验证结果会顺带漏给所有同族设备。

```jsonc
{ "vid": "0x24AE", "pid": "0x1412", "model": "VT3", "kind": "mouse", "protocol": "status-report7",
  "capabilities": {
    "batteryRead": "supported",
    "dpiRead": "supported",
    "dpiLevelRead": "supported",
    "rateRead": "unverified"       // 只有单点观测，证据不足；省略也是同一结果
  } }
```

**语义**：VigilWatt **针对这个型号、在当前适配与连接方式下已经实现并验证**的功能。
不是厂商理论能力，也不是"能读就能写"。

| 能力 | 含义 |
|---|---|
| `batteryRead` | 读电量 |
| `dpiRead` | 读当前生效 DPI 数值 |
| `dpiLevelRead` | 读 DPI 档位索引（不是 DPI 值，也不是档位总数） |
| `rateRead` | 读设备**保存的**回报率设置值，不是实测 USB/HID 吞吐率 |
| `dpiWrite` | 写 DPI |
| `rateWrite` | 写回报率 |

| 状态 | 含义 |
|---|---|
| `supported` | 已实现，且有足够证据（真机读数 / 被单测钉住的解析） |
| `unsupported` | 已确认当前 VigilWatt 适配不提供该能力 |
| `unverified` | 尚未验证或证据不足。**默认态**：没实现不等于已确认不支持 |

- 只写已验证的几项即可，其余省略即 `unverified`；`"capabilities": {}` 与省略等价。
- **唯一合法位置是 `devices[].capabilities`**：`ProtocolDto` 里没有这个字段，未知字段默认被忽略，写错层就是"加载成功但声明丢失"。
  加载器对**协议对象**和**设备 `override`** 的**任意层级**（嵌套对象、数组元素都算，字段名不区分大小写）都直接报错：
  协议层带协议名 + 字段路径（如 `passive.capabilities`、`interface.anyOf[0].capabilities`），
  `override` 层带 `VID:PID` + 字段路径（如 `override.passive.fields.capabilities`）。
  两处都跳过 `map` / `mapValue` 的值——它们是数字取值表，不是协议字段。
- **大小写**：字段名 `capabilities` 不区分大小写；**能力名区分大小写**，`batteryRead` 写成 `BatteryRead` 会报错。
- 其余未知字段仍按现有 JSON 反序列化策略忽略：**拼写错误（如 `capabilites`）不会被识别或报告**，别指望加载器兜住。
- **不继承**：`extends`、设备 `override`、共享协议引用都不会把能力带过去，每条设备各自声明。
- 与旧 `caps` 的区别：`caps` 是历史验证记录（只在雷柏目录、代码不读），`capabilities` 才是当前正式声明，
  两者**不自动映射**，见 [hardware/rapoo-catalog.md](../hardware/rapoo-catalog.md) §6。
- 查询入口 `ProtocolEngine.CapabilityOf(info, capability)`：纯配置查询——不开句柄、不发帧、不动解析状态；
  未收录设备一律 `unverified`，非法枚举值抛异常而不是回落到默认值。

## 3. 加一套协议（形态固定时，约 20–30 行）

`specs/*.protocols.json` 的 `protocols` 里加一条。可用词表：

```jsonc
{
  "kind": "mouse",                    // mouse/keyboard/headset/speaker/gamepad/unknown
  "pollable": false,                  // 有没有主动查询命令
  "chargeDebounce": false,            // 连续两帧才判充电（雷柏鼠标用）
  "probeWindowMs": 5000,
  "implementation": "data",           // data / code / none（none = 目录收录但没适配）
  "codeFamily": "MchosePlugin",       // 仅 implementation=code 时必填，填**真实存在的**类型名
  "extends": "status-report7",        // 可选：继承另一套，只写差异

  "interface": {                      // 认领判据，字段间是 AND
    "usagePage": 65280,               // 数字 / [65280] 任一 / {"min":65280} / {"eq":19} / {"not":[19]}
    "usage": [2, 14],
    "inputLength": { "min": 7 },
    "outputLength": { "min": 7 },
    "featureLength": { "eq": 0 },
    "reportId": { "eq": 85 },         // 接口的输出 report ID（HidDeviceInfo.OutputReportId）
    "pathContains": "&mi_00&col05#",
    "anyOf": [ { "usagePage": 65280 }, { "usage": 65280 } ]   // 任一组命中（组内仍 AND）
  },

  "passive": {                        // 被动帧；与 active 至少有一个
    "tryOffsets": [0, 1],             // Windows 缓冲带不带 report ID，两种偏移都试
    "reportId": [7, 8],
    "length": { "min": 19 },
    "xor": 255,                       // 整帧取反后再解析
    "prefix": [ { "offset": 1, "value": 176 } ],   // 这些位置必须等于这些值
    "fullAt": 100,                    // 电量达到它就判"已充满"，优先于充电位
    "validPercent": { "min": 1, "max": 100 },
    "fields": {
      "percent":  { "offset": 8, "type": "u8" },
      "charge":   { "offset": 7, "type": "u8",
                    "map": { "1": "discharging", "2": "charging" },
                    "onMiss": "discharging" },        // 也支持 "on": "nonzero" / "zero"
      "link":     { "offset": 18, "bit": 7, "connectedWhen": 1,
                    "onlyWhenMode": "wireless2g" },   // 有线时 bit 恒 0 不算断链
      "linkMode": { "offset": 1, "mask": 15, "map": { "0": "wireless2g", "2": "wired" } },

      // 指针遥测（可选，五个字段独立）：只在这套协议确有这些字段时才写
      "dpiX":     { "offset": 3, "type": "u16", "min": 50, "max": 26000 },
      "dpiY":     { "offset": 5, "type": "u16", "min": 50, "max": 26000 },
      "dpiLevel": { "offset": 2, "type": "u8", "min": 0, "max": 6 },   // 档位索引，不是 DPI
      "dpiLevelCount": { "offset": 9, "type": "u8" },
      "rateHz":   { "offset": 11, "type": "u8",
                    "mapValue": { "1": 1000, "132": 2000 } }   // 码 → 数值（与 charge 的 map 不同）
    }
  },

  "active": {                         // 主动查询；写失败一律当"不是这条通道"
    "query": { "length": 65, "hex": "00 55 30 A5 0B 2E 01 01 01", "pad": 0 },
    "windowMs": 1200,
    "attempts": 2,
    "rejectQueryEcho": true,          // 有的接收器把主机写入回显进输入队列
    "response": { /* 与 passive 同构 */ }
  }
}
```

`type` 的闭集合：`u8` `u16` `u24` `u32` `bit4` `bit`。**没有表达式、没有算术、没有条件**——
出现这类需求就该写 `codeFamily`。

### 3.1 指针遥测字段（PR-B 起）

`dpiX` / `dpiY` / `dpiLevel` / `dpiLevelCount` / `rateHz` 五个字段独立可选，缺哪个就是"这套协议没有"。
它们**只填遥测，不影响电量/充电/链路**，规则见 [project/pointer-telemetry.md](../project/pointer-telemetry.md)。

- `min` / `max`：取值域，越界给 `Unknown`（雷柏用 `min: 50` 挡住休眠零帧的 `0`）。
- `mapValue`：码 → 数值（`int → int`），**映射表里没有的码给 `Unknown`，不拿 0 冒充**。
  注意它与 `charge` 的 `map`（码 → 枚举）不是一回事。
- 来源固定是 `DeviceReported`：**码表属于协议本身**，查表不降级成 `Inferred`。
- 观测时刻由解释器在边界处盖（`ProtocolEngine.Parse`），JSON 里不写时间。
- **只给已验证的型号加**：写进协议本体会让同族所有设备一起产出遥测，通常应当放在
  设备目录的 `override` 里（`rapoo-catalog.json` 的 `0x1412` 就是这么做的）。

## 4. 校验（加载期就报错）

`ProtocolSpecLoader` 在插件构造时校验，失败直接抛异常（`DeviceManager` 会跳过该插件并记 Error）：

- `TryOffsets[0]` 或字段 offset 为负（与有没有帧长无关，不含回退分支）
- 字段宽度越界（只在有可靠帧长下界时，不含运行时回退分支）
- `active.query` 长于 `interface.outputLength` 的精确值
- `type` 不在闭集合、`map` 的键不是数字、`kind` 拼错
- 设备引用了不存在的协议、`extends` 成环
- `implementation=code` 但没给 `codeFamily`
- `active.query.hex` 超出 `query.length`
- `capabilities` 不是对象、能力名拼错（大小写敏感）、状态不是 `supported`/`unsupported`/`unverified`、
  状态值不是字符串。报错带设备 `VID:PID` 与 `capabilities.<字段名>`，**不许静默变成 `unverified`**

> `codeFamily` 必须是真实类型：抽族前填插件名（如 `MchosePlugin`），**不要预留不存在的族名**——数据里写了个找不到的类，下一个人会去翻文件。

宁可启动失败，不要静默不认领。加载期校验不改 `FrameReader` 的回退顺序，也不改 `IntMatch.Matches`。

### 4.1 帧字段边界

`passive` 和 `active.response` 用同一套检查。帧长**只**认这一帧自己的 `FrameSpec.Length`，禁止退回到 `interface.inputLength`。

`InputLength` 是 HID 接口声明的报告长度，不保证这次实际读到多少字节。拿它当帧长，会把「声明很长、实际是短帧」或「声明很短、缓冲更长」判错。

负数和帧长是两道检查。`TryOffsets[0] < 0`，以及 `percent` / `charge` / `link` / `linkMode` / `prefix` 的 `offset < 0`，没有可靠帧长也拒绝；正的主分支不能把负的字段偏移加回合法范围。后面的 `tryOffsets` 回退分支不在加载期查。只有拿到下界之后，才用 `long` 检查 `TryOffsets[0] + offset + width` 是否超出该下界。没有可靠下界时跳过的是这段宽度检查。迈从 Gmouse 的 response 没有 `length`，正偏移因此不会被卡宽度，不要为了让宽度检查跑起来补一个长度。

下界取 `Length` 能接受的最小值，优先级与 `IntMatch.Matches` 相同（`Any` 非 null 优先于 `Eq` / `Min`）：

| `length` | 用来卡偏移的长度 |
|---|---|
| `{"eq": N}` 或数字 `N` | 精确长度 N |
| `{"min": N}`（没有 `Eq`，也没有 `Any`） | 最小长度 N，字段必须放进最短的合法帧 |
| `[N, M, …]`（非空） | 集合里的最小值 |
| 没写、只有 `max` / `not`、或 `[]` | 没有可靠下界，跳过宽度检查（负偏移仍拒绝） |

`[]` 是空的 `Any`：任何长度都匹配失败，下界是「没有」，不是退回 `Eq` / `Min`。`{"any": []}` 这种对象写法加载器不认，仍然报「空对象」。

宽度用 `long` 相加：`int` 在接近 `int.MaxValue` 时会回绕成负数，越界会被当成合法。只拿 `TryOffsets[0]` 做主分支；后面的偏移是运行时回退（Windows 缓冲带不带 report ID），加载期不排序、不去重，也不按最坏的那条分支拒绝。没写 `tryOffsets`，或写成 `[]`，都归一成 `[0, 1]`；空数组若留在规格里，读取时一条分支都不会试。

检查的位置：`percent`、`charge`、`link`、`linkMode`、`prefix`。宽度按 `type`（`u16` / `u24` / `u32` 为 2 / 3 / 4，其余 1 字节）。

### 4.2 主动查询

`active.query` 的字节数只在 `interface.outputLength` 写了 `eq` 时，按这个精确长度卡上界（查询帧可以更短，不能更长）。`outputLength.min` 只说明接口至少这么长，实际输出报告可能更长，不能当成查询帧的长度。

## 5. 什么时候必须写代码

四类**有状态**的协议留在 C#（`codeFamily`）：会话握手、型号密码门、半字段事件、通知后读。
它们仍然**不许有硬编码**：vid/pid 进目录、偏移与命令码进 JSON，代码里只剩流程。

## 6. 改 JSON 之后必须跑

```
dotnet build VigilWatt.sln -c Debug
dotnet test src/tests/VigilWatt.Core.Tests --no-build --filter "FullyQualifiedName~Rapoo"
```

雷柏的真机帧用例在 `RapooSpecTests`（R1–R42）：偏移改错会被它们挡住。
