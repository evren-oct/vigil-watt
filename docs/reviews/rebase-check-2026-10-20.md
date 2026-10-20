# rebase 后构建与测试复核

## 1. 基线与执行方式

首轮检查的已提交版本为 `2d932820296cd6b1b084efa509c9dad6e4b8ed25`，当前分支 `add-peripheral-battery-support`。从 HEAD 导出独立副本，另测含现有 `BatteryPluginBase.cs` 未提交修改的工作区；首轮没有修改产品代码、提交或推送。随后用户要求修复，结果见 §4。

本机安装并选择 .NET SDK 8.0.426，两份解决方案均通过 `dotnet build VigilWatt.sln -c Debug`，0 警告、0 错误。

标准 `dotnet test --no-build --filter` 和直接 VSTest 均在测试宿主监听父进程时抛 `Win32Exception (5)`，未执行用例。改用测试输出中已有的官方 xUnit `AssemblyRunner.WithoutAppDomain`，保持原程序集和断言，逐类独立进程、关闭并行、每类限时 45 秒执行。临时 runner 与逐类日志在本机临时目录 `VigilWatt-rebase-check-a6e868062a9e46edafcf5f843099bf11`；未访问真实 HID、未替换正在运行的便携版。

## 2. 修复前结果

| 范围 | 用例数 | 通过 | 失败 | 跳过 |
|---|---|---|---|---|
| 已提交 HEAD | 580 | 573 | 7 | 0 |
| 含现有未提交修改的工作区 | 580 | 577 | 3 | 0 |

HEAD 的 `PluginReconnectTests` 失败用例：`ReconnectInSameScanKeepsOnlyTheNewOnlineStatus`、`MultipleInterfacesOfSameUnitReleaseDuplicateProbeHandle`、`PathCaseChangeDoesNotReopenOrRemoveSameWindowsInterface`、`CaseAliasesInOneScanDoNotOverwriteAndLeakFirstHandle`、`DifferentReceiverReusingSamePathCanBeRediscovered`。现有未提交修改修复其中四项，但同轮新路径探测被旧设备 ID 去重挡住，随后旧路径被标为离线，仍未满足在线重连断言。

两份版本的 `BrandAndVisibilityTests.MigrationPreservesBrandPermissionAndRemovesModelToggle(false/true)` 都失败：测试访问 `PluginEnabled["Other"]`，`NormalizePluginSettings` 已按默认品牌表删除该键。这里需要统一测试契约与实际清理规则，不能只据异常决定保留所有未知键。

## 3. 首轮跟踪与推送结论

CR-75 从已关闭归档移回 BACKLOG；设置迁移契约冲突登记 CR-76。测试未通过，未执行推送。只读核实 fork 的该分支已指向同一 HEAD，不能由远端已有该提交推定测试通过。

## 4. 修复与复测

在原有未提交修改上修复：`AttachAsync` 将消失接口的监听、句柄和认领释放提前到探测前；新身份状态落地时移除返回列表里同身份的旧离线快照。探测失败仍返回旧离线状态，仍存在的重复接口继续释放新句柄。认领注册表和探测冷却字典与设备字典保持相同的路径大小写规则，防止大小写变化后被另一插件重复认领或无法释放。

设置迁移保留现有实现：已知品牌选择不覆盖，缺失品牌补默认值，废弃及未知插件键删除。测试改为验证这一契约，并额外确认雷柏的关闭选择和重复迁移均保持不变；没有放宽任何硬件写入授权。

新增同路径、同身份重探回归，并加强路径大小写变化后的跨插件认领互斥与移除释放断言。使用 .NET SDK 8.0.426 构建解决方案，0 警告、0 错误；先按 §1 的 xUnit 方式逐类执行 **581 项全部通过、0 失败、0 跳过**，结果在临时目录的 `xunit-fixed/`。随后通过 §5 的宿主设置用标准 `dotnet test` 再逐类执行 44 个测试类，仍为 **581 项全部通过、0 失败、0 跳过**，逐类 TRX 与汇总保存在 `vstest-fixed/`。

L1 自查：R1/R2 的协议与写入闸门保持原样；R3 保持既有串行 pass 和下游克隆约定；R4 确认消失、重探、失败与别名路径均释放相应句柄和认领；R5 沿用现有异常日志。CR-75、CR-76 均移回已关闭归档。本轮未提交、未推送、未替换运行版，真实设备拔插仍未验证。

## 5. VSTest 启动诊断

失败点是默认 `testhost.exe` 在监听 VSTest 父进程退出时请求进程句柄，被系统返回错误码 5。对照程序在 .NET 8.0.17、8.0.32、10.0.12 下均能查询和监听父进程；直接以 `dotnet exec testhost.dll` 监听 PowerShell 也未复现此权限错误。VSTest 诊断模式核实了实际父子 PID，没有证据归因于某款防护软件。

VSTest 支持的正确配置键为 `RunConfiguration.DotNetHostPath`，指定本机 `dotnet.exe` 后由其加载同一 `testhost.dll`，标准测试入口恢复。早期尝试写成 `DotnetHostPath`（小写 n）被忽略，诊断日志明确提示意外 XML 节点；因此此前的失败不能作为该设置无效的证据。准确命令见 `docs/dev/build.md` §4.1，无需升级依赖、关闭系统防护或修改进程权限。

用户在独立 PowerShell 中对照复现：原命令同样因错误码 5 中止，加 `DotNetHostPath` 后 11 项重连测试全部通过，故问题不限于助手的沙箱执行。输出目录的 `testhost.exe` SHA256 与 NuGet 原包一致，Authenticode 签名有效。两种宿主进程均观察到 `C:\ProgramData\GhostProxifier\ghost_core_x64.dll`，模块出现只证明代理注入参与了运行环境，不能据此判定它造成权限错误；尚未完成无注入对照。

## 6. 文件完整性标签根因与默认配置

用户退出代理并新开 PowerShell 后，原命令仍报同样错误。随后读取进程令牌，默认 `testhost.exe` 为 `S-1-16-4096`（Low），VSTest 父进程为 `S-1-16-8192`（Medium）。`icacls` 确认仓库根为 `Low Mandatory Level:(OI)(CI)(NW)`，输出 EXE 继承了 Low；NuGet 原包和 Program Files 中的 `dotnet.exe` 没有该标签。文件标签的设置来源和时间尚未追溯。

将当前产物复制到未标 Low 的临时目录，不改二进制内容，使用默认 `testhost.exe` 即可通过 11 项重连测试。再仅切换该临时 EXE 的标签：Low 时重现同一错误码 5，Medium 时恢复 11 项通过；仓库原 EXE 和临时副本的 SHA256 均为 `D2EB1420117E8096F505ED0786C47E32E2E2E16CB80F90C4C308B1EF61A8E24E`。因此根因是宿主执行文件的完整性标签造成进程降权，不能再只解释为不明启动兼容问题，也不能从用户终端复现推断与目录的沙箱标签无关。

修复采用项目默认配置，不更改仓库/沙箱 ACL：Windows 标准安装路径存在时，`RunSettingsFilePath` 默认指向 `vstest.runsettings`，让 `dotnet.exe` 加载同一测试宿主。Linux 不启用此设置，用户可以显式指定其他 runsettings 或宿主路径。用户原命令无需额外参数已通过 11 项重连测试；配置关联 CR-77。

最终以 .NET SDK 8.0.426 构建解决方案，0 警告、0 错误；不传 `--settings` 或 `DotNetHostPath`，按原始 `dotnet test --no-build --no-restore --filter` 方式逐类复测，44 类共 581 项全部通过、0 失败。逐类 TRX 与汇总保存在临时目录 `vstest-default-fixed/`，CR-77 已关闭。
