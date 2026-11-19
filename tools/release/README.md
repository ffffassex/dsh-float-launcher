# DshFloat 发行包

把 `DshFloat.exe` 和 `dsh-float-launcher` 插件打成一个免管理员的 Windows 一键安装 zip。

## 生成

```powershell
pwsh -File tools/release/package.ps1 -Version 0.5
```

产物：`release/DshFloat-0.5-installer-win64.zip`

zip 内结构：

```text
DshFloat-0.5-installer-win64/
├─ install.cmd
├─ install.ps1
├─ uninstall.cmd
├─ uninstall.ps1
├─ version.txt
├─ 安装说明.txt
├─ app/
│  ├─ DshFloat.exe
│  ├─ DshFloatUI.html
│  └─ WebView2 DLL
└─ plugin/
   ├─ package.json
   ├─ index.js
   ├─ client.js
   └─ cordis.patch.yml
```

## 安装行为

- 当前用户安装，免管理员，不写系统级注册表
- 默认目录：`D:\DshFloat`；没有 D 盘时用 `%LOCALAPPDATA%\Programs\DshFloat`
- 插件装到 `%USERPROFILE%\.dsh\plugins\dsh-float-launcher`
- 修改 `%USERPROFILE%\.dsh\profiles\desktop\package.json`：
  - `dependencies` 加 `dsh-float-launcher`
  - `dsh.profile.bundles` 加 `dsh-float-launcher`
- 在 profile 的 `node_modules` 建 junction
- 创建桌面/开始菜单快捷方式；安装完成后启动浮窗
- 安装/卸载不会改 dsh 安装目录或 `app.asar`

可传参数：

```powershell
.\install.ps1 -InstallDir "D:\Tools\DshFloat" -DshHome "$env:USERPROFILE\.dsh" -Profile desktop
.\install.ps1 -NoShortcuts -NoLaunch
.\install.ps1 -Uninstall
```

## 卸载

- 开始菜单「DshFloat」→「卸载 DshFloat」
- 或安装目录的 `uninstall.cmd`
- 会移除浮窗、插件目录、profile 依赖/bundle、junction、快捷方式；保留用户自己的其他 dsh 插件

## 测试

```powershell
pwsh -File tools/release/test.ps1
```

测试会：

1. 打一个临时发行 zip
2. 解压并检查必需文件
3. 用临时 `DshHome` + 临时 `InstallDir` 跑安装
4. 验证插件注册、`exePath`、junction
5. 跑卸载并验证目录/注册全部清理

不会修改真实 `%USERPROFILE%\.dsh`。

## 限制

- 这是 zip + `install.cmd` 方案；双击 `install.cmd` 即一键安装，但不是单文件 `Setup.exe`
- 首次安装要求 dsh 已经启动过一次，存在 `profiles\desktop\package.json`
- 安装脚本用 Windows PowerShell 5.1 执行，`.ps1` 必须保留 UTF-8 BOM，否则中文会乱码
- 安装包不包含 `config.ini`、测试截图或个人路径
