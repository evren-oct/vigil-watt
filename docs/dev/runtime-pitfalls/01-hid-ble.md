# 运行时坑 · HID / BLE 读取

> 正文分册。索引与其余分册见 [runtime-pitfalls.md](../runtime-pitfalls.md)；**§N 是稳定编号**，代码与文档一律按 `runtime-pitfalls.md §N` 引用。

## 1. HID 读取必须用"单悬空读"模式（HidDeviceInfo.ReadInput）

**症状**：写入成功，但响应"永远收不到"。

**根因**：HID 的 ReadAsync 超时后并不会取消——旧读仍挂在驱动队列上。如果每次超时后开一个新读，设备迟来的响应会被旧读吃掉，新读继续干等。

**正确姿势**：每个句柄生命周期只保留一个 pending read：

```csharp
private Task<int>? _pendingRead;
public byte[]? ReadInput(TimeSpan timeout)
{
    if (_pendingRead == null) { _readBuffer = new byte[len]; _pendingRead = _stream.ReadAsync(...); }
    var completed = Task.WhenAny(_pendingRead, Task.Delay(timeout)).GetAwaiter().GetResult();
    if (completed != _pendingRead) return null;   // 超时，旧读继续挂着
    ...
}
```

**别用阻塞式 `ReadFile` 循环 + 每次重开**，也别用 `CancelIo` 后立刻重读（驱动队列行为不定）。

## 2. HID 其余小坑

- `HidP_GetCaps` 的成功码是 **0x00110000**，不是 0。判错会把正常接口当失败。
- 键盘/鼠标等系统标准 collection（如 dongle 的 MI_00）被 Windows 保护，CreateFile 打不开是正常的，不要当故障修。
- dongle 上的 consumer/system 等小接口（in=3/out=0 之类）**没有输出通道**——插件认领候选必须排除它们（MchosePlugin.Probe 开头的 `OutputReportLength < 20` 守卫），否则发帧时数组越界崩溃（踩过：IndexOutOfRangeException）。
- WriteFile 的输出缓冲长度必须正好等于接口的 OutputReportLength；report ID 不在描述符里时报"参数错误"（ERROR_INVALID_PARAMETER）——这个报错本身就是"该接口不认这个 report ID"的证据（41E4:2001 不认 0x4D 报文即由此发现）。

## 3. BLE 电量属性名在本机 Windows 上不可用

Windows 11 26200 上 `System.Devices.AepBattery_Level` 和 `System.Devices.Aep.Battery_Level` 都无效（CreateWatcher 报 COMException 0x8002802B）。BleBatteryWatcher 用属性候选列表逐个回退（只有 `System.Devices.Aep.IsConnected` 可用），电量直接走 GATT 读 Battery Service（0x180F/0x2A19），8 秒超时。别再尝试加新属性名，GATT 直读是可靠路径。

2026-10-06 补充：**经典蓝牙 selector 下同样不可用**（BluetoothClassicWatcher 首个候选 `AepBattery_Level` 也抛"找不到元素"，自动回退）。即本机任何 AEP 电量属性都拿不到。

**但经典蓝牙耳机的电量并非拿不到——换属性体系就行（2026-10-06 打通）**。Windows 11 设置页给耳机/音箱显示的电量来自 **PnP 设备节点属性**，不是 AEP 属性：

| 项 | 值 |
|---|---|
| 属性名 | `DEVPKEY_Device_BatteryLevel` |
| KEY | `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2` |
| 类型 | `DEVPROP_TYPE_BYTE`（也可能 UINT16/UINT32，读的时候按类型分支） |
| 挂在哪 | HFP 免手操作音频网关节点：`BTHENUM\{0000111E-…}_LOCALMFG&005D\7&1D3D8B&0&0CAEBDC6CDA7_C00000000` |
| 怎么读 | 只能用 SetupAPI：`SetupDiGetClassDevsW` → `SetupDiEnumDeviceInfo` → `SetupDiGetDeviceInstanceIdW` → `SetupDiGetDevicePropertyW`（WinRT `DeviceInformation` 拿不到 PnP 属性） |

实测 EDIFIER W820NB 双金标版读数 60%，与设置页逐字一致。要点：

- 实例 ID 末段 `&<12位十六进制>_C…` 就是设备 MAC（`0CAEBDC6CDA7`）；AEP ID 形如 `Bluetooth#Bluetooth<适配器MAC>-<设备MAC>`，取**最后**一段 MAC 才是设备本身，别取错。
- 过滤条件用 `BTHENUM\` 前缀，把 BLE（`BTHLE\`）节点排除，免得和 GATT 通道重复上报。
- 纯读、零写入，不打开设备、不建立连接；整表枚举一次约几毫秒，**做 5 秒缓存**再挂到每分钟的轮询上。
- 属性名别再造新词（`System.Devices.BatteryLevel` 之类在 WinRT 侧同样取不到），P/Invoke SetupAPI 是可靠路径。
- 排查脚本：`scripts/probe-devnode-battery.py`（纯 ctypes，`--all` 可 dump 任意节点全属性）。

### 3.1 经典蓝牙设备类型：不能假设"经典蓝牙 = 耳机"（2026-10-09 实修）

**症状**：`RAPOO BT3.0 Mouse`（经典蓝牙 BT3.0 鼠标）在列表里被显示成**耳机**（耳机图标 + 协议"经典蓝牙 A2DP/HFP"）。

**根因**：`BluetoothClassicWatcher` 新建条目时把 `Kind` 硬编码成 `DeviceKind.Headset`，理由是"A2DP/HFP 音频设备基本都是耳机/音箱"。
但 `BluetoothDevice.GetDeviceSelector()` 匹配的是**所有已配对经典蓝牙设备**（§3 自己也写了"含手机"），蓝牙鼠标/键盘同样会进来。
更糟的是 `DeviceStatusMerger` 是"声明覆盖猜测"（非 `Unknown` 才覆盖），这个错误类型会被**冻结**在进程内，永远改不回来。

**修法**：不再预设，按三级信号判定（`Models/ClassicDeviceKindResolver`，纯逻辑可单测）：

| 优先级 | 信号 | 来源 | 真机取值 |
|---|---|---|---|
| ① | **Windows 设备功能类别** | `DEVPKEY_DeviceDisplay_Category` = `{78C34FC8-104A-4ACA-9EA4-524D52996E57}, 90`（STRING_LIST），挂在**父节点** `BTHENUM\DEV_<MAC>` | `Input.Mouse` / `Audio.Headphone` |
| ② | **经典蓝牙服务 UUID** | **子节点**实例 ID 前缀 `BTHENUM\{00001124-…}_…` | `00001124`=HIDP；`0000111e`+`0000110b`=HFP+A2DP |
| ③ | 显示名兜底 | `DeviceKindGuesser` | `RAPOO BT3.0 Mouse` → Mouse |

判定顺序：类别命中即返回；否则**有音频服务（HSP/HFP/A2DP/AVRCP）= 耳机/音箱**（名字含"音箱"则 Speaker）；
都没有再按名字猜，**猜不出就 `Unknown`，绝不退回 Headset**（那正是本次 bug 的根因）。

**实测确认（本机）**：`BTHENUM` 共 3 个节点，全属同一台设备（MAC `B4EE25E54627`，VID_0002AE24 雷柏）：

```
BTHENUM\DEV_B4EE25E54627\…      ← 父节点：类别 = ["Input.Mouse"]
BTHENUM\{00001200-…}\…          ← 设备标识服务（SDP）
BTHENUM\{00001124-…}\…          ← HIDP（HID Profile）
```

关键细节（改代码时别踩）：

- **设备类别只在父节点上，服务 UUID 只在子节点上** → 必须按 MAC 归并两类节点（`ClassicDevNodeReader.ReadByMac`）。
- **实例 ID 里的 MAC 不能用"全局找 12 位十六进制"**：服务 UUID 段里的 `00805F9B34FB` 会先被匹配上。
  用 `&(?:BLUETOOTHDEVICE_)?([0-9A-Fa-f]{12})` 才能同时覆盖子节点末段 `<MAC>_C…` 与父节点的 `DEV_<MAC>` / `&BLUETOOTHDEVICE_<MAC>`。
- **设备类别依赖设备元数据包**，廉价设备可能没有（属性缺失），必须靠 ②③ 兜底。
- 类型**只在还没解析出来时重算**（名字与类别常常后到：WinRT 首次 `Added` 常只给设备 ID），一旦非 `Unknown` 就锁定。
- 类别取值是厂商可自定义的字符串（XSD 允许任意值），按关键字匹配而不是穷举枚举。
- 排查脚本：`scripts/probe-classic-kind.py`（`--all` dump 节点全属性）。
