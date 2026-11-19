[CmdletBinding()]
param(
  [string]$Version = '0.5'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$dist = Join-Path $repo 'tools\dsh-float\dist'
$plugin = Join-Path $repo 'dsh-float-launcher'
$release = Join-Path $repo 'release'
$stage = Join-Path $release "stage\DshFloat-$Version-installer-win64"
$zip = Join-Path $release "DshFloat-$Version-installer-win64.zip"

function Assert-File {
  param([string]$Path, [string]$Hint)
  if (-not (Test-Path -LiteralPath $Path)) {
    throw "缺少 $Path。$Hint"
  }
}

Assert-File (Join-Path $dist 'DshFloat.exe') '先运行 pwsh -File tools/dsh-float/build-webview.ps1 构建浮窗。'
Assert-File (Join-Path $plugin 'package.json') '缺少 dsh-float-launcher 插件源码。'
foreach ($name in @('install.ps1', 'install.cmd', 'uninstall.ps1', 'uninstall.cmd')) {
  Assert-File (Join-Path $PSScriptRoot $name) '发行工具不完整。'
}

New-Item -ItemType Directory -Path $release -Force | Out-Null
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $stage 'app') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'plugin') -Force | Out-Null

$appFiles = @(Get-ChildItem -LiteralPath $dist -File | Where-Object {
  $_.Name -notin @('config.ini', 'test-result.txt') -and $_.Extension -ne '.png'
})
foreach ($file in $appFiles) {
  Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $stage 'app') -Force
}

$pluginFiles = @(Get-ChildItem -LiteralPath $plugin -File)
foreach ($file in $pluginFiles) {
  Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $stage 'plugin') -Force
}

foreach ($name in @('install.ps1', 'install.cmd', 'uninstall.ps1', 'uninstall.cmd')) {
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage -Force
}

$usage = @'
DshFloat 一键安装说明

1. 解压整个压缩包，不要只拖出 install.cmd。
2. 双击 install.cmd。
3. 安装完成后，完全退出并重启 dsh，会话标题栏会出现「浮窗」入口。
4. 如果没有 D 盘，默认装到当前用户的 LocalAppData；有 D 盘时默认装到 D:\DshFloat。
5. 卸载：开始菜单「DshFloat」里的「卸载 DshFloat」，或运行安装目录的 uninstall.cmd。

安装包含：DshFloat.exe、dsh-float-launcher 插件、安装/卸载脚本。
'@
[IO.File]::WriteAllText((Join-Path $stage '安装说明.txt'), $usage, (New-Object System.Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $stage 'version.txt'), $Version, (New-Object System.Text.UTF8Encoding($false)))

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Write-Output "ZIP=$zip"
Write-Output "SHA256=$hash"
