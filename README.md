# DawnCapture

轻量、简洁的 Windows 屏幕录制工具。基于 WinUI 3 与 Windows Graphics Capture，支持窗口、全屏、区域与纯音频四种录制模式，录像与音频数据全程保存在本机。

## 功能特性

- **四种录制模式**：窗口 / 全屏（多显示器选择）/ 自选区域 / 纯音频（M4A）
- **双音源混音**：麦克风 + 系统声音（WASAPI loopback），可选关闭
- **视频编码**：H.264（AVC）默认，可选 HEVC，设备不支持时自动回退
- **悬浮控制条**：录制中置顶显示 REC 状态、暂停/恢复、停止、计时，可拖动
- **录制库**：SQLite 目录，缩略图、时长、大小、媒体信息探测，支持重命名 / 删除（回收站）/ 在文件夹中显示
- **全局热键**：可自定义开始/停止与暂停/恢复热键，录制时自动注销避免冲突
- **倒计时开录**：可配置 3 秒倒计时（Esc 取消），可选光标捕获
- **双语界面**：简体中文 / English，跟随系统或手动切换
- **隐私友好**：无遥测、无网络请求，录像与设置仅存本机

## 系统要求

- Windows 10 2004（10.0.19041）或更高版本
- x64 / ARM64（x86 可用但性能受限）

## 构建

前置：.NET 10 SDK。

```powershell
# 开发调试（clean + build + 启动）
./run.ps1

# 手动构建
dotnet build DawnCapture.csproj -c Debug -p:Platform=x64

# 代码格式校验
dotnet format whitespace DawnCapture.csproj --verify-no-changes --no-restore
```

## 分发

```powershell
# 三种渠道（无参数显示帮助）
./distribute.ps1 zip        # bin\DawnCapture-<ver>-<arch>-portable.zip   便携包，解压即用
./distribute.ps1 installer  # bin\DawnCapture-<ver>-<arch>-setup.exe     安装包
./distribute.ps1 msix       # bin\DawnCapture-<ver>-<arch>-sideload.zip  侧载包（含安装脚本）
./distribute.ps1 all
```

| 渠道 | 体积 | 需要签名 | 目标机前置条件 |
|---|---|---|---|
| `zip` | 64.5 MB | 不需要 | 解压即用 |
| `installer` | **44.1 MB** | **不需要** | 双击安装；per-user 安装，无 UAC，无前置运行时 |
| `msix` | 91.3 MB | **必须**（自签名） | 证书信任到 `LocalMachine\TrustedPeople` 后运行 `Add-AppDevPackage.ps1` |

> `installer` 渠道依赖 [Inno Setup 6](https://jrsoftware.org/isdl.php)（`ISCC.exe`）；未安装时脚本会提示安装命令：
> `winget install --id JRSoftware.InnoSetup -e`
>
> 免签名原理、关键决策、实测数据与已知边界记录在安装脚本 `installer\inno\DawnCapture.iss` 的注释中。
> 未签名的安装包从网络下载后会触发 SmartScreen 提示（可点「更多信息 → 仍要运行」）；Windows 11 若开启 Smart App Control 会拦截且用户无法绕过。

> 计划通过 Microsoft Store 分发：商店提交由微软签名。MSIX 自签名侧载包仅供本地测试。

## 数据存储

| 数据 | 位置 |
|---|---|
| 录像文件 | `%USERPROFILE%\Videos\DawnCapture`（可在设置中修改） |
| 设置 | `%LocalAppData%\DawnCapture\settings.json` |
| 录制目录 | `%LocalAppData%\DawnCapture\library.db` |
| 日志 | `%LocalAppData%\DawnCapture\logs\`（保留 14 天） |

## 项目结构

```
DawnCapture/
├── Helpers/      # 单一职责工具类（音频设备、缩略图、区域边界、电源管理等）
├── Models/       # 设置、目录条目、录制模式等数据模型
├── Services/     # 录制管线、音频采集、SQLite 目录、热键、设置、日志（DI 单例）
│   └── Audio/    # WASAPI 采集与混音
├── Strings/      # zh-CN / en-US 本地化资源
├── ViewModels/   # MVVM（CommunityToolkit.Mvvm）
├── Views/        # 页面、悬浮控制条、区域选择器、对话框
├── run.ps1       # 开发调试脚本
└── distribute.ps1 # 打包分发脚本
```

## 技术栈

WinUI 3（Windows App SDK 2.4）· Windows.Graphics.Capture · MediaTranscoder（硬件加速）· NAudio（WASAPI）· Vortice.Direct3D11（GPU 帧处理）· Microsoft.Data.Sqlite · CommunityToolkit.Mvvm

## 许可与声明

- 本软件：MIT 许可（Copyright © 2026 SHAO Liming），详见 `LICENSE`
- 第三方组件：见 `THIRD-PARTY-NOTICES.md`
- 隐私政策：见 `PRIVACY-POLICY.md` 与商店列表页
