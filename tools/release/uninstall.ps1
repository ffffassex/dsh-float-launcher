[CmdletBinding()]
param(
  [string]$InstallDir = '',
  [string]$DshHome = '',
  [string]$Profile = 'desktop'
)

$installScript = Join-Path $PSScriptRoot 'install.ps1'
if (-not (Test-Path -LiteralPath $installScript)) {
  Write-Host '[DshFloat] 找不到 install.ps1，无法卸载。' -ForegroundColor Red
  exit 1
}

& $installScript -Uninstall -InstallDir $InstallDir -DshHome $DshHome -Profile $Profile
exit $LASTEXITCODE
