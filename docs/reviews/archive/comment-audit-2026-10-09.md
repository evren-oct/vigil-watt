# VigilWatt 注释精简审计与 docs 归并映射（2026-10-09）

> 性质：**只读静态审计**。未改动 `src/` 或 `docs/` 下任何文件，未构建、未运行。
> 目的：为「注释精简与提炼」提供一张**可执行的逐块处置映射表**。代码里只保留「不写就会踩坑」的 why + 公共 API 一句摘要；有价值但 docs 里没有的信息归并进 `docs/`；复述代码 / 空壳摘要 / 装饰分隔线删除。**只动注释，代码逻辑一行不改。**
> 处置口径（与已确认的「激进」力度一致）：
> - `保留`：安全红线 / 协议魔法值 / 非显然的 why，压到 **≤2 行**，可留 1 行内联说明。
> - `压缩`：内容 `docs/` 已有 → 压到 **1-2 行 + docs 指针**（指向 `文件名 §小节`）。
> - `删除`：复述代码 / 空壳摘要 / 纯装饰分隔线。
> - `归纳`：`docs/` **没有**的有价值信息 → 先补进 docs 对应章节，代码侧只留 docs 指针。
>
> 行号为审计时（`src/` 当前工作树）实测值，改动后会漂移；定位到方法/常量级即可。

---

## 1. 总览统计

扫描范围：`src/` 下全部 `.cs`（跳过 `obj/`、`bin/`）+ 全部 `.xaml`。

| 指标 | 数值 |
|---|---|
| 含 ≥3 行连续注释块的文件数 | **72**（Core 42 / App 17 / tests 13，含 7 个 `.xaml` 另行计） |
| **≥3 行连续注释块总数** | **230** |
| — `VigilWatt.Core` | 168 |
| — `VigilWatt.App` | 39 |
| — `src/tests` | 23 |
| 块内注释行合计 | 1450 行 |
| 块长分布 | 3-4 行：98 ／ 5-9 行：97 ／ 10-19 行：32 ／ ≥20 行：**3** |
| 单行 `<summary>…</summary>` 出现次数 | **294**（其中一批为「变量名已说完」的冗余摘要，见 §4） |
| XAML 注释 | 约 45 处，其中 ≥3 行块仅 **4** 个（App.xaml、MainWindow.xaml、WidgetWindow.xaml×2） |

**最重的 10 个块**（按行数）：

| 行数 | 位置 | 主题 |
|---|---|---|
| 31 | `Plugins/Betop/BetopPlugin.cs:9-39` | 北通插件类头（协议/安全/占位帧，全量） |
| 26 | `Plugins/Rapoo/RapooPlugin.cs:7-32` | 雷柏插件类头（协议/离线两腿） |
| 20 | `App/WidgetWindow.xaml.cs:103-122` | Win10 透明外壳（DirectComposition 论证） |
| 19 | `Plugins/Rapoo/RapooCodec.cs:25-43` | 雷柏 0x07 帧布局 + bit7 |
| 19 | `Plugins/Rapoo/RapooPlugin.cs:64-82` | 雷柏 Offline 参数依据 |
| 18 | `Plugins/BatteryReadingFilter.cs:19-36` | 读数过滤两条规则 |
| 18 | `Utils/Log.cs:6-23` | 日志格式契约 + 时间口径 + 分级 |
| 16 | `App/Services/WidgetCompat.cs:3-18` | 小组件 OS 能力分流 |
| 15 | `Plugins/Betop/BetopPlugin.cs:70-84` | 北通 Offline 参数依据 |
| 15 | `Plugins/Mchose/MchosePlugin.cs:43-57` | 迈从 Offline 参数依据 |

**结论速览**：230 个长块中，`压缩` 占绝对多数（约 **186**）；`保留` 约 **25**（另 XAML 单行分区说明约 30 处亦按保留处理）；`归纳` **10 块 / 9 条 docs 缺失项**（见 §3）；`删除` **7**。真正需要「补进 docs」的缺失信息集中在**罗技 HID++ 报文层细节**（protocols.md §3 目前只到特征 ID 与级联顺序，未落字节级布局）与**离线墓碑 / 状态合并规则**两处。

---

## 2. 长注释块逐文件处置表

> 说明：同文件第一行给出文件名，后续行留空。`依据` 列为该块内容在 docs 中的已有位置（`文件名 §小节`）或「docs 无」。「docs 无」的有价值项统一在 §3 汇总。

### 2.1 `VigilWatt.Core`（168 块）

| 文件 | 行号 | 行数 | 主题 | 处置 | 依据 / docs 指针 |
|---|---|---|---|---|---|
| `Ble/BleBatteryWatcher.cs` | 11-15 | 5 | 类头：两条数据通道 | 压缩 | code-flow.md §3；runtime-pitfalls.md §3 |
| `Ble/BluetoothClassicWatcher.cs` | 9-18 | 10 | 类头：经典蓝牙单开一路 watcher | 压缩 | code-flow.md §3 |
| | 111-113 | 3 | 新增设备须确认「真连着」 | 压缩 | handoff.md「第二轮真机复测」③ |
| | 153-160 | 8 | VerifyConnectionAndAddAsync 论证 | 压缩 | code-flow.md §3 |
| | 202-206 | 5 | 显示名来源优先级 | 压缩 | handoff.md「经典蓝牙设备显示名修成原始 ID」 |
| | 255-258 | 4 | RefreshBattery 主动重读 | 压缩 | code-flow.md §3 |
| `Ble/ClassicAudioBattery.cs` | 7-20 | 14 | DEVPKEY + HFP 节点 + SetupAPI | 压缩 | runtime-pitfalls.md §3；code-flow.md §3 |
| | 163-166 | 4 | MacFromAepId 取 MAC | 压缩 | runtime-pitfalls.md §3 |
| `Ble/ClassicDeviceName.cs` | 5-13 | 9 | 占位名识别背景 | 压缩 | handoff.md「经典蓝牙设备显示名修成原始 ID」 |
| `Ble/ClassicPairedNames.cs` | 6-14 | 9 | 注册表配对名来源 | 压缩 | handoff.md 同上 |
| `Devices/DeviceManager.cs` | 13-17 | 5 | 类头：统一设备管理 | 压缩 | architecture.md §1 |
| | 108-118 | 11 | **离线墓碑机制与理由**（北通 dongle 退出枚举） | **归纳** | docs 无（仅 §3 记 code-flow §5） |
| | 121-124 | 4 | RestoreOfflineTombstones 只启动跑一次 | 归纳 | docs 无（并入上条） |
| | 204-206 | 3 | 快速发现节拍 | 压缩 | code-flow.md §2.1 |
| | 213-216 | 4 | RequestDiscoveryAsync 15s | 压缩 | code-flow.md §2.1 |
| | 229-235 | 7 | ApplySystemBatterySetting 撤条目 | 压缩 | code-flow.md §3.1 |
| | 253-258 | 6 | RequestPollAsync 复用进行中一轮 | 压缩 | handoff.md「第四轮：刷新按钮」 |
| | 269-280 | 12 | DiscoverOnlyAsync 两类插件 | 压缩 | code-flow.md §2.1 |
| | 292-295 | 4 | RunExclusiveAsync 用 Task.Run | 压缩 | code-flow.md §2 |
| | 320-324 | 5 | IsPluginEnabled 的 defaultOn（ble:/btc:） | 压缩 | code-flow.md §2.1 |
| | 340-348 | 9 | DropDevicesOf 理由 | 压缩 | architecture.md §6；code-flow.md §3.1 |
| | 414-416 | 3 | Attach 返回值立即 Upsert | 压缩 | code-flow.md §2.1「首扫提速」 |
| `Devices/DeviceStatusMerger.cs` | 5-7 | 3 | 类头：原地合并规则 | 压缩 | code-flow.md §5 |
| | 10-19 | 10 | **Kind/Protocol「声明覆盖猜测」规则** | **归纳** | docs 无 |
| `Hid/HidDeviceInfo.cs` | 20-23 | 4 | ReportId 语义（null / 0 约定） | 压缩 | protocols.md §4.3（部分） |
| | 75-78 | 4 | SendFeatureReport | 删除 | 复述代码 |
| | 90-97 | 8 | ReadReportId 论证（caps 唯一权威） | 压缩 | protocols.md §4.3；handoff.md |
| | 130-137 | 8 | WriteOutput quiet 语义 | 压缩 | code-flow.md §4.3 |
| `Hid/HidDeviceScanner.cs` | 39-43 | 5 | 明细仅集合变化时打 | 压缩 | logging.md §5 |
| `Hid/HidWriteProbe.cs` | 5-9 | 5 | 类头（**已退役**，仅存档） | 压缩 | architecture.md §1（标「已退役」） |
| `Hid/NativeHid.cs` | 6-9 | 4 | 类头：Win32 声明 | 压缩 | architecture.md §1 |
| | 55-59 | 5 | report ID 走 caps 的理由 | 压缩 | protocols.md §4.3 |
| | 119-122 | 4 | GetReportId 规则 | 压缩 | protocols.md §4.3 |
| | 190-196 | 7 | HIDP_CAPS 字段序（Usage 前 UsagePage 后） | 压缩 | handoff.md「北通真机验证」根因 B |
| `Models/DeviceKind.cs` | 3-10 | 8 | 显式声明设备类型、不猜 | 压缩 | architecture.md §6；handoff.md「第三轮」 |
| `Models/DeviceKindGuesser.cs` | 3-6 | 4 | 类头：按名猜兜底 | 压缩 | architecture.md §6 |
| | 9-15 | 7 | 手柄必须排在键鼠之前 | 压缩 | handoff.md「第二轮」③ |
| `Plugins/BatteryPluginBase.cs` | 7-19 | 13 | 类头：通用机制清单 | 压缩 | architecture.md §3；code-flow.md §4 |
| | 28-31 | 4 | SupportsFastDiscovery | 压缩 | code-flow.md §2.1；safety.md 规则4 |
| | 34-38 | 5 | SupportsDiscoveryProbe + Cooldown | 压缩 | code-flow.md §2.1 |
| | 47-54 | 8 | SupportsPassiveListen ≠ CountPollFailures | 压缩（留 1 行 why） | code-flow.md §4.2；handoff.md §12 |
| | 57-62 | 6 | Offline 三层配置定位 | 压缩 | code-flow.md §4.2 |
| | 65-69 | 5 | TryParsePassiveInput 契约 | 压缩 | code-flow.md §4.2 |
| | 73-80 | 8 | ProbeCooldown 相位坑 | 压缩 | code-flow.md §2.1 |
| | 107-111 | 4 | OfflineCandidateSince | 压缩 | code-flow.md §4.2 |
| | 113-116 | 4 | PendingConfirm 请求 | 压缩 | code-flow.md §4.2 |
| | 132-137 | 6 | MaxPlausibleStepPercent | 压缩 | runtime-pitfalls.md §12 |
| | 140-143 | 4 | AnomalyPersistWindow | 压缩 | runtime-pitfalls.md §12 |
| | 252-254 | 3 | 认领却无读数 = 离线 | 压缩 | handoff.md「认领但离线」规则 |
| | 295-298 | 4 | 收到任何报文都算存活 | 压缩 | code-flow.md §4.2；handoff.md §8 |
| | 335-340 | 6 | MarkOffline 唯一落地点（幂等） | 压缩 | code-flow.md §4.2 |
| | 352-358 | 7 | 延迟离线确认 tick | 压缩 | code-flow.md §4.2 |
| | 365-378 | 14 | RunPendingConfirm「立刻核实」 | 压缩 | code-flow.md §4.2 |
| | 402-406 | 5 | ConsumeOfflineRequest | 压缩 | code-flow.md §4.2 |
| | 456-458 | 3 | 判据二：查询失败熔断 | 压缩 | code-flow.md §4.2 |
| | 482-486 | 5 | TryApplyReading | 压缩 | code-flow.md §4.2 |
| | 556-561 | 6 | RequestOffline（零歧义证据） | 压缩 | code-flow.md §4.2 |
| | 564-571 | 7 | RequestConfirm（触发器非证据） | 压缩 | code-flow.md §4.2；protocols.md §1.3 |
| | 574-580 | 7 | MarkOfflineCandidate（幂等） | 压缩 | code-flow.md §4.2 |
| | 591-604 | 14 | ReportPollFailure（插件自报账） | 压缩 | code-flow.md §4.2；handoff.md §14 |
| | 635-637 | 3 | PollDevice 抽象 | 压缩 | code-flow.md §4.2 |
| `Plugins/BatteryReadingFilter.cs` | 19-36 | 18 | 两条规则 + 默认值 + 基准不清空 | 压缩（留 1 行 why） | runtime-pitfalls.md §12；code-flow.md §4.2 |
| `Plugins/Betop/BetopCodec.cs` | 3-6 | 4 | 类头：纯帧编解码 | 压缩 | protocols.md §4 |
| | 46-49 | 4 | T9NotLinkedRaw = 0x50（占位值） | 保留 | protocols.md §4.4.1 |
| | 55-58 | 4 | T9Status 记录 | 压缩 | protocols.md §4.4 |
| | 61-64 | 4 | ParseT9Status（只认 report ID） | 压缩 | protocols.md §4.4 |
| | 83-87 | 5 | CleanProductName | 压缩 | handoff.md「第二轮」① |
| | 114-118 | 5 | BuildQuery（写满接口长度） | 压缩 | protocols.md §4.3 |
| | 140-143 | 4 | ParseStatus（兼容带/不带前缀） | 压缩 | protocols.md §4.3 |
| `Plugins/Betop/BetopPlugin.cs` | 9-39 | 31 | 类头：协议/安全/占位帧全量 | 压缩（→3-4 行 + 指针） | protocols.md §4、§4.4.1、safety.md |
| | 64-67 | 4 | SupportsPassiveListen | 压缩 | protocols.md §4.4 |
| | 70-84 | 15 | Offline 参数依据 | 压缩 | code-flow.md §4.2 |
| | 91-96 | 6 | ProbeCooldown 30s | 压缩 | code-flow.md §2.1 |
| | 99-103 | 5 | IsCandidate | 压缩 | protocols.md §4.1 |
| | 110-120 | 11 | Probe（占位帧也算成功） | 压缩 | protocols.md §4.4.1 |
| | 147-149 | 3 | TryParsePassiveInput | 删除 | 复述签名 |
| | 171-174 | 4 | QueryResult | 压缩 | protocols.md §4.3 |
| | 177-180 | 4 | Query | 压缩 | protocols.md §4.3 |
| | 249-253 | 5 | ResolvePlan（report ID 从 caps 读） | 压缩 | protocols.md §4.3 |
| `Plugins/DeviceClaimRegistry.cs` | 5-9 | 5 | 类头：中央认领 | **压缩 + 修正** | 含过时引用 `DeviceManager.SafePoll`（见 §5） |
| `Plugins/Elan/ElanHeadsetPlugin.cs` | 7-11 | 5 | 类头：纯观察模式 | 压缩 | protocols.md §3（Elan） |
| `Plugins/IBatteryPlugin.cs` | 6-9 | 4 | 接口头 | 压缩 | architecture.md §1 |
| | 20-25 | 6 | SupportsFastDiscovery 契约 | 压缩 | code-flow.md §2.1；safety.md 规则4 |
| | 28-37 | 10 | SupportsDiscoveryProbe + 安全前提 | 压缩 | code-flow.md §2.1；safety.md 规则4 例外 |
| | 47-50 | 4 | StatusPushed | 压缩 | architecture.md §3 |
| `Plugins/Logitech/HidppCatalog.cs` | 7-10 | 4 | 类头：嵌入目录 | 压缩 | protocols.md §3 |
| `Plugins/Logitech/HidppCodec.cs` | 5-12 | 8 | 类头：响应解析布局 | 压缩 | protocols.md §3 |
| | 15-17 | 3 | 短/长偏好 | 压缩 | protocols.md §3 |
| | 47-50 | 4 | SelectReportId | 压缩 | protocols.md §3 |
| | 60-65 | 6 | BuildFeatureIdAndFunc ⚠ | 保留（留 ⚠ 1 行） | protocols.md §3 |
| | 69-72 | 4 | GetFeatureIndex + 旧 bug | 压缩 | protocols.md §3 |
| | 76-79 | 4 | ParseBatteryStatus | 压缩 | protocols.md §3 |
| | 94-97 | 4 | **Battery Voltage 0x1001 flags 位语义** | **归纳 + 保留** | docs 无 |
| | 128-130 | 3 | **ADC 0x1F20 flags 值** | **归纳 + 保留** | docs 无 |
| | 157-159 | 3 | 电压曲线插值 | 删除 | 复述函数 |
| | 182-185 | 4 | **HID++2.0 充电状态码表** | **归纳 + 保留** | docs 无 |
| | 193-196 | 4 | 配对信息 kind 字节 | 压缩 | protocols.md §3 |
| | 213-216 | 4 | **Unifying 0x2N 配对字节布局** | **归纳 + 保留** | docs 无 |
| | 228-231 | 4 | **Bolt 0x5N 配对字节布局** | **归纳 + 保留** | docs 无 |
| | 243-248 | 6 | ShouldSkipInterface | 压缩 | protocols.md §3 |
| `Plugins/Logitech/HidppIO.cs` | 6-15 | 10 | **短/长报文布局 + 错误响应 + 通知 sw** | **归纳 + 保留** | docs 无（protocols.md §3 未落字节级） |
| | 31-34 | 4 | NotificationReceived | 压缩 | protocols.md §3 |
| | 44-47 | 4 | Request | 压缩 | protocols.md §3 |
| | 83-86 | 4 | PumpNotifications | 压缩 | protocols.md §3 |
| | 144-147 | 4 | IsNotification | 压缩 | protocols.md §3 |
| `Plugins/Logitech/LogitechPlugin.cs` | 8-14 | 7 | 类头：级联 + 目录 | 压缩 | protocols.md §3 |
| | 23-26 | 4 | ping 超时 ≥1.5s | 压缩 | protocols.md §3 |
| | 48-50 | 4 | IsHidppCandidate（两字段都查） | 压缩 | protocols.md §3 |
| | 385-388 | 4 | 0x1004 func1/func0 | 压缩（留 ⚠） | protocols.md §3 |
| | 530-533 | 4 | 配对信息 0xB5 | 压缩 | protocols.md §3 |
| `Plugins/Mchose/MchosePlugin.cs` | 9-22 | 14 | 类头：V1 协议 + QHW + 安全 | 压缩 | protocols.md §1、§2；safety.md |
| | 43-57 | 15 | Offline 参数依据 | 压缩 | code-flow.md §4.2 |
| | 64-69 | 6 | SupportsDiscoveryProbe | 压缩 | code-flow.md §2.1 |
| | 72-81 | 10 | ProbeCooldown 10s | 压缩 | code-flow.md §2.1 |
| | 99-103 | 5 | KnownProtocolByPath（失败不清） | 压缩 | code-flow.md §4.1 |
| | 106-112 | 7 | LogOnceByKey 去重 | 压缩 | logging.md §5；code-flow.md §4.1 |
| | 135-140 | 6 | 已知协议失败不清记忆 | 压缩 | code-flow.md §4.1 |
| | 184-188 | 5 | 失败记账（自报） | 压缩 | code-flow.md §4.2 |
| | 195-198 | 4 | TryParsePassiveInput | 压缩 | protocols.md §1.3 |
| | 209-215 | 7 | payload 0x00 当触发器 | 压缩 | protocols.md §1.3；code-flow.md §4.2 |
| | 306-311 | 6 | SendHandshake quiet | 压缩 | code-flow.md §4.3 |
| | 321-325 | 5 | TryQhw 官方时序 | 压缩 | protocols.md §2.2 |
| | 347-352 | 6 | 先握手省 3 秒 | 压缩 | code-flow.md §4.1 |
| | 425-437 | 13 | keyboard-v1 协议全量 | 压缩 | protocols.md §1.3 |
| | 496-499 | 4 | InterpretInputFrame | 压缩 | protocols.md §1.3 |
| | 515-522 | 8 | 0x02 不当休眠 | 压缩 | protocols.md §1.3 |
| `Plugins/Mchose/MchoseV1Codec.cs` | 32-46 | 15 | 帧布局 + 类型位关键 | 压缩 | protocols.md §1.2、§1.3 |
| | 60-64 | 5 | StatePayloadZero 语义未定 | 压缩 | protocols.md §1.3 |
| | 100-103 | 4 | ParsePush | 压缩 | protocols.md §1.3 |
| `Plugins/OfflinePolicy.cs` | 3-16 | 14 | 类头：三层配置 + 三判据 | 压缩 | code-flow.md §4.2 |
| | 22-26 | 5 | Silence | 压缩 | code-flow.md §4.2 |
| | 29-39 | 11 | Confirm（延迟确认理由） | 压缩 | code-flow.md §4.2 |
| | 42-46 | 5 | MaxFailures | 压缩 | code-flow.md §4.2 |
| | 49-61 | 13 | CountPollFailures（必须与监听分开） | 压缩（留 1 行 why） | code-flow.md §4.2；handoff.md §12 |
| | 64-70 | 7 | IsConfirmExpired 边界 | 压缩 | code-flow.md §4.2 |
| `Plugins/Rapoo/RapooCodec.cs` | 25-43 | 19 | 帧布局 + bit7 断链 | 压缩 | protocols.md §3 |
| | 52-55 | 4 | Parse | 压缩 | protocols.md §3 |
| `Plugins/Rapoo/RapooPlugin.cs` | 7-32 | 26 | 类头：协议出处 + 离线两腿 | 压缩 | protocols.md §3 |
| | 41-52 | 12 | ProbeCooldown 3min→5s 历史 | 压缩 | handoff.md「第五轮」 |
| | 64-82 | 19 | Offline 参数依据 | 压缩 | protocols.md §3；code-flow.md §4.2 |
| | 90-98 | 9 | IsCandidate ==19 | 压缩 | protocols.md §3；code-flow.md §2.1 |
| | 106-110 | 5 | Probe 5s 窗口 | 压缩 | protocols.md §3 |
| | 118-121 | 4 | PollDevice 不占 ReadInput | 压缩 | code-flow.md §4.2 |
| `Power/SystemBatteryWatcher.cs` | 6-18 | 13 | 类头：事件式数据源 + 充电推导 | 压缩 | code-flow.md §3.1 |
| | 44-47 | 4 | Refresh | 压缩 | code-flow.md §3.1 |
| | 59-66 | 8 | Stop 撤条目 | 压缩 | code-flow.md §3.1 |
| | 125-127 | 3 | DeriveCharging | 压缩 | code-flow.md §3.1 |
| `Power/SystemPowerReader.cs` | 5-8 | 4 | 快照记录 | 压缩 | code-flow.md §3.1 |
| | 15-25 | 11 | 为何用 GetSystemPowerStatus | 压缩 | code-flow.md §3.1；architecture.md §3 |
| | 55-58 | 4 | Read 失败退化 | 压缩 | code-flow.md §3.1 |
| | 72-75 | 4 | Interpret 可单测 | 压缩 | code-flow.md §3.1 |
| `Settings/AppSettings.cs` | 26-30 | 5 | WidgetMaxDevices | 压缩 | code-flow.md §7 |
| | 33-37 | 5 | WidgetDisplayMode | 压缩 | runtime-pitfalls.md §15 |
| | 45-49 | 5 | WindowBackdrop | 压缩 | runtime-pitfalls.md §13 |
| | 69-75 | 7 | MonitorSystemBattery | 压缩 | code-flow.md §3.1 |
| | 78-81 | 4 | LogLevel | 压缩 | logging.md §3 |
| | 116-119 | 4 | PluginEnabled 补键 | 压缩 | architecture.md §6 |
| `Storage/HistoryStore.cs` | 204-208 | 5 | VACUUM 理由 | 压缩 | architecture.md §3 |
| `Utils/AppPaths.cs` | 3-5 | 3 | 类头 | 删除 | 空壳摘要（见 §4） |
| `Utils/ByteSize.cs` | 3-6 | 4 | 类头 | 压缩 | code-flow.md §7 |
| `Utils/Log.cs` | 6-23 | 18 | 格式契约 + 时间口径 + 分级 | 压缩（+ 修正指针） | logging.md §4、§6.5（**代码写 §3.4，实为 §4**，见 §5） |
| | 65-68 | 4 | MinLevel | 压缩 | logging.md §3 |
| | 78-85 | 7 | 降噪入口 | 压缩 | logging.md §5 |
| | 90-96 | 7 | InfoOnChange | 压缩 | logging.md §5 |
| | 181-184 | 4 | Sanitize（一行一条） | 压缩 | logging.md §4 |
| | 284-288 | 5 | WriteTimeBasis | 压缩 | logging.md §6.5 |
| `Utils/LogLevel.cs` | 3-7 | 5 | Off 哨兵 | 压缩（留 why） | logging.md §2、§9 |
| | 17-21 | 5 | LogLevelNames 单一来源 | 压缩 | logging.md §2 |

### 2.2 `VigilWatt.App`（39 块）

| 文件 | 行号 | 行数 | 主题 | 处置 | 依据 / docs 指针 |
|---|---|---|---|---|---|
| `App.xaml.cs` | 35-37 | 3 | 托盘事件订阅 | 压缩 | runtime-pitfalls.md §5.1 |
| | 77-81 | 5 | ShutdownLogging（幂等） | 压缩 | logging.md §6.4 |
| `MainWindow.xaml.cs` | 30-35 | 6 | 标题栏融入（Win10 双标题） | 压缩 | runtime-pitfalls.md §13 |
| | 55-58 | 4 | ApplyBackdrop 三级回退 | 压缩 | runtime-pitfalls.md §13 |
| | 90-94 | 5 | 系统按钮前景色 | 压缩 | runtime-pitfalls.md §13 |
| | 113-117 | 5 | ApplyWindowIcon（构造期吞异常） | 压缩 | runtime-pitfalls.md §5.2 |
| `Program.cs` | 6-11 | 6 | Velopack 自定义入口 | 压缩 | build.md §8「应用侧接入」 |
| `Services/LogLevelOptions.cs` | 5-9 | 5 | 档位展示 | 压缩 | logging.md §3 |
| `Services/Native.cs` | 140-146 | 7 | Win11 原生圆角 | 压缩 | runtime-pitfalls.md §14 |
| | 153-156 | 4 | DWM 边框/阴影策略 | 压缩 | runtime-pitfalls.md §14 |
| | 174-180 | 7 | MakeWindowTransparent 论证 | 压缩 | runtime-pitfalls.md §14 |
| | 204-207 | 4 | MakeWindowTransparent 摘要 | 压缩 | runtime-pitfalls.md §14 |
| `Services/TrayService.cs` | 9-14 | 6 | 类头：纯 Win32 托盘 | 压缩 | runtime-pitfalls.md §5；code-flow.md §8 |
| | 46-55 | 10 | RequestUpdate 合并 + 回 UI 线程 | 压缩 | runtime-pitfalls.md §5.1 |
| | 150-152 | 3 | 本机电量不参与托盘 | 压缩 | code-flow.md §8 |
| | 222-224 | 3 | 提示变化才记日志 | 压缩 | runtime-pitfalls.md §5.1 |
| | 301-304 | 4 | ExitApplication 唯一退出通道 | 压缩 | architecture.md §3 |
| `Services/UiPalette.cs` | 6-9 | 4 | 类头：状态色板 | 压缩 | runtime-pitfalls.md §11 |
| `Services/UpdateService.cs` | 6-11 | 6 | 类头：Velopack 包装 | 压缩 | build.md §8 |
| `Services/WidgetCompat.cs` | 3-18 | 16 | 类头：OS 能力分流 | 压缩 | runtime-pitfalls.md §14 |
| | 24-27 | 4 | NeedsTransparentChrome | 压缩 | runtime-pitfalls.md §14 |
| `Services/WidgetDisplayMode.cs` | 3-13 | 11 | 类头：三档层级 | 压缩 | runtime-pitfalls.md §15 |
| | 42-45 | 4 | WidgetMaxDevicesOptions | 压缩 | architecture.md §3 |
| `Services/WindowBackdrop.cs` | 6-14 | 9 | 类头：材质回退 | 压缩 | runtime-pitfalls.md §13 |
| | 38-41 | 4 | Resolve | 压缩 | runtime-pitfalls.md §13 |
| `ViewModels/DeviceRowModel.cs` | 68-74 | 7 | GlyphFor（优先 Kind） | 压缩 | handoff.md「第三轮」；architecture.md §6 |
| `ViewModels/HistoryDeviceRow.cs` | 5-9 | 5 | 类头：不做双向绑定 | 压缩 | handoff.md「第七轮」坑2 |
| `Views/BatteryChartControl.xaml.cs` | 11-17 | 7 | 类头：自绘曲线 + domain | 压缩 | runtime-pitfalls.md §9 |
| `Views/HistoryPage.xaml.cs` | 8-12 | 5 | 类头：删除须 VACUUM | 压缩 | architecture.md §3 |
| `Views/SettingsPage.xaml.cs` | 127-131 | 5 | 动态生成插件行 | 压缩 | architecture.md §6 |
| `Views/StatsPage.xaml.cs` | 61-63 | 3 | 设备下拉按历史库取 | **归纳** | docs 无（低优先，code-flow §7） |
| `WidgetWindow.xaml.cs` | 15-26 | 12 | 类头：两套外壳 | 压缩 | runtime-pitfalls.md §14 |
| | 46-49 | 4 | Win10 圆角半径 | 压缩 | runtime-pitfalls.md §14 |
| | 103-122 | 20 | ApplyWin10Chrome 论证 | 压缩 | runtime-pitfalls.md §14 |
| | 209-211 | 3 | 高度封顶 | 压缩 | architecture.md §3 |
| | 231-240 | 10 | ApplyRoundedCorner（仅 Win11） | 压缩 | runtime-pitfalls.md §14 |
| | 524-527 | 4 | 小组件配色硬编码 | 压缩 | runtime-pitfalls.md §11 |
| | 560-563 | 4 | ApplySettings 即时生效 | 压缩 | code-flow.md §9 |
| | 571-583 | 13 | ApplyDisplayMode 三档 | 压缩 | runtime-pitfalls.md §15 |

### 2.3 `src/tests`（23 块）

> 测试文件的块几乎全是**测试类头**：说明「这个类测什么 + 用了哪些真机帧 + 为什么必须单测」。它们承载的是**测试意图**（docs 里没有逐类对应），建议**保留但压到 ≤3 行**（删掉重复的协议解释，只留「测什么 + 真机帧来源 + 回归点」）。个别含独特取证信息的按 `归纳`。

| 文件 | 行号 | 行数 | 主题 | 处置 | 依据 |
|---|---|---|---|---|---|
| `AssemblyInfo.cs` | 3-5 | 3 | 关闭并行（Log 全局静态） | 压缩 | logging.md §7（N6） |
| `BatteryReadingFilterTests.cs` | 6-9 | 4 | 类头 | 压缩 | runtime-pitfalls.md §12 |
| `BetopCodecTests.cs` | 6-11 | 6 | 类头（鲲鹏20 真机帧） | 压缩 | protocols.md §4.3 |
| | 116-119 | 4 | 占位帧用例 | 压缩 | protocols.md §4.4.1 |
| | 127-130 | 4 | byte[1]=0x0C 真机帧 | 压缩 | handoff.md「第五轮」② |
| | 138-141 | 4 | 关机帧（17:03 起） | 压缩 | protocols.md §4.4.1 |
| | 225-227 | 3 | 换算 vs 占位 | 压缩 | protocols.md §4.4 |
| `ByteSizeTests.cs` | 6-8 | 3 | 类头 | 压缩 | — |
| `ClassicDeviceNameTests.cs` | 6-10 | 5 | 类头（真机占位名） | 压缩 | handoff.md「经典蓝牙设备显示名」 |
| `DeviceKindGuesserTests.cs` | 6-9 | 4 | 类头 | 压缩 | handoff.md「第三轮」 |
| `DeviceStatusMergerTests.cs` | 7-11 | 5 | 类头（墓碑接管） | 压缩 | （配合 §3 归纳项 7） |
| `HidppCodecTests.cs` | 7-10 | 4 | 类头 | 压缩 | protocols.md §3 |
| `HistoryStoreTests.cs` | 8-12 | 5 | 类头（真 SQLite） | 压缩 | architecture.md §3 |
| `LogTests.cs` | 6-9 | 4 | 类头 | 压缩 | logging.md §7 |
| `MchoseV1CodecTests.cs` | 6-10 | 5 | 类头（真机帧 + 休眠回归） | 压缩 | protocols.md §1.3 |
| `OfflinePolicyTests.cs` | 6-11 | 6 | 类头 | 压缩 | code-flow.md §4.2 |
| | 16-20 | 5 | 雷柏取值硬编码松动点 | 保留 | （真实风险提示，勿删） |
| | 35-37 | 3 | 默认面向查询型插件 | 压缩 | code-flow.md §4.2 |
| `RapooCodecTests.cs` | 7-10 | 4 | 类头（真机断链帧） | 压缩 | protocols.md §3 |
| `SystemBatteryChargingTests.cs` | 7-14 | 8 | 类头：为何需要这层 | 压缩 | code-flow.md §3.1 |
| `SystemPowerReaderTests.cs` | 6-15 | 10 | 类头：三种「读不到」语义 | 压缩 | code-flow.md §3.1 |
| `TestFrames.cs` | 3-6 | 4 | 类头：真机帧 | 压缩 | protocols.md §1.3 |
| `WidgetCompatTests.cs` | 6-9 | 4 | 类头（W1–W4） | 压缩 | runtime-pitfalls.md §14 |

### 2.4 XAML（7 文件，约 45 处；≥3 行块仅 4 个）

| 文件 | 行号 | 行数 | 主题 | 处置 | 依据 |
|---|---|---|---|---|---|
| `App.xaml` | 11-15 | 5 | 字体统一（Win10 排版差） | 压缩 | runtime-pitfalls.md §13 |
| | 19 / 27 / 32 / 43 | 1 | 卡片样式说明 | 保留 | — |
| `MainWindow.xaml` | 8-10 | 3 | Root 背景随材质 | 压缩 | runtime-pitfalls.md §13 |
| | 17-18 | 2 | 标题栏融入区 | 保留 | — |
| `Views/DevicesPage.xaml` | 9-10 | 2 | 标题行补齐 | 保留 | — |
| `Views/HistoryPage.xaml` | 9/23/26/68/93 | 1 | 分区标记 + CheckBox MinWidth | 保留 | handoff.md「第七轮」坑1 |
| `Views/SettingsPage.xaml` | 7-8 | 2 | 设置行统一语言 | 压缩 | — |
| | 31/41/48/73/115/147/168/177/186/225/233/247/279/319 | 1 | 分区标记 | 保留 | — |
| `Views/StatsPage.xaml` | 8/24/44/86 | 1 | 分区标记 | 保留 | — |
| `WidgetWindow.xaml` | 7-17 | 11 | 间距常量同步 + 圆角两条路 | 压缩 | runtime-pitfalls.md §14 |
| | 36-40 | 5 | ScrollViewer 上限 | 压缩 | architecture.md §3 |

---

## 3. 归纳清单（`docs/` 确实缺失、需补进文档）

> 以下 9 条是审计确认**在 `docs/` 里找不到**（已逐条 grep 核对）、但代码注释里有价值的信息。按「最有价值」排序。补完 docs 后，代码侧对应块改为 1 行 + 指针即可。

| # | 缺失信息 | 代码出处 | 建议补进 |
|---|---|---|---|
| **1** | **HID++ 底层报文格式**：短报文 7 字节 `[0x10, devIdx, feature, (func<<4)\|sw, p0..p2]`；长报文 20 字节 `[0x11, devIdx, feature, …, p0..p15]`；错误响应 `data[0]=0x8F(1.0)/0xFF(2.0)`、`data[1..2]` 回显、`data[3]`=错误码；设备自发通知 `sw` 低 4 位=0（请求应答 SwId=0x0B） | `Plugins/Logitech/HidppIO.cs:6-15` | `docs/hardware/protocols.md §3 罗技` |
| **2** | **HID++ Battery Voltage (0x1001) flags 位语义**：`bit7`=充电中；低 3 位 `1`=充满；`bit5`=危急 | `Plugins/Logitech/HidppCodec.cs:94-97` | `docs/hardware/protocols.md §3 罗技` |
| **3** | **HID++ ADC Measurement (0x1F20) flags 值**：`0x01`=放电 / `0x03`=充电 / `0x07`=充满 | `Plugins/Logitech/HidppCodec.cs:128-130` | `docs/hardware/protocols.md §3 罗技` |
| **4** | **HID++ 2.0 统一充电状态码表**：`1/2/4`=充电中、`3`=充满、其余=放电（Unified Battery 与 Battery Status 同表） | `Plugins/Logitech/HidppCodec.cs:182-185` | `docs/hardware/protocols.md §3 罗技` |
| **5** | **HID++ 配对信息（0xB5）字节布局**：Unifying `0x2N` 子寄存器 → 参数区 `[3..4]`=WPID、`[7]`=kind；Bolt `0x5N` → `[1]`=kind、`[2..3]`=WPID（小端，交换后成大端） | `Plugins/Logitech/HidppCodec.cs:213-216,228-231` | `docs/hardware/protocols.md §3 罗技` |
| **6** | **离线墓碑（Tombstone）机制与存在理由**：设备「消失」有两条路径——① 2.4G 拔/关机 → 插件标离线、条目留在 `_devices`（字典从不删）；② 蓝牙/dongle 不被枚举 → **从未进过列表**（北通关机后接收器连 HID 枚举都退出，实测 19:02 后扫描再无 `VID_20BC`）。后者导致「共 N 台」恒等于「N 台在线」。解法：用历史库最近见过的记录补离线条目（`TombstoneWindow` 内有历史、当前不在位），只在启动后跑一次 | `Devices/DeviceManager.cs:108-124` | `docs/project/code-flow.md §5`（或 architecture.md） |
| **7** | **DeviceStatus 合并的「声明覆盖猜测」规则**：离线墓碑先于插件建条目，`Kind` 只能按历史名猜（真机名一个关键字都没有 → 全 Unknown）；设备上线后插件声明的 `Kind`/`Protocol` 必须能覆盖墓碑的猜测，否则图标在进程内冻结。反向不成立：`Unknown` / 空串视为「本次未声明」，不得洗掉已知类型 | `Devices/DeviceStatusMerger.cs:10-19` | `docs/project/architecture.md §6`（或 code-flow §5） |
| **8** | **StatsPage 设备下拉按历史库取**：所选时间范围内有过采样的设备都在列（含已下线 / 换过 Id 的历史设备），**不跟随当前在线设备**——否则旧协议采的曲线永远选不到 | `Views/StatsPage.xaml.cs:61-63` | `docs/project/code-flow.md §7`（低优先） |
| **9** | **HID 输出 report ID 的取值约定**：`null`=读不到；`0`=接口不使用 report ID（报文首字节即数据、无 ID 前缀）；`>0`=该 ID。HID++ 长接口应 `0x11`、短接口 `0x10` | `Hid/HidDeviceInfo.cs:20-23`、`Hid/NativeHid.cs:119-122` | `docs/hardware/protocols.md §4.3`（低优先） |

> 其余约 220 个块的内容经逐条核对**均能在 docs 中找到对应章节**（见 §2 的 `依据` 列），无需新增文档，按 `压缩` 处理即可。**没有为了凑数而编造的缺失项。**

---

## 4. 可直接删除的极短摘要清单

> 判定标准：`/// <summary>` 的内容**完全由成员名 + 声明字面量即可推知**，属于「变量名已说完」的复述。以下按文件列出（行号为审计时实测）。可直接删，不必改写。

### 4.1 明确可删（复述名/字面量）

| 文件 | 行号 | 原文（摘要） | 挂在 |
|---|---|---|---|
| `Plugins/Betop/BetopCodec.cs` | 19 | `整帧偏移：电量百分比（0~100）。` | `BatteryOffset = 2` |
| `Plugins/Betop/BetopCodec.cs` | 22 | `整帧偏移：充电标志所在字节（取低 4 位）。` | `ChargeOffset = 4` |
| `Plugins/Betop/BetopCodec.cs` | 37 | `T9 状态流帧最小长度（含 report ID）。` | `T9MinLength = 24` |
| `Plugins/Betop/BetopCodec.cs` | 40 | `T9 电量原始字节所在偏移。` | `T9BatteryOffset = 23` |
| `Plugins/Betop/BetopCodec.cs` | 16 | `header 字节 = (subcmd << 4) \| cmd = 0x15。` | `StatusHeader`（表达式即代码） |
| `Plugins/Betop/BetopCodec.cs` | 52 | `把 T9 原始电量字节（0~120）换算成百分比…` | `T9ToPercent()` |
| `Plugins/Logitech/HidppCodec.cs` | 41 | `HID++ 2.0 Battery Voltage 特征。` | `FeatBatteryVoltage` |
| `Plugins/Logitech/HidppCodec.cs` | 44 | `HID++ 2.0 ADC Measurement 特征（G 系耳机等）。` | `FeatAdcMeasurement` |
| `Plugins/Mchose/MchoseV1Codec.cs` | 74 | `帧总长度。` | `FrameLength` |
| `Plugins/PluginUtils.cs` | 3 | `插件间共享的小工具。` | 类头（空壳） |
| `Utils/AppPaths.cs` | 10 | `应用数据根目录。` | `RootDir` |
| `Utils/AppPaths.cs` | 3-5 | `统一管理应用数据根目录…`（3 行） | 类头（空壳） |
| `Utils/LogLevel.cs` | 30 | `默认级别。` | `Default` |
| `Utils/LogLevel.cs` | 59 | `枚举 → 规范字符串。` | `ToName()` |
| `Utils/Log.cs` | 138 | `把字节数组格式化成十六进制，协议探测时用。` | `Hex()` |
| `Models/DeviceKind.cs` | 20 | `本机（笔记本电池 / 系统电源）。` | `Computer` 枚举值 |
| `Plugins/Betop/BetopPlugin.cs` | 147-149 | `被动解析 T9 状态流…`（3 行，复述签名） | `TryParsePassiveInput` |
| `Hid/HidDeviceInfo.cs` | 75-78 | `发送 feature report 并立即读回…`（复述方法名+签名） | `SendFeatureReport` |

### 4.2 建议删/并（描述性但信息量≈0，可合进所在方法的注释）

| 文件 | 行号 | 原文 | 说明 |
|---|---|---|---|
| `Utils/Log.cs` | 40 / 43 / 46 / 49 / 52 | `滚动保留的历史份数…` / `兜底文件上限…` / `连续写失败达…` / `退避时间…` / `节流间隔…` | 常量名+默认值已表达；保留 1 行汇总即可 |
| `BatteryPluginBase.cs` | 25 / 41 / 44 / 95 / 97 / 99 / 101 / 103 / 105 | 字段级一行摘要 | 大多可由字段名推知，保留「非显然」的 1-2 条 |
| `DeviceManager.cs` | 36 / 41 / 44 / 47 / 50 / 66 / 174 / 219 / 366 / 440 / 507 | 字段/事件级一行摘要 | 同上 |
| `HistoryStore.cs` | 9 / 12 / 15 / 69 / 116 / 143 / 163 / 190 | 记录/方法级一行摘要 | 同上（`GetStats`/`GetDeviceStats` 语义可留 1 行） |
| `DeviceStatus.cs` | 24 / 31 / 34 / 39 / 46 | 字段级一行摘要 | 保留 `BatteryPercent`（null 语义）与 `Kind`（兜底）两条 |
| `App/Services/WidgetDisplayMode.cs` | 16 / 19 / 22 | 枚举值一行摘要 | 枚举名已表达，可删 |
| `App/Services/WindowBackdrop.cs` | 17 / 20 / 23 / 26 / 29 | 枚举值一行摘要 | 保留 `Acrylic`（省电模式回退）一条 |

> 统计：`§4.1` 明确可删 **18** 处；`§4.2` 建议删/并 **约 40** 处。删除后单行摘要从 294 降到约 **230**。

---

## 5. 不可删 / 需核对清单

> 以下注释**不能直接删**：或承载安全红线、或为对外契约、或已被 review 点名为「失实注释」需先核对实际行为再改写。

### 5.1 安全红线 / 硬件写入（删了会踩坑）

| 位置 | 内容 | 处理 |
|---|---|---|
| `Hid/HidDeviceInfo.cs:130-137` | WriteOutput `quiet` 语义 + **「别用它掩盖真正的写入故障」** | **保留**（压到 2 行），安全口径 |
| `Hid/HidDeviceInfo.cs:90-97` | report ID 必须从 caps 读、**绝不能硬编码** | **保留**（压到 2 行） |
| `Plugins/Betop/BetopCodec.cs:46-49` | `0x50` 占位值 = 未连接（零风险判据） | **保留** |
| `Plugins/Logitech/HidppCodec.cs:60-65` | ⚠ 切勿写成 `(feature<<8)\|func`（会被 SwId 补丁抹成 func 0） | **保留 ⚠ 1 行** |
| `Plugins/BatteryPluginBase.cs:47-54` / `OfflinePolicy.cs:49-61` | `SupportsPassiveListen` 与 `CountPollFailures` **必须分开**（合成一个布尔量 → 迈从关机永不显示离线） | **保留 1 行 why** |
| `Plugins/BatteryReadingFilter.cs:19-36` | 基准**不随离线清空**（休眠伪读数常在掉线/回来窗口） | **保留 1 行 why** |
| `Plugins/Logitech/LogitechPlugin.cs:385-388` | `0x1004` func 1 才是电量、func 0=Capabilities（真机误读 15%） | **保留 1 行 why** |
| `Plugins/Mchose/MchosePlugin.cs:135-140` / `Plugins/Rapoo/RapooPlugin.cs:90-98` | 已知协议失败不清记忆 / IsCandidate 收敛 `==19` | 保留 1 行 why（防回退） |

### 5.2 日志格式契约（对外，破坏会打断 docs 的 grep 考古）

| 位置 | 内容 | 处理 |
|---|---|---|
| `Utils/Log.cs:6-23` | 行首时间格式、级别标签 6 字符、一行一条、UTF-8 无 BOM、滚动文件名 | **保留契约要点**（压到 3-4 行），指向 logging.md §4 |
| `Utils/Log.cs:28` | 无 BOM UTF-8，**别改成 `Encoding.UTF8`** | **保留**（一行） |
| `Utils/Log.cs:31` | 异常堆栈缩进前缀，保证 `grep "^20"` 只命中真日志 | **保留**（一行） |
| `Utils/Log.cs:181-184` | Sanitize：转义换行/控制符，防外部字符串伪造假日志行 | **保留**（一行） |

> ⚠ **文档指针漂移**：`Utils/Log.cs:6-23` 与 `docs/project/handoff.md:684` 都写「`docs/dev/logging.md` **§3.4** 格式契约」，但现行 `logging.md` 里「格式契约」是 **§4**（§3 是「配置」）。**核对后再改指针**（要么改代码/handoff 指向 §4，要么给 logging.md 补 §3.4 锚点）。

### 5.3 被 `docs/reviews/*` 点名的「失实注释」——须先核对行为

| 位置 | 注释原文 | 核对结果 | 处理 |
|---|---|---|---|
| `Plugins/Betop/BetopPlugin.cs:61` | 「查询需要写帧；用户关掉开关即**完全静默（不认领、不写入）**」 | **仍失实**：`BatteryPluginBase.RunProbe`（L189）→ `Probe(handle)`（L195）**无 `WritesEnabled`/`CanWrite` 检查**；`BetopPlugin.Probe`（L121）直接 `Query`→`WriteOutput`。关掉 `BetopProbingEnabled` 后**探测路径仍写帧**。review-followup §3.1/§4.1 已点名（基线 `ddd3b39`），截至审计仍未修 | **修正为与行为一致**（或先补探测闸门再保留原话）；勿照原样删 |
| `Settings/AppSettings.cs:65-66` | 「…默认开启；关掉即**完全静默（不认领、不写入）**」 | 同上（同一失实承诺） | **修正** |
| `docs/hardware/protocols.md:434`（§4.7） | 「关掉即完全静默（不认领、不写入）」 | **docs 侧重复了同一失实承诺** | **同步修正 docs** |
| `Plugins/DeviceClaimRegistry.cs:5-9` | 类头仍写 `DeviceManager.SafePoll` | review-followup §3.3 点名「残留注释」；现为 `RequestPollAsync`/`PollOnceAsync` | **压缩 + 修正方法名** |

> 另注：`BetopPlugin.cs:81-83`（Offline 注释里「待确认」段）与 `handoff.md` 待办③ 相互印证——「手柄休眠翻离线其实不会发生」，属**有意保留的诚实口子**，不应删，但可压到 1 行。

### 5.4 其余「保留」性质（勿误删）

- `OfflinePolicyTests.cs:16-20`：注明「雷柏窗口取值在本测试里是硬编码复制，改插件不会自动同步」——真实松动点，保留。
- `Plugins/Betop/BetopCodec.cs:46-49`、`Plugins/Betop/BetopPlugin.cs:70-84`：占位帧 `0x50` 判据「宁可漏判不可误判」的安全取舍，保留。
- `Plugins/Rapoo/RapooPlugin.cs:64-82`、`Plugins/OfflinePolicy.cs:29-39`：延迟确认窗口（6s/12s）的实测依据，保留 1 行结论。

---

## 6. 执行提示（给逐文件精简的人）

1. **顺序**：先做 §3 归纳（补 docs）→ 再做 §2 `归纳` 块改指针 → 再做 `压缩` → 最后 `删除` §4 冗余摘要。
2. **压缩模板**：类头保留「一句话职责 + `详见 docs/xxx.md §y`」；方法保留「不写会踩坑的 why」1 行。
3. **不改逻辑**：本审计未发现任何需要改代码逻辑的点；§5.3 的「失实注释」是**注释与行为不符**，精简时要么把注释改成与现状一致，要么（另开工单）补探测闸门后再保留原话——**不要只删注释了事**。
4. **指针核对**：凡「详见 docs/…」都要核到小节存在（本次已发现 `Log.cs` §3.4 漂移一处，见 §5.2）。
5. **验证**：精简后 `dotnet build VigilWatt.sln -c Debug` 应仍 0 错误（注释不影响编译），可顺带确认没有误删 `///` 的 XML 结构标签导致 CS1570 类警告。

---

## 7. docs 归纳执行记录（2026-10-09，第二轮）

> 范围：**只改 `docs/`**，未触碰 `src/`。执行 §3 的 9 条归纳 + 两处文档修正。

### 7.1 归纳补入（§3 的 9 条）

| 目标文档 / 章节 | 补入内容 | 对应 §3 条目 |
|---|---|---|
| `docs/hardware/protocols.md` §3 罗技（新增「HID++ 报文层」表） | 短 7B / 长 20B 布局、错误响应 `0x8F`/`0xFF`+错误码、自发通知 `sw` 低 4 位=0；点明实现 `HidppIO.cs`/`HidppCodec.cs` | 1 |
| `docs/hardware/protocols.md` §3 罗技（新增「各特征电量/充电编码」表） | `0x1001` flags 位、`0x1F20` flags 值、`0x1004`/`0x1000` 充电状态码表、`0xB5` 配对字节布局 | 2-5 |
| `docs/hardware/protocols.md` §4.3（新增 report ID 取值约定 bullet） | `null`/`0`/`>0` 三态；HID++ 长 `0x11`、短 `0x10` | 9 |
| `docs/project/code-flow.md` §5（新增 §5.1「离线占位（墓碑）」） | 两条「消失」路径、北通 dongle 退出枚举实测、补离线条目解法、只启动跑一次 | 6 |
| `docs/project/architecture.md` §6（新增 bullet） | `DeviceStatusMerger` 的「声明覆盖猜测」规则 | 7 |
| `docs/project/code-flow.md` §7（StatsPage bullet 扩写） | 设备下拉按历史库取（含已下线/换 Id） | 8 |

风格：全部按各文档既有写法（表格 / bullet / 代码块），未原样粘贴代码注释；每处均点明代码位置（`文件:方法名` 或类型名）。

### 7.2 两处文档修正

| 位置 | 原内容 | 改后 | 说明 |
|---|---|---|---|
| `docs/hardware/protocols.md` §4.7 | 「关掉即完全静默（不认领、不写入）」 | 改为「**关掉只挡住轮询写入，不挡探测**」+ ⚠️ 探测路径 `RunProbe → Probe` **仍会写帧**、「待修」（指向 `BetopPlugin.WritesEnabled`） | 与行为一致；标注为待修项 |
| `docs/project/handoff.md:684` | 「不动 **§3.4** 格式契约」 | 「不动 **logging.md §4「格式契约」**」 | 见下 |

**指针漂移处理选择**：选**方案 (b)——改引用到 §4**，未在 logging.md 里造 `#### 3.4` 假标题。理由：
1. logging.md 的「格式契约」现行就是 `## 4.`，硬塞一个 `#### 3.4` 子标题与文档结构不符，反而制造新的错乱；
2. 改引用是**一处一 token** 的最小改动，且我顺带补上了**文档名 + 小节标题**（`logging.md §4「格式契约」`）——即便将来编号再变，凭标题仍能定位，比纯数字更抗漂移。

**遗留（src 侧，非本次可改）**：`src/VigilWatt.Core/Utils/Log.cs:6-23` 的类头注释仍写「docs/dev/logging.md §3.4 的『格式契约』」——应同步改为 §4（由负责 `src/` 的同事一并处理）。

### 7.3 改动文件清单

- `docs/hardware/protocols.md`（§3 罗技 +2 表；§4.3 +1 bullet；§4.7 改写 1 条）
- `docs/project/code-flow.md`（§5 +§5.1 小节；§7 StatsPage bullet 扩写）
- `docs/project/architecture.md`（§6 +1 bullet）
- `docs/project/handoff.md`（:684 指针修正）
- `docs/reviews/archive/comment-audit-2026-10-09.md`（本记录）

> 未新建集中归档文件，全部按既定策略补进现有 docs 对应章节。

