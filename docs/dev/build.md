# 构建与环境坑

全部实踩于 2026-10-04~05，特征大多是"静默失败"。机器：Windows 11 x64（26200），.NET 8 SDK 8.0.425（winget 装的，机器原本只有运行时）。

## 1. XamlCompiler 静默崩溃（最阴的坑）

**症状**：`dotnet build` 报 MSB3073（退出码 1），XamlCompiler.exe 崩溃，**没有任何报错内容**——不写 output.json、不进事件日志，错误列表全空。

**根因**：XAML 里写了控件上不存在的属性。实例：`NumberBox` 用了 `Min`/`Max`（正确是 `Minimum`/`Maximum`）。

**排查法**：二分注释各页面 XAML 定位文件；确认文件后逐属性核对。改完 XAML 属性名后 pass1 挂了，优先怀疑属性名。

## 2. 双输出路径，容易跑旧二进制

- 直接 `dotnet build src/.../xx.csproj` → 产物在 `bin\Debug\...`
- `dotnet build VigilWatt.sln`（Platform=x64）→ 产物在 `bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\`

**规则：始终用 sln 构建、从 `bin\x64\...\win-x64\` 启动**。跑错目录会拿着旧 exe 验证半天新代码，浪费一小时还得出错误结论。

## 3. 应用运行时锁 DLL

构建前先 `taskkill //IM VigilWatt.App.exe //F`，否则 MSB3021/3027 文件被占用。

## 4. 发布

```
dotnet publish src/VigilWatt.App -c Release -p:Platform=x64 --self-contained true -o publish
```

self-contained（含 .NET 运行时与 Windows App SDK 运行时），约 102MB / 335 个文件，无需安装任何依赖。产物落在仓库根 `publish/`（已 gitignore）。sln 的 GUID 是手写的，VS 能正常打开。

⚠ **`--self-contained true` 不能省**（2026-10-19 实测）：csproj 里虽然写了 `RuntimeIdentifier=win-x64`，但 .NET 7 起带 RID 发布不再隐含自包含。本机 SDK 10 下不带该参数只出 **153 个文件、缺 `coreclr.dll`/`hostfxr.dll`**，安装包会要求目标机自备 .NET 8，与「self-contained 无需依赖」的承诺不符。

**手工替换便携版 `current` 时（2026-10-19 实踩）**：先对比目标 `current` 与 `publish/` 的文件清单。`sq.version` 由 Velopack 生成、`dotnet publish` **不产出**；漏掉它，启动器 `VigilWatt.exe` 会**静默退出（退出码 0、无任何提示）**，表现为"exe 打不开"。

**发布位只有这一个**：仓库根 `publish/`。2026-10-06 目录重整时曾迁到过 `src/publish/`，但打包脚本 `scripts/build-installer.ps1` 一直用仓库根，2026-10-09 统一回仓库根——`changelog.md` 里残留的 `src/publish` 是历史流水账，不要照着它改回来。

**坑：Release 发布必须开 `AllowUnsafeBlocks`**（2026-10-08 实踩）。`dotnet publish -c Release` 时 WinRT AOT 源生成器（`WinRTAotSourceGenerator`）会生成 `obj\...\intermediatexaml\WinRT.SourceGenerator\...\WinRTGenericInstantiation.g.cs`，内含 `unsafe` 代码；工程若没开 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`，就会刷一屏 `error CS0227: 不安全代码只会在使用 /unsafe 编译的情况下出现`，并连带 XamlCompiler 报 `error MSB3073 ... XamlCompiler.exe 已退出，代码为 1`。

**别被 MSB3073 带偏**——它的表象和 §1「XAML 属性名写错」一模一样，但这里根因是**编译选项缺失**：XamlCompiler 的 pass1 中间项目编译失败，才以退出码 1 收场。判别法：错误列表里**先有 CS0227**（在 `WinRTGenericInstantiation.g.cs` 上）就是本坑；只有 MSB3073、没有任何 CS 错误才是 §1。

Debug 构建不启用该生成器，所以**只有 Release 打包时暴露**（`dotnet build -c Debug` 一直 0 错误，容易误判成"工程没问题"）。已在 `VigilWatt.App.csproj` 加好该项。

## 4.1 测试

后续自动化验证以 `.github/workflows/ci.yml` 为准，在 GitHub Actions 的干净 Windows / Ubuntu 环境中运行完整核心测试；本机默认不再执行自动测试。Windows 同时构建整个 WinUI 解决方案，Ubuntu 只构建 `net8.0` 核心测试项目。CI 的触发、报告和验证边界见 §4.2。

以下命令保留用于用户明确要求的本机故障复现：

```
dotnet test src/tests/VigilWatt.Core.Tests --no-build --filter "FullyQualifiedName~<测试类名>"
```

**不带 `--filter` 的全量 `dotnet test` 在本机会卡住**（复现于 2026-10-09：卡在"正在启动测试执行"之后无任何输出，单跑 `LogTests` 即复现）。
这是**环境问题，与代码改动无关**——所以验证时一律用 `--no-build --filter "FullyQualifiedName~<类名>"` 指定相关测试类，秒级返回。

**2026-10-20 的启动权限错误**：仓库根存在可继承的 `Low Mandatory Level` 标签，默认 `testhost.exe` 继承后以 Low 运行，无法取得监听 Medium 父进程退出所需的权限，在 `SetParentProcessExitCallback` → `Process.EnableRaisingEvents` 处抛 `Win32Exception (5)`，尚未执行用例。同一临时副本只切换 EXE 标签即复现 Low 失败、Medium 通过，详见 `docs/reviews/rebase-check-2026-10-20.md` §6。标签设置来源与时间未追溯，不归因于代理软件。

测试项目在 Windows 标准 .NET 安装存在时，默认加载 `vstest.runsettings`，由 Program Files 中的 `dotnet.exe` 加载同一个 `testhost.dll`，不修改仓库或沙箱的权限标签。原测试命令可直接使用；需要手工指定宿主时：

```powershell
dotnet test src/tests/VigilWatt.Core.Tests --no-build --filter "FullyQualifiedName~<测试类名>" -- "RunConfiguration.DotNetHostPath=$env:ProgramFiles\dotnet\dotnet.exe"
```

键名必须是 **`DotNetHostPath`**（大写 N）：写成 `DotnetHostPath` 会被 VSTest 忽略，只有诊断日志提示无效 XML 节点。自定义 .NET 安装路径可用该参数或 `--settings` 显式覆盖；Linux 不启用 Windows 宿主配置。无需关闭系统防护或修改产品依赖。

xUnit 项目（`net8.0`、AnyCPU），用 `<Compile Include>` 直接链接 `src/VigilWatt.Core` 里的真源文件，不引 WinRT。
**链接清单就是"可测边界"**：文件在 `VigilWatt.Core.Tests.csproj` 里出现，才谈得上单测覆盖；新增纯逻辑文件应一并加进去（见 `docs/dev/review-standard.md` R8）。

**绝不要并行跑 `dotnet build` 和 `dotnet test`**：会抢 `bin/` 锁，构建报 MSB4166 被掐死、测试吐假的"测试主机进程崩溃"。

## 4.2 持续集成与本地验证边界

`push`、`pull_request` 与手动触发运行同一套 workflow，fork 也可独立使用。两种 runner 都安装并显式选择 .NET 8 SDK，顺序还原、构建和执行完整 `VigilWatt.Core.Tests`，不使用测试类过滤。Windows 用 sln 构建，保留 WinUI/XAML 编译检查；Ubuntu 不尝试运行 WinUI 或真实 HID。

CI 的构建将警告视为错误。单项测试挂起超过 2 分钟时中止测试宿主并采集诊断，整个 job 限时 20 分钟；TRX 和故障诊断无论成功失败均尝试上传，保存 7 天。工作流只有仓库内容读取权限，不依赖发布凭据，也不上传安装包。

本机曾因 NTFS Low 完整性标签导致 VSTest 在执行用例前就失败；这说明本地权限、SDK 和常驻工具会影响验证结果。CI 提供可复现的独立环境，不能替代 Windows 界面、任务栏集成、真实设备读写、充电编码和休眠重连验证。全量仅指测试项目当前纳入的全部用例，不代表整个应用所有功能均有自动测试。

外部贡献者的 workflow 可能需要上游维护者批准后才运行；CI 是否纳入上游由维护者决定。首次 GitHub 运行尚未完成前，只能报告配置已静态检查，不能报告 CI 通过。

## 5. NuGet 版本降级链

引入 System.Drawing.Common 时曾报 NU1605：H.NotifyIcon 的依赖链要求 ≥9.0.1，显式引用 9.0.1 解决。

**2026-10-09 修正**：H.NotifyIcon 早已整个移除（托盘改纯 Win32，见 runtime-pitfalls §5），这条约束不复存在。
而 9.0.x 是 .NET 9 代的包，会带入 `System.Private.Windows.Core`，后者要求 `System.Collections.Immutable` /
`System.Reflection.Metadata` 的 **9.0.0.0**，与 .NET 8 ref pack 的 **8.0.0.0** 冲突 → 每次构建报 MSB3277 ×2。
已改为 **8.0.7**（与 net8.0 同代，依赖只有 `Microsoft.Win32.SystemEvents`）。托盘图标只用 GDI+ 基础 API，8.0 完全够。

**查这类"版本冲突"警告别等完整构建**：WinUI 全量构建本机要 8–10 分钟，而 MSB3277 源于依赖图——
直接看 `src/VigilWatt.App/obj/project.assets.json` 里该包的版本，以及根因包（如 `System.Private.Windows.Core`）
是否还在，几秒就能定性。

## 6. 输出目录里的 88 个语言文件夹（.mui 语言包）

**症状**：`bin`/`publish` 下出现 `af-ZA`、`de-DE`、`fr-FR`… 共 88 个语言目录，各含 2 个 `.mui` 文件（WinUI XAML 框架的多语言 UI 字符串）。

**来源**：`WindowsAppSDKSelfContained=true` 时，`Microsoft.WindowsAppSDK.SelfContained.targets` 的 `AddMicrosoftWindowsAppSDKPayloadFiles` 把 WindowsAppRuntime MSIX 解包并**全量**拷贝，`.mui` 在其中。注意它们不走 .NET 卫星程序集管道，`SatelliteResourceLanguages` 属性管不到。

**解法**（已加在 VigilWatt.App.csproj）：官方排除口 `MicrosoftWindowsAppSDKFilesExcluded`，自定义 Target `TrimWinUiMuiLanguages`（BeforeTargets=AddMicrosoftWindowsAppSDKPayloadFiles）把白名单（en-us/zh-CN/zh-TW）以外的 .mui 塞进去。改白名单时同步改 `MuiLanguagesToKeep` 属性和 `_MuiKeep` 的显式路径。

**两个坑**：
1. `$(MicrosoftWindowsAppSDKMsixContent)` 属性**不带尾反斜杠**，glob 拼接要自己补 `\`（`...MsixContent)\**\*.mui`），漏了会匹配到 0 个文件、排除静默失效——诊断用 `@(_MuiAll->Count())` 消息打印计数；
2. 该拷贝是 `PreserveNewest` 只增不减，加过滤后要**手动清一次旧输出目录**才干净。

## 7. 临时 PowerShell 测试脚本只能用 C# 5

本机 PowerShell 5.1 的 `Add-Type` 不支持：字符串插值 `$"..."`、`out var`/`out uint x` 内联声明、`string?` 可空注解。写 P/Invoke 测试脚本（如 scripts/test-rapou7.ps1）要用老式语法：先声明变量、string.Format/拼接。**更省事的替代：`pip install hidapi` 用 python 直接测 HID**（见 debugging.md）。脚本一律放 `scripts/`（见 scripts/README.md）。

## 8. 安装包（Velopack）

2026-10-06 接入。一条命令出安装包：

```
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

脚本做的事：taskkill 运行中实例 → `dotnet publish`（Release x64，同 §4）→ 读发布 exe 的 FileVersion 当包版本（截成 3 段，vpk 不收 4 段）→ `vpk pack`。产物在 `dist/`：

- `VigilWattApp-win-Setup.exe`：安装包（**仅当前用户**、无 UAC、装到 `%LocalAppData%\VigilWattApp`，自动建桌面+开始菜单快捷方式、写卸载条目）
- `VigilWattApp-win-Portable.zip`：绿色免安装版（vpk 顺带生成）
- `VigilWattApp-*-full.nupkg`、`RELEASES`、`releases.win.json`：Velopack 更新数据；发布到 Gitee Release 时需将 `dist/` 全部产物作为附件上传，确保清单引用的包都可下载

### 关键决策与坑

1. **packId 必须叫 `VigilWattApp`，不能叫 `VigilWatt`**。Velopack 默认装到 `%LocalAppData%\<packId>`，叫 VigilWatt 会和应用数据目录 `%LOCALAPPDATA%\VigilWatt`（settings.json/history.db/logs）撞车，升级时数据被清。脚本里有注释，改 packId 前想清楚。
2. **vpk 不指定 `--runtime` 会默认 x86**（仅警告不报错）。脚本是 x64-only 应用，传了 `--runtime win-x64`；改打包方式时别丢。
3. **同一个版本不能重复 pack**（FTL: release in channel win equal or greater）。重打同版本要先把 `dist/` 清空；
   **发新版本时不要清空**——旧版本的 `*-full.nupkg` 是 delta 的基准，留着才会自动生成
   `新版本-delta.nupkg`（2026-10-12 实测：1.0.3 → 1.0.4 的 delta 只有 **97KB**，而全量包 36MB）。
   `RELEASES` / `releases.win.json` 会同时列出新旧包，客户端自动挑 delta。
4. **PowerShell 5.1 的编码坑**：.ps1 含中文必须存成 **UTF-8 带 BOM**，否则中文注释被当 GBK 读、字符串终止符解析直接报错。改 build-installer.ps1 后确认 BOM 还在（VS Code 右下角选"通过编码保存 → UTF-8 with BOM"）。
5. **未签名**：安装/运行时 SmartScreen 可能弹"未知发布者"，点"仍要运行"即可；要消除需买代码签名证书后给 vpk 加 `--signParams`。
6. **从 bin/publish 直接跑的副本不能自更新**（Velopack `IsInstalled=false`），设置页"检查更新"会明确提示。装了的实例启动时还会**自动应用已下载的更新**（Velopack 默认行为，无需写代码）。
7. **`/releases/latest` 在"仓库一个 Release 都没有"时返回 404**（端点本身正常：有 Release 的仓库返回 200 + `tag_name`）。曾自写的 Gitee 适配器用这个端点定位最新 tag，`EnsureSuccessStatusCode()` 一撞 404 就把"没发过版"报成"检查失败"。改用内置 `GiteaSource` 后走的是 `/releases` 列表端点，空仓库返回 `[]`、视为"无新版本"，不再抛错。
8. **`publish-gitee.ps1` 有三个 Gitee 特有坑**（2026-10-14 首发 1.0.11 实踩，不真跑一次发现不了）：
   - **PowerShell 5.1 把 JSON 的 `null` 反序列化成字符串 `"null"`**（`[bool]"null"` = `$true`）。存在性判断因此把"还没有 Release"误判成"Release 已存在"，**首次发布必踩**。判据：`Invoke-RestMethod` 返回类型是 `System.String`、值是 `null`，脚本里显式归一化掉。
   - **Gitee 建 Release 只收表单参数**（`application/x-www-form-urlencoded`）。发 JSON body 返 **HTTP 400**，且响应体不说明原因。
   - **Gitee 强制要求 `target_commitish`**（Gitea/GitHub 不要求）。缺了报 `{"messages":["target_commitish is missing"]}`；脚本用 `git ls-remote` 取到的 tag 指向的 commit 填。

### 应用侧接入（已做，改这些文件时注意）

- `Program.cs`：自定义入口（csproj `DISABLE_XAML_GENERATED_MAIN` 关掉了 XAML 生成的 Main）。`VelopackApp.Build().Run()` 必须最先跑——安装/更新/卸载钩子进程带 `--veloapp-*` 参数被拉起，在这里被消费后直接退出，所以 `App.OnLaunched` 的单实例互斥不会误杀钩子进程，也不用改。
- 卸载清理：`OnBeforeUninstallFastCallback` 里调 `AutostartService.SetEnabled(false)` 删 HKCU Run 自启动残留。
- `Services/UpdateService.cs` + 关于页"软件更新"区：更新源是 Velopack **内置的 `GithubSource` / `GiteaSource`**，不是自写适配器——Gitee 的 `/api/v1` 与 `/api/v5` 返回一致（2026-10-14 实测：同仓库同键同值），而 `GiteaSource` 的 API 基址正是硬编码的 `{host}/api/v1/`，所以直接可用。**GitHub 优先，8 秒内没结果或抛错就回退 Gitee**（`ProbeTimeout`）；检查成功后源固定，delta 包必须从给出清单的那个源继续下载。两个仓库 URL 是常量，必须分别与 `scripts/publish-github.ps1`、`scripts/publish-gitee.ps1` 指向同一个仓库，且两边的 Release 资产要一致。启动时检查一次，关于页保留手动检查。运行副本未安装时不执行安装更新。
- 版本号唯一来源：`VigilWatt.App.csproj` 的 `<Version>`，exe FileVersion、设置页显示、安装包版本都从它来。

### 更新说明（release notes）

`docs/project/release-notes.md` 是**用户向**更新日志（`changelog.md` 是开发向，两者别混）。同一份文件喂给三个地方：

- `build-installer.ps1` 按 `<Version>` 截出当前版本段 → `vpk pack --releaseNotes`，落进 `releases.win.json` 的 `NotesMarkdown` / `NotesHTML`（2026-10-18 用临时目录实测确认）；
- `publish-gitee.ps1` 用同一段当 Release 正文；
- 应用把整份文件打进包里（csproj `Content`）：关于页离线渲染全部历史，更新确认框读 feed 里目标版本的 notes。

格式契约（`## 版本` / 可选 `YYYY-MM-DD` 行 / `- ` 条目）写在文件头，解析器是 `Services/ReleaseNotes.cs`，截段脚本是 `scripts/get-release-notes.ps1`。**改格式要三处一起改**。版本段缺失只警告不阻断打包，但应用内会显示「本版本没有更新说明」；`NotesMarkdown` 为空时确认框只显示版本行，不留空框。

### 已验证（2026-10-06）

安装（静默）→ 目录/快捷方式/卸载条目正确、数据目录不冲突；自动更新（把 1.0.1 nupkg 丢进 `packages\` 后启动 1.0.0 → 自动升级重启到 1.0.1）；卸载 → 安装目录/快捷方式/卸载条目/HKCU Run 残留全部清掉，用户数据保留。

### 发布到 Gitee（回退源）

自动更新**先读 GitHub**（`github.com/evren-oct/vigil-watt`），8 秒内不可达或报错才回退 `gitee.com/YGWG-LX/vigil-watt`。两边走的是同一套 Velopack 清单：`GiteaSource` 经 `/api/v1/repos/.../releases` 列出版本（空仓库返回 `[]`，视为无新版本），Velopack 清单与包从 Release 附件下载。Gitee Release 下载路由见[官方说明](https://blog.gitee.com/2022/08/18/update/)；`vpk pack` 产出的 `releases.win.json` 与各 `.nupkg` 都要作为附件上传，确保清单引用的包都可下载。**两个仓库都要传同一份资产**，否则国内用户回退到 Gitee 时会看不到新版本。

**一次性配置**：Gitee 个人访问令牌（需仓库写权限）。脚本只认两种来源：`-Token` 参数，或 `GITEE_TOKEN` 环境变量——**它不会自己去读令牌文件**（读文件的是 `publish-github.ps1`）。本机的令牌在仓库根 `.vpk-github-token`（**文件名是历史遗留，内容是 Gitee 令牌**，已 gitignore），所以要先把它读进环境变量再跑脚本：

```
$env:GITEE_TOKEN = (Get-Content .vpk-github-token -Raw).Trim()
powershell -ExecutionPolicy Bypass -File scripts\publish-gitee.ps1
```

令牌只用于发布脚本，不放进客户端或仓库文件；脚本经 `Authorization` 头传递，不走 URL query。

**日常发布**（以 `1.0.12` 为例，替换为本次版本）。**顺序不能变**：`publish-gitee.ps1` 用 `git ls-remote --exit-code origin refs/tags/<tag>` 校验 tag 已在 origin，没推 tag 会直接 throw。

```
# 1) 改 src/VigilWatt.App/VigilWatt.App.csproj 的 <Version>，然后提交（本地，不推送）
#    中文提交信息先写文件，见 AGENTS.md §1
git commit -F <提交信息文件>

# 2) 打 tag 并推送。分支和 tag 都要推：只推 tag，main 不会前进
$version = "1.0.12"
git tag "v$version"
git push origin HEAD
git push origin "v$version"

# 3) 打包。别清 dist\，留着上一版全量包才有 delta（见上文第 3 条）
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1

# 4) 发布。先把令牌读进环境变量（见上文「一次性配置」）
$env:GITEE_TOKEN = (Get-Content .vpk-github-token -Raw).Trim()
powershell -ExecutionPolicy Bypass -File scripts\publish-gitee.ps1

# 5) 复核附件是否齐全（应含 releases.win.json + 全部 .nupkg）：
#    GET https://gitee.com/api/v5/repos/YGWG-LX/vigil-watt/releases
```

`publish-gitee.ps1` 自动从 csproj 读版本号，拒绝覆盖已存在的 Release，并把 `dist/` 下全部文件上传到该版本 Release。Release 必须包含 `releases.win.json` 及其引用的全部 `.nupkg`。客户端启动时检查一次；新版本会提示用户确认，启动到托盘时先显示托盘通知，用户打开窗口后再显示安装确认框。
