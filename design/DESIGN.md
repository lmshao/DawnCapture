# Design System — DawnCapture

> 本文档由设计工作流生成（2026-09-08）。
> 本次范围：**Recording Dock（录制浮窗）** 的设计规范与缩放正确性方案。
> 基准：`design/dawncapture-ui-prototype.html`（整体 UX 原型）+ `Styles.xaml`（实现令牌）。
>
> **入口导航**：从原型到代码的整体映射、手机录制功能规格、缺口清单见
> `design/UX-IMPLEMENTATION-GUIDE.md`（开发实现指引）。手机录制细节以
> `design/dawncapture-phone-recording-prototype.html` 为唯一权威来源。

---

## Product Context

- **What this is:** 一款 Windows 11 本地优先的屏幕/音频录制工具（WinUI 3 / C# / .NET 8）。
- **Who it's for:** 需要录制演示、教程、会议画面的 Windows 用户；重视隐私（录制内容不出设备）。
- **Space/industry:** 桌面录屏工具（同类：OBS Studio、Windows Game Bar、ShareX、Screen Studio）。
- **Project type:** 桌面应用（单窗口 + 独立浮窗/选择器窗口）。

---

## Aesthetic Direction

- **Direction:** Industrial-Utilitarian（轻量版）—— 系统级工具的真实感：精确、低调、可信。
- **Decoration level:** minimal —— 字体与几何承担一切，无纹理、无渐变装饰。
- **Mood:** 「这是认真做事的软件」。像 Windows 系统自带部件，不抢内容的风头。
- **Reference:** Windows 11 系统录制指示、Windows Game Bar 浮窗的克制感。

## Typography

- **Display/Hero:** Segoe UI Variable（系统字体）—— 与 Windows 11 原生一致，零加载成本。
- **Body/UI:** Segoe UI Variable Text / Segoe UI（主窗口现状，保持）。
- **Data/计时:** Consolas / Cascadia Mono，`font-variant-numeric: tabular-nums` —— 秒跳时数字不抖动。
- **图标:** Segoe Fluent Icons（WinUI `SymbolIcon` / `FontIcon`），不用自定义字形文本（如 "II"）。
- **Scale:** 页面标题 28 / 正文 14 / 辅助 11~12 / 浮窗 UI 11~12（与主窗口一致）。

## Color

- **Approach:** restrained —— 一个品牌强调色 + 中性色阶；红色只出现在「停止/危险」语义。
- **Primary (Accent):** `#D85B42`（浅）/ `#F07A61`（深）—— 与主窗口 `DawnAccent` 完全同值；品牌记忆点。
- **Danger:** `#C42B1C`（浅）/ `#FF7669`（深）—— 与主窗口 `DawnDanger` 同值；仅停止按钮。
- **Neutrals:** `#1C2024→#858C94`（浅）/ `#F3F4F5→#8F969E`（深），与主窗口一致。
- **Surfaces:** 浮窗 = 主窗口同款半透明 app 背景（浅 `rgba(248,249,251,.95)` / 深 `rgba(30,33,37,.96)`）+ 22px 模糊。
- **Dark mode:** 全套令牌双份（ThemeDictionaries），跟随系统主题，与主窗口自动同步。

### 浮窗专用令牌（RecordingDock*，内联 ThemeDictionaries）

| 令牌 | Light | Dark | 用途 |
|------|-------|------|------|
| DockAccent | `#D85B42` | `#F07A61` | 信号条 / 录制点 / REC chip / 停止按钮（统一品牌红） |
| DockDanger | `#C42B1C` | `#FF7669` | 预留：确认删除等强危险语义（当前浮窗未使用） |
| DockSurface | `rgba(248,249,251,.95)` | `rgba(30,33,37,.96)` | 浮窗背景 + blur |
| DockStroke | `#D6D9DD` | `#41464C` | 1px 边框 |
| DockText | `#1C2024` | `#F3F4F5` | 计时 |
| DockTextMuted | `#626971` | `#B5BAC0` | 图标按钮默认色 |
| DockHover | `#EBEEF2` | `#33383E` | 按钮悬停 |

## Spacing

- **Base unit:** 8px；浮窗内部间距 6px（紧凑胶囊）。
- **Density:** compact（浮窗）/ comfortable（页面）。
- **Scale:** xs(4) sm(8) md(16) lg(24) xl(32)。

## Layout

- **Approach:** grid-disciplined —— 单行水平胶囊，无网格打破。
- **浮窗结构（左→右）:** 信号条(3×24) → 录制点(9 圆) → 分隔线(1×18) → 状态区（REC chip 或 ⏸/⏹ 图标按钮）→ 计时(MinWidth 44) → ✕（仅等待态）。
- **Border radius:** 浮窗 8（DWM 圆角同值）；chip 5；按钮 6。
- **投影:** `0 8 28 rgba(0,0,0,.18)`（与原型 controller 一致）。

## Motion

- **Approach:** minimal-functional —— 只为传达状态。
- **呼吸动画:** 录制点 opacity 1→0.35→1，1.6s ease-in-out（暂停时停止）。
- **状态切换:** 120ms ease-out 淡入淡出（元素显隐）。
- **Easing:** enter ease-out · exit ease-in · move ease-in-out。

---

# Recording Dock 设计规范（本次交付）

## 目标

1. 浮窗视觉与主窗口完全一致（同一套令牌色值、字体、圆角、投影语言）。
2. **系统缩放正确性**：100% / 150% / 200%（及任意 DPI）下图标与文本完整、无截断、无溢出。
3. 三种状态清晰可辨：等待录制 / 录制中 / 已暂停。

## 状态矩阵

| 状态 | 元素 | 行为 |
|------|------|------|
| 等待录制 | ● REC · 00:00 · ✕ | ✕ = 放弃录制；● 静止 |
| 录制中 | ● REC · ⏸ · ⏹ · 计时 | ● 呼吸；✕ 隐藏 |
| 已暂停 | ● REC · ▶ · ⏹ · 计时 | ● 静止；呼吸停止；⏸→▶ |

> **REC 文案**：固定三字母 `REC`，**不参与本地化翻译**（国际通用缩写，与 OBS / Windows Game Bar 惯例一致）。作为品牌化标识保留英文。

## 尺寸规格（DIP，内容 Auto 测量）

| 元素 | 值 |
|------|-----|
| 浮窗高度 | 40 |
| ● REC chip | 高 24 · padding 0×9 · 圆角 5 · 11px SemiBold · accent-soft 背景；圆点 7px 内嵌于 chip |
| 录制点 | 7 圆点，呼吸动画 1.6s ease-in-out（位于 chip 内） |
| 图标按钮 | 32×32 热区 · 图标 14 · 圆角 6 |
| 停止按钮 | 同图标按钮，Accent 色 + Accent-soft hover（与 REC 圆点同色） |
| 计时器 | 12px Consolas 600 · tabular-nums · MinWidth 44 |
| 窗口圆角 | 8（DWM `DWMWA_WINDOW_CORNER_PREFERENCE` + 内容一致） |
| 投影 | 0 8 28 rgba(0,0,0,.18) |

## 缩放正确性方案（关键工程决策）

### 现状问题

| # | 问题 | 现实现 |
|---|------|--------|
| 1 | 窗口宽度固定 `WindowWidthDip = 220`，但内容（REC 文本 / ⏹ Stop 本地化文本）宽度随状态与语言变化 → 可能截断 | `MoveAndResize(220×36 × dpiScale)` |
| 2 | `PauseButton.Content = "\uE768"` 未设 `FontFamily="Segoe Fluent Icons"` → 显示方框 | 文本字形 |
| 3 | `_dpiScale` 在录制开始时固定；跨缩放显示器拖动后不更新 → 尺寸错位 | 构造时传入一次 |
| 4 | 硬编码红色体系（`#EF4444` 等）与主窗口 accent 不一致 | XAML 内硬编码 |

### 目标方案

1. **内容自动测量**：浮窗内容统一 `Auto` 尺寸；状态切换/语言变化后调用 Measure，取 `DesiredSize`（DIP）。
2. **物理尺寸实时换算**：窗口物理像素 = 实测内容宽高 × `root.XamlRoot.RasterizationScale`；每次显隐状态变化后重算并 `MoveAndResize`（保持锚点位置）。
3. **缩放变化监听**：订阅 `XamlRoot.Changed`（RasterizationScale 变化，如跨显示器拖动）→ 重算窗口物理尺寸；移除静态 `_dpiScale` 参数依赖（可保留为初始定位提示）。
4. **图标用 SymbolIcon**：Play / Pause / Stop / Cancel 全部使用 `SymbolIcon`（系统自动处理 Segoe Fluent 字体），**禁止** Unicode 字形文本。
5. **令牌化**：颜色、圆角、字体全部走自定义 ThemeDictionaries（含明暗双份），与主窗口 `Dawn*` 令牌同值；删除 XAML 内硬编码色值。
6. **触控热区**：所有图标按钮 32×32（>27px 最小目标），任何 DPI 下保持物理可点。

## Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-09-08 | 记忆点 = 专业可靠的系统指示器 | 用户不可用，按推荐选项自主决策（AUTO_DECIDE） |
| 2026-09-08 | 浮窗跟随系统明暗主题 | 与用户「风格与主窗口一致」的要求一致；ThemeDictionaries 双份令牌 |
| 2026-09-08 | 品牌 accent 用 `#D85B42/#F07A61` 而非纯红 | 与主窗口 DawnAccent 同值；红色仅保留给停止/危险语义 |
| 2026-09-08 | 尺寸改为内容 Auto 测量 + RasterizationScale 实时换算 | 消除固定 220 DIP 宽度与本地化文本的截断风险 |
| 2026-09-08 | 图标全部用 SymbolIcon | 修复 \uE768 方框问题；系统字体自动适配 DPI |

---

## Deliverables

- **本规范**: `design/DESIGN.md`（浮窗设计规范）
- **整体 UX 原型**: `design/dawncapture-ui-prototype.html`（浮窗视觉已并入主原型）
- **参考**: `design/UX_IMPLEMENTATION_GUIDE.md`（开发实现指引）、`design/ICON_ASSETS.md`（图标对照）
