# 凌豹 LINGBAO M5PRO Max（接收器 3151:402D）

> 2026-10-24 新增。协议取自官方驱动 JS + 本机真机，**电量字段的语义仍有一个未闭合点**（§1.4）。
> 改插件前先读本文与 [safety.md](safety.md)。

## 1. 设备与接口

| 形态 | VID:PID | 配置接口 | 状态 |
|---|---|---|---|
| 2.4G 接收器 | `3151:402D` | `MI_02`，usagePage `0xFFFF` / usage 2，feature 65 字节 | 本机实测 |
| 有线/直连 | `3151:4026` | 同上（官方驱动设备表里同型号的另一个 PID） | 未实测 |

- 厂商集合**只有一个 feature report（report ID 0）**，没有 output/input 通道；键盘/鼠标/consumer 那几个标准集合不要当配置通道。
- `3151` 是荣源/ROYUAN 共用号段：官方驱动里同 VID 的键盘/鼠标型号有几百个，**不能按 VID 推断协议**，判据必须带 PID + usage + feature 长度。

## 1.2 帧格式

64 字节 payload（Windows 侧由 `HidD_SetFeature` 自动补 report ID 0，共 65 字节）：

```
[0]=命令  [1..62]=参数  [63]=校验和
校验和 = 0xFF - (前 63 字节之和 & 0xFF)
```

电量查询（`FEA_CMD_GET_BATTERY`，来自官方驱动渲染进程 JS 的 `getBattery()`）：

```
83 00 00 … 00 7C        # 0x83 = 131，0xFF-0x83 = 0x7C
```

## 1.3 响应

`HidD_GetFeature(0)` 返回 65 字节（含 report ID）：

```
[0]=00  [1]=接收器 ack（0/1）  [2]=设备返回码  [3]=电量%  [4]=状态字节  [5]=低电阈值
```

本机样本（2026-10-24，接收器在位、鼠标 2.4G）：

```
00 00 00 53 01 00 01 02 | 00 00 01 20 03 40 06 80 0c 00 19 90 65 00 …
```

- `[3]=0x53` = **83%**，是整条响应里唯一落在 1..100 的电量候选。相隔约 15 分钟再采一次是 `0x52` = 82%，与持续放电一致——这是"`[3]` 是电量"的旁证（仍不是官方界面数值对照）。
- `[10..]` 是 DPI 档位表（`0x190/0x320/0x640/0xC80/0x1900/0x6590` = 400/800/1600/3200/6400/26000，与官方设备表 `defaultDPI` 逐项相同）+ RGB 值——**这条响应是"状态块"，不是纯电量帧**，所以不能按"电量一定在 [1]"之类的直觉读。
- 重复查询时 `[1]` 出现过 `0x00`（未受理）与 `0x01`（受理），解析对两者都放行；`[2]` 必须为 0。

## 1.4 未闭合点（别当已验证）

1. **电量下标没有官方界面数值对照**。官方驱动（Electron）在这台机器上起不了窗口（进程 exit 0，沙箱内外都一样），取不到它显示的百分比，因此 `[3]` 是按"唯一合理候选"定的，不是与官方读数对齐过的。
2. **状态字节 `[4]` 语义未校准**。本机读到 `0x01`；官方 JS 把 `1/2` 解释为"充电中/已充满"，但 2.4G 鼠标没插线时不该是充电中。**插件因此不显示充电状态**（`ChargingState.Unknown`），等插线/放电两种状态各采一条再补映射。
3. 有线形态 `3151:4026` 未实测。

## 1.5 写入与开关

查询是只读命令（GET_BATTERY），但仍要写 feature report，所以插件**默认关闭**，插件开关即写入授权
（`AppSettings.PluginEnabled["Lingbao"]`，见 [safety.md](safety.md) §3.1）。日志里 `凌豹 电量查询` 一行带原始帧，便于下次对照官方软件校准。

## 1.6 证据来源

- 官方驱动：`%LOCALAPPDATA%\Programs\LB-M5PRO Max Driver`，渲染进程 `dist/static/js/main_8e553aff.js`
  的 `getBattery()`（`FEA_CMD_GET_BATTERY = 131`）与 `commomFeature`；设备表条目
  `vid:12625,pid:16429,name:"mouse_pan1080_xmg_m5promax_1k_8k",displayName:"M5PRO Max",featureReportByteLength:65`。
- 校验和与"64 字节 feature + usagePage 0xFFFF/usage 2"来自同一芯片家族的公开逆向文档
  （[ROYUAN HID protocol](https://dnim.dev/blog/royuan-keyboard-protocol)，见 [sources.md](sources.md)）。
- 本机实测：`3151:402D&MI_02` 发 `83 … 7C` → `00 00 00 53 01 00 01 02 …`（两次一致）。
