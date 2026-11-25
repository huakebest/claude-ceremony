# ClaudeBgm 卸载：停进程、删自启、删程序目录
# 用法：powershell -ExecutionPolicy Bypass -File uninstall.ps1 [-KeepData]
param([switch]$KeepData)
$ErrorActionPreference = 'SilentlyContinue'
$dir = $PSScriptRoot

Write-Host "停止 ClaudeBgm…"
Stop-Process -Name ClaudeBgm -Force
Start-Sleep -Milliseconds 800

Write-Host "删除自启项…"
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ClaudeBgm'

if ($KeepData) {
    Remove-Item (Join-Path $dir 'ClaudeBgm.exe') -Force
    Remove-Item (Join-Path $dir '_installer') -Recurse -Force
    Write-Host "已删除程序，$dir 里的 assets 与 _data 保留。"
} else {
    # 先删内容，脚本自身和空目录交给 cmd 延时清理
    Get-ChildItem -LiteralPath $dir -Exclude 'uninstall.ps1' | Remove-Item -Recurse -Force
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c timeout /t 2 >nul & rmdir /s /q "' + $dir + '"' -WindowStyle Hidden
    Write-Host "程序目录已清空，2 秒后删除目录本身。"
}
