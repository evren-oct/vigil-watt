# 罗技 Logitech (VID_046D) —— ✅ Bolt 真机验证（2026-10-08）；协议驱动 + 嵌入目录

> 从 [protocols.md](protocols.md) 拆出（2026-10-21）。结论均来自真机验证或官方软件抓包；改协议代码前先读本文。


HID++ 1.0/2.0 协议发现（无型号白名单）。电量级联：
`0x1004` Unified Battery → `0x1000` Battery Status → `0x1001` Battery Voltage → `0x1F20` ADC Measurement → 1.0 寄存器 `0x07`/`0x0D`。

嵌入资源 [`logitech-hidpp.json`](../../src/VigilWatt.Core/Plugins/Logitech/logitech-hidpp.json) 提供接收器槽位/报文偏好与毫伏曲线；**目录是补充不是白名单**——未知 PID 仍打开 HID++ 接口（默认最多 6 槽、有长则跳短）。

| 新设备类型 | 怎么扩展 |
|---|---|
| 新的 HID++ 鼠标 / 键盘 / 演示器（已有特征） | **零代码**——ping + GetFeature 自动发现 |
| 新的接收器族（确认 PID / 槽位 / 长短偏好后） | 在 JSON `receivers` **加一行** |
| 某 WPID 电压曲线与默认锂电差很大 | JSON `wpidCurves` + `voltageCurves` |
| 全新电量特征 ID，或根本不是 HID++ | **改 C#**（耳机 / Centurion / 仅 G HUB 等） |

蓝牙罗技走标准 GATT `0x180F`（`BleBatteryWatcher`），不在此插件。

**本机 Logi Bolt 接收器 `046D:C548` + MX Master 3S（2026-10-08）：**
- 必须走 20 字节长接口（usage `FF00/0002`，report **0x11**）；旁边 7 字节短接口（`0x10`）对 Bolt 鼠标不应答，应跳过（目录 `reportPreference=long`）。
- Unifying（`C52B`/`C532`）常只在短接口应答 → 目录 `reportPreference=short`，有短同伴时跳过长接口。
- 把 `0x10` 补零写进长接口 → 驱动 `ERROR_INVALID_FUNCTION`（与北通硬编码错 report ID 同类）。
- `Request()` 返回缓冲从 feature 字节起，**参数在下标 2**：GetFeature 下标 / 电量% / 充电态分别是 `resp[2]` / `resp[2]` / `resp[4]`（旧代码读 `resp[0]` 会把 feature 下标当成电量）。
- 发现 ping 醒着约 1s 才回，超时宜 ≥1.5s；设备名走 `GetFeature(0x0005)` 再 GetCount/GetChar；设备类型优先读接收器配对寄存器 `0xB5`（Unifying `0x2N` / Bolt `0x5N`），读不到再 `DeviceKindGuesser`。
- Unified Battery `0x1004`：**func 1 = GetStatus**（电量% + 充电态），**func 0 = GetCapabilities**（不能当电量）。组装请求必须 `(feature<<8)|(func<<4)`，写成 `|func` 会被 SwId 补丁抹成 func 0（真机曾把 Capabilities 的 15 误当电量，充着电也读不到 charging）。
- **实时充电**：插拔电时设备会发 HID++ 自发通知（`sw` 低 4 位=0，payload 与 GetStatus 同布局）。插件后台 `PumpNotifications` 只读不写，经 `StatusPushed` 即时 Upsert——不必等 60 秒轮询（与 Logi Battery Dashboard 同源）。未知 `devIdx` 的通知会当场补 ping 发现。
- 每次 `Attach` 按目录槽位数补扫尚未发现的索引（接收器上第二台不必等重启）。

**HID++ 报文层（字节级；实现见 `Plugins/Logitech/HidppIO.cs` / `HidppCodec.cs`）：**

| 报文 | 布局 |
|---|---|
| 短（7 字节） | `[0x10, devIdx, feature, (func<<4)\|sw, p0, p1, p2]` |
| 长（20 字节） | `[0x11, devIdx, feature, (func<<4)\|sw, p0..p15]` |
| 错误响应 | `data[0]` = `0x8F`（1.0）/ `0xFF`（2.0）；`data[1..2]` 回显请求前两字节；`data[3]` = 错误码 |
| 设备自发通知 | `sw` 低 4 位 = 0（本应用请求应答用 SwId `0x0B`，据此区分） |

**各特征的电量 / 充电编码（实现见 `HidppCodec.cs`）：**

| 特征 | 编码 |
|---|---|
| Battery Voltage `0x1001` | 大端毫伏 + flags；`bit7`=充电中、低 3 位 `1`=充满、`bit5`=危急 |
| ADC Measurement `0x1F20` | 大端毫伏 + flags；`0x01`=放电 / `0x03`=充电 / `0x07`=充满 |
| 统一充电状态码（`0x1004` / `0x1000` 共用） | `1`/`2`/`4`=充电中、`3`=充满、其余=放电 |
| 配对信息 `0xB5` 字节布局 | Unifying `0x2N`：参数区 `[3..4]`=WPID、`[7]`=kind；Bolt `0x5N`：`[1]`=kind、`[2..3]`=WPID（小端，交换后成大端） |
