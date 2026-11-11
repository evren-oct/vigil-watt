# 前行者 EWEADN X87 键盘 / S9 鼠标

> 从 [protocols.md](../protocols.md) 拆出（2026-10-21）。结论均来自真机验证或官方软件抓包；改协议代码前先读本文。

## 1. 前行者 EWEADN X87 键盘 / S9 鼠标 —— ✅ 两套均已打通，应用在用

两套设备都是**纯查询型**：所有 vendor 接口被动监听 8~25 秒零帧推送，必须主动发帧。协议分别来自官方驱动 `DeviceDriver.exe`（Ghidra 反编译）与官方网页驱动 `hub.eweadn.cn/by/web`（WebHID JS），两处都不是猜的。

### 1.1 X87 键盘（接收器 `0C45:FEFE`）

官方驱动 `config.xml` 只认 `VID_0C45&PID_FEFE&MI_03`（另有 USB 直连 `0C45:8006&MI_00`、`05AC:024F&MI_03` 备用）。本机枚举到 **两个** 同 usage `0xFF60/0x0061` 的接口：MI_03（in=33 out=33）与 MI_04（in=65 out=65）。**只能写 MI_03**，MI_04 长度不符必失败——插件用 `OutputReportLength == 33` 区分。

帧长 33 字节，`[0]` 是 report ID（0），`[32]` 是校验和：

```
请求：00 20 01 00 00 … 00 21      // [1]=0x20 [2]=0x01 [32]=sum([0..31])&0xFF
响应：00 20 01 00 64 00 … 00 85   // [4]=电量%   实测 0x64 = 100%
```

响应判据（官方 `FUN_004364e0`）：剥掉首字节 report ID 后 `body[0]==0x20 && body[3]!=0`，电量取 `body[3]`，>100 截为 100。官方**不解析充电位**，固定传 0——插件跟随，充电态交给 `ChargingStateMap.FromChargingFlag(false, percent)`（100% 显示为「满」）。

官方轮询间隔 15 秒。接口走 overlapped 异步 IO：同步 `ReadFile` 会永久阻塞，必须 `FILE_FLAG_OVERLAPPED` + `WaitForSingleObject` + `GetOverlappedResult`。

**坑（2026-10-18 实机踩到）**：键盘休眠时查询无应答，`Probe` 返回 null。若插件不参与快速发现节拍，只在启动时探一次，键盘睡着启动就永久漏掉（日志里只见鼠标、不见键盘）。因此 `SupportsDiscoveryProbe => true` 且 `ProbeCooldown` 必须 < 15 秒节拍（取 12 秒）。

### 1.2 S9 鼠标（`372E:1026`，贝盈 `beiyin_uuid` 协议）

`372E:1026` 是**多型号共用 PID**（S9 Pro / Max / Ultra 靠 UUID 区分），官方 `by.json` 指定 `usagePage 0xFF00 / usage 0x0001`。该接口只有一个 **feature report `0x03`**（63 字节），没有 input/output report；旁边的 `0xFF06/0x0001`（out 0x09 63B + in 0x09 31B）是**死路**——实测写成功但零响应。

帧 63 字节，`[0] = sum([1..62]) & 0xFF`：

```
请求：[0]=0x22 [1]=0x50 [2]=0x00 [3]=0x02 [4]=0x4F [5]=0x81 其余 0
响应：03 50 00 0B 02 01 49 21 07 01 90 65 04 A5 5A 00       // [6]=0x49=73%
```

解析（官方 `decodeGetBasics`，**直接读含 report ID 的原始响应，不剥首字节**）：`[6]` bit7 = 充电中、低 7 位 = 电量%；`[7]` 低 4 位 = currentProfile、高 4 位 = maxProfile；`[8]` = maxReport；`[9]` = sensorInfo；`[10] + [11]*255` = maxDpi；`[12]` = macroSize。交叉验证：`0x90 + 0x65*255 = 25899` ≈ S9 Pro 的 26000 DPI，证明偏移正确。

**坑**：`GetFeature` 必须按 `FeatureReportLength`（64）传，传 63 报错；返回值只含实际数据长度，无请求时是 5 字节残留 `03 00 02 00 00`。鼠标休眠时 dongle 回**结构合法但电量字节为 0** 的满长帧——真 0% 不存在（自动关机），codec 层直接返回 null 拒收，否则每轮都落到读数过滤器刷 WARN。

### 1.3 安全边界

键盘插件会写帧（`ProbeWrites = true`），受 `PluginEnabled["EweadnKeyboard"]` 开关与熔断退避管辖，默认关。鼠标插件只写 feature report 查询帧，同受开关管辖。两者 `Offline` 都是 `Silence = Zero + CountPollFailures = false`：休眠不应答不等于离线，靠熔断表达。
