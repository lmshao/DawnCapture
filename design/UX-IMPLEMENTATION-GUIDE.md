# UX 实现指引 — DawnCapture

> 本文档是**开发的单一入口**：把两份原型（整体 UX + 手机录制细节）转译成可直接开工的规格与映射。
> 更新：2026-10-07。
> 配套：`DESIGN.md`（设计令牌 / 录制浮窗规范）、`ICON_ASSETS.md`（图标对照）。

---

## 0. 文档地图（谁是权威来源）

| 文件 | 角色 | 权威范围 |
|---|---|---|
| `dawncapture-ui-prototype.html` | **整体 UX 原型** | 应用整体：5 页 + 侧栏 + 浮窗 + 明暗双主题 |
| `dawncapture-phone-recording-prototype.html` | **手机录制原型细节** | 手机录制功能的完整 UX 规格（**唯一权威**） |
| `dawncapture-installer-prototype.html` | 安装器原型 | 安装体验（独立于主应用） |
| `DESIGN.md` | 设计系统 + 浮窗规范 | 令牌色值、字体、圆角、投影、浮窗状态矩阵 |
| `ICON_ASSETS.md` | 图标对照 | Segoe Fluent 图标 ↔ 语义映射 |

**判断规则**：UI 长什么样 → 看两个 HTML 原型；色值 / 令牌 → 看 `DESIGN.md`；图标 → 看 `ICON_ASSETS.md`。

---

## 1. 应用结构（原型 → 实现映射）

### 1.1 侧栏导航

| 原型页面 | 实现 | 状态 |
|---|---|---|
| Capture（录制） | `MainWindow.xaml` `Tag="Capture"` | ✅ 已实现 |
| **Phone（手机录制）** | — | ❌ **待新增**（`Tag="Phone"`，插在 Capture 与 Recordings 之间） |
| Recordings（录像库） | `Tag="Recordings"` | ✅ 已实现 |
| Settings（设置） | `Tag="Settings"` | ✅ 已实现 |
| About（关于） | `Tag="About"` | ✅ 已实现 |

- 主窗口是**根目录的 `MainWindow.xaml`**（不在 `Views/` 下）。导航由 `NavButton_Click` → `MainViewModel.RequestNavigation(pageTag)` 以字符串 Tag 驱动。
- 新增手机录制页时：加一个 `Tag="Phone"` 的 NavButton + 对应页注册，`Nav_Workspace` 分组下按原型顺序排列。

### 1.2 页面 → 代码

| 原型页 | Views | ViewModels | Services / 关键对话框 |
|---|---|---|---|
| Capture | `CapturePage.xaml`(+`.cs`) | `CaptureViewModel.cs` · `CaptureViewModel.Source.cs` · `CaptureViewModel.Output.cs` | `DisplayPickerDialog`（选显示器）、`RegionPickerWindow`（区域）、`CountdownOverlayNative`（倒计时） |
| **Phone** | **`PhonePage.xaml`（新建）** | **`PhoneViewModel.cs`（新建）** | **手机采集服务 + `LiveMonitorWindow`（新建）** |
| Recordings | `RecordingsPage.xaml`(+`.cs`) | `RecordingsViewModel.cs` | `RecordingCatalogService.cs` · `RecordingLibraryService.cs` · `RenameRecordingDialog` · `DeleteRecordingDialog` |
| Settings | `SettingsPage.xaml`(+`.cs`) | `SettingsViewModel.cs` | `SettingsService.cs` |
| About | `AboutPage.xaml`(+`.cs`) | `AboutViewModel.cs` | `LegalDocumentDialog`（第三方声明 / 隐私政策） |

### 1.3 独立窗口 / 覆盖层

| 原型元素 | 实现 | 关键点 |
|---|---|---|
| 录制浮窗（REC 控制条） | `RecordingControlWindow.xaml`(+`.cs`) | **`WDA_EXCLUDEFROMCAPTURE`**（不被任何捕获抓取，见 §5） |
| 倒计时覆盖层 | `CountdownOverlayNative.cs` + `CountdownOverlayHelper.cs` | 原生分层窗口，Narrator 不可达 |
| 区域选择器 | `RegionPickerWindow.xaml`(+`.cs`) · `RegionMarkerWindow.cs` · `RegionPickerHelper.cs` | 已支持 Esc/Enter |
| 系统托盘 | `TrayIconService.cs` | 最小化到托盘 |
| 全局热键 | `GlobalHotkeyService.cs` + `HotkeyHelper.cs` | 运行时注册（**无字面量默认值**，原型里的键位为占位） |
| **实时监视窗口（手机）** | — | ❌ **待新建**，规格见 §3.6 |

---

## 2. 设计令牌（速查）

> 完整色值、字体、圆角、投影、浮窗专用 `Dock*` 令牌见 `DESIGN.md`。此处只放开发时最常用的一组（浅色 / 深色）：

| 令牌 | Light | Dark | 用途 |
|---|---|---|---|
| Accent | `#D85B42` | `#F07A61` | **只表示「正在录制 / 停止」** |
| Accent soft | `#F7E4DF` | `#4A2C27` | REC chip 底 / 选中态 |
| Danger | `#C42B1C` | `#FF7669` | 删除等强危险语义 |
| Surface | `#F8F9FB` | `#1E2125` | 主表面 |
| Stroke | `#D6D9DD` | `#41464C` | 1px 描边 |
| Text | `#1C2024` | `#F3F4F5` | 正文 |
| Text muted | `#626971` | `#B5BAC0` | 次要文字 |

- 字体：显示/正文 `Segoe UI Variable`；计时/数据 `Consolas` + `font-variant-numeric: tabular-nums`（秒跳不抖）。
- 图标：**一律 `SymbolIcon` / `FontIcon`（Segoe Fluent Icons）**，禁止 Unicode 字形文本（如 `\uE768` 会显示方框）。
- 圆角：窗口/卡片 8、chip 5、按钮 6。

---

## 3. 手机录制功能规格（核心待开发项）

> 唯一权威来源：`dawncapture-phone-recording-prototype.html`。当前**全库零实现**（无 `PhonePage` / 无采集服务）。
> 记忆点：**「不装 App，插上就能录。」**

### 3.1 范围边界

- 入口 = **侧栏独立项**，与 3 种录制模式**不并列**（它是「来源」不是「模式」）。
- 传输 = **USB 有线**；手机端**无需安装 App、无账号、无无线配对**，首次只需在手机上点一次「允许 USB 调试」。
- 明确**不做**：手机端控制（点击 / 滑动 / 键盘注入一律不做）。
- 平台底线：**Android 8.0+**；参考实现路线为 scrcpy 式 `adb` 屏幕流（具体技术栈为待定决策，见 §3.3）。

### 3.2 已定决策

| # | 决策 |
|---|---|
| 1 | 默认画质 **1080p · 30 fps**、声音 **手机系统声音**、保存沿用输出文件夹 |
| 2 | **不新增任何颜色**：连接状态只用中性「点」（空心=待操作 / 实心=就绪 / 红=出错 / 呼吸=在录）；**不用绿色**（色板里没有） |
| 3 | 录制中手机旋转 = **锁定方向（方案 B2）**：单文件 + GPU 实时转正 + 不足补黑边 |
| 4 | 弹出窗口命名为「**实时监视窗口**」（录制优先，不是投屏控制器） |
| 5 | 监视窗口**锁定手机原始比例 + 拖拽缩放**；底栏如实显示 `显示 % · 原始像素` |
| 6 | 监视窗口自带**常驻「录制」按钮**（只录手机流、原生分辨率）；**未录制时界面上没有浮窗** |

### 3.3 待定问题（实现前需拍板）

| # | 问题 | 建议 |
|---|---|---|
| 1 | 录制中是否保留预览 | 手机原型倾向「录制中关预览省解码开销」，见 §3.6 |
| 2 | 「录制时自动隐藏监视窗口」默认值 | **建议默认关**——要合成时它必须可见 |
| 3 | 与桌面录制的合成方式 | 依赖窗口摆放（已可用）；「固定内嵌」属未来进阶 |
| 4 | 技术栈（adb / scrcpy 库 / 自研协议） | 待选型，需真机验证旋转/黑边行为 |
| 5 | 录制中退出 / 休眠 / 断电 | **建议复用桌面录制的退出保护 + 防休眠**（`PowerStateHelper` + 关闭确认） |
| 6 | 录制中参数（画质 / 帧率 / 声音）是否锁定 | **建议锁定**，停止后才可改 |
| 7 | 录像库中手机录像的呈现 | 竖屏缩略图；文件名是否带设备名；「无音轨」标记 |
| 8 | 磁盘不足 / 保存路径不可写 | **建议同桌面录制**的失败提示与收尾 |
| 9 | 连接态（未录制）手机息屏 | **建议预览暂停、连接保留** |

### 3.4 状态机（14 状态）

| 状态 | 状态带 | 主按钮 | 说明 |
|---|---|---|---|
| 未插线 | `未检测到设备`（空心点） | 禁用 + 重新检测 | 展示三步引导 + 手机上会出现的授权框 |
| 已插线未授权 | `等待手机授权`（空心点） | 禁用 | 高亮第 2 步 |
| 建立画面中 | `正在建立画面…`（空心点） | 禁用 | 检测 → 授权 → 视频流，三阶段 |
| 已连接 | `设备名 · 分辨率 · 帧率`（实心点） | **开始录制** | 预览确认后再开录 |
| 录制中 / 已暂停 | 浮窗接管 | — | 主窗口显示「后台录制中」 |
| 手机旋转（未录制） | 预览容器跟随变宽/高 | — | 应用界面与侧栏**不**旋转 |
| 手机旋转（录制中） | `方向已锁定`（呼吸点） | — | 单文件；GPU 转正 + 黑边（设置可选「旋转时自动分段」） |
| 监视窗口已弹出 | — | — | 可被桌面录制捕获（与浮窗相反） |
| 放大监视窗口 | `显示 142% · 原始 1080×2400` | — | 锁比例缩放；只录手机流不影响输出，录桌面则窗口大小即成品 |
| 已断开 | `连接已断开`（红点） | 重新连接 | 文件已自动保存 |
| 正常停止 | `设备名 · 已停止` | 开始录制 | 文件封口 + 完成通知；回到已连接预览 |
| 已暂停（主窗口） | 浮窗接管（▶ 继续） | — | 主窗口仍显示「后台录制中」，仅浮窗 ⏸→▶ |
| 降级 / 不稳定 | `设备名 · 良好 · 已降为 720p` | 开始录制 | 带宽不足时降画质，如实写明 |
| 多设备 | `选择设备`（空心点） | — | 先选设备再建立画面；换设备走同一选择器 |

### 3.5 异常处理（4 种，每种都要给「怎么办」）

| 异常 | 提示 | 恢复动作 |
|---|---|---|
| 授权未完成 | 手机还没点「允许」 | 保持手机解锁重新插线；查「开发者选项 → USB 调试」 |
| 连接断开 | 数据线拔出 | 已录部分**已自动保存**；重插继续录到新文件 |
| 设备不支持 | 系统版本过低 / 厂商关闭调试通道 | 需 Android 8.0+；部分定制系统需额外开关 |
| 被占用 | 手机正被 scrcpy 等占用 | 关掉对方后点「重新检测」 |

### 3.6 工程要点

- **比例锁定**：拦截 `WM_SIZING` 修正矩形——WinUI 3 的 `OverlappedPresenter` 本身不管理宽高比。
- **跨显示器 DPI**：内容 `Auto` 测量 + `RasterizationScale` 实时换算（沿用浮窗那次决定）。
- **⚠️ 必须不被排除捕获**：监视窗口**不能**设 `WDA_EXCLUDEFROMCAPTURE`（与录制浮窗**正好相反**）——设错，「自动合成」（录桌面时把监视窗口录进去）永远不成立。
- **单文件为什么成立**：锁定方向 = 采集面尺寸恒定 → 编码器不重启 → 分辨率不变 → MP4 单文件成立。旋转后是「转正」还是「加黑边」取决于 Android 版本与厂商实现，**必须真机验证**。
- **一条流两个消费者**：预览 60 fps / 录制 30 fps——预览顺滑，文件按输出帧率丢帧。
- **录制中不显示实时预览**（省解码开销）；**手机锁屏后录到的是锁屏画面**（非黑屏、非隐私问题，但要预先告知用户）。
- **只录手机流**：输出永远是原生分辨率，窗口缩放只影响显示。**录「桌面 + 监视窗口」**：窗口在屏幕上的大小就是成品里的大小。
- **一次只允许一路录制**：桌面在录时，监视窗口的「录制」按钮禁用并写明原因。

### 3.7 建议实现切分

| 阶段 | 交付物 | 依赖 |
|---|---|---|
| M1 | `PhonePage` + `PhoneViewModel` + 侧栏 `Tag="Phone"` | 空状态/已连接/异常 4 态 UI |
| M2 | 手机采集服务（连接 + 授权 + 视频流） | 技术栈选型（§3.3-4） |
| M3 | `LiveMonitorWindow`（锁比例 / 常驻录制按钮 / 状态机） | M2 + `WM_SIZING` 比例锁定 |
| M4 | 浮窗「来源标记」（手机图标）+ 旋转锁定 B2 | 浮窗扩展 |
| M5 | 真机验证：旋转 / 黑边 / 锁屏 / 断开恢复 | 全套 |

---

## 4. 实现缺口清单（原型 ↔ 现状差异）

| # | 缺口 | 位置 | 优先级 |
|---|---|---|---|
| 1 | **手机录制功能整套** | §3 | **最大项** |
| 2 | 侧栏第 5 项（Phone） | `MainWindow.xaml` | 随 #1 |
| 3 | 浮窗「来源标记」（手机图标，录制手机时显示在计时前） | `RecordingControlWindow.xaml` | 随 #1 |
| 4 | 无障碍 `AutomationProperties` + 键盘导航 | 全视图 | P1 |
| 5 | 倒计时覆盖层高对比度 / Narrator | `CountdownOverlayNative.cs` | P1 |
| 6 | 商店提交剩余项（隐私政策 URL、CI 等） | 上架流程 | P0/P1 |

> 除手机录制外，其余页面（Capture / Recordings / Settings / About）原型已与实现对齐（2026-10-07 核对）。

---

## 5. 开发硬性规范（不妥协项）

1. **图标用 `SymbolIcon` / `FontIcon`**，禁止 Unicode 字形文本。
2. **录制浮窗 = `WDA_EXCLUDEFROMCAPTURE`**（不能出现在用户录像里）；**监视窗口 = 不得排除**（要能被桌面录制捕获）。
3. **本地化走 `x:Uid` + `Strings/*/Resources.resw`**（zh-CN / en-US 双语）；`REC` 三字母为国际惯例，**不参与翻译**。
4. **计时一律 `tabular-nums`**，秒跳不抖动。
5. **零外部资源**：不加载 Web 字体 / 远程资源，离线可用。
6. **Accent 语义唯一**：只表示「录制 / 停止」；删除用 Danger；连接状态用中性色，**不引入绿色**。
7. **明暗双主题**：所有自定义画刷提供 ThemeDictionaries 双份，跟随系统主题。
