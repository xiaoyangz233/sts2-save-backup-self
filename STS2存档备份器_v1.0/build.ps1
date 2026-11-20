$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$src = Join-Path $root 'src\Sts2SaveBackup.cs'
$icon = Join-Path $root 'assets\app.ico'
$dist = Join-Path $root 'dist'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }
if (-not (Test-Path $src)) { throw "找不到源码：$src" }
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll'
)

$common = @('/nologo', '/platform:anycpu', '/optimize+', '/codepage:65001', '/warn:4')
if (Test-Path $icon) { $common += "/win32icon:$icon" }

$gui = Join-Path $dist 'STS2存档备份器.exe'
$cli = Join-Path $dist 'sts2-backup-cli.exe'

Write-Host '编译 GUI ...'
& $csc @common '/target:winexe' "/out:$gui" @refs $src
if ($LASTEXITCODE -ne 0) { throw "GUI 编译失败：$LASTEXITCODE" }

Write-Host '编译 CLI ...'
& $csc @common '/target:exe' "/out:$cli" @refs $src
if ($LASTEXITCODE -ne 0) { throw "CLI 编译失败：$LASTEXITCODE" }

Get-ChildItem $dist -File | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
Write-Host '编译完成。'
