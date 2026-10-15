$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $compiler '/nologo' '/target:winexe' '/optimize+' '/out:萌栖安装版.exe' '/win32icon:src\app.ico' '/resource:萌栖.exe,mengqi.payload' '/reference:System.Windows.Forms.dll' '/reference:System.Drawing.dll' 'installer\Setup.cs'
if ($LASTEXITCODE -ne 0) { throw '安装版编译失败' }
Write-Host '已生成 萌栖安装版.exe'
