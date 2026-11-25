# 弱水时砂 ROSE FIRST ANGEL（2.4G dongle = 38FF:0001）—— ❌ 2.4G 无电量通道（2026-10-05 完整勘察）

> 从 [protocols.md](../protocols.md) 拆出（2026-10-21）。结论均来自真机验证或官方软件抓包；改协议代码前先读本文。


**身份澄清（修正旧记录）**：`ROSE FIRST ANGEL (VID_38FF&PID_0001)` 就是**2.4G dongle 本体**（USB 声卡 + HID 复合设备，MI_00=MEDIA 音频、MI_03=HID），不是耳机或无关设备。旧记录里的 `04F3:026F`（义隆 Elan）是另一台设备，与此耳机无关。官方配套软件 RoseLink（Flutter + BLE 自定义协议）与官方升级助手 first_angel_upgrade.exe 均已逆向。

**结论：2.4G dongle 模式下主机侧不存在电量数据通道。** 逐路证伪：

| 通道 | 勘察结果 |
|---|---|
| dongle MI_03 HID（usage 0x0C/0x01，in=3B，out=0，feat=0） | 只有耳机按键透传（音量/播放）；被动监听多次零帧 |
| dongle HID 端点（物理 64B INT-IN 0x82 / INT-OUT 0x02 存在） | Windows HID 栈 preparsed 全 0 不路由数据，应用/脚本均读不到 |
| USB Audio（if0 控制、if1/2 ISO 流） | 控制接口无 interrupt 状态端点；ISO 纯音频 |
| 官方 0xAB OTA 协议 | 仅在耳机**OTA 升级模式**（重新枚举出 usagePage 0xFFAB 接口）可用；正常模式无处发送；官方工具也只做 OTA 不查电量 |

**官方升级助手协议出土（OTA 模式专用，备查勿日常使用）**：report ID 0xAB（64B），查询帧 `[0x92, seq, TLV...]`，TLV=`[type, len, data]`，type 1=版本 / 3=TWS / 4=状态 / 6=通道；单查 `[0x91, 01, type]`；OTA 握手 `CC AA 55 EE 12 19 E4`；OTA 数据头 `[0xA0, seq, filesize u32...]`。设备响应 `[0x47 'G', 0x4D 'M', cmd, len, payload...]`，cmd 0x87=原始日志 / 0x88=版本(ASCII) / 0x89=OTA 数据 / 0x8A=GUID / 0x8B=GUID 回传。芯片平台 AB123A（Actions），USB 侧电量命令**不存在**。

**RoseLink 电量通道**：BLE 自定义服务 `0000AE00`（AE01=notify 设备→App，AE02=write App→设备），设备协议族 rs_common_v2（FirstAngel 专属 `first_angle_receive_handler.dart`）。不走标准 0x180F。**耳机在 2.4G 模式下不发 BLE 广播**（系统 AEP 枚举 0 台）。
**2026-10-06 更新**：RoseLink 已完成逆向，**其协议内不含电量命令**（原"继续逆向 AE00 命令字节"的待办作废）——即官方 App 侧同样拿不到电量。

**2.4G 模式电量唯一官方途径 = 耳机语音播报**（不经主机）。主机侧与官方 App 侧均已确认无通道，**此设备在本应用中不提供电量显示，勿再投入**。

## 2026-10-06 主机侧复检（只读实测，结论不变）

用户复报"2.4G 读不到电量"，对 `38FF:0001` 做了一轮独立真机复检，全部只读、零写入：

| 检查 | 结果 |
|---|---|
| USB 接口树（CM 枚举父设备全部子节点） | 仅 `MI_00`（USB Audio Device）+ `MI_03`（HID），**无隐藏厂商接口** |
| `MI_03` HID caps（`HidP_GetCaps`） | UsagePage `0x000C` / Usage `0x0001`（Consumer Control），IN=3B，**OUT=0、FEATURE=0** |
| `MI_03` 输入报文内容（`HidP_GetButtonCaps`） | 9 个 consumer 按钮（音量/播放等），**无任何电量值字段**（NumberInputValueCaps=0） |
| PnP 属性（`probe-devnode-battery.py`） | 全机仅 EDIFIER W820NB 有 `DEVPKEY_Device_BatteryLevel`(60%)，**38FF 各节点均无** |
| 蓝牙记录 | 当时系统中无炽天使的 BLE/经典蓝牙配对记录（2026-10-18 已配对经典蓝牙，见下） |

判据：`OUT=0 / FEATURE=0` ⇒ 主机**物理上无法向 dongle 发起任何查询**（连"问一句"的通道都没有）；输入报文无电量字段 ⇒ 即便被动监听也没有数据源。**2.4G 无解是硬件事实，与代码适配无关**（当前 App 里 `38FF` 本就不在任何插件候选范围，连设备列表都不会出现）。

## 2026-10-18 复检：描述符级确认 + 耳机 USB-C 直连零枚举

`scripts/probe-hid-caps.py`（只读）重取 MI_03：

```
UsagePage=0x000C Usage=0x0001   IN=3B  OUT=0B  FEATURE=0B
ButtonCaps: IN=9  ValueCaps: 0
  [in] 0x000C: E2/B6/B5/40/CF/EA/E9/CD    [in] 0x000B: 21（Hook Switch）
```

- **`FEATURE=0B` 是描述符级事实**：`HidD_GetFeature` / `IOCTL_HID_GET_REPORT(Feature)` 在 Windows 侧就被拒，**不存在"发了没回显"**——"GET_REPORT 回显设备信息"这类现象在本机永远观察不到。
- 输入侧全是按钮位、`ValueCaps=0`，**没有可承载数值的字段**：所以"低电量语音播报瞬间抓包"对炽天使无效（播报在耳机侧产生，主机侧没有落点）。
- 耳机 USB-C 直连（线已确认可传数据）：**零枚举**——无新节点、无未知设备、`LastArrivalDate` 仍是开机时刻 → **该口正常状态只充电**。
- 同刻勘察：耳机以**经典蓝牙**挂着（MAC `41:42:02:D6:1E:40`），`ROSE FIRST ANGEL Hands-Free AG` 节点 `DEVPKEY_Device_BatteryLevel` = **100%**，即**蓝牙模式下电量走 HFP 网关可读**（`Ble/BluetoothClassicWatcher.cs` 那条路）。它暴露 A2DP / AVRCP / HFP / SPP(→COM6) 与厂商服务 `0CF12D31-FAC3-4553-BD80-D6832E7B3931`。
- 蓝牙节点里的 `OTA1` 属于 **EDIFIER W820NB（`0C:AE:BD:C6:CD:A7`）**，与炽天使无关，别误认。
- 对照 Feaulle SHINE（`0124:FB01`）：其 MI_03 有厂商页 0xFF01 + 63B 双向 IN/OUT——**那台有主机→设备通道，本机没有**，两台的结论不可互推。

推论（未验证）：若 OTA 模式确实存在，`0xFFAB` 更可能是耳机进升级模式后 **dongle 重新枚举**出来的，而非耳机 USB 口给的（该口不枚举数据设备）。进升级模式的条件无记录，官方升级助手 exe 不在本机。

## 2026-10-18 绕过 HID 驱动直读端点 —— ❌ 同样证伪

前面所有结论都建立在"Windows HID 栈"之上，唯一没验证的是：**端点上有没被 HID 栈丢掉的数据**。为此用 Zadig 把 `MI_03` 从 `HidUsb` 换成 WinUSB（`oem40.inf`，libwdi 生成），用 pyusb 直读。全部只读，零写入。

**配置描述符（首次直读）**：`bNumInterfaces=4`——if0 UAC 控制、if1/2 音频流、if3 HID；if3 有 **两个 64 字节中断端点**（`0x82` IN、`0x02` OUT，`bInterval=1`）。

**原始 HID 报告描述符（47 字节，标准 GET_DESCRIPTOR，只读）**：

```
05 0C 09 01 A1 01 85 01 15 00 25 01 09 CD 09 E9 09 EA 09 CF 09 40 09 B5 09 B6 09 E2
95 08 75 01 81 02 05 0B 95 01 09 21 81 02 95 07 81 05 C0
```

8 个 consumer 位（Play/Pause、Vol±、Play、Menu、Next、Prev、Mute）+ 1 个 telephony Hook Switch + 7 位填充 = 2 字节载荷 + report ID = 3 字节。**描述符完整闭合：只有 Input，没有 Output / Feature / 厂商页。**

**直读 `0x82` 结果**：按键时只有 3 字节帧，与 HID 层完全一致（`01 01 00` / `01 00 00` / `01 40 00` / `01 20 00`）；**耳机断电再上电、重连 2.4G 期间零帧**——连"耳机是否连上"都不上报主机。

判据：`0x02` 这个 64 字节 OUT 端点物理存在，但描述符里没有任何输出报告，固件也没有向主机推状态帧的迹象 ⇒ **"绕过 HID 栈拿隐藏数据"这条路同样走不通**。加上 `FEATURE=0`，`38FF:0001` 的 HID 接口已无剩余可挖空间。

回滚：`pnputil /delete-driver oem40.inf /uninstall /force`（管理员）→ `pnputil /scan-devices`，Windows 自动装回 `HidUsb`。

## 附：Elan (VID_04F3&PID_026F) —— 已删除占位（与炽天使无关）

义隆另一台 2.4G 接收器，曾被误记为弱水时砂耳机接收器（2026-10-05 已澄清）。仅确认过 `GetFeature(0x05)` 回 ASCII `"1010"`（固件版本），电量命令未知；占位插件已从应用移除，**勿再投入**。
