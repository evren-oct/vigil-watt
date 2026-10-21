<#
    功 能： VigilWatt 安装向导 —— 先让用户选安装目录，再用 Velopack 的 --installto 安装。
    2026/10/21

    为什么需要它：Velopack 的 Setup.exe 默认装到 %LocalAppData%\VigilWattApp（C 盘），
    官方没有目录选择界面，但支持 `Setup.exe --installto {dir}` 覆盖安装位置。
    本脚本只做两件事：弹一个目录选择框、把选中的路径交给 Setup.exe。
    真正干活的还是官方安装器，本脚本不碰应用自身的任何文件。

    用法：powershell -ExecutionPolicy Bypass -File choose-install-dir.ps1 -Setup "D:\...\VigilWattApp-win-Setup.exe"
    调试：加 -DryRun 只选目录不安装。
#>
param(
    [string]$Setup = "",
    [switch]$DryRun
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

if ([string]::IsNullOrWhiteSpace($Setup)) {
    $Setup = Join-Path $PSScriptRoot "VigilWattApp-win-Setup.exe"
}
if (-not (Test-Path -LiteralPath $Setup)) {
    [System.Windows.Forms.MessageBox]::Show("找不到安装器：$Setup", "VigilWatt 安装向导") | Out-Null
    exit 1
}

# 默认给个非 C 盘的位置；没有其它盘就退回用户目录下的同名文件夹
$default = if (Test-Path "D:\") { "D:\VigilWatt" }
           elseif (Test-Path "E:\") { "E:\VigilWatt" }
           else { Join-Path $env:LOCALAPPDATA "VigilWatt" }

$form = New-Object System.Windows.Forms.Form
$form.Text = "VigilWatt 安装向导"
$form.Size = New-Object System.Drawing.Size(560, 260)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

$lbl = New-Object System.Windows.Forms.Label
$lbl.Text = "选择安装位置（安装程序文件、快捷方式会放到这里）："
$lbl.AutoSize = $true
$lbl.Location = New-Object System.Drawing.Point(18, 18)
$form.Controls.Add($lbl)

$txt = New-Object System.Windows.Forms.TextBox
$txt.Text = $default
$txt.Location = New-Object System.Drawing.Point(18, 44)
$txt.Width = 430
$form.Controls.Add($txt)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Text = "浏览…"
$btnBrowse.Location = New-Object System.Drawing.Point(456, 42)
$btnBrowse.Size = New-Object System.Drawing.Size(80, 26)
$form.Controls.Add($btnBrowse)

$lblHint = New-Object System.Windows.Forms.Label
$lblHint.Text = "提示：装到 D 盘等非系统盘时，升级也会在该目录内进行。"
$lblHint.AutoSize = $true
$lblHint.ForeColor = [System.Drawing.Color]::DimGray
$lblHint.Location = New-Object System.Drawing.Point(18, 82)
$form.Controls.Add($lblHint)

$lblWarn = New-Object System.Windows.Forms.Label
$lblWarn.Text = "若电脑上已装过默认位置（%LocalAppData%）的版本，请先在「应用和功能」里卸载旧版，" + "`r`n" +
                 "否则会出现两份安装，升级和卸载都会指向不确定的那一份。"
$lblWarn.AutoSize = $true
$lblWarn.ForeColor = [System.Drawing.Color]::DarkOrange
$lblWarn.Location = New-Object System.Drawing.Point(18, 106)
$form.Controls.Add($lblWarn)

$btnOk = New-Object System.Windows.Forms.Button
$btnOk.Text = "开始安装"
$btnOk.Location = New-Object System.Drawing.Point(352, 200)
$btnOk.Size = New-Object System.Drawing.Size(92, 30)
$form.Controls.Add($btnOk)

$btnCancel = New-Object System.Windows.Forms.Button
$btnCancel.Text = "取消"
$btnCancel.Location = New-Object System.Drawing.Point(452, 200)
$btnCancel.Size = New-Object System.Drawing.Size(84, 30)
$form.Controls.Add($btnCancel)

$btnBrowse.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "选择安装位置"
    $dlg.ShowNewFolderButton = $true
    $cur = $txt.Text.Trim()
    if ($cur -and (Test-Path -LiteralPath $cur)) { $dlg.SelectedPath = $cur }
    elseif ($cur) { $dlg.SelectedPath = Split-Path -Parent $cur }
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $txt.Text = $dlg.SelectedPath
    }
    $dlg.Dispose()
})

$btnCancel.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })

$btnOk.Add_Click({
    $path = $txt.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($path)) {
        [System.Windows.Forms.MessageBox]::Show("请填写或选择安装目录。", "VigilWatt 安装向导") | Out-Null
        return
    }
    if (-not [System.IO.Path]::IsPathRooted($path)) {
        [System.Windows.Forms.MessageBox]::Show("请填写绝对路径，例如 D:\VigilWatt。", "VigilWatt 安装向导") | Out-Null
        return
    }
    $root = [System.IO.Path]::GetPathRoot($path).TrimEnd('\')
    if ($path.TrimEnd('\') -eq $root) {
        [System.Windows.Forms.MessageBox]::Show("不能直接装到盘符根目录，请指定一个子目录，例如 D:\VigilWatt。", "VigilWatt 安装向导") | Out-Null
        return
    }
    if ($path -match '^[A-Za-z]:\\Program Files' -or $path -match '^[A-Za-z]:\\Windows') {
        $r = [System.Windows.Forms.MessageBox]::Show(
            "该目录受系统保护，Velopack 的免管理员安装器可能无权写入。`r`n仍然继续吗？（建议改用 MSI 安装包）",
            "VigilWatt 安装向导", [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($r -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    }

    if ($DryRun) {
        Write-Host "DryRun: Setup.exe --installto `"$path`""
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
        return
    }

    try {
        $form.Enabled = $false
        $proc = Start-Process -FilePath $Setup -ArgumentList @("--installto", $path) -Wait -PassThru
        if ($proc.ExitCode -eq 0) {
            [System.Windows.Forms.MessageBox]::Show("安装完成。`r`n安装位置：$path", "VigilWatt 安装向导") | Out-Null
            $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        } else {
            [System.Windows.Forms.MessageBox]::Show("安装程序返回 $($proc.ExitCode)。`r`n若提示权限不足，请改用 MSI 安装包。", "VigilWatt 安装向导") | Out-Null
        }
    } catch {
        [System.Windows.Forms.MessageBox]::Show("安装失败：$($_.Exception.Message)", "VigilWatt 安装向导") | Out-Null
    } finally {
        $form.Enabled = $true
    }
})

$form.AcceptButton = $btnOk
$form.CancelButton = $btnCancel
[void]$form.ShowDialog()
$form.Dispose()
