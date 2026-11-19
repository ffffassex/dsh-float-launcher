# dsh 浮窗（独立窗口版）

一个真正独立的小窗，压在别的软件之上，直接显示并操作 dsh 对话。**不受 dsh 主窗 520×600 最小尺寸限制**，默认 480×600（逻辑），可自由缩放。

界面是自绘的紧凑对话流，不走官方 Web 客户端的移动版布局。

## 为什么不是"缩小主窗口"

实测：从外部用 `SetWindowPos` 把 dsh 主窗改到 400×500，会被 Chromium 拉回 **534×607**（520×600 客户区 + 边框）。`minWidth:520 / minHeight:600` 是 Electron 硬性下限，外部改不动，所以小窗必须由自己的窗口承载。

## 架构

```
DshFloat.exe（WinForms + WebView2）
   ├─ 本地桥接 HttpListener（127.0.0.1:3083 起，自动找空闲端口）
   │     GET  /                 → DshFloatUI.html
   │     GET  /health           → dsh 是否可达
   │     GET  /workspaces       → 读取 dsh 的 workspace.json（工作区 / 置顶 / 归档）
   │     POST /rpc/<method>     → 包装 RPC 信封，转发 127.0.0.1:3082/api/<method>
   └─ WebView2 加载桥接地址（同源，绕开 3082 没有 CORS 头的问题）
   └─ Win32：置顶、无边框可缩放、托盘、全局热键、悬浮球
```

- **零补丁**：不碰 `app.asar`，dsh 升级不受影响。
- **零外网**：只连 `127.0.0.1`。
- **数据落在本地**：WebView2 数据目录在 exe 同级的 `data/`，卸载 = 删目录。

## RPC 约定（已实测）

信封：`{"type":"client-request","rpcId":"…","method":"<method>","payload":{"args":{…}}}`

| 方法 | args | 说明 |
|---|---|---|
| `session/list` | `{"_request":{}}` | 会话列表（标题、cwd、running） |
| `session/page` | `{"request":{"address":{"kind":"session","sessionId":"…"},"maxMessages":24}}` | 历史消息 |
| `session/prompt` | `{"request":{"requestId","sessionId","mode":"queue","content":[{"type":"text","text"},{"type":"file","receiptId"}],"clientTimeZone"}}` | 发送（可带文件附件） |
| `fileUploads/upload` | `{"agentId":"<sessionId>","request":{"data":"<base64>","name":"…"}}` | 上传文件，返回 `receiptId` |
| `session/state` | `{"request":{"sessionId"}}` | 是否在生成 |
| `session/cancel` | `{"request":{"sessionId"}}` | 停止 |
| `session/create` | `{"request":{"cwd":"…"}}` | 新会话 |
| `workspace/pinSession` | `{"request":{"sessionId":"…"}}` | 置顶会话，返回完整 `pinnedSessionIds` |
| `workspace/unpinSession` | `{"request":{"sessionId":"…"}}` | 取消置顶 |

`GET /workspaces` 读取 `%USERPROFILE%\.dsh\storages\workspace.json`，提供 `workspaceIds`、`workspaces[].sessionIds`、`archivedSessionIds`、`pinnedSessionIds`。dsh 装在别处时，用 `config.ini` 的 `dsh_home` 指到 `.dsh` 目录。

## 构建

```powershell
pwsh -File tools/dsh-float/fetch-webview2.ps1   # 一次性，下载 WebView2 SDK
pwsh -File tools/dsh-float/build-webview.ps1
```

产物在 `tools/dsh-float/dist/`：`DshFloat.exe` + `DshFloatUI.html` + 3 个 DLL。

`DshFloatUI.html` 是独立文件（桥接服务按修改时间热加载），改样式不用重编 exe，重启浮窗即可。

## 自检

```powershell
tools/dsh-float/dist/DshFloat.exe --test
```

20+ 项：WebView2 打开本地界面、标题、桥接健康、HTML 可访问、工作区数据接口、RPC 会话列表、未知方法兜底、发送参数 dry-run、界面输入区、顶栏长标题不换行、代码块渲染、拖拽附件入口、上传接口转发、附件条增删、真实会话标题、会话按工作区分组、窗口尺寸、配置读写、收起/恢复悬浮球。退出码 `0` 通过，结果写入 `dist/test-result.txt`。

诊断：

```powershell
tools/dsh-float/dist/DshFloat.exe --list
```

打印 dsh 端口是否可用、界面文件、配置路径、SDK 状态。

## 使用

双击 `dist/DshFloat.exe`：

- 标题栏拖动移动，四边/四角拖拽缩放（最小 340×470，最大 820×1000）
- `Ctrl+Alt+F` 显示 / 收起；托盘双击同理
- `◉` 置顶开关，`—` 收起到悬浮球，`✕` 同理
- 右上「会话」切换历史，「＋ 新会话」新建；上次打开的会话会记住
- 会话列表按工作区分组：顶部「置顶」，下面按 `workspaceIds` 排工作区，最后「其他目录」；组头可折叠，搜索框跨组过滤
- 会话行右侧图钉可直接置顶 / 取消置顶；置顶后从工作区移到顶部
- 未归入工作区的会话按 `cwd` 并入同名工作区，匹配不上的进「其他目录」
- 文件可直接拖进窗口、点「＋ 附件」选择，或粘贴；最多 6 个、单个 20 MB，发送时自动上传并作为 dsh 文件附件
- 回复里的 Markdown 代码块带语言标签与「复制」按钮，行内代码、加粗正常渲染
- 配置存 `dist/config.ini`（尺寸、位置、置顶、球位置、`dsh_port`），删掉即恢复默认

## 依赖与限制

- Windows 10/11 + WebView2 Runtime；dsh 正在运行（默认 `127.0.0.1:3082`，可用 `config.ini` 的 `dsh_port` 改）
- 生成中是整条出现，不是逐字流式（流式通道 `/api/remote.mux` 未接）
- 历史只取最近 24 条，更早的还没做分页
- 工作区 / 置顶来自 dsh 内部存储 `workspace.json`（unit version 2）；解析失败会自动退回按 `cwd` 分组并隐藏置顶，不影响聊天
- 置顶状态在打开列表时读取；官方 UI 同时改置顶时最多 stale 到下次打开
- 会话列表里归档会话不计入显示（当前 101 条里 70 条活跃）
- 附件走 base64 一次上传（单文件 ≤20 MB）；大文件上传期间只显示「上传 x/y」，没有百分比进度
- 图片附件按普通文件上传，浮窗里显示文件卡片，不做内联大图预览
- 全屏独占游戏可能盖住置顶窗口（Windows 限制）
