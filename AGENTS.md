# AGENTS.md

> 给 AI 助手（以及未来的自己）的**行为规则**。开工前先读本文件，再读 `docs/project/handoff.md`。
> 项目知识在 `docs/`（入口 `docs/README.md`）；本文件只写"必须遵守的约束"，不重复知识。

VigilWatt：Windows 无线设备电量监控（WinUI 3 / .NET 8，个人自用）。不做游戏内显示、HTTP API、账号体系。

## 1. git 与远程红线（最硬，无例外）

### 1.0 一句话里只认字面授权（2026-10-15 事故后加硬）

**授权范围 = 用户最新一条消息里写明的动词。** 上一轮说过「提交」、这一轮只说「发布」→ **只发布，不提交**。禁止把多轮意向合并成「顺手一起做」。

| 用户说了什么 | 允许做什么 | 禁止做什么 |
|---|---|---|
| 「发布」/「打安装包」/「出新版本」 | 本机 `dotnet publish` + `vpk pack`（可改 csproj `<Version>`、changelog 版本表、handoff §1 版本号——**仅当发版必需**） | `git commit`、任何 push / `gh` / 上传 Release |
| 「提交」/「commit」 | 征得同意后的本机 `git commit` | push；不要顺便 `vpk pack` |
| 「提交并发布」等**同一条**里写全 | 本机 commit + 本机 pack | 仍禁止远程 |
| 「推送」/「上传 Release」等**明确**远程词 | 才可动远程 | — |

「发布」**永远不等于**推 GitHub。没有「推送」二字就不要碰远程——哪怕刚打完包、哪怕用户正在高兴。

### 1.0.1 含糊指令：禁止猜测，必须先问（强制）

指令**不够直白**时（一词多义、缺宾语、像「弄一下」「处理好」「同步一下」「更新一下」「发一下」、或既像提交又像打包），**停下来用一句话问清要做什么**，等用户确认后再动。**禁止按「最常见理解」或「顺手帮忙」自行选动作。**

对 **git / 远程 / 发版** 尤其如此：只要吃不准是提交、打包、推送、改版本号里的哪一种（或几种），**先列选项问用户**，不要先执行再解释。

能直接对照上表、动词唯一且无歧义的（例如只写「打安装包」「git commit」「推送到 origin」）才可直接做。

### 1.1 其它 git 约束

- **未经用户明确确认，不得执行任何 git 写操作**：`commit` / `merge` / `rebase` / `reset` / `checkout` / `branch` / `tag` / `stash` / 改 remote。（用户刚说「撤销提交」则该次 `reset` 算已授权。）
- **未经用户明确指示，禁止操作远程 GitHub**：推送、删除远程分支或 tag、创建或删除 Release、上传安装包、改仓库设置或权限。`git push`、`gh`、网页、token 都算。
- 提交和发布只在本机：`git commit` 不推送；`dotnet publish` / `vpk pack` 只写本机 `publish/` 和 `dist/`。
- 只读命令可自由执行：`status` / `log` / `diff` / `show` / `blame` / `ls-files`。
- 要提交时：先说清"改了什么、打算怎么提交"，等用户同意再动。
- 确认后：提交信息**从简**；一轮改动（哪怕跨几个主题）**合并成一个提交**，不拆分。
- 中文提交信息先写文件，再 `git commit -F <文件>`（避免 Git Bash 传中文乱码）。

## 2. 代码注释

- 严格按 `docs/dev/comment-style.md`。注释只回答"**为什么**"，且只在不写就会踩坑时才写。
- "是什么 / 曾经怎样 / 怎么验证的 / 帧长什么样" 一律进 `docs/`，代码里只留指针。
- `src/` 下每个 `.cs` 的主要类型，类头用文件头：贴在类型声明正上方（`using` 与 `namespace` 之后）。「功 能：」写一句话职责；下一行是创建时间 `yyyy/MM/dd HH:mm:ss`（月、日补零）和当时的 Windows 用户名。why 最多再加一行，类头仍 ≤ 5 行。格式见 `docs/dev/comment-style.md` 的文件头一节。
- 硬配额：**类头 ≤ 5 行、方法/属性 ≤ 2 行、字段 ≤ 1 行**。超了说明内容该进 `docs/`。
- 注释引用的 `docs/xxx.md §y` 必须指向真实存在的小节。

## 3. 动代码前

- 先读 `docs/project/handoff.md`（会话快照）+ 相关专题文档；冲突时以专题文档为准。
- **`handoff.md` 与 `changelog.md` 不许再写长。** 协议、坑、流程进专题文档，这两份只留指针。
  - `handoff.md` 只改 §1 现状和 §4 未完成待办。不追加本轮经过，不保留已完成待办，不抄发布过程。**全文超过 120 行，先删历史再写。**
  - `changelog.md` 按日期追加：每条一两句（改了什么、为什么）。不记构建结果、打包耗时、文件清单。发版只在文末版本表加一行。单日新增超过 15 行，就是把专题文档抄进来了。
  - `docs/reviews/BACKLOG.md` 只留 `open` / `doing`，每条一行。关闭后立刻挪到 `docs/reviews/archive/BACKLOG-CLOSED.md`，编号不复用。调查过程写审查报告或专题文档。**全文超过 120 行，先挪已关闭的再写。**
- **UI 改版：先出方案，用户确认后再改代码。**
- 硬件写入类改动：先读 `docs/hardware/safety.md`（零写入原则、写入闸门；各插件开关现状见其 §3.1）。
- 改读数过滤器 / 帧解析 / 历史库后，**必须重跑单测**。
- 动代码前对照 `docs/dev/review-standard.md` 的 R 清单；**审查发现的未修项在 `docs/reviews/BACKLOG.md`**（唯一载体）。
- **收进社区贡献后补两处，各管一段，不重复**：根目录 `CONTRIBUTORS.md` 按人加一行（用户向，只写"谁做了什么"、关联 PR）；`docs/project/contributions.md` 记这个 PR 收了什么、**没**收到什么、是否本机复测。

## 4. 构建与测试

- 必须用 **.NET 8 SDK**（机器上只有运行时时：`winget install Microsoft.DotNet.SDK.8`）。
- 构建：`dotnet build VigilWatt.sln -c Debug`
- 运行：从 `src\VigilWatt.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\` 启动。
  必须用 **sln** 构建（产物在 `bin\x64\...`），直接构建 csproj 的产物在 `bin\...`，不要混用。
- 测试：`dotnet test src/tests/VigilWatt.Core.Tests`
- 后续自动测试默认交给 `.github/workflows/ci.yml`，不再本地执行；仅用户明确要求本机复现时例外。本地环境局限与 CI 范围见 `docs/dev/build.md` §4.1–§4.2。
- 发布：`dotnet publish src/VigilWatt.App -c Release -p:Platform=x64 -o publish`
  发布前确认没有实例在运行——exe 被占用会导致 `MSB3027` 构建失败。

## 5. 不要碰

- 构建/发布产物：`bin/`、`obj/`、`publish/`、`dist/`（均已 gitignore）。
- 助手元数据目录：`.workbuddy/`、`.workbuddy-ai/`、`.zcode/`（已 gitignore，**规则写这里等于没写**）。
- `.vpk-github-token`（**文件名是历史遗留，内容实际是 Gitee 令牌**，已 gitignore，绝不入库）。`publish-github.ps1` 读同名文件当 GitHub 令牌，所以该脚本目前用不了。
- 仓库根不放临时文件；一次性调试脚本放 `scripts/`，并补 `scripts/README.md` 索引。

## 6. 静默失败的两个坑（必记）

- XAML 里写了不存在的属性（如 `NumberBox` 的 `Min`，应为 `Minimum`）会让 XamlCompiler **静默崩溃**（`MSB3073`，无任何报错输出）。
- 项目目录下不能有多余的 `.xaml`（会被自动 glob 成编译项）。

其余坑见 `README.md` 末尾"最常踩的几条速记"与 `docs/dev/`。

## 7. 沟通

- 中文交流，文档中文；界面只中英文。

## 8. 审查（提交前必过三条）

- **L0 机械层**：由 GitHub Actions 构建 `0 警告 0 错误` 并运行完整核心测试；改了帧解析 / 读数过滤 / 历史库 / 离线判定必须有对应回归覆盖。不再默认本地跑测试，首次 CI 尚未运行时不得将静态检查写成通过，见 `docs/dev/build.md` §4.1–§4.2。
- **L1 自查**：碰了红线区域（硬件写入 / 状态模型 / 线程 / 资源释放 / 异常）就对着 R 清单过一遍，
  标准见 `docs/dev/review-standard.md`。其中 **R2「插件必须继承 `BatteryPluginBase`」** 是历史事故的唯一成因，无例外。
- **发现必须进 `docs/reviews/BACKLOG.md`**——只写在审查报告或 changelog 里等于没记。报告只承载证据与推理。

## 9. 设置页只列品牌（不列具体型号）

- 设置页的插件开关是**品牌级**开关，**中英两名都必须有**：主标题 `DisplayName` 写中文（`"北通"`），
  副标题 `DisplayNameEn` 写英文（`"BETOP"`）。只给中文或只给英文都不合格。
- 标题不带型号、不带品类：是 `"北通"`，不是 `"北通手柄"`；是 `"高斯"`，不是 `"GANSS GS3104T-Pro 2.4G"`。
- 具体型号是**扫描期产物**：由插件的 `BuildDisplayName` 识别，只出现在设备列表 / 托盘 / 历史里。设置页不关心用户插的是哪个型号。
- 一个品牌接多个型号（迈从键+鼠、罗技键鼠）时，设置页仍然只有**一行**开关，型号差异在插件内部消化。
- 一行只有两样东西：**中文名 + 英文名**。不挂品类、协议、通道——那些是扫描结果或能力细节，不进设置页。
