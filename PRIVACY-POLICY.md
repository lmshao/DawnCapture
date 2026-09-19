Last updated: 2026-09-20

DawnCapture is a screen recording app. This policy explains what data it
collects and how that data is handled.

— What DawnCapture does not do

• DawnCapture does not collect, upload, or transmit any of your data over the network.
• There is no telemetry, no analytics, no advertising SDK, and no account system.
• Your screen recordings, system audio, microphone audio, and settings never leave your device.

— Data stored on your device

• Recordings (video/audio) — saved in your chosen output folder (default: Videos\DawnCapture). These are the files you create; you control and delete them.
• Settings — %LocalAppData%\DawnCapture\settings.json — remembers your preferences, including the output folder you picked.
• Recording library index — %LocalAppData%\DawnCapture\library.db — lists your recordings with thumbnails and metadata.
• Technical logs — %LocalAppData%\DawnCapture\logs\ — kept for 14 days and used only for diagnosing crashes and errors. A log can name your Windows user account (it appears inside file paths), your displays and their resolutions, your audio devices, and where recordings were written; it never contains the picture or sound of a recording.
• Notification registration — two keys under HKCU\Software\Classes: an AppUserModelId entry for DawnCapture, and the CLSID entry that activates it. Windows needs these to show the "recording finished" notification and to reopen the app when the notification is clicked. They hold the app's name, its icon and the path to its executable — nothing about you or your recordings.
• Temporary files — a hidden .dawncapture_*.tmp inside your output folder while the app checks that it can write there, and a settings.json.tmp next to the settings file while settings are saved. Both are removed as soon as the operation finishes.

Where the settings, the library index and the logs live depends on how you installed the
app, because Windows redirects writes inside %LocalAppData% for apps it installed as a
package:

• Installer and portable ZIP — %LocalAppData%\DawnCapture
• Microsoft Store — the package's own private store, which is
  %LocalAppData%\Packages\SHAOLiming.DawnCapture_c731qgphmzkst\LocalCache\Local\DawnCapture

Recordings are not affected either way: they go to the output folder you chose (by default
Videos\DawnCapture), which is an ordinary folder in your profile.

— Permissions

• Screen capture (Windows Graphics Capture) — used only while recording, and only for the window, region, or display you select.
• Microphone — used only when you enable microphone recording; it is mixed into your recording and never stored separately or transmitted.
• System audio — used only when you enable system audio recording.

— Deleting your data

• Delete recordings in the app's library (they go to the Recycle Bin), or delete the output folder manually.
• Uninstalling the installer version asks whether to delete the settings, the library index and the logs. Say yes and they are removed; say no and they stay, ready for a later reinstall. A silent uninstall never deletes them.
• Uninstalling the Microsoft Store version removes them without asking, because they sit inside the package's own private store and Windows deletes that folder together with the app.
• The notification registration is always removed by the uninstaller, so no entry is left pointing at a folder that no longer exists.
• Your recordings are never deleted by the uninstaller: they are your files. DawnCapture never keeps a remote copy.

— Policy changes

If this policy changes, the updated version ships with the app.

— Contact

Questions about this policy: lmshao@163.com

──────── 简体中文 ────────

最近更新：2026-09-20

DawnCapture 是一款屏幕录制应用。本政策说明它会收集哪些数据以及如何处理这些数据。

— DawnCapture 不会做的事

• DawnCapture 不会收集、上传或通过网络传输你的任何数据。
• 没有遥测、没有统计分析、没有广告 SDK、没有账号系统。
• 你的屏幕录像、系统声音、麦克风声音和设置永远不会离开你的设备。

— 存储在你设备上的数据

• 录像文件（视频/音频）— 保存在你选择的输出文件夹中（默认：视频\DawnCapture）。这些是你创建的文件，由你控制并删除。
• 设置 — %LocalAppData%\DawnCapture\settings.json — 记住你的偏好设置，包括你选择的输出文件夹。
• 录像库索引 — %LocalAppData%\DawnCapture\library.db — 存放录像列表、缩略图与元数据。
• 技术日志 — %LocalAppData%\DawnCapture\logs\ — 保留 14 天，仅用于诊断崩溃和错误。日志中可能出现你的 Windows 用户名（出现在文件路径中）、显示器名称与分辨率、音频设备名称以及录像文件路径；日志不包含录像的画面或声音。
• 通知注册项 — HKCU\Software\Classes 下的两个键：DawnCapture 的 AppUserModelId 项，以及用于激活它的 CLSID 项。Windows 依靠它们显示「录制完成」通知，并在你点击通知时唤回应用。其中只保存应用名称、图标与可执行文件路径，不含任何与你或你的录像有关的信息。
• 临时文件 — 应用检查输出文件夹是否可写时，会在其中短暂生成一个隐藏的 .dawncapture_*.tmp；保存设置时会生成 settings.json.tmp。两者在操作结束后立即删除。

设置、录像库索引与日志的具体位置取决于安装方式 —— 对"以包形式安装"的应用，Windows 会把 %LocalAppData% 下的写入重定向到该包自己的私有存储：

• 安装包与便携版 — %LocalAppData%\DawnCapture
• Microsoft Store 版 — 包私有存储，即
  %LocalAppData%\Packages\SHAOLiming.DawnCapture_c731qgphmzkst\LocalCache\Local\DawnCapture

录像文件不受影响：它们始终写入你选择的输出文件夹（默认「视频\DawnCapture」），那是你用户目录下的普通文件夹。

— 权限

• 屏幕捕获（Windows Graphics Capture）— 仅在你录制时使用，且仅捕获你选择的窗口、区域或显示器。
• 麦克风 — 仅在你开启麦克风录制时使用；混入你的录像中，不会单独存储或传输。
• 系统声音 — 仅在你开启系统声音录制时使用。

— 删除你的数据

• 在应用的录像库中删除录像（会移入回收站），或手动删除输出文件夹。
• 卸载安装包版时会询问是否删除设置、录像库索引与日志。选择删除则一并清除；选择保留则留待日后重装使用。静默卸载从不删除这些内容。
• 卸载 Microsoft Store 版时不会询问，而是随应用一并清除 —— 因为它们在包自己的私有存储里，Windows 会连同那个文件夹一起删除。
• 通知注册项由卸载程序始终清除，不会留下指向已删除文件夹的无效项。
• 卸载程序不会删除你的录像：它们是你自己的文件。DawnCapture 不会在远端保留任何副本。

— 政策变更

如果本政策有更新，新版本将随应用一起发布。

— 联系方式

如对本政策有疑问，请联系：lmshao@163.com


