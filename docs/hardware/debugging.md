# 调试手法手册

按优先级排序。**核心原则：让官方软件去和硬件说话，我们只旁听**——比任何猜测都快、都安全。

## 0. 日志考古（永远第一步）

先翻 `%LOCALAPPDATA%\VigilWatt\logs\`（按日文件 `app-日期.log`）：接口扫描清单（VID/PID/usage/长度）、历次探测的原始报文、错误都在里面。过去的探测结论可能已经回答了当前问题（2026-10-05 的"邮箱扫描无响应"结论就是这样从旧日志里直接复用的）。

> **默认 `Info` 档看不到逐帧报文**（自 2026-10-09 日志分级改造起）：接口清单、逐帧原始报文、"读了但没解析出东西"的中间帧都是 `Debug`，
> 默认不写。要考古这些内容，先去**设置页把日志级别切到"详细"**（或改 `settings.json` 的 `LogLevel=Debug`），**重启应用**后再复现。
> 协议解析**结果**（`雷柏 状态帧` / `迈从 … 解析`）仍是 `Info`，行尾带 `原始=<HEX>`，默认档下照样能排障。
> 历史日志**按日分文件**：`app-2026-10-09.log` / `app-2026-10-10.log`…（默认保留 14 天）；`grep app-*.log` 可一次翻全部日期。

常用 grep：

```bash
grep -a "迈从" app-*.log | tail -30          # 迈从全部动作（跨全部日期）
grep -a "HID 元数据\|usage=" app-*.log      # 接口清单（需先切 Debug）
grep -a "ERR \|WARN " app-*.log | tail      # 最近错误
grep -a "时间基准" app-*.log                 # 文件首行：本地时区 + UTC offset（与 history.db 的 UTC 对齐）
```

设备/设置：`%LOCALAPPDATA%\VigilWatt\settings.json`（PluginEnabled——各插件的开关，同时就是它的写入授权）、history.db（SQLite，samples 表）。

## 1. 抓官方软件的包（浏览器 WebHID monkey-patch）★主力手法

不装 USBPcap、不碰硬件，让官方 M HUB WEB 自己驱动真机，我们记录每个字节。

### 1.1 启动带调试端口的 Edge

```bash
"/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" \
  --remote-debugging-port=9333 --remote-allow-origins='*' \
  --user-data-dir=<独立目录> --no-first-run "https://www.mchose.com.cn/#/home"
```

（`pip install websocket-client` 后用 python 走 CDP；`--remote-allow-origins` 必加，否则 WS 403。）

### 1.2 注入 HID 探针（关键：要在页面脚本运行前）

用 `Page.addScriptToEvaluateOnNewDocument` 注入，包住四条通道：

- `HIDDevice.prototype.sendReport` → 记 out
- `HIDDevice.prototype.getFeatureReport` / `sendFeatureReport` → 记 featIn/featOut（**官方电量可能走 feature 读**，漏包这个会一无所获）
- `addEventListener('inputreport')` 和 `oninputreport` setter 都要包（不同代码路径用不同写法）
- `HIDDevice.prototype.open` 记录 collections（report ID 布局白送）

字节全部 push 进 `window.__frames`，事后 `Runtime.evaluate("JSON.stringify(window.__frames)")` 收走。

### 1.3 过 HID 选择器弹窗（native，CDD 点不到）

- 点页面"连接设备"按钮必须用 `Input.dispatchMouseEvent`（真实鼠标事件，满足 user activation）。
- 弹窗是独立 Win32 窗口（标题含"想要连接到 HID 设备"）：`EnumWindows` 按标题找 → `PostMessageW(WM_LBUTTONDOWN/UP)` 点设备行和"连接"按钮（坐标从截图量取，客户区坐标 = 截图坐标 × client/位图尺寸比，顺带完成 DPI 折算）。
- 弹窗失焦会自动关闭：点完连接按钮的同一脚本内立刻处理，别跨脚本调用。

### 1.4 截图弹窗内容

- **必须 `PrintWindow(hwnd, dc, 2)`（PW_RENDERFULLCONTENT）**：浏览器窗口是 GPU 合成，`GetWindowDC + BitBlt` 出来全黑；屏幕 DC 抓屏又会被上层窗口遮挡。
- python 进程先 `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)`，否则多 DPI 下 GetWindowRect 坐标是虚拟化的，截出来张冠李戴。
- PIL 一行转 PNG 后用看图工具读。

### 1.5 注意事项

- **会话排他**（safety.md §2 事故二）：抓包时本应用必须退出；页面刷新会弄脏 dongle 会话，抓完把 Edge 整个关掉。
- CDP 驱动页面用的是独立 user-data-dir，不会污染用户浏览器配置；但 `taskkill msedge.exe` 会连用户的 Edge 一起杀——**先提醒或让用户自己关**。
- 官方抓到的帧先和公开逆向文档（arutkowski00/mchose-hid-bridge 的 K99 V1）对照，命名/校验逻辑能对上再写进插件。

### 1.6 Electron 官方软件：翻它自己的日志和前端缓存

Razer Synapse 4（`RazerAppEngine`）这类 Electron 客户端会把**每次 HID 收发**写进按产品分的日志，命令表与帧构造则在它下载的前端 JS 里——不装抓包工具也能拿到字节级依据（2026-10-19 Kraken V4 就是这样做的）：

1. **日志**：`%LOCALAPPDATA%\<厂商>\<客户端>\User Data\Logs\products_*_mw*.log`。搜 `getBatteryStatus` / `hid.read data is` / `dataHeader`，能直接看到请求头、应答帧和"命令→描述"表（日志里的数组是十进制）。
2. **前端 JS**：Chromium 缓存里是 **gzip 过的 webpack chunk**（`User Data\Default\Cache\Cache_Data\f_*`），按开头 `1F 8B 08` 解压后搜类名（如 `AudioBillieT2Dongle`）。读 `_createDataSend` / `_calculateChecksum` / 应答解析函数即可复原帧布局；设备清单（productId/claimInterface）在 IndexedDB blob 里。
3. **交叉验证**：日志应答帧要与 JS 的校验和、字段布局逐字节对上，再上真机发一帧确认。

注意日志/缓存可能不是当前安装版本（官方滚动更新），发帧前用真机应答复核一次。

## 2. HID 直测（拿到帧之后的验证）

`pip install hidapi`，比手搓 SetupAPI/CtecTypes 快十倍：

```python
import hid, time, threading
target = [d['path'] for d in hid.enumerate(vendor_id=0x41E4) if b'Col01' in d['path']][0]
h = hid.device(); h.open_path(target)
# 后台线程 h.read(64, timeout_ms=150) 收报文
h.write([0x13, 0x05, 0x01, 0, ...])   # 只发手册里记载的帧
```

坑：自己用 ctypes 调 SetupAPI 枚举路径很容易错（cbSize/对齐/DPI），直接 hidapi 枚举；PowerShell Add-Type 只支持 C# 5（dev/build.md §6）。

## 3. 排查"零响应"的顺序

1. **谁持有会话？**（safety.md §2 事故二）关掉本应用、浏览器里的 M HUB、官方桌面版，再看。
2. 帧校验对不对？（0x13 协议 = 前 19 字节和；QHW = 从 ver 字节起 XOR）
3. 会话开了没有？（0x13 协议必须先 `05 01`）
4. report ID 接受吗？（WriteFile 报"参数错误" = 描述符里没这个 ID，换通道）
5. 读取端是单悬空读模式吗？（dev/runtime-pitfalls.md §1）

## 4. 验证清单（改完协议代码必做）

1. `taskkill` 旧实例 → sln 构建 → **从 `bin\x64\...` 启动**（build.md §2）
2. 日志出现 `认领迈从设备 ... 电量=NN`，NN 与键盘实际指示一致（FN+B 看键盘灯）
3. 等 1 个轮询周期（1 分钟）+ 1 个采样周期（5 分钟），`history.db` 的 samples 表出现新行且百分比正确
4. 拔掉 dongle → 设备离线；插回 → 自动恢复上线
5. 涉及充电标志的改动：插充电线看 `状态字节` 日志再下结论

## 5. 修改硬件交互代码前

重读 safety.md。所有写帧逻辑必须有出处（本文档/protocols.md/官方抓包），并过**该插件自己的写入开关**（逐插件现状见 safety.md §3.1）+ 3 次失败熔断。**新增会写帧的插件必须显式声明 `WritesEnabled`，不许沿用基类默认值。**

## 6. 帧字节 ↔ 物理状态对照法 ★判断"某个字节到底是不是状态位"

**适用**：怀疑某个字节是状态/标志（休眠、充电、连接…），但只有一两次观察、分不清是"状态位"还是"数据本身"。
**做法**：把日志里**所有**该类型帧按时间排序，逐字节对比，找与物理状态**一一对应、零例外**的那个字节。

```bash
# 1. 统计每种帧变体出现次数（先看有几个不同取值）
grep -a -oE "收到报文: 04 [0-9A-F]{2} 80 80 80 80 [0-9A-F ]+" app-*.log | sort | uniq -c | sort -rn
# 2. 按某个字节取值分段，看每段对应什么现场（配合时间戳与用户口述的物理动作）
grep -a -E "收到报文: 04 " app-*.log | sed -E 's/(收到报文: 04 [0-9A-F]{2}).*/\1/' | awk '{print $2, $NF}'
```

**判据（2026-10-06 第九轮 T9 手柄实战，protocols.md §4.4.1）**：
- 该字节必须与状态**一一对应、一次例外都没有**（T9：`0x58` 只在手柄开机时出现、`0x50` 只在关机/休眠时出现 → 定案）。
- **一次观察不够**：当时另一个候选字节 `byte[1]` 在 `{0x02,0x04,0x0C}` 之间乱变（关机 `0x04`、休眠 `0x02`、开机 `0x02`/`0x0C`），
  只看到一次就会误判成"状态位"。**必须让用户复现"开机 → 关机"全过程，拿到 ≥2 段状态才下结论。**
- **拿不定语义时选保守规则**：`0x50`/`0x58` 只差最低位，既可读作"等级×8"、也可读作"高 4 位等级 + 低位链路标志"。
  按"低位=标志"判更"漂亮"，但若实际是等级×8，则**所有偶数等级都会被误判离线**（等于打掉一半正常读数）；
  只认 `0x50` 这一个值则在两种读法下都正确。**宁可漏判、不可误判**，并把未定论的口子写进文档（含验证方法）。
- **判完要顺手回答"误判的代价"**：标离线（设备还在列表、下轮能恢复）代价小；漏报在线/误报低电量代价大。

## 7. 现象分类：先分清"没做"还是"做了但看不见"

2026-10-06 第九轮的两个教训都来自这一步：
- 用户说"设备关了还显示在线" → 先问"是**判定错了**，还是**列表根本没更新**"。前者查协议/判定逻辑，后者查刷新链路。
- 用户说"按钮点了没反应"（2026-10-06 第八轮） → 可能是"没做"，也可能是"做了但反馈被自己抹掉"。
- 判定类问题优先看**日志里被拒绝/被过滤的帧**（本项目有 `读数异常已过滤`、`手柄未连接` 这类显式日志，就是为这个留的）。

## 8. 验证时先确认"跑的是新代码"

**踩过的大坑**：应用被 `taskkill /F` 强杀后**单实例互斥量仍由旧实例持有**（强杀不触发退出收尾），
新实例启动时会检测到并**静默退出**——日志里只会多一行 `检测到已有实例运行，退出`，
很容易被误判成"新代码没生效"。

> 自 2026-10-09 日志改造后有两处变化：
> 1. 日志文件以 `FileShare.ReadWrite` 打开，新实例的日志**能**正常写入
>    （旧版用 `FileShare.Read`，两个实例互相挡住、日志完全写不进，才造成"没有新启动记录"的假象）；
> 2. 托盘"退出"会先 `ReleaseMutex` 再结束进程，所以**正常退出**后新实例能立刻启动；
>    只有 `taskkill /F` 强杀才会留下"互斥量被占"的假象。

```bash
tasklist //FI "IMAGENAME eq VigilWatt.App.exe"     # 先确认没有旧实例
grep -a "启动 VigilWatt" app-*.log | tail -3       # 每次启动都有一行；没有 = 新实例根本没跑起来
```
另外：**从本仓库的 agent shell 拉起 GUI 应用会被进程树清理在 ~7 秒内带走**（日志只写到首轮扫描就断）。
要长驻请让用户双击 `publish\VigilWatt.App.exe`；验证只求"看到首轮日志"时可以用 `cmd //c start`。

## 9. 通用协议采集工具

独立工具 `src/tests/VigilWatt.ProtocolCapture/` 提供 USBPcap、WebHID 与 BLE 被动广告采集，不加入 VigilWatt 主应用。使用 `dotnet run --project src/tests/VigilWatt.ProtocolCapture -- help` 查看命令。

USB Root Hub 枚举只查询 Windows 设备路径；用 Wireshark 的 USBPcap 接口详情把 Root Hub 对应到实际插入位置。示例：

```powershell
dotnet run --project src/tests/VigilWatt.ProtocolCapture -- usb roots
dotnet run --project src/tests/VigilWatt.ProtocolCapture -- usb capture --root-hub '\\.\USBPcap1' --out "$env:TEMP\device.pcap"
dotnet run --project src/tests/VigilWatt.ProtocolCapture -- usb export --in "$env:TEMP\device.pcap" --out "$env:TEMP\device.jsonl" --device 7
```

采集调用已安装的 `USBPcapCMD.exe`。USBPcap 原生输出经典 pcap；导出器也接受 pcapng。Root Hub 级采集会包含其下其他 USB 设备的数据；导出时可按 USB device address 筛选。USBPcap 以自身方式处理权限请求，工具不安装/配置驱动。不要用 USBPcap 无参数交互模式：过滤驱动不可用时，该模式可能提示重启全部 USB 设备。

WebHID 需要使用**独立 Chromium 用户配置目录**启动远程调试实例，避免复用现有浏览器进程：

```powershell
$profile = Join-Path $env:TEMP 'vw-capture-edge'
Start-Process msedge.exe -ArgumentList @('--remote-debugging-port=9222', '--remote-allow-origins=*', "--user-data-dir=$profile", 'https://www.mchose.com.cn/#/home')
dotnet run --project src/tests/VigilWatt.ProtocolCapture -- webhid targets --port 9222
dotnet run --project src/tests/VigilWatt.ProtocolCapture -- webhid capture --port 9222 --target '<页面 ID、URL 片段或标题片段>' --out "$env:TEMP\webhid.jsonl"
```

采集器旁路记录 WebHID API 调用和输入报告，不自行发送设备报文；官方页面会照常与设备通信。

BLE 使用 Windows Passive Advertisement watcher，只记录广播，不连接、读取 GATT 或写 CCCD：

```powershell
dotnet run --project src/tests/VigilWatt.ProtocolCapture -- ble capture --seconds 60 --out "$env:TEMP\ble.jsonl"
```

记录文件默认写到用户指定路径。USB 原始数据保存在 pcap；WebHID、BLE 和 USB 导出统一为每行一个 JSON 对象的 JSONL。USB device address 是临时地址，仅对该次采集有效。

**USBPcap 不在本机**（2026-10-18 查：`C:\Program Files\USBPcap` 不存在），所以上面 `usb capture` 那条路径在本机从未跑过，只用过 WebHID 与 BLE。装它最省事的是 Wireshark 官方 Windows 安装器勾选 "Install USBPcap" 可选组件；也可装 desowin/usbpcap 的独立包。它是过滤驱动，装卸都要重启，个别 USB 设备会受影响。

**向别人要采集材料时只要五样**：USBPcap 的 pcap（一次完整会话）、HID Report Descriptor 的**原始字节**（不是 preparsed 结果）、GET_REPORT 请求+响应完整 hex（注明用的哪个 API、连发三次句柄是否变化）、带时间戳且标注了操作的被动监听日志、VID/PID + 固件版本 + 芯片丝印。录屏与截图堆没有价值。
