# 编译 DshFloat（独立浮窗，WebView2 版）
# 用法: pwsh -File tools/dsh-float/build-webview.ps1
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dist = Join-Path $here 'dist'

if (-not (Test-Path -LiteralPath (Join-Path $here 'webview2\Microsoft.Web.WebView2.Core.dll'))) {
  throw '缺少 webview2 SDK dll，见 webview2\README.txt'
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$manifestArg = '/win32manifest:' + (Join-Path $here 'app.manifest')
$outArg = '/out:' + (Join-Path $dist 'DshFloat.exe')
$refCore = '/r:' + (Join-Path $here 'webview2\Microsoft.Web.WebView2.Core.dll')
$refForms = '/r:' + (Join-Path $here 'webview2\Microsoft.Web.WebView2.WinForms.dll')
$source = Join-Path $here 'DshFloat.cs'

$cscArgs = @(
  '/nologo', '/target:winexe', '/platform:x64', '/codepage:65001',
  $manifestArg, $outArg, $source,
  $refCore, $refForms,
  '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
  '/r:System.Windows.Forms.dll', '/r:System.Net.dll'
)
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc 失败，退出码 $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $here 'webview2\Microsoft.Web.WebView2.Core.dll') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $here 'webview2\Microsoft.Web.WebView2.WinForms.dll') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $here 'webview2\WebView2Loader.dll') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $here 'DshFloatUI.html') -Destination $dist -Force

Write-Output ("built -> {0}" -f (Join-Path $dist 'DshFloat.exe'))
