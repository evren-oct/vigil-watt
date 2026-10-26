# scripts/ — 构建、发布与调试脚本

> 本目录放构建/发布流程脚本与一次性 / 临时调试脚本（PowerShell / Python / 一次性 P/Invoke 探针）。
> **长期存在的 C# 项目不放这里**：测试项目在 [`src/tests/`](../src/tests/)，工程源码在 [`src/`](../src/)。
> 临时产物用 `%TEMP%`，仓库根目录只放工程文件。新增脚本后在下面补一行索引。

## 脚本索引

| 脚本 | 用途 |
|---|---|
| `run-app.ps1` | **运行 Debug 版**：结束运行中实例（不然锁 DLL）→ 把 exe 的完整性级别提回 Medium（工作区目录带 `Low` 标签，重建的 exe 会继承它，导致没托盘/贴片跑到原点/写不进注册表）→ 从 `bin\x64\Debug\...\win-x64` 启动。Debug 的数据目录就在该目录下的 `data\`（`AppPaths` 用 `#if DEBUG` 分流），所以默认不用复制、不碰 `%LOCALAPPDATA%`。`-Copy` 是兜底：复制到工作区之外（默认 `C:\VigilWattRun`）并用 `VIGILWATT_DATA_DIR` 指过去。判据见 `docs/dev/runtime-pitfalls.md` §20，用法见 `docs/dev/build.md` §3.1 |
| `build-installer.ps1` | 打安装包：`dotnet publish`（Release x64 self-contained）→ 读发布 exe 的 FileVersion 当版本（截 3 段）→ `vpk pack`。产物落 `dist/`（Setup.exe、Portable.zip、`releases.win.json`、`RELEASES`、`*.nupkg`）。发新版**别清 `dist/`**，留着上一版全量包才有 delta；详见 `docs/dev/build.md` §8 |
| `publish-gitee.ps1` | 将 `dist/` 全部打包产物作为当前版本的 Gitee Release 附件发布；需要已推送的 `v<版本>` 标签与 Gitee 写权限令牌（`-Token` 或 `GITEE_TOKEN`，**脚本不读令牌文件**；本机令牌在仓库根 `.vpk-gitee-token`，需人工读进环境变量）。完整发版序列与三个 Gitee 特有坑见 `docs/dev/build.md` §8 |
| `get-release-notes.ps1` | 发布流水线共用函数：按版本号从 `docs/project/release-notes.md` 截出当前版本段（`Get-ReleaseNotes`），并以 UTF-8 无 BOM 落临时文件（`Write-ReleaseNotesFile`）。`build-installer.ps1` 与 `publish-gitee.ps1` 都 dot-source 它；格式契约见该 md 文件头 |
| `code-fingerprint.py` | **代码指纹校验器**（纯标准库）：剥掉注释与空白后对代码 token 序列算 sha256，用来**证明"只改了注释、逻辑一行没动"**。`snapshot <out.json>` 记基线 / `verify <in.json>` 比对（`-xaml` 后缀对 `.xaml` 生效，剥 `<!-- -->` 后比空白归一化文本）。做注释精简、批量重排这类"只该动注释"的改动前先 `snapshot`，改完 `verify` 必须输出 `[OK]` |
| `make-app-icon.py` | **程序图标生成器**（纯标准库，无需 Pillow）：解析式绘制绿色圆角方块 + 白色闪电，超采样抗锯齿，输出 `src/VigilWatt.App/Assets/VigilWatt.ico`（16~256 共 9 档）。`--preview x.png` 出一张放大预览图便于肉眼比对。**改图标配色/字形改这个脚本后重跑即可，不要手改 .ico** |
| `probe-devnode-battery.py` | **PnP 设备节点电量探针**（纯 ctypes + SetupAPI）：枚举全机节点读 `DEVPKEY_Device_BatteryLevel`。经典蓝牙耳机电量就是这么找到的（EDIFIER W820NB 挂在 HFP 网关节点上）；`--all --match X` 可 dump 匹配节点的全部属性，查任何"Windows 能看到但代码读不到"的设备属性都用它 |
| `probe-process-memory.py` | **进程内存分层采样**（纯 ctypes、只读）：按 `VirtualQueryEx` 的 `Type` 分层统计 **PRIVATE / MAPPED / IMAGE**，并附工作集、提交大小、句柄数。位置参数：`<进程名或PID> [秒数] [输出文件] [间隔秒]`。用来区分**"私有提交泄漏"（真泄漏，PRIVATE 单调涨）**与**"工作集可回收驻留"（高水位，回收后 PRIVATE 不动）**——判据见 `docs/reviews/archive/memory-growth-2026-10-12.md` §6。**注意：已提交虚拟内存总量 ≠ 任务管理器「提交大小」**，IMAGE 与文件后备 MAPPED 不计入进程提交量 |
| `check-taskbar-pill.py` | **任务栏贴片窗口探针**（纯 ctypes、只读）：打印显示器、任务栏（含 dpi/rect）与 `Shell_TrayWnd` 下的 `VigilWattTaskbarPill` 子窗口，贴片窗口 >1 时退出码 1。用来验证贴片窗口唯一性——"多一个并重叠"的判据见 `docs/dev/runtime-pitfalls.md` §16.12 |
| `parse-minidump.py` | **minidump 内存布局解剖**（纯标准库，无需 WinDbg）：解析 stream 3/4/9/12/16/17，给出进程时长、句柄/模块/线程数、dump 实抓量、分层提交（PRIVATE/MAPPED/IMAGE）与最大区域。**核心判据：dump 文件大小 ≠ 进程占用**（本机 725 MB 的 dump 里 181 MB 是 NVIDIA 驱动 DLL）。另有 FILETIME 时区坑：必须走 UTC 再本地化 |
| `probe-classic-kind.py` | **经典蓝牙设备类型信号探针**（纯 ctypes + SetupAPI）：枚举 `BTHENUM\*` 节点，打印服务 UUID（实例 ID 前缀）、`DEVPKEY_DeviceDisplay_Category`（如 `Input.Mouse`）与 ClassGuid。用来回答"这台经典蓝牙设备到底是耳机还是鼠标"，`--all` dump 全属性 |
| `test-rapou7.ps1` | 雷柏 dongle（VID_24AE）HID P/Invoke 直测。也是 build.md §7"PowerShell 5.1" 的范例：本机 Add-Type 不支持字符串插值/内联 out/可空注解，要用老式语法 |
| `probe-hid-caps.py` | **HID 接口能力探针**（纯 ctypes，只读）：`HidP_GetCaps` + `HidP_GetButtonCaps` 打出 UsagePage、IN/OUT/FEATURE 报告长度与逐条 button caps。回答"这个接口到底有没有主机→设备通道"——`OUT=0 / FEATURE=0` 即物理上无法主动查询（弱水时砂 dongle 的判据）。用法 `python probe-hid-caps.py 38FF`（参数为设备路径里的大小写无关片段，可省） |
| `probe-atk-battery.py` | **ATK / VXE（`373B`）电量通道体检**：按 PID 分组列出各接口的 usage page 与 IN/OUT/FEATURE 长度，再对 `0xFF02` 电量口只发 `docs/hardware/atk.md` §2 记载的那一条查询帧 `08 04 00×14 49`（`--caps` 一帧不发，`--listen` 纯被动收帧）。用于新增机型适配：确认 PID / 产品串 / 接口形状 / 应答结构是否与老机型一致。实测 A9 Air 大师版+ 的 GEM 接收器 `0x1278` 与本体 `0x1293` 均为 17/17 且应答一致，见 atk.md §2.2 |
| `probe-bolt-battery.py` | **罗技 Bolt 电量探针**（`pip install hidapi`）：在 `046D:C548` 的 `0xFF00/0x02` 长接口上发 HID++ 请求，优先 `0x1004` Unified Battery、回退 `0x1000` Battery Status，打印百分比与充电状态。用来核对插件读数是否与官方 HUB 一致 |
| `probe-bolt-ping.py` | **罗技 Bolt 连通性探针**：同一接口发 `Root.Ping`，只看有没有回应——用来区分"设备没连、被别的进程占了会话、还是协议读错" |
| `test-rose-dongle-interactive.py` | ROSE dongle（38FF:0001）交互式被动监听 5 分钟，零写入——耳机按键操作会经 dongle 出现在这里，可当"耳机已连接"探针 |
| `test-rose-dongle-passive.py` | ROSE dongle MI_03（consumer）被动监听 60 秒，零写入 |
| `test-rose-dongle-endpoint.py` | ROSE dongle 接口 3 **绕过 HID 栈直读物理端点**（pyusb/libusb），只读不写。前提是 MI_03 已用 Zadig 换成 WinUSB——HID 驱动下 libusb 打不开设备，且 HID 栈只按描述符的 3 字节取包，端点上的长帧会被它丢掉。`--info` 打印配置/接口/端点，默认监听 0x82 300 秒 |
| `test-rose-headset-getinfo.py` | FIRST ANGEL USB 直连官方 GET_INFO 查询（report 0xAB，帧格式来自官方升级工具反编译），纯只读 |
| `test-rose-headset-passive.py` | FIRST ANGEL USB 直连 MI_03 被动监听 60 秒，零写入——看耳机是否自发推 0xAB 帧 |
| `probe-mojhon-battery.py` | **墨将 E2B7 电量寄存器只读探针**：只重放实录里官方助手用过的读帧 `A5 04 <reg> <cks>`（寄存器 0x04 电量+充电标志 / 0x0B / 0xFA），不做命令枚举。解出 `payload[0]`=电量%、`payload[1]`=充电标志（0x00 放电 / 0x01 充电，见 `docs/hardware/mojhon.md` §6.8）。`python probe-mojhon-battery.py [轮数] [间隔秒]`。**注意写入须 65 字节、首字节 0x00**，见 §6.6 |
| `decode-frames.py` | **厂商通道帧解码器**（纯标准库）：读 USBPcap 导出脚本产出的 jsonl，按 `A5/A4/5A` 帧格式切包、逐条校验 checksum，打印「请求 → 应答」对照表。用来把抓包转成人能读的寄存器流水，见 `docs/hardware/mojhon.md` §6.4 |
| `probe-vendor-passive.py` | **通用厂商页 HID 被动监听，零写入**：只打开 `usage page >= 0xFF00` 的可读接口收 input report，不发任何字节。`python probe-vendor-passive.py <VID十六进制> [秒数]`。用于判断某设备的厂商通道是否自发上报（墨将为零自发帧，据此判定为查询型协议） |
| `probe-mojhon-wired.py` | **墨将 E2B7 双形态（2.4G / 有线）体检**：按 PID 分组列出所有接口的 usage page 与 IN/OUT/FEATURE 报文长度、报告描述符，再对厂商通道逐个发 `A5 04 04`（电量）与 `A5 04 E2`（型号标识）。`--caps` 只列能力、一帧不发。用来区分「接收器形态」与「有线直连形态」——实测两者同为 `122C`，见 `docs/hardware/mojhon.md` §7.4 |
| `probe-mojhon-jitter.py` | **墨将 E2B7 链路抖动测量**（只读）：10Hz 连发 `A5 04 04`，统计丢包率、往返延迟中位/最大、最长连续应答间隔、读数变化序列。`python probe-mojhon-jitter.py [秒数] [毫秒间隔]`。实测有线 241/241 全中、往返 3ms，见 §7.4 |
| `probe-mojhon-linkwatch.py` | **墨将 E2B7 联合监控**（只读）：250ms 采一次 HID 接口集合 + 每轮一帧 `A5 04 04`，输出「USB 层 / 协议层」合并时间线。用于把"手柄断连重连"定位到具体层：接口集合变化=USB 重枚举，集合不变但无应答=仅协议掉线。实测插接收器会让有线设备断 0.5 秒，见 §7.5 |

## 约定

- **零写入原则**：协议未摸清前只做被动监听；主动写帧前先读 `docs/hardware/safety.md`（硬件安全红线）。
- Python 脚本依赖 `pip install hidapi`（HID 直测手法见 docs/hardware/debugging.md §2）。
- 脚本里的设备路径/接口号是本机实测值，换设备后以日志（按日 `app-日期.log`）的"HID 元数据"行为准。
- 排查完的脚本：能沉淀成知识的写进 `docs/`，脚本本身可删；值得留的再进索引。
