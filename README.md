# DshFloat · dsh 对话浮窗

给 Windows 版 **DeepSeek Harness (dsh)** 用的独立置顶对话浮窗，以及一个在会话标题栏启动/唤起浮窗的 dsh 插件。

目标很简单：在别的软件里干活时，不切窗口就能继续用 dsh 追问、看结果。

> 非官方项目，不改 dsh 的 `app.asar`，也不包含 dsh 本体。

## 功能

- 独立无边框置顶窗，不占任务栏；支持自由缩放、拖动、置顶开关
- `Ctrl+Alt+F` 唤起 / 收起；托盘常驻；可收起到悬浮球
- 会话列表、切换、新建；按工作区分组，支持全局置顶
- 文件拖拽、选择、粘贴上传（发送时作为 dsh 附件）
- Markdown 代码块语言标签与一键复制
- dsh 会话标题栏「浮窗」入口：
  - 浮窗未运行 → 启动 `DshFloat.exe`
  - 浮窗已运行 → 把现有窗口唤到前台，不重复启动
- 不改 dsh 安装目录；通过本机 `127.0.0.1` 桥接复用 dsh 会话

## 组成

| 目录 | 内容 |
|---|---|
| `tools/dsh-float/` | `DshFloat.exe`：WinForms + WebView2 浮窗、本地桥接、UI |
| `dsh-float-launcher/` | dsh 插件：会话标题栏入口 + `/dsh-float/api/launch` 宿主路由 |
| `tools/release/` | 一键安装打包、安装/卸载脚本与发行回归 |
| `docs/product-design.md` | MVP 产品设计与边界 |

## 环境

- Windows 10/11
- 已安装并运行的 dsh 桌面版（当前验证版本：`0.2.2-rc.3`）
- WebView2 Runtime（Windows 11 通常自带）
- 构建需要 PowerShell 7 和 .NET Framework 的 C# 编译器

## 快速开始

### 一键安装版（推荐）

在仓库根目录生成发行包：

```powershell
pwsh -File tools/release/package.ps1 -Version 0.5
```

产物为 `release/DshFloat-0.5-installer-win64.zip`。用户解压后双击 `install.cmd` 即可：

- 当前用户免管理员安装，不写系统级注册表
- 默认装到 `D:\DshFloat`；没有 D 盘则装到 `%LOCALAPPDATA%\Programs\DshFloat`
- 自动安装 `DshFloat.exe`、dsh 插件、桌面与开始菜单快捷方式
- 开始菜单「DshFloat」内提供「卸载 DshFloat」

安装包目前是 zip + `install.cmd`，不是单文件 `Setup.exe`；需要单文件安装器时可再包 Inno Setup。

### 手动构建浮窗

```powershell
pwsh -File tools/dsh-float/fetch-webview2.ps1   # 首次，下载 WebView2 SDK
pwsh -File tools/dsh-float/build-webview.ps1
```

产物在 `tools/dsh-float/dist/`：`DshFloat.exe`、`DshFloatUI.html` 和 WebView2 DLL。

### 2. 运行

```powershell
& ".\tools\dsh-float\dist\DshFloat.exe"
```

要求 dsh 正在运行，默认连接 `127.0.0.1:3082`。端口可在 `dist/config.ini` 的 `dsh_port` 修改。

### 3. 安装 dsh 插件

把 `dsh-float-launcher` 放到 dsh 插件目录，并在 desktop profile 里登记：

在仓库根目录执行：

```powershell
cmd /c mklink /J "%USERPROFILE%\.dsh\plugins\dsh-float-launcher" "$PWD\dsh-float-launcher"
```

然后在 `%USERPROFILE%\.dsh\profiles\desktop\package.json` 的 `dependencies` 里加入：

```json
"dsh-float-launcher": "file:../../plugins/dsh-float-launcher"
```

并在 `dsh.profile.bundles` 里加入 `"dsh-float-launcher"`。完整步骤、配置和卸载方式见 [`dsh-float-launcher/README.md`](dsh-float-launcher/README.md)。

重启 dsh 后，打开任意会话，标题栏会出现「浮窗」按钮。它不依赖「梁神模式」——后者是你自行安装的第三方插件，不是 dsh 原生功能；如果同时启用了它，浮窗入口通常会排在它右侧。

## 配置

- 浮窗：`tools/dsh-float/dist/config.ini`（尺寸、位置、置顶、悬浮球、`dsh_port`）
- 插件：`dsh-float-launcher/config.json`（可选）
  - `exePath`：自定义 `DshFloat.exe` 路径
  - `ports`：桥接端口探测列表
- 环境变量 `DSH_FLOAT_EXE` 可覆盖 exe 路径

## 测试

```powershell
# 插件回归
node dsh-float-launcher/test/run-all.mjs

# 浮窗自检（先生成 dist）
& ".\tools\dsh-float\dist\DshFloat.exe" --test

# 发行安装/卸载回归（临时 profile，不碰真实 dsh）
pwsh -File tools/release/test.ps1
```

## 已知限制

- 仅支持 Windows；全屏独占游戏可能盖住置顶窗口
- 浮窗回复目前是完成后整条出现，没有逐字流式
- 历史消息只取最近 24 条，更早分页尚未实现
- 插件入口只在会话页出现；
- 这是自用验证版，不保证适配所有 dsh 版本

## 更多文档

- [`tools/dsh-float/README.md`](tools/dsh-float/README.md)：浮窗架构、构建、RPC 约定、限制
- [`dsh-float-launcher/README.md`](dsh-float-launcher/README.md)：槽位、路由、安装、测试
- [`tools/release/README.md`](tools/release/README.md)：发行包结构与安装器行为
- [`docs/product-design.md`](docs/product-design.md)：MVP 设计稿
