# 三款无线设备电量支持审查（2026-10-19）

## 1. 基线与边界

基线 `fbcf8661fc49293cc5b3aee2e28b93c1ea342342`。范围为三个新插件、纯解析器、Feature 长度参数、协议路由、设置默认值与协议文档。协议依据来自本机官方 CHERRY 软件静态分析、M HUB / Haute 官方网页及实机报文；未复用厂商实现代码。

运行了独立控制台调用真实 Core 的 `AttachAsync`、`Poll`、`Detach`，只发送文档中列出的已知电量查询；未保存应用设置、未替换便携安装目录、未启动新 WinUI 实例。已有官方软件仍在运行。完整 GUI、长时间休眠唤醒、充电/充满与手柄无线断开动作尚未实机覆盖，不能把单测当作这些动作的实测。

## 2. 逐条检查

- R1：CHERRY 和 MchoseMouse 默认关闭；探测由基类 `ProbeWrites/WritesEnabled` 拦截，轮询有 `CanWrite`，实际写点再次检查开关。每轮最多一条有出处的查询；无命令枚举。连续失败进入基类递增退避。
- R2/R4：三个插件均继承 `BatteryPluginBase`；不创建独立句柄集合或生命周期，探测失败及 Detach 由基类释放。CHERRY 被动监听复用基类和 `IoGate`。
- R3：插件状态只通过基类读数管线更新，未新增跨线程可变集合；MchoseMouse 的通知与 Feature GET 均在同一次串行读写操作内。
- R5：I/O 失败返回 null 并由基类失败计数/日志处理；未新增吞异常。Haute 仅读取，显式 FF 断开通过 RequestOffline 处理。
- R7/R8：纯解析器无 WinRT 依赖，已链接真实源文件至 Core.Tests；包含实机报文、错误报告/命令/身份、越界、截断、低电量、Haute 断开及充电分支用例。
- 协议隔离：通用 MCHOSE 排除两个专用接收器；CHERRY 精确匹配 report 04 的 64 字节 collection；A5 精确匹配 FF01 配置通道，Feature 使用实际 21 字节并等待通知；Haute 先核对 `Haute42 ` 固件前缀，避免认领普通 Sony 手柄。
- 读数下界：新增 `MinValidPercent` 虚属性，旧插件默认仍为 2；三个新插件覆盖为 0，回归测试同时验证旧占位帧规则及新协议合法 0/1% 范围。

本次未发现仍需修复的 P0/P1 项。协议的未验证场景已明确列在协议文档及本报告，不扩大设备支持声明。

## 3. 验证证据

.NET SDK **8.0.426**：`dotnet build VigilWatt.sln -c Debug --no-restore`，0 警告、0 错误。

定向测试：CherryCodecTests、MchoseMouseCodecTests、HauteCodecTests、BatteryReadingFilterTests、MchoseV1CodecTests、WriteBackoffTests、OfflinePolicyTests、DeviceClaimRegistryTests、DeviceStatusMergerTests、DeviceChangeDetectorTests、ChargingStateMapTests；**100 / 100 通过**。未运行文档指出会卡住的无过滤全量测试。

硬件控制台输出：

```
Generic MCHOSE exclusion: PASS
Disabled write plugin gates: PASS
Cherry: CHERRY MX BOARD 8.2 Xaga 92% Discharging online=True protocol=cherry-xaga-report4
MchoseMouse: MCHOSE A5 V2 Ultra 100% Discharging online=True protocol=mchose-mouse-feature11
Haute: DualSense (Haute Adapter 4DS) 70% Discharging online=True protocol=haute4ds-feature-f9
Poll after attach: PASS (zero failures)
HARDWARE_SMOKE: 3/3
```

开关关闭测试使用独立插件实例；之后另建实例启用查询，避免基类探测失败冷却影响同一次测试。两次 Poll（跳过 FreshlyClaimed 后实际查询）均无失败。测试结束显式 Detach，应用设置只在测试进程内改动。

## 4. 风险与范围

CHERRY 的非零供电状态保留 Unknown；MCHOSE / Haute 的充电编码按官方解析实现，未做接线校准。设备关机/休眠的通知与恢复时机仍需真实动作验证；当前不据沉默臆造连接状态。支持范围限于协议文档 §13–§15 的接收器 ID 与 Haute DS5 模式。

## 5. 结论

L0 构建和相关回归通过，L1 安全/状态/生命周期自查及本次改动面静态复核完成，三款设备的真实电量读取已验证。没有本次新发现的未修复缺陷需要加入 BACKLOG；保留已有 BACKLOG 内容。
