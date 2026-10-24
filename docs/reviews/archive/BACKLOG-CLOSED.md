# 已关闭的审查条目

> 从 `docs/reviews/BACKLOG.md` 移出的 `done` / `wontfix`。编号不复用。
> 只留结论。调查过程在 `docs/project/changelog.md`、`docs/dev/runtime-pitfalls.md` 和本目录的审查报告里。

## done

| 编号 | 结论 |
|---|---|
| CR-99 | 2026-10-24 使用真实 DeviceManager 原生一分钟节拍完成首次认领及五轮自然轮询：初次第 14 次读取返回长度 0，其后各轮第 1 次返回长度 0，均读取 100%，未失败或熔断。原始日志与边界见 `hardware/akg.md` §3。原分支编号 CR-94 与 dev 的迈从条目重号，合入时迁为 CR-99。 |
| CR-95 | AKG 空闲态确认及响应阶段对瞬时空读、其它报告 ID 有界重试；持续失败不写入或不重发，格式错误仍立即停止。回归覆盖恢复、耗尽窗口及等待中关闭开关。 |
| CR-96 | AKG 只在本轮按有效区域跳过相邻重复快照，空闲态及新查询重置比较；回归覆盖重复分片、尾部旧缓存变化及连续两轮相同电量。无事务号的剩余歧义见 `hardware/akg.md` §2。 |
| CR-98 | HERO 68 Air 文档与 changelog 明确仅处理完整包之间的通知穿插；响应分片中途插入通知仍可能使本轮失败，不再声称普遍支持。传输层离线测试仍跟踪 CR-97。 |
| CR-85 | 补齐 endermiteDOTcn 的贡献者名单和 PR #44 待合入范围，明确贡献者真机验证及上游维护者尚未复测。 |
| CR-86 | AKG 每轮独立等待成功 ACK，忽略此前的异步结果；增加独立报告、同报告、缺失 ACK 和跨轮超时的 4 项回归用例。无事务号的协议边界见 `hardware/akg.md` §2。 |
| CR-87 | 传输层返回匹配的电量消息，成功 Info 日志按电量变化记录 `原始=<HEX>`，不输出报告尾部旧缓存；回归测试验证返回消息与所采纳电量一致。 |
| CR-73 | 贴片拖动改为轮询驱动（Win11 任务栏 XAML 吃掉子窗口鼠标消息）。`WM_MOUSEACTIVATE` 让 DefWindowProc 先处理，只在父窗口要吃消息时干预；fallback 为常驻 10Hz 轮询（拖动中 60Hz）。设置页新增「重置贴片位置」按钮。见 runtime-pitfalls.md §16.5.1 |
| CR-62 | 空槽负缓存 5 分钟 + 每轮最多补探 1 槽；空接收器也启只读监听。见 code-flow.md §2.1 |
| CR-01 | 罗技补上写入闸门、熔断和默认关闭 |
| CR-02 | 北通探测在写帧前检查写入开关 |
| CR-03 | `Upsert` 先克隆再比较，变化判定不再恒为假 |
| CR-04 | 历史清理改为定时任务 |
| CR-05 | 历史库开启 WAL 和 busy_timeout |
| CR-06 | 历史页和统计页的查询改到后台 |
| CR-07 | 退出时释放托盘、设备和历史库 |
| CR-08 | 未处理异常在短窗口内达到次数后不再吞掉 |
| CR-09 | 小组件关闭时解绑订阅 |
| CR-10 | Elan 的 feature 扫描离开正常路径 |
| CR-11 | `safety.md` 的写入规则改成按插件开关 |
| CR-12 | 独立写入许可取消，插件开关即授权 |
| CR-14 | 删掉没有调用点的 HID 写入变体 |
| CR-15 | 低电量提醒回到阈值之上会复位 |
| CR-17 | 罗技打开失败后会再试 |
| CR-18 | 文档里不存在的 `safety.md` §2.2 改为 §2 |
| CR-19 | 没声明写入开关的插件默认不许写 |
| CR-20 | 设置文件改为原子写 |
| CR-21 | HID 读失败留 Debug，不再完全静默 |
| CR-22 | 插件没真卸载时不再每分钟打「已卸载」 |
| CR-23 | 低电量提醒表随设备移除清理 |
| CR-24 | 电量文案收到 `DeviceStatusFormatter` |
| CR-25 | 迈从的日志去重改用 `Log.Once` |
| CR-26 | 删掉罗技的 `StableHash` 包装 |
| CR-27 | 热路径写日志前先看级别 |
| CR-28 | 雷柏心跳只在状态变化时记 Info |
| CR-29 | 小组件和设备页的刷新合并成一轮一次 |
| CR-34 | 设置页拆成只建一次和每次重读 |
| CR-37 | 罗技不再因为第二套开关而发现不了设备 |
| CR-38 | 贴片改原生窗口后，不再依赖尚未就绪的 XamlRoot 算 DPI |
| CR-40 | 贴片是任务栏子窗口，不再抢 Z 序 |
| CR-43 | 旧 WinUI 贴片的透明问题随方案替换消失。小组件仍走 `TransparentWindowSurface` |
| CR-45 | 贴片宽度按位图实测，不再估算 |
| CR-46 | 点任务栏闪一下的成因（抢 Z 序）已消失 |
| CR-47 | 16ms 抬 Z 序的定时器已删除 |
| CR-49 | 窗口子类化按 HWND 保存旧过程 |
| CR-53 | 熔断改为递增退避，设备自己的通知可以打破熔断 |
| CR-54 | Win10 白框：小组件两层背景都清。贴片已不走这条路 |
| CR-55 | Win10 上 `%` 被裁：贴片改为按位图定宽 |
| CR-56 | Win10 小组件白方块：去掉与透明冲突的 DWM 非客户区策略 |
| CR-58 | 右键菜单改成 Win32 弹出菜单 |
| CR-59 | 任务栏文档补上 `SetParent`，并更正「deskband 是唯一真方案」 |
| CR-65 | 导航后清空 `BackStack` |
| CR-D1 | 蓝牙离线判定改为属性缺失时沿用现状，移除时发布离线 |
| CR-D2 | 合并状态时带上类型和协议 |
| CR-D3 | 关掉插件时设备下线 |
| D2 | 状态文案抽成 `DeviceStatusFormatter` |
| D7 | 日志「只记一次」抽成 `Log.Once` |
| D8 | 充电状态映射抽成 `ChargingStateMap` |
| D9 | 删除罗技 `StableHash` 直通 |
| D10 | 作废。独立写入许可被「插件开关即授权」取代 |
| CR-75 | 拔出后 HID++ 通知空转：`BatteryPluginBase.StartPassiveListen` 与 `LogitechPlugin.EnsureListening` 按"连续 3 次读取在 100ms 内返回 null"判定句柄失效，退避 200ms。正常静音仍等满 500ms，节拍不受影响 |
| CR-76 | 与 CR-75 同一修法落地。Inphic/Hecate/Delux 的读窗口 `continue` 有 deadline 兜住（不无限循环），未改 |
| CR-77 | `MarkOffline` 注释改为"插件内软离线的唯一落地点"，并注明拔出走 `ReconcileRemovals`（不该盖 `LastUpdated`）。行为本来就对，是注释过满 |
| CR-68 | 未采用 PR #14 的按显示名去重，改为按 MAC 判定自研实现：`Ble/DualChannelId` 归一 `btc:`/`ble:` 端点，`DeviceManager.AbsorbDualChannel` 收敛到经典那条。PR #14 的 `Devices` getter 副作用与 `NormalizeName` 大小写问题随之不适用 |

## wontfix

| 编号 | 为什么不修 |
|---|---|
| CR-30 | HID 枚举改只读打开会改变能看见的设备。每分钟几十次打开不值得换这个风险 |
| CR-31 | 更新源由用户自己填，下载前有确认。签名属于发布流程 |
| CR-32 | GitHub token 已在 `.gitignore`。分享仓库目录前先删那个文件 |
| CR-33 | 本地互斥被抢占时，攻击者能做的事已经不止占一个名字 |
| CR-44 | 在旧 WinUI 贴片上 `WS_EX_LAYERED` 测过无效。原生贴片后来用的是另一条合成路径，不要把这条结论套过去 |
| CR-51 | 日志按用户要求一天一个文件，大小靠保留天数限制 |
| CR-64 | 反复打开主窗口抬高的是工作集，不是泄漏。判泄漏只看私有提交。见 `memory-growth-2026-10-12.md` |
| CR-78 | 原判断有误：`RunExclusiveAsync` 已 catch `WaitAsync` 的 `ObjectDisposedException` 并 return，`Release` 也有 catch。剩下的 `Detach` 与在跑 `Poll` 并发由插件内 try/catch 兜住，退出时不影响用户 |
