# 本地诊断包

## 1. 使用与范围

设置页“日志”区域的“导出诊断包”打开本地保存框，默认文件名为 `VigilWatt-diag-yyyyMMdd-HHmmss.zip`。取消不生成文件；选定位置后在后台写包，不自动上传。

这是 issue #3 第一阶段的缓存快照，不是完整的设备探测模式。导出只读取 `DeviceManager` 已有设备状态、`HidDeviceScanner.CachedSnapshot`、`DeviceClaimRegistry.Snapshot` 以及按日日志。它不调用扫描、打开设备、Feature GET、HID 写入或 GATT 读取；正常监控仍按原节拍运行，因此不能把导出期间整个进程的写入次数宣称为零。

HID 缓存更新时间写入报告。状态和缓存不是跨线程事务快照，可能来自不同更新时刻；没有记录不能证明设备不存在或没有电量通道。尚未实现被动录帧、静默窗口、未识别设备列表和插件脚手架。

## 2. ZIP 格式与脱敏

| 文件 | 内容 |
|---|---|
| `report.md` | 数量摘要、缓存时间、数据边界与采集提示 |
| `environment.json` | 应用/系统版本、架构、时区、插件开关、实际与配置日志级别 |
| `devices.json` | 匿名设备 ID、名称、来源、协议、在线状态、电量与更新时间 |
| `hid-interfaces.json` | 匿名接口路径、VID/PID、usage、报告长度、产品/厂商和当前认领者 |
| `logs/recent.log` | 最近两个按日文件的末尾切片 |

- 不导出完整 `settings.json`、历史数据库、序列号原始路径或抓包文件。
- 设备 ID 与接口路径使用每包独立 HMAC 标识；同一包的日志与快照可对应，不支持跨包按匿名值追踪。
- 日志及文本字段替换已知用户名、计算机名、用户域、Windows/UNC/设备路径、MAC、标注为原始数据的报文与长十六进制串。产品名仍保留，分享前需人工检查，不能承诺任意自由文本中所有个人信息都可自动识别。
- 每个日志最多取末尾 512 KiB，最多两个；截断时丢弃首条残缺记录。读取兼容正在写入的日志，不关闭日志句柄、不修改日志级别。
- 设备和接口各最多 1000 条，文本字段最多 1024 字符；ZIP 内容未压缩总量不超过 10 MiB，超限则失败，不覆盖旧包。
- 日志不可读时仍导出快照，并在报告记录缺失原因的异常类型；导出过程中不新增硬件访问。
- 先写同目录临时包、刷盘后替换目标；失败清理本次临时文件，保留已有目标。

## 3. 实现与验证

- 纯 .NET 导出与脱敏逻辑在 `VigilWatt.Core/Diagnostics/`，测试项目直接链接真源文件。
- 设置页仅选择路径、取得内存快照并通过 `Task.Run` 导出，不将 ZIP I/O 放在 UI 线程。
- 保存框使用 Win32 `GetSaveFileNameW`，不升级当前 Windows App SDK 1.7，并兼容管理员运行。参考 [GetSaveFileNameW](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/nf-commdlg-getsavefilenamew)、[OPENFILENAMEW](https://learn.microsoft.com/en-us/windows/win32/api/commdlg/ns-commdlg-openfilenamew)。
- `DiagnosticArchiveTests` 覆盖全包脱敏、历史标识、独立匿名密钥、日志共享读取/截断、文件白名单、日志缺失、失败保护及认领表快照。
- 手动验收清单（尚未完成）：入口可搜索；取消不留文件；选择中文/带空格路径可导出并解压；已有文件覆盖有系统确认；导出前后设备监控继续运行。2026-10-23 已启动隔离开发版，后续验收被 Esc 中止，不能视为保存框或导出流程已实测。
