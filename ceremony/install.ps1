# 安装开屏动画：复制到本用户目录、登记开机自启、立即启动
# 用法：powershell -ExecutionPolicy Bypass -File install.ps1 [-Dest 目录] [-NoStart] [-Force]
param(
    [string]$Dest = "$env:LOCALAPPDATA\Programs\ClaudeBootAnimation",
    [switch]$NoStart,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$name = 'ClaudeBootAnimation'
if (-not (Test-Path "$PSScriptRoot\$name.exe")) {
    throw "install.ps1 必须和 $name.exe 放在同一个文件夹里运行"
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$old = (Get-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue).$name
if ($old) {
    $oldDir = Split-Path ($old.Trim('"')) -Parent
    if ($oldDir -ne $Dest -and (Test-Path "$oldDir\$name.exe") -and -not $Force) {
        Write-Warning "已经装过一份：$oldDir"
        Write-Warning "要搬家请先运行那边的 uninstall.ps1，或者加 -Force 覆盖（旧目录会保留）。"
        exit 1
    }
}

Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force -Path $Dest, "$Dest\assets" | Out-Null
Copy-Item "$PSScriptRoot\$name.exe" $Dest -Force
Copy-Item "$PSScriptRoot\uninstall.ps1" $Dest -Force
Copy-Item "$PSScriptRoot\assets\*" "$Dest\assets\" -Force -Recurse

Set-ItemProperty -Path $runKey -Name $name -Value "`"$Dest\$name.exe`""

if (-not $NoStart) { Start-Process "$Dest\$name.exe" }

Write-Host ""
Write-Host "已安装到  $Dest"
Write-Host "开机自启  已登记（HKCU\...\Run\$name）"
Write-Host "窗口托盘  右下角图标，左键点开设置，可「播放一次」先看看效果"
Write-Host "卸载      运行 $Dest\uninstall.ps1"
