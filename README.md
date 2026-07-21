# Codex 皮肤主题管理器

面向 Windows x64 的 Codex 桌面端皮肤管理器源码。支持导入、预览、重命名、导出与切换皮肤，并兼容两种皮肤格式：

- Codex Dream Skin schema v1
- [awesome-codex-skins](https://github.com/Wangnov/awesome-codex-skins) schema v2

当前版本：**2.5.4**

## 功能

- 中文 WinForms 管理界面
- 自定义安装位置与桌面快捷方式
- 导入皮肤保存在安装目录的 `skin` 文件夹
- 离线内置 29 套皮肤：原有 3 套，加上 awesome-codex-skins `skins-v1.1.0` 的全部 26 套认证皮肤
- 检测运行中的 Codex、管理器和皮肤注入引擎
- 通过仅绑定回环地址的 CDP 注入皮肤，不修改 Codex 安装包
- 兼容 Codex 26.715+ Windows 顶部菜单与连续圆角外壳
- 严格 UTF-8 配置事务、备份和恢复
- 支持 WebP 皮肤预览
- 顶部动态特效总开关，覆盖 schema v1 / v2 星光与舞台动画

## 目录结构

```text
windows/
  manager/          管理器 WinForms 源码
  installer/        安装器和卸载器源码
  assets/           v1 渲染器注入运行时
  scripts/          安装、启动、恢复及双格式注入脚本
  scripts/theme-v2/ awesome schema v2 兼容运行时
  skins/            29 套离线内置皮肤与可审计目录清单
  third-party/      上游皮肤授权与免责声明
  runtime/webp/     官方 libwebp dwebp 预览工具
  tests/            Windows 回归测试
```

## 开发要求

- Windows 10/11 x64
- PowerShell 7（Windows PowerShell 5.1 也可运行大部分脚本）
- .NET Framework 4.x C# 编译器
- Node.js 20 或更高版本
- 网络连接（构建安装包时下载并校验官方 Node.js x64 运行时）

## 运行测试

```powershell
pwsh -NoProfile -File .\windows\tests\run-tests.ps1
```

## 构建管理器

```powershell
pwsh -NoProfile -File .\windows\manager\build-manager.ps1
```

默认输出：

```text
windows\manager\bin\Codex皮肤主题管理器.exe
```

## 同步 awesome-codex-skins

仓库已提交解包后的成品资源，普通构建不依赖网络。需要重新同步固定发布版时运行：

```powershell
pwsh -NoProfile -File .\windows\scripts\sync-awesome-skins.ps1 -ReleaseTag skins-v1.1.0
```

同步脚本会从 GitHub Release 下载全部 `.codexskin`，执行路径、格式、大小与清单校验，并刷新 `windows\skins\bundled-skins.json` 中的版本和 SHA-256。

## 构建完整安装包

```powershell
pwsh -NoProfile -File .\windows\scripts\build-installer.ps1
```

默认输出：

```text
release\Codex-Dream-Skin-Setup-Windows-x64-v2.5.4.exe
release\Codex-Dream-Skin-Setup-Windows-x64-v2.5.4.exe.sha256
```

构建流程会自动运行回归测试、管理器自检、首次安装、升级保留、运行占用、v1/v2 载荷和卸载保留测试。

## 安装注意事项

安装前请完全退出 Codex 和正在运行的皮肤管理器。选择安装位置时请选择父目录，例如目标为 `E:\codex-skin-manager` 时应选择 `E:\`，安装器会自动创建 `codex-skin-manager`。

## 安全边界

- CDP 仅监听 `127.0.0.1` 或 `::1`。
- 不修改 Codex 的 `app.asar`、签名或 WindowsApps 文件。
- 不在仓库中保存 API Key、代理地址、用户皮肤或本机配置。
- 安装器不会擅自结束 Codex 或皮肤注入进程。

## 第三方组件

第三方来源和许可证见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。引用现有 IP 的 awesome-codex-skins 同人皮肤仅供个人非商业使用，具体以各皮肤 `theme.json` 的 `license` 字段及上游免责声明为准。
