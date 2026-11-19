# 下载 WebView2 SDK（仅构建需要，运行不需要）
$ErrorActionPreference = 'Stop'
$ver = '1.0.4314.35'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $here 'webview2'
$tmp = Join-Path $env:TEMP ("wv2-" + $ver)
$pkg = Join-Path $env:TEMP ("wv2-" + $ver + ".nupkg")

New-Item -ItemType Directory -Force -Path $dest | Out-Null
if (Test-Path -LiteralPath $pkg) { Remove-Item -LiteralPath $pkg -Force }
if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }

Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$ver/microsoft.web.webview2.$ver.nupkg" -OutFile $pkg -TimeoutSec 120
Expand-Archive -LiteralPath $pkg -DestinationPath $tmp -Force

Copy-Item -LiteralPath (Join-Path $tmp 'lib\net462\Microsoft.Web.WebView2.Core.dll') -Destination $dest -Force
Copy-Item -LiteralPath (Join-Path $tmp 'lib\net462\Microsoft.Web.WebView2.WinForms.dll') -Destination $dest -Force
Copy-Item -LiteralPath (Join-Path $tmp 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $dest -Force

Remove-Item -LiteralPath $pkg -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Output ("webview2 SDK ready: " + $dest)
