# AGENTS.md

## 提交要求

每次改动完成后，必须创建一个对应的 git commit。

## 测试要求

每次改动后，必须编写或更新相关测试，并在交付给用户前，确保所有测试和验证全部通过。

## 常用命令

- 插件回归：`node dsh-float-launcher/test/run-all.mjs`
- 首次下载 WebView2 SDK：`pwsh -File tools/dsh-float/fetch-webview2.ps1`
- 构建 DshFloat：`pwsh -File tools/dsh-float/build-webview.ps1`
- DshFloat 自检：`tools/dsh-float/dist/DshFloat.exe --test`（退出码 0 通过）
- 打包发行版：`pwsh -File tools/release/package.ps1 -Version 0.5`
- 发行安装/卸载回归：`pwsh -File tools/release/test.ps1`

## 架构约束

- 不修改 dsh 安装目录里的 `app.asar`；浮窗由独立 exe + 本机桥接实现。
- 插件只负责在 dsh 会话标题栏提供入口，启动或唤起 DshFloat。
