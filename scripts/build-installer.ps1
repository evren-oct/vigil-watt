# 打安装包：dotnet publish（self-contained）+ vpk pack（Velopack），产物在 dist\。
# 产物：VigilWattApp-win-Setup.exe（仅当前用户安装、无 UAC、自动建开始菜单+桌面快捷方式）
#       releases.win.json + RELEASES + *.nupkg（Velopack 更新清单与全量/增量包）
# 用法：powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 [-Version 1.0.1]
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot   # scripts\ 的上一级 = 仓库根

# 1) 应用运行时锁 DLL，先杀掉（见 docs/dev/build.md 第 3 条）
cmd /c "taskkill /IM VigilWatt.App.exe /F >nul 2>&1"

# 2) 发布 self-contained（含 Windows App SDK 运行时，目标机器无需装任何依赖）
$publishDir = Join-Path $root "publish"
$csproj = Join-Path $root "src\VigilWatt.App\VigilWatt.App.csproj"
dotnet publish $csproj -c Release -p:Platform=x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

# 3) 版本号：默认从发布 exe 的 FileVersion 读（单一来源是 csproj 的 <Version>）。
#    FileVersion 是 4 段（1.0.0.0），vpk 只收 3 段 SemVer2，截掉第 4 段。
$exe = Join-Path $publishDir "VigilWatt.App.exe"
if ($Version -eq "") { $Version = (Get-Item $exe).VersionInfo.FileVersion }
$parts = $Version.Split('.')
if ($parts.Length -gt 3) { $Version = ($parts[0..2] -join '.') }
Write-Host "==> 打包版本: $Version"

# 4) 更新说明：截 docs\project\release-notes.md 里当前版本那一段（格式契约见该文件头）。
#    缺这一段只警告不中断——应用内会显示「本版本没有更新说明」，比打不出包好。
. (Join-Path $PSScriptRoot "get-release-notes.ps1")
$notesFile = Join-Path ([System.IO.Path]::GetTempPath()) "vigilwatt-release-notes-$Version.md"
$notes = Get-ReleaseNotes -Root $root -Version $Version
if ($notes) {
    Write-ReleaseNotesFile -Text $notes -Path $notesFile | Out-Null
    Write-Host "==> 更新说明: release-notes.md 的 $Version 段"
}
else {
    Write-Warning "release-notes.md 里没有 $Version 段，本次打包不带更新说明"
}

# 5) Velopack 打包。packId 必须用 VigilWattApp 而不是 VigilWatt：
#    Velopack 默认装到 %LocalAppData%\<packId>，叫 VigilWatt 会和应用数据目录
#    %LOCALAPPDATA%\VigilWatt（settings.json/history.db/logs）撞车，升级时数据被清。
$dist = Join-Path $root "dist"
$vpkArgs = @(
    "pack"
    "--packId", "VigilWattApp"
    "--packTitle", "VigilWatt"
    "--packVersion", $Version
    "--packDir", $publishDir
    "--mainExe", "VigilWatt.App.exe"
    "--icon", (Join-Path $root "src\VigilWatt.App\Assets\VigilWatt.ico")
    "--runtime", "win-x64"
    "--outputDir", $dist
)
if ($notes) { $vpkArgs += @("--releaseNotes", $notesFile) }
vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack 失败" }

$feed = Join-Path $dist "releases.win.json"
if (-not (Test-Path -LiteralPath $feed)) { throw "vpk pack 未生成 releases.win.json 更新清单" }

$setup = Get-ChildItem $dist -Filter "*-win-Setup.exe" | Select-Object -First 1
Write-Host "==> 完成。安装包: $($setup.FullName)"
Write-Host "    更新产物需整体上传到 Gitee Release，发布脚本会上传本 dist 目录全部文件"
