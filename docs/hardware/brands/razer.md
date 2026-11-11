# 雷蛇 Razer (VID_1532) —— 经典 90 字节 feature 协议 —— ✅ 插件已落地（会写帧，默认关闭）

> 2026-10-24 新增。结论来自本机真机验证（毒蝰 Viper V4 Pro 接收器）+ 公开逆向成果交叉核对。
> 改协议代码前先读本文；写入边界见 [safety.md](../safety.md)。

**协议出处**（公开逆向，多实现交叉验证）：
- [openrazer/openrazer](https://github.com/openrazer/openrazer)：`razer_report` 帧结构、`0x07/0x80` 电量命令、`0x07/0x84` 充电命令、事务号表；
- [xzeldon/razer-battery-report](https://github.com/xzeldon/razer-battery-report)：设备 PID 表与"部分型号不报充电"的说明；
- [etherfun/razer-taskbar](https://github.com/etherfun/razer-taskbar)：HID 直读实现与响应校验顺序（status 先于 echo）；
- 雷蛇官方网页驱动 `synapse.razer.com/products/229/`：确认 `{productId:229, dongleId:230}` 与 `floor(raw/255*100)` 缩放口径。

## 1. 接口：认长度，不要认 usage page

同一台毒蝰 V4 Pro（接收器 `1532:00E6`）有**两条互不相干的厂商通道**：

| 接口 | usage | 报告 | 用途 |
|---|---|---|---|
| `MI_03`（interface 3） | `0x000C/0x01` 消费控制 | **feature 91 字节**（90 载荷 + report ID） | **经典协议，电量走这里** |
| `MI_04`（interface 4） | `0xFF13/0x01` 厂商 | in `0x07` / out `0x06` / feature `0x14`，各 61 字节 | Synapse 4 新架构，语义未逆完，别碰 |

**教训**：第一版按 `usage_page >= 0xFF00` 过滤，正好把 91 字节那条漏了，于是误判"经典协议不存在"。
认领判据用 **`FeatureReportLength == 91` + 已知 PID**，一条就够。

PID 关系：`productId = 0x00E5`（鼠标本体），`dongleId = 0x00E6`（接收器，无线使用时枚举出来的是这个）。

## 2. 帧结构（90 字节载荷，report ID 0x00）

| 偏移 | 字段 | 查电量取值 |
|---|---|---|
| 0 | status | `0x00`（请求） |
| 1 | transaction_id | `0x1F`（本机验证；无应答时可试 `0xFF`） |
| 2..3 | remaining_packets | 0（大端） |
| 4 | protocol_type | 0 |
| 5 | data_size | `0x02` |
| 6 | command_class | `0x07`（BATTERY） |
| 7 | command_id | `0x80` 电量 / `0x84` 充电 |
| 8..87 | arguments | 全 0 |
| 88 | checksum | **payload 字节 2..87 逐字节异或** |
| 89 | reserved | 0 |

**校验不是 CRC-8**。OpenRazer 里函数名叫 `calculate_crc`，实际是 XOR：
`tx=0x1F` 查电量时 `0x02 ^ 0x07 ^ 0x80 = 0x85`。拿这条向量验算，一眼就能发现 CRC-8 是错的。

## 3. 响应解析

`HidD_GetFeature` 返回 91 字节，`[0]` 是 report ID：

```
[0] report id · [1] status · [2] transaction echo · [5] data size
[6] class · [7] command echo · [8..] arguments
```

判定顺序（重要）：**先判 status，再判 echo**。忙/无响应时，应答头可能还是上一条命令的 echo，
先判 echo 会把旧值当新值。

| status | 含义 | 处理 |
|---|---|---|
| `0x02` | 成功 | 取 `arguments[1]`（整包 byte 10） |
| `0x01` | 忙 | 重发 |
| `0x04` | 无响应（**鼠标休眠**） | 重试 1~2 轮后放弃，交给离线策略 |
| `0x03` / `0x05` | 失败 / 不支持 | 放弃 |

## 4. 电量缩放

`percent = raw × 100 / 255`（本机 `raw=0xFF` → 100%，与网页驱动 `floor(data[0]/255*100)` 一致）。
不要按 `<=100 就当百分比` 的启发式：本机满电就报 255，启发式会得到 255%。

## 5. 本机验证记录（2026-10-24）

```
请求  00 1F 00 00 00 02 07 80 00 … 85 00
应答  00 02 1F 00 00 00 02 07 80 00 FF 00 … 85 00   → 100%
充电  00 02 1F 00 00 00 02 07 84 00 00 … 81 00      → 未充电
休眠  00 04 1F 00 00 00 02 07 80 00 00 …           → status=0x04，参数全 0
```

**应用内验收（同日 23:40，本体 PID 有线模式）**：

```
认领雷蛇设备: Razer Viper V4 Pro [VID_1532&PID_00E5]
    usage=0x000C/0x0001 in=0 out=0 feat=91 proto=razer-hid 电量=100 充电=Unknown
```

`0x00E5`（本体/有线）与 `0x00E6`（接收器）两条路径都验过：feature 都是 91 字节、都能读电量。
有线模式下 `0x84` 充电查询没有得到有效应答，充电状态显示 `Unknown`——按"解析失败就退回 Unknown"处理。

充电命令的部分型号不支持（`razer-battery-report` 对 Viper V4 Pro 标注"不报充电"），
本机实测 `0x84` 有应答且返回 0，但仍按"解析失败就退回 Unknown"实现。

## 6. 写入安全

- 本插件**会写 feature report**，因此默认关闭：`AppSettings.PluginEnabled["Razer"] = false`，用户开开关 = 授权。
- 每轮 2 帧（电量 + 充电），失败重试最多 2 轮；`ProbeCooldown` 8 秒，满足 15 秒发现节拍的写入上限。
- 只写 `report ID 0x00` 的 91 字节 feature 通道；**不要**往 `0xFF13` 那条 62 字节新通道写任何东西。

## 7. 其他已验证接口

- [Viper V3 Pro SE 无线](../devices/razer-viper-se.md)：`1532:00DF` 的受保护鼠标集合，零访问权限 Feature 句柄，严格校验与独立缩放。
- [BlackShark V2 Pro (2023)](../devices/razer-headset.md)：`1532:0555` 的 PA 音频协议，按会话进入、查询和释放。

这些型号共用品牌开关，分别匹配接口和解析协议，不参与 V4 的事务号学习。
V4 保留原来的 PID、缩放、查询重试及公共编解码 API；新型号每轮只读取电量或充电之一。
