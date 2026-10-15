$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$speech=(Get-ChildItem -Path (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech') -Filter System.Speech.dll -Recurse | Select-Object -First 1).FullName
$arguments=@('/nologo','/target:winexe','/optimize+','/platform:anycpu','/out:萌栖.exe','/win32icon:src\app.ico','/resource:src\app.ico,mengqi.icon.ico','/resource:src\spritesheet.png,xiaohei.atlas.png','/resource:src\extra.png,mengqi.extra.png','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Net.Http.dll','/reference:System.Data.dll','/reference:System.Security.dll',('/reference:'+$speech),'/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
$arguments+=(Get-ChildItem -LiteralPath src -Filter *.cs).FullName
$arguments+=('/resource:src\chouchou.png,chouchou.atlas.png','/resource:src\chouchou-extra.png,chouchou.extra.png')
$arguments+='/resource:src\app-icon.png,mengqi.icon.png'
& $compiler $arguments 2>&1 | Tee-Object -FilePath build-log.txt
if ($LASTEXITCODE -ne 0) {throw '编译失败'}
Write-Host '已生成 萌栖.exe'
