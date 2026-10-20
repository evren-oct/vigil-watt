# scripts/ — 构建、发布与调试脚本

> 本目录放构建/发布流程脚本与一次性 / 临时调试脚本（PowerShell / Python / 一次性 P/Invoke 探针）。
> **长期存在的 C# 项目不放这里**：测试项目在 [`src/tests/`](../src/tests/)，工程源码在 [`src/`](../src/)。
> 临时产物用 `%TEMP%`，仓库根目录只放工程文件。新增脚本后在下面补一行索引。

## 脚本索引

| 脚本 | 用途 |
|---|---|
| `.research/Smoke/`（临时，未入库） | 本机三设备协议验证；`--haute-recovery` 仅 GET Haute 电量，验证品牌元数据、关闭写入闸门及失效句柄恢复，不保存设置、不替换运行版 |
| `build-installer.ps1` | 打安装包：`dotnet publish`（Release x64 self-contained）→ 读发布 exe 的 FileVersion 当版本（截 3 段）→ `vpk pack`。产物落 `dist/`（Setup.exe、Portable.zip、`releases.win.json`、`RELEASES`、`*.nupkg`）。发新版**别清 `dist/`**，留着上一版全量包才有 delta；详见 `docs/dev/build.md` §8 |
| `publish-gitee.ps1` | 将 `dist/` 全部打包产物作为当前版本的 Gitee Release 附件发布；需要已推送的 `v<版本>` 标签与 Gitee 写权限令牌（`-Token` 或 `GITEE_TOKEN`，**脚本不读令牌文件**；本机令牌在仓库根 `.vpk-github-token`）。完整发版序列与三个 Gitee 特有坑见 `docs/dev/build.md` §8 |
| `get-release-notes.ps1` | 发布流水线共用函数：按版本号从 `docs/project/release-notes.md` 截出当前版本段（`Get-ReleaseNotes`），并以 UTF-8 无 BOM 落临时文件（`Write-ReleaseNotesFile`）。`build-installer.ps1` 与 `publish-gitee.ps1` 都 dot-source 它；格式契约见该 md 文件头 |
| `code-fingerprint.py` | **代码指纹校验器**（纯标准库）：剥掉注释与空白后对代码 token 序列算 sha256，用来**证明"只改了注释、逻辑一行没动"**。`snapshot <out.json>` 记基线 / `verify <in.json>` 比对（`-xaml` 后缀对 `.xaml` 生效，剥 `<!-- -->` 后比空白归一化文本）。做注释精简、批量重排这类"只该动注释"的改动前先 `snapshot`，改完 `verify` 必须输出 `[OK]` |
| `make-app-icon.py` | **程序图标生成器**（纯标准库，无需 Pillow）：解析式绘制绿色圆角方块 + 白色闪电，超采样抗锯齿，输出 `src/VigilWatt.App/Assets/VigilWatt.ico`（16~256 共 9 档）。`--preview x.png` 出一张放大预览图便于肉眼比对。**改图标配色/字形改这个脚本后重跑即可，不要手改 .ico** |
| `probe-devnode-battery.py` | **PnP 设备节点电量探针**（纯 ctypes + SetupAPI）：枚举全机节点读 `DEVPKEY_Device_BatteryLevel`。经典蓝牙耳机电量就是这么找到的（EDIFIER W820NB 挂在 HFP 网关节点上）；`--all --match X` 可 dump 匹配节点的全部属性，查任何"Windows 能看到但代码读不到"的设备属性都用它 |
| `probe-process-memory.py` | **进程内存分层采样**（纯 ctypes、只读）：按 `VirtualQueryEx` 的 `Type` 分层统计 **PRIVATE / MAPPED / IMAGE**，并附工作集、提交大小、句柄数。位置参数：`<进程名或PID> [秒数] [输出文件] [间隔秒]`。用来区分**"私有提交泄漏"（真泄漏，PRIVATE 单调涨）**与**"工作集可回收驻留"（高水位，回收后 PRIVATE 不动）**——判据见 `docs/reviews/archive/memory-growth-2026-10-12.md` §6。**注意：已提交虚拟内存总量 ≠ 任务管理器「提交大小」**，IMAGE 与文件后备 MAPPED 不计入进程提交量 |
| `parse-minidump.py` | **minidump 内存布局解剖**（纯标准库，无需 WinDbg）：解析 stream 3/4/9/12/16/17，给出进程时长、句柄/模块/线程数、dump 实抓量、分层提交（PRIVATE/MAPPED/IMAGE）与最大区域。**核心判据：dump 文件大小 ≠ 进程占用**（本机 725 MB 的 dump 里 181 MB 是 NVIDIA 驱动 DLL）。另有 FILETIME 时区坑：必须走 UTC 再本地化 |
| `probe-classic-kind.py` | **经典蓝牙设备类型信号探针**（纯 ctypes + SetupAPI）：枚举 `BTHENUM\*` 节点，打印服务 UUID（实例 ID 前缀）、`DEVPKEY_DeviceDisplay_Category`（如 `Input.Mouse`）与 ClassGuid。用来回答"这台经典蓝牙设备到底是耳机还是鼠标"，`--all` dump 全属性 |
| `test-rapou7.ps1` | 雷柏 dongle（VID_24AE）HID P/Invoke 直测。也是 build.md §7"PowerShell 5.1" 的范例：本机 Add-Type 不支持字符串插值/内联 out/可空注解，要用老式语法 |
| `probe-hid-caps.py` | **HID 接口能力探针**（纯 ctypes，只读）：`HidP_GetCaps` + `HidP_GetButtonCaps` 打出 UsagePage、IN/OUT/FEATURE 报告长度与逐条 button caps。回答"这个接口到底有没有主机→设备通道"——`OUT=0 / FEATURE=0` 即物理上无法主动查询（弱水时砂 dongle 的判据）。用法 `python probe-hid-caps.py 38FF`（参数为设备路径里的大小写无关片段，可省） |
| `test-rose-dongle-interactive.py` | ROSE dongle（38FF:0001）交互式被动监听 5 分钟，零写入——耳机按键操作会经 dongle 出现在这里，可当"耳机已连接"探针 |
| `test-rose-dongle-passive.py` | ROSE dongle MI_03（consumer）被动监听 60 秒，零写入 |
| `test-rose-dongle-endpoint.py` | ROSE dongle 接口 3 **绕过 HID 栈直读物理端点**（pyusb/libusb），只读不写。前提是 MI_03 已用 Zadig 换成 WinUSB——HID 驱动下 libusb 打不开设备，且 HID 栈只按描述符的 3 字节取包，端点上的长帧会被它丢掉。`--info` 打印配置/接口/端点，默认监听 0x82 300 秒 |
| `test-rose-headset-getinfo.py` | FIRST ANGEL USB 直连官方 GET_INFO 查询（report 0xAB，帧格式来自官方升级工具反编译），纯只读 |
| `test-rose-headset-passive.py` | FIRST ANGEL USB 直连 MI_03 被动监听 60 秒，零写入——看耳机是否自发推 0xAB 帧 |

## 约定

- **零写入原则**：协议未摸清前只做被动监听；主动写帧前先读 `docs/hardware/safety.md`（硬件安全红线）。
- Python 脚本依赖 `pip install hidapi`（HID 直测手法见 docs/hardware/debugging.md §2）。
- 脚本里的设备路径/接口号是本机实测值，换设备后以日志（按日 `app-日期.log`）的"HID 元数据"行为准。
- 排查完的脚本：能沉淀成知识的写进 `docs/`，脚本本身可删；值得留的再进索引。
