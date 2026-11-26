# 文档索引

> 本文件由 `python scripts/gen-doc-index.py` 生成，不要手改。
> 新增/移动文档后重跑；`--check` 会在过期时退出码 1。日期取 git 最后提交，未入库文件用文件时间。
> 共 105 份 Markdown；最后生成：2026-11-26。

## architecture

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [active-query-safety.md](architecture/active-query-safety.md) | 主动查询安全规范 | 规范生效；M2 阶段 1 方案定稿，手动目标探测设计稿待审查，全面门控受能力声明覆盖阻塞，尚未实施 | 2026-11-24 |
| [device-identity-and-capabilities.md](architecture/device-identity-and-capabilities.md) | 设备身份与能力声明规范 | 规范生效；身份与能力模型已有实现，主动查询的全路径能力门控仍在 M2 | 2026-11-24 |
| [protocol-definition-standard.md](architecture/protocol-definition-standard.md) | 协议定义与加载规范 | 规范生效；M1 严格加载已提交，本文档收尾未提交 | 2026-11-23 |
| [protocol-engine-roadmap.md](architecture/protocol-engine-roadmap.md) | 通用协议引擎治理路线图 | M1 文档收尾完成；M2 阶段 1 方案定稿，手动目标探测设计稿待审查，受能力覆盖阻塞，待实施 | 2026-11-24 |
| [protocol-regression-testing.md](architecture/protocol-regression-testing.md) | 协议回归测试规范 | 规范生效；M3 待开展，现有测试分散但可复用 | 2026-11-23 |
| [protocol-telemetry-integration.md](architecture/protocol-telemetry-integration.md) | 统一遥测衔接规范 | 规范生效；指针遥测与连接方式已有统一模型，电量/充电仍是原始字段 | 2026-11-23 |

## dev

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [build.md](dev/build.md) | 构建与环境坑 | — | 2026-11-25 |
| [comment-style.md](dev/comment-style.md) | 注释规范 | — | 2026-11-11 |
| [logging.md](dev/logging.md) | 日志设计 | 已实施（2026-10-09 起；2026-10-10 改为按日分文件） | 2026-10-16 |
| [monitor-identity.md](dev/monitor-identity.md) | 显示器身份与名称（任务栏贴片选屏） | — | 2026-10-26 |
| [plugin-standard.md](dev/plugin-standard.md) | 厂商插件架构标准 | — | 2026-11-24 |
| [protocol-json.md](dev/protocol-json.md) | 协议 JSON 怎么写（数据化的边界） | — | 2026-11-23 |
| [review-standard.md](dev/review-standard.md) | 代码审查标准与流程 | — | 2026-11-19 |
| [runtime-pitfalls.md](dev/runtime-pitfalls.md) | 运行时坑 | — | 2026-11-25 |
| [ui-style.md](dev/ui-style.md) | 界面视觉规则 | — | 2026-11-12 |
| [vendor-onboarding-process.md](dev/vendor-onboarding-process.md) | 新厂商接入流程 | — | 2026-11-23 |
| [vendor-plugin-reference-index.md](dev/vendor-plugin-reference-index.md) | 四大厂商参考实现索引 | — | 2026-11-23 |

## dev/runtime-pitfalls

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [01-hid-ble.md](dev/runtime-pitfalls/01-hid-ble.md) | 运行时坑 · HID / BLE 读取 | — | 2026-11-26 |
| [02-shell-ui.md](dev/runtime-pitfalls/02-shell-ui.md) | 运行时坑 · 窗口、页面与列表 | — | 2026-11-26 |
| [03-tray.md](dev/runtime-pitfalls/03-tray.md) | 运行时坑 · 托盘 | — | 2026-11-26 |
| [04-widget.md](dev/runtime-pitfalls/04-widget.md) | 运行时坑 · 桌面小组件 | — | 2026-11-26 |
| [05-taskbar-pill.md](dev/runtime-pitfalls/05-taskbar-pill.md) | 运行时坑 · 任务栏电量贴片 | — | 2026-11-26 |
| [06-runtime-env.md](dev/runtime-pitfalls/06-runtime-env.md) | 运行时坑 · 构建与运行环境 | — | 2026-11-26 |
| [07-device-state.md](dev/runtime-pitfalls/07-device-state.md) | 运行时坑 · 设备状态与内存 | — | 2026-11-26 |

## hardware

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [debugging.md](hardware/debugging.md) | 调试手法手册 | — | 2026-11-08 |
| [protocols.md](hardware/protocols.md) | 2.4G 协议手册（索引） | — | 2026-11-23 |
| [README.md](hardware/README.md) | 硬件协议文档 | — | 2026-11-23 |
| [safety.md](hardware/safety.md) | 硬件安全规则（必读） | — | 2026-11-23 |
| [sources.md](hardware/sources.md) | 协议与实现来源索引 | — | 2026-11-11 |
| [support-matrix.md](hardware/support-matrix.md) | 设备支持矩阵 | — | 2026-11-23 |

## hardware/brands

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [asus-rog.md](hardware/brands/asus-rog.md) | ROG Strix Scope II 96 Wireless / Omni | — | 2026-11-11 |
| [atk.md](hardware/brands/atk.md) | ATK / VXE 2.4G 电量（接收器 VID_373B） | — | 2026-11-23 |
| [aula.md](hardware/brands/aula.md) | AULA 键盘电量协议 | — | 2026-11-11 |
| [betop.md](hardware/brands/betop.md) | 北通手柄 BETOP（接收器 20BC / 20DD） | — | 2026-11-23 |
| [cherry.md](hardware/brands/cherry.md) | CHERRY PIXIU75（dongle 046A:01E4 / 有线 046A:01E2） | — | 2026-11-11 |
| [darkrim.md](hardware/brands/darkrim.md) | 寸渊影弑 DARKRIM SHADOW（dongle 3654:6901） | — | 2026-11-11 |
| [delux.md](hardware/brands/delux.md) | DELUX M900 Pro（接收器 1D57:FA65） | — | 2026-11-11 |
| [eweadn.md](hardware/brands/eweadn.md) | 前行者 EWEADN X87 键盘 / S9 鼠标 | — | 2026-11-11 |
| [flydigi.md](hardware/brands/flydigi.md) | 飞智 FLYDIGI APEX5 2.4G 电量 | — | 2026-11-11 |
| [gamesir.md](hardware/brands/gamesir.md) | 盖世小鸡 GameSir G7 Pro CE（dongle 3537） | — | 2026-11-11 |
| [ganss.md](hardware/brands/ganss.md) | GANSS GS3104T-Pro / 狼蛛 F108 Pro 2.4G 电量 | — | 2026-11-11 |
| [hecate.md](hardware/brands/hecate.md) | HECATE 漫步者电竞（接收器 35BB:D00B） | — | 2026-11-11 |
| [hyperx-cf2.md](hardware/brands/hyperx-cf2.md) | HyperX Cloud Flight 2 飓风炫彩版（dongle VID_03F0&PID_0AC1） | — | 2026-11-11 |
| [inphic.md](hardware/brands/inphic.md) | 英菲克（ZHEN 1D57:FA65 / IN9 248A:5B2F） | — | 2026-11-11 |
| [lingbao.md](hardware/brands/lingbao.md) | 凌豹 LINGBAO（M5PRO Max `3151:402D` / K98 `3554:FA09`） | — | 2026-11-23 |
| [logitech.md](hardware/brands/logitech.md) | 罗技 Logitech (VID_046D) —— ✅ Bolt 真机验证（2026-10-08）；协议驱动 + 嵌入目录 | — | 2026-11-23 |
| [mchose.md](hardware/brands/mchose.md) | 迈从 MCHOSE（G87 键盘 / 0x4D 鼠标通道 / Gmouse G3 V2 / 耳机） | — | 2026-11-11 |
| [mojhon.md](hardware/brands/mojhon.md) | 墨将（MOJHON / 原 BIGBIGWON）手柄：调查记录 | **插件已实现并通过真机验证**——电量（§6.7）与充电标志（§6.8）均已定案；仅「满电标志」未实测（§8.1） | 2026-11-11 |
| [pulsar.md](hardware/brands/pulsar.md) | 派世 Pulsar X2 V2（dongle 3554:F508） | — | 2026-11-11 |
| [rapoo.md](hardware/brands/rapoo.md) | 雷柏 Rapoo (VID_24AE) —— V2 世代 status-report7 协议 —— ✅ 插件已落地（纯被动） | — | 2026-11-16 |
| [razer.md](hardware/brands/razer.md) | 雷蛇 Razer (VID_1532) —— 经典 90 字节 feature 协议 —— ✅ 插件已落地（会写帧，默认关闭） | — | 2026-11-11 |
| [rose.md](hardware/brands/rose.md) | 弱水时砂 ROSE FIRST ANGEL（2.4G dongle = 38FF:0001）—— ❌ 2.4G 无电量通道（2026-10-05 完整勘察） | — | 2026-11-11 |
| [vgn.md](hardware/brands/vgn.md) | VGN 蜻蜓（Dragonfly）2.4G | — | 2026-11-11 |

## hardware/devices

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [ak870-ml903.md](hardware/devices/ak870-ml903.md) | 黑爵 AK870 与雷神 ML903 NearLink Se | — | 2026-11-11 |
| [akg.md](hardware/devices/akg.md) | AKG N9 Hybrid 接收器电量 | — | 2026-11-11 |
| [atk-n9.md](hardware/devices/atk-n9.md) | ATK N9 系列耳机（dongle `373B:1204`）—— 协议已记录，**代码未实现** | — | 2026-11-11 |
| [dualsense.md](hardware/devices/dualsense.md) | DualSense / DS5 蓝牙电量 | — | 2026-11-11 |
| [haute42.md](hardware/devices/haute42.md) | Haute42 Adapter 4DS（DualSense 适配器） | — | 2026-11-11 |
| [ilovbee.md](hardware/devices/ilovbee.md) | ilovbee b98 键盘（`258A:01BB`） | — | 2026-11-11 |
| [inzone-h9.md](hardware/devices/inzone-h9.md) | 索尼 INZONE H9 II（WH-G900N2，`054C:0FA8`）—— ✅ 插件已落地（会写帧，默认关闭） | — | 2026-11-11 |
| [langtu.md](hardware/devices/langtu.md) | 狼途 LANGTU T98（`1A2C:8FFF`） | — | 2026-11-11 |
| [mchose-v9-pro.md](hardware/devices/mchose-v9-pro.md) | MCHOSE V9 Pro 耳机 2.4G 电量 | — | 2026-11-11 |
| [mchose-x9.md](hardware/devices/mchose-x9.md) | MCHOSE X9 耳机 2.4G 电量 | — | 2026-11-23 |
| [razer-headset.md](hardware/devices/razer-headset.md) | Razer BlackShark V2 Pro (2023) | — | 2026-11-11 |
| [razer-kraken.md](hardware/devices/razer-kraken.md) | 雷蛇 Kraken V4 耳机（接收器 `1532:056C`） | — | 2026-11-11 |
| [razer-viper-se.md](hardware/devices/razer-viper-se.md) | 雷蛇 Viper V3 Pro SE | — | 2026-11-11 |

## hardware/reference

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [betop-catalog.md](hardware/reference/betop-catalog.md) | 北通设备目录（2026-11-16） | — | 2026-11-23 |
| [betop-official-client.md](hardware/reference/betop-official-client.md) | 北通（BETOP）智控客户端协议逆向报告 | — | 2026-11-18 |
| [mchose-catalog.md](hardware/reference/mchose-catalog.md) | 迈从设备目录与鼠标 0x4D 规格（2026-11-01 逆向） | — | 2026-11-19 |
| [mchose-dpi-report-rate-audit.md](hardware/reference/mchose-dpi-report-rate-audit.md) | MCHOSE 0x4D：DPI / 回报率只读协议证据审计（M2-4A） | — | 2026-11-11 |
| [rapoo-catalog.md](hardware/reference/rapoo-catalog.md) | 雷柏设备目录与协议族（2026-11-01 逆向 AHUB 2.0） | — | 2026-11-19 |

## project

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [architecture.md](project/architecture.md) | 项目结构与架构 | — | 2026-11-23 |
| [capture.md](project/capture.md) | 设备诊断（USB/HID 采集） | — | 2026-11-23 |
| [changelog.md](project/changelog.md) | 变更历史（changelog） | — | 2026-11-25 |
| [code-flow.md](project/code-flow.md) | 代码流程 | — | 2026-11-23 |
| [contributions.md](project/contributions.md) | 贡献记录 | — | 2026-11-25 |
| [device-images-remote.md](project/device-images-remote.md) | 产品图远端化与按需下载（方案） | 未实施 | 2026-11-23 |
| [device-images.md](project/device-images.md) | 设备产品图接入 | — | 2026-11-23 |
| [experience.md](project/experience.md) | 项目经验 | 经验附录，不是规则 | 2026-11-23 |
| [handoff.md](project/handoff.md) | 会话交接（给下一个 AI / 未来的自己） | — | 2026-11-25 |
| [pointer-telemetry.md](project/pointer-telemetry.md) | 指针遥测（DPI / 回报率） | — | 2026-11-23 |
| [release-notes.md](project/release-notes.md) | 更新日志（用户向） | — | 2026-11-25 |

## project/archive

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [changelog-2026-10.md](project/archive/changelog-2026-10.md) | 变更历史归档 · 2026-10 | — | 2026-11-26 |

## reviews

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [2026-11-18-betop-p2-design.md](reviews/2026-11-18-betop-p2-design.md) | BETOP 厂商级支持 · 第一批（协议 JSON 数据化）设计说明 | **已实现（2026-11-18）** | 2026-11-18 |
| [2026-11-23-active-query-gate-design.md](reviews/2026-11-23-active-query-gate-design.md) | 主动查询门控方案（M2 阶段 1） | 方案定稿；未改代码 | 2026-11-24 |
| [2026-11-23-active-query-write-audit.md](reviews/2026-11-23-active-query-write-audit.md) | 主动查询写入点审计（2026-11-23） | M2 阶段 0 完成；只读静态审计，未向任何设备发帧 | 2026-11-24 |
| [2026-11-23-backlog-triage.md](reviews/2026-11-23-backlog-triage.md) | BACKLOG 分诊表（2026-11-23） | — | 2026-11-23 |
| [2026-11-24-active-query-capability-coverage.md](reviews/2026-11-24-active-query-capability-coverage.md) | 主动查询能力覆盖与证据盘点（2026-11-24） | M2 阶段 2 只读审计完成；未改代码、协议 JSON、设备目录、测试或运行行为 | 2026-11-24 |
| [2026-11-24-manual-target-probing-design.md](reviews/2026-11-24-manual-target-probing-design.md) | 手动目标探测设计（2026-11-24） | 设计稿，待审查；未实施 | 2026-11-24 |
| [2026-11-24-test-project-core-logic-extraction-plan.md](reviews/2026-11-24-test-project-core-logic-extraction-plan.md) | 测试工程去 Compile Include：抽取 VigilWatt.Core.Logic | 方案，待审查；未实施 | 2026-11-25 |
| [BACKLOG.md](reviews/BACKLOG.md) | 审查发现 BACKLOG | — | 2026-11-25 |
| [beta-1-release-readiness-audit.md](reviews/beta-1-release-readiness-audit.md) | VigilWatt Beta 1 发版准备审计 | 审计完成，结论 B（需要处理少量阻塞项后进入 Beta） | 2026-11-25 |

## reviews/archive

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [2026-10-25-asus-razer-adaptation-review.md](reviews/archive/2026-10-25-asus-razer-adaptation-review.md) | 2026-10-25 ASUS / Razer 适配审查 | — | 2026-11-11 |
| [ajazz-thunderobot-2026-10-19.md](reviews/archive/ajazz-thunderobot-2026-10-19.md) | 黑爵 / 雷神电量适配审查 | — | 2026-11-11 |
| [akg-n9-2026-10-23.md](reviews/archive/akg-n9-2026-10-23.md) | AKG N9 Hybrid 改动审查 | — | 2026-10-24 |
| [aula-hero68-air-2026-10-23.md](reviews/archive/aula-hero68-air-2026-10-23.md) | HERO 68 Air 适配审查 | — | 2026-10-23 |
| [BACKLOG-CLOSED.md](reviews/archive/BACKLOG-CLOSED.md) | 已关闭的审查条目 | — | 2026-11-25 |
| [code-review-2026-10-06.md](reviews/archive/code-review-2026-10-06.md) | 全仓代码与架构审查（2026-10-06） | — | 2026-10-11 |
| [code-review-followup-2026-10-06.md](reviews/archive/code-review-followup-2026-10-06.md) | 审查报告复核与优化路线图（2026-10-06） | — | 2026-11-11 |
| [comment-audit-2026-10-09.md](reviews/archive/comment-audit-2026-10-09.md) | VigilWatt 注释精简审计与 docs 归并映射（2026-10-09） | — | 2026-11-11 |
| [fix-round-2026-10-10.md](reviews/archive/fix-round-2026-10-10.md) | 修复轮 L2 轻审查（2026-10-10） | — | 2026-10-11 |
| [memory-growth-2026-10-12.md](reviews/archive/memory-growth-2026-10-12.md) | 主窗口显示后私有字节爬升 · 分析报告（2026-10-12） | — | 2026-10-21 |
| [README.md](reviews/archive/README.md) | 审查报告归档 | — | 2026-11-23 |

## user

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [user-guide.md](user/user-guide.md) | 使用说明 | — | 2026-11-12 |

## 根目录

| 文档 | 标题 | 状态 | 最后提交 |
|---|---|---|---|
| [README.md](README.md) | 文档导航 | — | 2026-11-25 |
