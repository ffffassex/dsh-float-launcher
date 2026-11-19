[CmdletBinding()]
param(
  [string]$InstallDir = '',
  [string]$DshHome = '',
  [string]$Profile = 'desktop',
  [switch]$NoShortcuts,
  [switch]$NoLaunch,
  [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$PackageRoot = $PSScriptRoot
$PluginPackageName = 'dsh-float-launcher'
$PluginRelative = 'plugins\dsh-float-launcher'

function Write-Utf8NoBom {
  param([string]$Path, [string]$Text)
  $parent = Split-Path -Parent $Path
  if ($parent -and -not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
  }
  [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Write-Step {
  param([string]$Text)
  Write-Host "[DshFloat] $Text"
}

function Test-ReparsePoint {
  param([string]$Path)
  try {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    return (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
  } catch {
    return $false
  }
}

function Get-DefaultInstallDir {
  if (Test-Path -LiteralPath 'D:\') {
    return 'D:\DshFloat'
  }
  return (Join-Path $env:LOCALAPPDATA 'Programs\DshFloat')
}

function Get-DshHomePath {
  param([string]$Value)
  if ($Value) { return [IO.Path]::GetFullPath($Value) }
  return (Join-Path $env:USERPROFILE '.dsh')
}

function Stop-DshFloatProcess {
  $procs = @(Get-Process -Name DshFloat -ErrorAction SilentlyContinue)
  if ($procs.Count -eq 0) { return }
  Write-Step '关闭正在运行的 DshFloat...'
  foreach ($p in $procs) {
    try { $p.CloseMainWindow() | Out-Null } catch { }
  }
  Start-Sleep -Milliseconds 500
  foreach ($p in $procs) {
    try { if (-not $p.HasExited) { $p.WaitForExit(3000) | Out-Null } } catch { }
  }
  $remaining = @(Get-Process -Name DshFloat -ErrorAction SilentlyContinue)
  foreach ($p in $remaining) {
    try { $p | Stop-Process -Force -ErrorAction Stop } catch { }
  }
}

function New-Shortcut {
  param(
    [string]$Path,
    [string]$TargetPath,
    [string]$WorkingDirectory,
    [string]$IconLocation = ''
  )
  $shell = New-Object -ComObject WScript.Shell
  $shortcut = $shell.CreateShortcut($Path)
  $shortcut.TargetPath = $TargetPath
  $shortcut.WorkingDirectory = $WorkingDirectory
  if ($IconLocation) { $shortcut.IconLocation = $IconLocation }
  $shortcut.Save()
}

function Get-PackageVersion {
  $file = Join-Path $PackageRoot 'version.txt'
  if (Test-Path -LiteralPath $file) {
    $value = ([IO.File]::ReadAllText($file)).Trim()
    if ($value) { return $value }
  }
  return '0.5'
}

function Install-App {
  param([string]$Destination)
  $appSource = Join-Path $PackageRoot 'app'
  $exeSource = Join-Path $appSource 'DshFloat.exe'
  if (-not (Test-Path -LiteralPath $exeSource)) {
    throw '安装包缺少 app\DshFloat.exe，请重新下载完整发行包。'
  }

  New-Item -ItemType Directory -Path $Destination -Force | Out-Null
  $files = @(Get-ChildItem -LiteralPath $appSource -File | Where-Object {
    $_.Name -notin @('config.ini', 'test-result.txt') -and $_.Extension -ne '.png'
  })
  foreach ($file in $files) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) -Force
  }

  foreach ($name in @('install.ps1', 'uninstall.ps1', 'uninstall.cmd')) {
    $source = Join-Path $PackageRoot $name
    if (Test-Path -LiteralPath $source) {
      Copy-Item -LiteralPath $source -Destination (Join-Path $Destination $name) -Force
    }
  }

  $versionFile = Join-Path $PackageRoot 'version.txt'
  if (Test-Path -LiteralPath $versionFile) {
    Copy-Item -LiteralPath $versionFile -Destination (Join-Path $Destination 'version.txt') -Force
  }

  $usage = Join-Path $PackageRoot '安装说明.txt'
  if (Test-Path -LiteralPath $usage) {
    Copy-Item -LiteralPath $usage -Destination (Join-Path $Destination '安装说明.txt') -Force
  }
}

function Install-Plugin {
  param([string]$DshHomePath, [string]$Destination, [string]$ExePath)
  $pluginSource = Join-Path $PackageRoot 'plugin'
  $required = Join-Path $pluginSource 'package.json'
  if (-not (Test-Path -LiteralPath $required)) {
    throw '安装包缺少 plugin\package.json，请重新下载完整发行包。'
  }

  New-Item -ItemType Directory -Path $Destination -Force | Out-Null
  $files = @(Get-ChildItem -LiteralPath $pluginSource -File)
  foreach ($file in $files) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) -Force
  }

  $cfgPath = Join-Path $Destination 'config.json'
  $cfg = @{}
  if (Test-Path -LiteralPath $cfgPath) {
    try {
      $old = [IO.File]::ReadAllText($cfgPath) | ConvertFrom-Json
      foreach ($prop in $old.PSObject.Properties) { $cfg[$prop.Name] = $prop.Value }
    } catch {
      Write-Step '现有 config.json 无法解析，将写入新的 exePath。'
    }
  }
  $cfg['exePath'] = $ExePath
  if (-not $cfg.ContainsKey('ports')) {
    $cfg['ports'] = @(3083, 3084)
  }
  Write-Utf8NoBom $cfgPath ($cfg | ConvertTo-Json -Depth 20)

  $profileDir = Join-Path $DshHomePath "profiles\$Profile"
  $pkgPath = Join-Path $profileDir 'package.json'
  if (-not (Test-Path -LiteralPath $pkgPath)) {
    throw "找不到 dsh profile: $pkgPath。请先安装并启动一次 dsh 桌面版，确认 profile 名为 $Profile。"
  }

  $pkg = [IO.File]::ReadAllText($pkgPath) | ConvertFrom-Json
  if ($null -eq $pkg.dependencies) {
    $pkg | Add-Member -NotePropertyName dependencies -NotePropertyValue ([pscustomobject]@{})
  }
  $pkg.dependencies | Add-Member -NotePropertyName $PluginPackageName -NotePropertyValue 'file:../../plugins/dsh-float-launcher' -Force

  if ($null -eq $pkg.dsh) {
    $pkg | Add-Member -NotePropertyName dsh -NotePropertyValue ([pscustomobject]@{})
  }
  if ($null -eq $pkg.dsh.profile) {
    $pkg.dsh | Add-Member -NotePropertyName profile -NotePropertyValue ([pscustomobject]@{})
  }
  $bundles = @($pkg.dsh.profile.bundles | Where-Object { $_ })
  if ($bundles -notcontains $PluginPackageName) {
    $bundles += $PluginPackageName
  }
  $pkg.dsh.profile.bundles = @($bundles)
  Write-Utf8NoBom $pkgPath ($pkg | ConvertTo-Json -Depth 100)

  $nodeModules = Join-Path $profileDir 'node_modules'
  New-Item -ItemType Directory -Path $nodeModules -Force | Out-Null
  $link = Join-Path $nodeModules $PluginPackageName
  if (Test-Path -LiteralPath $link) {
    if (Test-ReparsePoint $link) {
      [IO.Directory]::Delete($link, $false)
    } else {
      $backup = "$link.bak-$(Get-Date -Format 'yyyyMMddHHmmss')"
      Move-Item -LiteralPath $link -Destination $backup -Force
      Write-Step "检测到已有插件目录，已备份到 $backup"
    }
  }
  New-Item -ItemType Junction -Path $link -Target $Destination | Out-Null
}

function Install-Shortcuts {
  param([string]$InstallDirPath)
  $exe = Join-Path $InstallDirPath 'DshFloat.exe'
  $desktop = [Environment]::GetFolderPath('Desktop')
  $programs = [Environment]::GetFolderPath('Programs')

  if ($desktop) {
    try {
      New-Shortcut (Join-Path $desktop 'DshFloat.lnk') $exe $InstallDirPath $exe
    } catch { Write-Step "桌面快捷方式创建失败：$($_.Exception.Message)" }
  }

  if ($programs) {
    $menu = Join-Path $programs 'DshFloat'
    try {
      New-Item -ItemType Directory -Path $menu -Force | Out-Null
      New-Shortcut (Join-Path $menu 'DshFloat.lnk') $exe $InstallDirPath $exe
      New-Shortcut (Join-Path $menu '卸载 DshFloat.lnk') (Join-Path $InstallDirPath 'uninstall.cmd') $InstallDirPath $exe
    } catch { Write-Step "开始菜单快捷方式创建失败：$($_.Exception.Message)" }
  }
}

function Install-DshFloat {
  param([string]$Destination, [string]$DshHomePath)
  if (-not $Destination) { $Destination = Get-DefaultInstallDir }
  $Destination = [IO.Path]::GetFullPath($Destination)

  Stop-DshFloatProcess
  Write-Step "安装到 $Destination"
  Install-App $Destination

  $pluginDir = Join-Path $DshHomePath $PluginRelative
  Write-Step "安装 dsh 插件到 $pluginDir"
  Install-Plugin $DshHomePath $pluginDir (Join-Path $Destination 'DshFloat.exe')

  $meta = [ordered]@{
    version = (Get-PackageVersion)
    installDir = $Destination
    dshHome = $DshHomePath
    profile = $Profile
    pluginDir = $pluginDir
    installedAt = (Get-Date).ToString('o')
  }
  Write-Utf8NoBom (Join-Path $Destination 'install.json') ($meta | ConvertTo-Json -Depth 10)

  if (-not $NoShortcuts) { Install-Shortcuts $Destination }

  Write-Step '安装完成。'
  Write-Host '  DshFloat: ' $Destination
  Write-Host '  dsh 插件: ' $pluginDir
  Write-Host '  请完全退出并重启 dsh，插件入口才会出现。'

  if (-not $NoLaunch) {
    try {
      Start-Process -FilePath (Join-Path $Destination 'DshFloat.exe') -WorkingDirectory $Destination | Out-Null
      Write-Step '已启动 DshFloat。'
    } catch {
      Write-Step "DshFloat 启动失败：$($_.Exception.Message)"
    }
  }
}

function Remove-Link {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) { return }
  if (Test-ReparsePoint $Path) {
    [IO.Directory]::Delete($Path, $false)
  } else {
    Write-Step "保留非 junction 的目录：$Path"
  }
}

function Uninstall-DshFloat {
  param([string]$Destination, [string]$DshHomePath)
  if (-not $Destination) { $Destination = Get-DefaultInstallDir }
  $Destination = [IO.Path]::GetFullPath($Destination)
  $metaPath = Join-Path $Destination 'install.json'
  $meta = $null
  if (Test-Path -LiteralPath $metaPath) {
    try { $meta = [IO.File]::ReadAllText($metaPath) | ConvertFrom-Json } catch { }
  }

  if ($meta) {
    if (-not $DshHomePath -and $meta.dshHome) { $DshHomePath = $meta.dshHome }
    if ($meta.profile) { $Profile = $meta.profile }
  }
  if (-not $DshHomePath) { $DshHomePath = Get-DshHomePath '' }
  $pluginDir = Join-Path $DshHomePath $PluginRelative
  $profileDir = Join-Path $DshHomePath "profiles\$Profile"
  $pkgPath = Join-Path $profileDir 'package.json'

  Stop-DshFloatProcess
  Write-Step '移除 dsh 插件注册...'

  if (Test-Path -LiteralPath $pkgPath) {
    try {
      $pkg = [IO.File]::ReadAllText($pkgPath) | ConvertFrom-Json
      if ($pkg.dependencies) {
        $pkg.dependencies.PSObject.Properties.Remove($PluginPackageName)
      }
      if ($pkg.dsh -and $pkg.dsh.profile) {
        $pkg.dsh.profile.bundles = @($pkg.dsh.profile.bundles | Where-Object { $_ -ne $PluginPackageName })
      }
      Write-Utf8NoBom $pkgPath ($pkg | ConvertTo-Json -Depth 100)
    } catch {
      Write-Step "profile package.json 更新失败：$($_.Exception.Message)"
    }
  }

  Remove-Link (Join-Path $profileDir "node_modules\$PluginPackageName")
  if (Test-Path -LiteralPath $pluginDir) {
    Remove-Item -LiteralPath $pluginDir -Recurse -Force -ErrorAction SilentlyContinue
  }

  $desktop = [Environment]::GetFolderPath('Desktop')
  $programs = [Environment]::GetFolderPath('Programs')
  if ($desktop) { Remove-Item -LiteralPath (Join-Path $desktop 'DshFloat.lnk') -Force -ErrorAction SilentlyContinue }
  if ($programs) { Remove-Item -LiteralPath (Join-Path $programs 'DshFloat') -Recurse -Force -ErrorAction SilentlyContinue }

  Set-Location $env:TEMP
  if (Test-Path -LiteralPath $Destination) {
    Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction SilentlyContinue
  }
  Write-Step '卸载完成。'
}

try {
  if (-not $DshHome) { $DshHome = Get-DshHomePath '' }
  if ($Uninstall) {
    Uninstall-DshFloat $InstallDir $DshHome
    exit 0
  }
  Install-DshFloat $InstallDir $DshHome
  exit 0
} catch {
  Write-Host ''
  Write-Host "[DshFloat] 安装失败：$($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
