[CmdletBinding()]
param(
  [string]$Version = 'test'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$release = Join-Path $repo 'release'
$zip = Join-Path $release "DshFloat-$Version-installer-win64.zip"
$stage = Join-Path $release "stage\DshFloat-$Version-installer-win64"
$tmp = Join-Path $env:TEMP ("DshFloat-release-test-" + [guid]::NewGuid().ToString('N'))

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

try {
  & (Join-Path $PSScriptRoot 'package.ps1') -Version $Version
  Assert-True (Test-Path -LiteralPath $zip) '发行 zip 未生成'

  Expand-Archive -LiteralPath $zip -DestinationPath $tmp -Force
  $pkg = Get-ChildItem -LiteralPath $tmp -Directory | Select-Object -First 1
  $required = @(
    'install.cmd', 'install.ps1', 'uninstall.cmd', 'uninstall.ps1',
    'app\DshFloat.exe', 'app\DshFloatUI.html',
    'plugin\package.json', 'plugin\index.js', 'plugin\client.js', 'plugin\cordis.patch.yml'
  )
  foreach ($rel in $required) {
    Assert-True (Test-Path -LiteralPath (Join-Path $pkg.FullName $rel)) "发行包缺少 $rel"
  }
  Assert-True (-not (Test-Path -LiteralPath (Join-Path $pkg.FullName 'app\config.ini'))) '发行包不应包含个人 config.ini'
  Assert-True (-not (Test-Path -LiteralPath (Join-Path $pkg.FullName 'app\test-result.txt'))) '发行包不应包含 test-result.txt'

  $fakeDsh = Join-Path $tmp 'fake-dsh'
  $profileDir = Join-Path $fakeDsh 'profiles\test'
  New-Item -ItemType Directory -Path $profileDir -Force | Out-Null
  $profileJson = @{ name = 'release-test'; dependencies = @{}; dsh = @{ profile = @{ bundles = @() } } } | ConvertTo-Json -Depth 20
  [IO.File]::WriteAllText((Join-Path $profileDir 'package.json'), $profileJson, (New-Object System.Text.UTF8Encoding($false)))

  $installDir = Join-Path $tmp 'installed'
  $installOut = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pkg.FullName 'install.ps1') -InstallDir $installDir -DshHome $fakeDsh -Profile test -NoShortcuts -NoLaunch
  if ($LASTEXITCODE -ne 0) { throw "install.ps1 退出码 $LASTEXITCODE" }
  Assert-True (Test-Path -LiteralPath (Join-Path $installDir 'DshFloat.exe')) '安装目录缺少 DshFloat.exe'
  Assert-True (Test-Path -LiteralPath (Join-Path $installDir 'install.json')) '安装目录缺少 install.json'

  $pluginDir = Join-Path $fakeDsh 'plugins\dsh-float-launcher'
  $cfg = Get-Content -LiteralPath (Join-Path $pluginDir 'config.json') -Raw | ConvertFrom-Json
  Assert-True ($cfg.exePath -eq (Join-Path $installDir 'DshFloat.exe')) '插件 config.json 的 exePath 不正确'

  $profile = Get-Content -LiteralPath (Join-Path $profileDir 'package.json') -Raw | ConvertFrom-Json
  Assert-True ($profile.dependencies.'dsh-float-launcher' -eq 'file:../../plugins/dsh-float-launcher') 'profile 依赖未写入'
  Assert-True (@($profile.dsh.profile.bundles) -contains 'dsh-float-launcher') 'profile bundles 未写入'

  $link = Get-Item -LiteralPath (Join-Path $profileDir 'node_modules\dsh-float-launcher') -Force
  Assert-True ($link.LinkType -eq 'Junction') 'node_modules 下未创建 junction'

  $uninstallOut = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $installDir 'uninstall.ps1') -InstallDir $installDir -DshHome $fakeDsh -Profile test
  if ($LASTEXITCODE -ne 0) { throw "uninstall.ps1 退出码 $LASTEXITCODE" }
  Assert-True (-not (Test-Path -LiteralPath $installDir)) '卸载后安装目录仍存在'
  Assert-True (-not (Test-Path -LiteralPath $pluginDir)) '卸载后插件目录仍存在'
  Assert-True (-not (Test-Path -LiteralPath (Join-Path $profileDir 'node_modules\dsh-float-launcher'))) '卸载后 junction 仍存在'
  $profile = Get-Content -LiteralPath (Join-Path $profileDir 'package.json') -Raw | ConvertFrom-Json
  Assert-True (-not ($profile.dependencies.PSObject.Properties.Name -contains 'dsh-float-launcher')) '卸载后依赖仍在'
  Assert-True (-not (@($profile.dsh.profile.bundles) -contains 'dsh-float-launcher')) '卸载后 bundle 仍在'

  Write-Output 'PASS release installer'
} finally {
  if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
  if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue }
  if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
