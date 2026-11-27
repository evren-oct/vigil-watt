# 运行 Debug 版。
#
# Debug 构建的数据目录在 exe 旁边的 data\（见 AppPaths.ResolveRootDir），
# 所以默认直接从 bin 启动就行：不复制、不碰 %LOCALAPPDATA%、也不影响正式版。
#
# 若从工作区启动仍被环境限制（日志不写 / 保存失败 / 无托盘图标 / 贴片跑到屏幕原点），
# 加 -Copy：脚本会把整个输出目录复制到工作区之外再启动，并用 VIGILWATT_DATA_DIR
# 指向复制后的 data\。原因见 docs/dev/runtime-pitfalls.md §20。
#
# 用法：powershell -File scripts\run-app.ps1 [-Copy] [-Destination C:\VigilWattRun]

param(
    [switch]$Copy,
    [string]$Destination = "C:\VigilWattRun"
)

$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "..\src\VigilWatt.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64"
if (-not (Test-Path (Join-Path $source "VigilWatt.App.exe"))) {
    throw "找不到 Debug 产物：$source。先跑 dotnet build VigilWatt.sln -c Debug。"
}
$source = (Resolve-Path $source).Path

# 运行中的实例会占住 DLL，复制或启动前先结束
Get-Process -Name "VigilWatt.App" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "结束运行中的实例 pid=$($_.Id)"
    $_ | Stop-Process -Force
}
Start-Sleep -Milliseconds 500

if ($Copy) {
    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $Destination -Recurse -Force

    $dataDir = Join-Path $Destination "data"
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    $env:VIGILWATT_DATA_DIR = $dataDir
    $exe = Join-Path $Destination "VigilWatt.App.exe"
    Write-Host "已复制到 $Destination（$((Get-ChildItem $Destination).Count) 项）"
}
else {
    $exe = Join-Path $source "VigilWatt.App.exe"
    $dataDir = Join-Path $source "data"
}

Write-Host "数据目录：$dataDir"

Start-Process -FilePath $exe
Write-Host "已启动：$exe"
