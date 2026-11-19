# dsh-float-launcher

在 dsh 会话标题旁放一个紧凑的「浮窗」入口（不依赖「梁神模式」这类第三方 header 插件）：

- DshFloat 没运行 → `spawn` 独立的 `DshFloat.exe`，置顶窗启动；
- DshFloat 已在运行 → `POST /show` 把现有窗口唤到前台，不重复启动。

## 槽位

| 项 | 值 |
|---|---|
| 槽位 | `conversation.session.header.actions` |
| id | `dsh-float.launcher` |
| order | `-9`（若同时启用了 order 更小的 header 插件，例如用户自行安装的「梁神模式」，会排在它右侧；未安装时不影响） |
| 范围 | 仅会话页；首页没有会话 header，因此首页仍用 `Ctrl+Alt+F` |

## 桌面端

网页版来源是 `http://127.0.0.1:3082`，桌面版 dsh 的来源是 `dsh-app://app`。
两者都是本机页面，客户端都显示入口；桌面端的相对 `fetch` 由 Electron
自定义协议转发给本机 Host。

## 宿主路由

`POST /dsh-float/api/launch`

必须带 `x-dsh-float-launch: 1`，只接受本机回环，且经过 dsh 的
`connection.requestRejection` 信任栅栏。

## 配置

插件目录下的 `config.json`（可选）：

```json
{
  "exePath": "D:\\tools\\DshFloat\\DshFloat.exe",
  "ports": [3083, 3084]
}
```

查找顺序：`DSH_FLOAT_EXE` → `config.exePath` → 插件 `bin/DshFloat.exe` →
仓库相对路径 `../tools/dsh-float/dist/DshFloat.exe` → 常见安装目录。

## 安装

1. 把本目录放到 `%USERPROFILE%\.dsh\plugins\dsh-float-launcher`（也可用目录 junction）；
2. profile 的 `package.json` 加 `"dsh-float-launcher": "file:../../plugins/dsh-float-launcher"`；
3. 同文件的 `dsh.profile.bundles` 加 `dsh-float-launcher`；
4. 在 profile 的 `node_modules` 下建立同名 junction，指向插件目录；
5. 完全退出并重启 dsh 后，在会话标题旁出现「浮窗」。

`engines.dsh` 用 `>=0.2.1`：dsh `0.2.2-rc.3` 在 semver 里小于 `0.2.2`，
写成 `>=0.2.2` 会被插件页判为不兼容并拒绝加载。

## 测试

```powershell
node test/run-all.mjs
```

另外 DshFloat 侧需保证 `DshFloat.exe --test` 覆盖了 `/show`。
