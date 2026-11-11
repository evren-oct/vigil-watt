# 派世 Pulsar X2 V2（dongle 3554:F508）

> 从 [protocols.md](../protocols.md) 拆出（2026-10-21）。结论均来自真机验证或官方软件抓包；改协议代码前先读本文。

## 1. 派世 Pulsar X2 V2（dongle VID_3554&PID_F508）—— ✅ 已打通，应用在用

### 1.1 接口地图

- VID `0x3554`：`0xF507` 有线 / `0xF508` 1 kHz dongle（本机）/ `0xF509` 4 kHz dongle。产品名 `X2 V2`。
- 十七字节双向通道：`MI_01&Col05`，usage page `0xFF02` / usage `0x0002`，in=out=**17**，report ID `0x08`。
- 同一 dongle 的 `0xFF05`(8B)、`0xFF03`(8B)、`0xFF04`(feature 8B, RID 6) 都没有电量字段。

### 1.2 帧格式（17 字节，report ID `0x08`）

| 偏移 | 含义 |
|---|---|
| `[0]` | report ID `0x08` |
| `[1]` | 命令 |
| `[2..15]` | 参数 / 应答数据 |
| `[16]` | 校验 = `(0x55 - sum(buf[0..15])) & 0xFF` |

真机帧：

```
STATUS 请求  08 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 4a
STATUS 应答  08 03 00 00 00 01 00 65 06 71 00 00 00 00 00 00 6d   ← [6]=0 表示鼠标当时在休眠
POWER  请求  08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49
POWER  应答  08 04 00 00 00 02 14 00 0d f2 00 00 00 00 00 00 34   ← 20%、未充电、0x0DF2=3570 mV
```

### 1.3 命令表

| 命令 | 方向 | 含义 |
|---|---|---|
| `0x01` | 下行/上行 | INFO，挑战-应答（`resp[i] = challenge[i]*(i+1) + challenge[(i+1)%4] + device_id[i]`），只用于设备校验，**读电量不需要** |
| `0x03` | 下行/上行 | STATUS，应答 `[6]`：1=设备在线、0=dongle 在位但鼠标没连上 |
| `0x04` | 下行/上行 | POWER，应答 `[6]`=电量 0..100、`[7]`=充电中、`[8..9]`=电压 mV 大端 |
| `0x0a` | 仅下行 | EVENT，`data[6]==0x40 && data[16]==0xF9` 表示电量变化推送（本应用未启用） |

### 1.4 实测与行为

- 本机：STATUS 在线；POWER = 20%、未充电、3570 mV（应用里随后读到 3566 mV，正常波动）。
- **鼠标休眠时 POWER 可能不回应**：插件容忍超时，靠基类熔断退避，不判离线。
- 传输同样用 `WriteFile`（与 CHERRY 一致，`HidD_SetOutputReport` 不适用）。

### 1.5 协议来源

- Linux 内核补丁 `[PATCH] HID: pulsar: add driver for Pulsar gaming mice`（Nikolas Koesling，2026-03-18，`https://lkml.iu.edu/2603.2/07112.html`）。
- 该补丁基于 `https://github.com/andrewrabert/python-pulsar-mouse-tool`。

### 1.6 安全与开关

- `PluginEnabled["Pulsar"]` **默认关闭**；只发 STATUS / POWER 只读查询，不写配置、不改 DPI。
- 已按 VID `0x3554` 收窄，且要求 17 字节双向 + `0xFF02`，不会误认其他厂商接口。
