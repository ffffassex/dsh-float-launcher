# WebView2 SDK 说明

`DshFloat.cs` 编译需要本目录下的三个文件：

- `Microsoft.Web.WebView2.Core.dll`
- `Microsoft.Web.WebView2.WinForms.dll`
- `WebView2Loader.dll`

它们是 NuGet 包 `Microsoft.Web.WebView2` 里的二进制，未纳入 git。运行一次：

```powershell
pwsh -File tools/dsh-float/fetch-webview2.ps1
```

即可重新下载（版本写在脚本里）。

运行时要求系统已安装 **WebView2 Runtime**（Win11 自带，Win10 一般随 Edge 安装）。
