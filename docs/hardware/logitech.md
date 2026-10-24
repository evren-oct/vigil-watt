# 罗技 Logitech (VID_046D) —— ✅ Bolt 真机验证（2026-10-08）；协议驱动 + 嵌入目录

> 从 [protocols.md](protocols.md) 拆出（2026-10-21）。既有电量结论来自真机验证或官方软件抓包；新增物理身份的资料依据与未验证部分见 §1。

## 1. 充电直连与物理身份（2026-10-24）

本机 PRO 2 MOUSE 的 USB 直连 PID 为 `C09A`，无线接收器为 `C543`。Windows 缓存节点、用户充电场景和历史库一致：旧版将直连误当未知六槽接收器，在同一接口下登记槽位 1～6；拔线后六条保留为离线，接收器另占一条。不是 BLE/2.4G 双读。

- 已知目录项或 `C5xx` 按接收器发现；其他直连走地址 `0xFF`，不先扫 1～6。`C543` 补为单槽 Lightspeed；Bolt 仍为六槽。类型判断依据为本机 USB 节点和 [Solaar base_usb.py](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/base_usb.py) 的产品分区说明。
- 首次发现 HID++ 2.0 设备时，用现有 Root.GetFeature 找 `0x0003 DEVICE_FW_VERSION`，再发其函数 0 读取 GetDeviceInfo；只读取一次，不进周期诊断或命令枚举。请求仍经 `HidppIO.WritesAllowed`。字段依据是 [Solaar Hidpp20.get_ids](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)：参数区 `[1..4]` 为 unit ID，`[7..12]` 为 model ID。本项目响应含两字节 feature/function 前缀，所以分别从 `[3]`、`[9]` 起取值。
- 两字段均有效才生成跨通道物理 ID；全零、全 FF 或短响应不作为身份，保留通道 ID，不按名称猜测关联。协议字段参考后自行实现，未复制 Solaar 源码（来源代码 GPL-2.0-or-later）。本机实际跨通道身份一致性仍需插拔充电线验收。
- 通道保持独立状态，优先在线且有读数的通道，其次选 USB 直连；离线通道不会覆盖在线通道。移除后释放通道快照，避免每次插拔积累状态。
- 精确旧 ID 别名保存至设置，再由 SQLite 事务归并所属设备；保留每条采样的主键、电量、充电位、名称与时间，不删除采样。保存失败回滚设置；数据库失败由下次上报或启动重试。历史归并前已有设置映射是可重试依据，自定义名/图标及屏蔽标记随别名转移。
- 直连接口旧的 1～6 槽和 `255` 都属于该接口的旧 ID；只有重新观察到该直连接口后才归并这些历史。首次运行修复版时，应插一次充电线让程序确认物理身份；未观察到的旧记录不凭同名清除。

回归覆盖：PID 分类、独立物理身份、充电切换、离线来源覆盖、旧槽位映射、设置保存失败/别名冲突、真实 SQLite 归并及事务回滚。测试向量是协议布局合成样本，不宣称为本机原始抓包。

## 2. 既有电量协议


HID++ 1.0/2.0 协议发现（无型号白名单）。电量级联：
`0x1004` Unified Battery → `0x1000` Battery Status → `0x1001` Battery Voltage → `0x1F20` ADC Measurement → 1.0 寄存器 `0x07`/`0x0D`。

嵌入资源 [`logitech-hidpp.json`](../../src/VigilWatt.Core/Plugins/Logitech/logitech-hidpp.json) 提供接收器槽位/报文偏好与毫伏曲线；目录不作为型号白名单。未知接收器保留最多 6 槽的默认值，USB 直连按 §1 仅发现地址 `0xFF`；报文仍按有长则跳短的默认偏好。

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
