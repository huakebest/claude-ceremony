# 用 Windows 自带的 .NET Framework 编译器编译工作音乐播放器，不需要装 SDK
# 用法：powershell -ExecutionPolicy Bypass -File build.ps1 [-Out 输出目录]
param([string]$Out = "$PSScriptRoot\out")
$ErrorActionPreference = 'Stop'
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path "$fw\csc.exe")) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }
$csc = "$fw\csc.exe"
if (-not (Test-Path $csc)) { throw "找不到系统自带的 C# 编译器（$csc）。需要 Windows 10/11。" }
$wpf = "$fw\WPF"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$refs = @("$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll",
          "$fw\System.Xaml.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll",
          "$fw\System.Web.Extensions.dll", "$fw\System.Core.dll") | ForEach-Object { "/r:$_" }
$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001') + $refs +
           @("/out:$Out\ClaudeBgm.exe", "$PSScriptRoot\src\ClaudeBgm.cs", "$PSScriptRoot\src\ClaudeBgm.App.cs")
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败" }
New-Item -ItemType Directory -Force -Path "$Out\assets" | Out-Null
Copy-Item "$PSScriptRoot\assets\loop.wav" "$Out\assets\" -Force
Copy-Item "$PSScriptRoot\install.ps1" "$Out\" -Force
Copy-Item "$PSScriptRoot\uninstall.ps1" "$Out\" -Force
Write-Host "编译完成：$Out\ClaudeBgm.exe"
Write-Host "自启：HKCU\Software\Microsoft\Windows\CurrentVersion\Run 新建字符串值 ClaudeBgm = `"$Out\ClaudeBgm.exe`" --startup"
