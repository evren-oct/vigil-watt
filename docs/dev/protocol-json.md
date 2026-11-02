# 协议 JSON 怎么写（数据化的边界）

> 雷柏与迈从的帧解析已从 C# 搬到 JSON：解释器在 `src/VigilWatt.Core/Plugins/Protocol/`，
> 数据在 `Plugins/<品牌>/specs/*.protocols.json` 与 `Plugins/<品牌>/*-catalog.json`。
> **新增设备先想"能不能只改 JSON"**，改不动才写代码。

## 1. 切分线

| 进 JSON | 留代码 |
|---|---|
| vid / pid / 型号 / kind / pairedPid | 去抖（`chargeDebounce` 只开开关，算法在解释器里） |
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
      "linkMode": { "offset": 1, "mask": 15, "map": { "0": "wireless2g", "2": "wired" } }
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

## 4. 校验（加载期就报错）

`ProtocolSpecLoader` 在插件构造时校验，失败直接抛异常（`DeviceManager` 会跳过该插件并记 Error）：

- 偏移越界（帧长确定时）
- `type` 不在闭集合、`map` 的键不是数字、`kind` 拼错
- 设备引用了不存在的协议、`extends` 成环
- `implementation=code` 但没给 `codeFamily`

> `codeFamily` 必须是真实类型：抽族前填插件名（如 `MchosePlugin`），**不要预留不存在的族名**——数据里写了个找不到的类，下一个人会去翻文件。

宁可启动失败，不要静默不认领。

## 5. 什么时候必须写代码

四类**有状态**的协议留在 C#（`codeFamily`）：会话握手、型号密码门、半字段事件、通知后读。
它们仍然**不许有硬编码**：vid/pid 进目录、偏移与命令码进 JSON，代码里只剩流程。

## 6. 改 JSON 之后必须跑

```
dotnet build VigilWatt.sln -c Debug
dotnet test src/tests/VigilWatt.Core.Tests --no-build --filter "FullyQualifiedName~Rapoo"
```

雷柏的真机帧用例在 `RapooSpecTests`（R1–R26）：偏移改错会被它们挡住。
