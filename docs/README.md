# 文档导航

按当前问题进入对应专题。协议事实、安全边界与排查步骤以专题文档为准；项目当前状态见 [handoff.md](project/handoff.md)。

## 新开发者从这里开始

不必读完 70 份文档，按这个顺序四份就够上手：

1. [architecture.md](project/architecture.md)：模块边界、线程模型、数据存储，改代码前必看 §6「约定」。
2. [build.md](dev/build.md)：怎么构建、怎么跑、发布与打包。
3. [review-standard.md](dev/review-standard.md)：提交前的 R 清单（红线区域逐条自查）。
4. [../AGENTS.md](../AGENTS.md)：硬约束——git 红线、注释配额、只读原则。

改界面加读 [ui-style.md](dev/ui-style.md)；碰硬件协议加读 [hardware/README.md](hardware/README.md)。

## 面向使用者

- [user-guide.md](user/user-guide.md)：逐页、逐开关说明——设备列表各元素含义、四个页面的用法、托盘与小组件交互、诊断采集四步、数据存在哪。
- [release-notes.md](project/release-notes.md)：用户向更新日志，随安装包分发。

## 项目与运行机制

- [handoff.md](project/handoff.md)：当前状态、设备验证情况和待办；新会话先读。
- [architecture.md](project/architecture.md)：模块职责、关键设计和数据存储。
- [capture.md](project/capture.md)：设备诊断（USB/HID 采集）的流程、能力边界、包格式与验证记录。
- [code-flow.md](project/code-flow.md)：启动、设备发现、轮询和 UI 更新流程。
- [pointer-telemetry.md](project/pointer-telemetry.md)：DPI / 回报率遥测模型、来源口径与过期规则（尚无插件接入）。
- [experience.md](project/experience.md)：项目经验与后续规划；不维护待办。
- [changelog.md](project/changelog.md)：按日期记录重要改动及原因。
- [contributions.md](project/contributions.md)：收进仓库的社区改动、贡献者和没有采纳的部分。
- [release-notes.md](project/release-notes.md)：用户向更新日志，随安装包分发；关于页与更新确认框读它。

## 开发与排查

- [build.md](dev/build.md)：构建、测试、发布和环境问题。
- [runtime-pitfalls.md](dev/runtime-pitfalls.md)：HID、蓝牙、窗口、DPI 和 XAML 运行时问题。
- [monitor-identity.md](dev/monitor-identity.md)：显示器身份与名称（贴片选屏），三套 API 实测对照与降级链。
- [logging.md](dev/logging.md)：日志级别、文件位置、保留和排障用法。
- [comment-style.md](dev/comment-style.md)：代码注释规范。
- [ui-style.md](dev/ui-style.md)：界面视觉规则——状态色、来源徽标、图标、字号与卡片；颜色只表达电量状态。
- [review-standard.md](dev/review-standard.md)：审查标准、红线和闭环流程。

## 硬件协议与安全

**先看 [hardware/README.md](hardware/README.md)**：这一层按文档职责分了三层，写新设备文档前先确认放哪。

规则层（不属于任何品牌）：

- [sources.md](hardware/sources.md)：协议、功能方案的依据、外部参考和验证范围；追溯来源先看这里。
- [protocols.md](hardware/protocols.md)：**索引**——品牌 / 型号 / VID:PID 对照表、共用号段陷阱、官方资料地图。
- [safety.md](hardware/safety.md)：硬件写入规则与事故复盘；任何写入改动前必读。
- [debugging.md](hardware/debugging.md)：官方软件抓包、HID 观测和协议验证步骤。

设备文档（39 份，按职责分三层）：

| 目录 | 份数 | 内容 |
|---|---|---|
| [hardware/brands/](hardware/brands/) | 23 | 一品牌一份，品牌内多套协议同份分节 |
| [hardware/devices/](hardware/devices/) | 13 | 单型号深挖（耳机、适配器、单型号鼠标） |
| [hardware/reference/](hardware/reference/) | 3 | 厂商软件逆向出的型号表 / 命令表 / 档位表 |

按设备找文档，直接查 [protocols.md](hardware/protocols.md) 的 VID:PID 对照表，比翻目录快。

## 审查跟踪

- [BACKLOG.md](reviews/BACKLOG.md)：仍未修复的审查项（`open` / `doing`）。
- [archive/BACKLOG-CLOSED.md](reviews/archive/BACKLOG-CLOSED.md)：已关闭审查项及结论。
- [审查报告归档](reviews/archive/)：历史代码审查、复核、注释审计和内存调查。

## 症状速查

| 遇到的问题 | 从这里开始 |
|---|---|
| 编译失败、启动了旧版本、发布文件被占用 | [dev/build.md](dev/build.md) |
| 设备读不到电量、读数异常、dongle 无响应或离线误判 | [hardware/protocols.md](hardware/protocols.md) · [hardware/debugging.md](hardware/debugging.md) |
| 蓝牙设备未出现、类型识别错误或电量为空 | [project/code-flow.md](project/code-flow.md) · [dev/runtime-pitfalls.md](dev/runtime-pitfalls.md) |
| 托盘、小组件、任务栏贴片显示或交互异常 | [dev/runtime-pitfalls.md](dev/runtime-pitfalls.md) |
| 设备列表某行变空白/变黑、点刷新后进程直接退出 | [dev/runtime-pitfalls.md](dev/runtime-pitfalls.md) §22 |
| 贴片选错屏、换显示模式后贴片不见、设置页屏幕名不对 | [dev/monitor-identity.md](dev/monitor-identity.md) |
| 准备向设备发送数据 | 先读 [hardware/safety.md](hardware/safety.md)，再查 [hardware/sources.md](hardware/sources.md) |
| 需要查看未修问题 | [reviews/BACKLOG.md](reviews/BACKLOG.md) |
| 设备没被识别、想采证据给维护者 | [project/capture.md](project/capture.md) |

## 调试脚本

临时 PowerShell / Python / P/Invoke 探针统一放在仓库根目录的 `scripts/`；用途见 [scripts/README.md](../scripts/README.md)。长期测试和工具放在 `src/tests/`，不放仓库根目录。
