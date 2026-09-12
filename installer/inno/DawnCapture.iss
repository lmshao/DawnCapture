; ============================================================================
; DawnCapture - Inno Setup 6 installer script
; ============================================================================
;
; Builds a single self-contained Setup .exe from the unpackaged publish output
; that distribute.ps1 already produces for the portable ZIP channel.
;
; Compile:
;   ISCC.exe installer\inno\DawnCapture.iss
;   ISCC.exe /DPayloadDir=<abs publish dir> /DAppVersion=1.0.0.0 installer\inno\DawnCapture.iss
;
; Every path below that starts with ".." is relative to THIS script's own folder, not to
; the repository root - the script sits one level down in installer\inno\, so those paths
; climb two levels.
;
; ----------------------------------------------------------------------------
; Why this installer needs no code signing
; ----------------------------------------------------------------------------
; A Win32 .exe is not signature-gated by Windows. Unlike an MSIX package -
; where the OS refuses to activate the package unless the certificate chains
; to a trusted root, so sideloading drags in a self-signed cert plus a
; TrustedPeople import - the loader runs any PE image the user double-clicks.
; Authenticode buys identity and reputation, not the ability to load.
;
; ----------------------------------------------------------------------------
; Why there is no prerequisite detection anywhere in this script
; ----------------------------------------------------------------------------
; The payload is a self-contained publish: it carries .NET 10, the Windows App
; SDK, and links only against the OS-provided UCRT (verified: no
; VCRUNTIME140 / MSVCP140 import in any native binary). So there is nothing to
; check for and nothing to bootstrap. The [Files] section below is the whole
; installer.
; ============================================================================

; --- Payload: the unpackaged self-contained publish output -------------------
; Overridable so distribute.ps1 can point at a freshly published folder.
; SourcePath is the directory holding this .iss file.
#ifndef PayloadDir
  #define PayloadDir SourcePath + "..\..\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif

; --- Application identity ----------------------------------------------------
#define AppName "DawnCapture"
#define AppExeName "DawnCapture.exe"

; The AppId value as the registry sees it: one leading brace, because the doubled
; brace below is how it is written in a directive. [Code] needs this form twice
; (per-user and all-users uninstall keys), so it lives here once.
#define AppIdPlain "{5A9D9DBE-CDF2-45C6-8868-3EB111170803}"

; The app's own per-user folders, named the way the app names them - deliberately
; NOT derived from AppName. DawnCapture creates and uses these paths itself, and
; the uninstaller's checkboxes are about removing exactly those folders, so the
; two have to agree on the literal name. Deriving them from AppName would look
; tidy and silently stop matching the app's data the moment the display name
; changes.
#define AppDataFolderName "DawnCapture"

; --- Version -----------------------------------------------------------------
; Read from the published executable's version resource rather than hardcoded,
; so the installer version can never drift from the payload it wraps. The value
; originates in DawnCapture.csproj as <Version>.
#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(PayloadDir + "\" + AppExeName)
#endif

; --- Publisher ---------------------------------------------------------------
; Kept in step with Identity/@Publisher in Package.appxmanifest.
#ifndef AppPublisher
  #define AppPublisher "SHAO Liming"
#endif

; --- Target architecture -----------------------------------------------------
; The payload is published for exactly one architecture, so the installer has
; to match it. distribute.ps1 passes /DAppArch=<x64|arm64|x86>.
#ifndef AppArch
  #define AppArch "x64"
#endif

; --- Payload size quoted on page 1 -------------------------------------------
; The welcome page states the disk space requirement before the Ready page shows
; Inno's own computed figure. Keeping it here rather than as a literal means the
; build can pass /DAppSizeMB=<n> from the publish folder; the fallback only
; applies to a hand-run compile.
#ifndef AppSizeMB
  #define AppSizeMB "175"
#endif

; Fail early and legibly if the payload folder is missing or incomplete,
; instead of letting the [Files] section fail with a bare path error later.
#if !FileExists(PayloadDir + "\" + AppExeName)
  #error Payload not found. Publish first (dotnet publish, WindowsPackageType=None) or pass /DPayloadDir=<dir>.
#endif
#if !FileExists(PayloadDir + "\DawnCapture.pri")
  #error Payload is missing DawnCapture.pri - the app would crash at startup with 0xC000027B.
#endif


[Setup]
; Stable product identity. Deliberately reuses Identity/@Name from
; Package.appxmanifest so the product has one identity value across both
; distribution channels. NEVER change this: it is the key Inno Setup uses to
; match an upgrade to a previous install.
AppId={{5A9D9DBE-CDF2-45C6-8868-3EB111170803}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}

; Explorer shows these in the file properties of the Setup .exe. The Setup
; .exe itself is unsigned, so this metadata is what a user sees before the
; SmartScreen prompt.
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

; Per-user install by default - no UAC prompt and no admin rights.
; {autopf} resolves to %LocalAppData%\Programs under PrivilegesRequired=lowest.
; This matches where the app already keeps everything it owns (settings, logs,
; catalog and recordings all live under %LocalAppData% and the Videos folder),
; so a per-user install never creates a mixed-privilege layout.
DefaultDirName={autopf}\{#AppName}

; yes - the install location is asked by the custom page that [Code] adds
; (prototype page 3), which pushes its answer into DirEdit. Leaving Inno's own
; Select Destination Location page enabled would put the same setting in two
; places and let them disagree.
DisableDirPage=yes
; Page 4 of the design is ONE page - the tasks - drawn by [Code], so Inno's native
; tasks page and Ready page are both off: the tasks page would wrap the single
; checkbox in its group box, and the Ready page would be a second confirmation with
; an empty memo box. Nothing depends on the native TasksList any more: with its page
; skipped that list comes up empty, so [Icons] reads WantDesktopIcon instead (see the
; [Icons] note). AlwaysShowDirOnReadyPage would only matter on the Ready page, so it
; is gone with it. There is no DisableTasksPage directive, so the native tasks page is
; skipped from [Code] (see ShouldSkipPage).
DisableReadyPage=yes

; No - page 1 is part of the design and has to be shown. Inno's default for this
; directive is YES, so leaving it out hides the welcome page; that is what put
; page 3 first, because a custom page anchored to wpWelcome still lands directly
; after the (hidden) welcome page.
DisableWelcomePage=no
; commandline only, no dialog. The install is always per-user - the wizard has no
; scope to ask for any more - so the pre-wizard mode dialog would offer a choice
; the page no longer has. commandline stays enabled rather than being switched off
; entirely because an unattended install needs to pass it: the upgrade path runs this
; package with /VERYSILENT, and a scripted install passes /CURRENTUSER.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline

; no - always show the scope page. The default (yes) makes Setup look up the
; previous install's mode in the registry and reuse it silently, so on a machine
; that already has DawnCapture the page never appears and the scope cannot be
; changed without uninstalling first. One extra page per upgrade buys a scope
; that is always switchable.
UsePreviousPrivileges=no

; The payload is built for one architecture only, so the installer must match
; it. x64compatible also accepts ARM64, where the x64 binaries run under
; emulation; an arm64 payload must not be offered to x64 hosts.
#if AppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif AppArch == "x86"
ArchitecturesAllowed=x86compatible
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
; Mirrors TargetPlatformMinVersion in DawnCapture.csproj.
MinVersion=10.0.19041

DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; Never ask. Setup still resolves the language by itself: it matches the user's
; UI language against each entry's LanguageID ($0804 zh / $0409 en) and falls
; back to the FIRST [Languages] entry when nothing matches - which is why "en"
; is listed first. Chinese systems get Chinese, everything else gets English.
ShowLanguageDialog=no

OutputDir=..\..\bin
OutputBaseFilename={#AppName}-{#AppVersion}-{#AppArch}-setup

SetupIconFile=..\..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

; LZMA2 with a 64 MB dictionary over the whole payload as one solid block.
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; No `dynamic` modifier here, deliberately, and it is what keeps the uninstall screens
; consistent with each other. This version supports dark mode in Uninstall as well, and
; WizardSizePercent now applies to it too - so adding `dynamic` would turn Inno's confirm
; box, progress window and completion window dark on a dark system, while the wizard
; pages here are drawn by [Code] with explicit colours of their own (ColorText and the
; rest), which follow no style at all: the result would be black text on a dark surface
; on every custom page. Light-only is the setting under which Inno's windows and ours
; are the same colour. The same reasoning rules out WizardBackColor and
; WizardBackColorDynamicDark: they restyle Setup and Uninstall both, and activate the
; windows11 custom style when no custom style is named.
; The design's canvas is 700x580. Inno's own default is 120%, which at 100% DPI
; gives roughly 497x360 - far too small for the cover layout (a 132px hero, the
; rule, a lead line, the option row, the location row and a closing line need
; about 380px of page height, and the inner pages get only ~309pt of it).
; 150 is the top of the range the compiler accepts.
WizardSizePercent=150,150

; Blank: page 1 and page 6 no longer use the wizard's left image strip. That strip
; only stops being drawn - Inno's layout does not change, so the welcome/finish
; labels still start at the old image edge; [Code] moves them to the content
; margin (measured on 6.7.3: Left 427 = 213.5 unscaled at 200% DPI).
WizardImageFile=
; The corner mark Inno draws itself, on the inner pages - the ones the mark [Code] draws
; does not paint on at all. Exported 3x so high-DPI hosts stay crisp; its size and its
; position are both set to the design's in [Code], because Inno's own land it flush
; against the window's right edge and clip it. See CurPageChanged.
WizardSmallImageFile=brand\wizard-small.png

; CloseApplications asks the Restart Manager for anything holding the files Setup is
; about to write, and offers to close it. A backstop, not the mechanism: the app is
; force-closed earlier and elsewhere - page 2 does it with the user watching,
; PrepareToInstall does it again before the previous build is removed, and the uninstaller
; does the same before it deletes anything. Left to itself the Restart Manager would not
; be enough, and that is measured rather than assumed: it closes by posting a window
; close, and this app's close button minimises to the tray, so the process would stay up
; holding every file it had loaded.
;
; RestartApplications=no: relaunching someone's recorder unasked is rude, and on an
; upgrade the user is mid-flow.
CloseApplications=yes
RestartApplications=no

; No SignTool is configured - that is the point of this channel. To sign later,
; add a SignTool= directive and pass the resulting exe to signtool.exe.


[Languages]
; Two languages, selected silently by Setup from the user's UI language.
; en MUST stay first: it is the fallback for every system that matches neither
; Chinese nor English (see ShowLanguageDialog=no above).
Name: "en"; MessagesFile: "compiler:Default.isl"
; Inno Setup ships no Chinese translation, so this file is the user-contributed
; one from the official translation list (jrsoftware.org/files/istrans/).
; Chosen over an English-only wizard because the app itself ships zh + en.
Name: "zh"; MessagesFile: "ChineseSimplified.isl"


[Messages]
; No SelectDir* overrides any more: DisableDirPage=yes means Inno's Select
; Destination Location page is never shown, and page 3 in [Code] owns the folder.
; Leaving the overrides here would be dead configuration.

; Page 1: the product name is the heading (the two-line lockup in [Code] draws
; the version line under it). Three sentences here, no step list.
;
; The closing sentence is NOT part of this message: Inno draws its own
; ClickNext line on the welcome page ("点击“下一步”继续，或点击“取消”退出安装程序。"
; / "Click Next to continue, or Cancel to exit Setup."), so writing it here too
; printed it twice - the second copy directly under the first. The design asks
; for Inno's ClickNext verbatim, and that is exactly what the native label
; already is.
en.WelcomeLabel1=DawnCapture
zh.WelcomeLabel1=DawnCapture
en.WelcomeLabel2=Setup will now guide you through the installation of DawnCapture.%n%nIf DawnCapture is running, Setup closes it automatically before continuing.%n%nRequires Windows 10 2004 (10.0.19041) or later. At least {#AppSizeMB} MB of free disk space is required.
zh.WelcomeLabel2=安装程序现在将引导你完成 DawnCapture 的安装。%n%n如果 DawnCapture 正在运行，安装程序会在继续之前自动关闭它。%n%n需要 Windows 10 2004（10.0.19041）或更高版本；至少需要有 {#AppSizeMB} MB 的可用磁盘空间。

; (Page 4 is a custom page now - the native tasks/ready messages no longer show.)

; Page 5: the status line Inno shows while files are copied.
en.ExtractingLabel=Extracting files...
zh.ExtractingLabel=正在提取文件...
en.InstallingLabel=Please wait while Setup installs DawnCapture on your computer.
zh.InstallingLabel=安装程序正在安装 DawnCapture 到你的计算机，请稍候。

; Page 6: the outcome plus the one thing worth repeating - the hotkey.
en.FinishedHeadingLabel=DawnCapture
zh.FinishedHeadingLabel=DawnCapture
; No FinishedLabel override: that label is hidden, because Setup appends its own
; "click Finish to exit Setup" sentence to its text and the design's finish page
; has no such line. The sentence shown on the page lives in [CustomMessages]
; FinishLead, so there is only one copy of it to keep up to date.

; ---- Uninstall ----
; The uninstaller's own windows are Inno's and stay that way: it cannot host wizard
; pages, and its confirm box, progress window and completion window are scriptable only
; as far as their texts. Those three are already consistent with each other by
; construction. These overrides put them in the product's voice; keeping the one dialog
; we do draw - the data question in [Code] - in the same voice and the same visual
; language is the other half, and that lives in [CustomMessages].
;
; ConfirmUninstall is the box asked before anything is removed. The stock Chinese reads
; "您确认要完全移除 DawnCapture 及其所有组件吗？" - a literal translation, and in this
; flow actively wrong: the user has just been asked what to remove (or is about to be),
; so promising that all components go contradicts the answer they gave.
en.ConfirmUninstall=Uninstall DawnCapture?%n%nIts program files and shortcuts will be removed.
zh.ConfirmUninstall=要卸载 DawnCapture 吗？%n%nDawnCapture 的程序文件和快捷方式将被删除。

; The progress window's status line.
en.UninstallStatusLabel=Removing DawnCapture. Please wait.
zh.UninstallStatusLabel=正在卸载 DawnCapture，请稍候。

; The completion window. "已成功从您的计算机中移除" is machine translation, and this
; message cannot be conditional: it is one static string, and whether the settings folder
; is still there depends on the answer given to UninstallAskConfig. So it states only what
; is always true - the program files and shortcuts are gone - and says nothing about the
; settings either way.
en.UninstalledAll=DawnCapture has been uninstalled.%n%nIts program files and shortcuts have been removed.
zh.UninstalledAll=DawnCapture 已卸载。%n%n程序文件和快捷方式已删除。

en.UninstalledMost=DawnCapture was uninstalled, but some items could not be removed and have to be deleted by hand.
zh.UninstalledMost=DawnCapture 已卸载，但部分项目未能删除，需要手动清理。

en.UninstalledAndNeedsRestart=DawnCapture has been uninstalled. Windows must be restarted to finish removing it.%n%nRestart now?
zh.UninstalledAndNeedsRestart=DawnCapture 已卸载，需要重启 Windows 才能完成清理。%n%n现在重启吗？


[CustomMessages]
; Page 3b: shown inline under the folder box when the chosen folder cannot be
; written to. Setup has no elevation path any more, so the note says what it did
; about it - the location goes back to the per-user default - instead of telling
; the user to retry as administrator.
en.DirNotWritable=That folder cannot be written to. Setup has put the install location back on the default folder: %1
zh.DirNotWritable=无法写入该文件夹，安装程序已将安装位置重置为默认目录：%1

; Page 3 - install location, and nothing else. The scope is fixed - the install is
; always per-user (see [Setup]) - so the page carries no option row to pick from: it
; opens on the default folder and its only control is the folder box.
;
; The wording is Inno's own SelectDir* text with the parts that do not apply taken
; out. Deliberately absent is any sentence about install scope or administrator
; rights: the scope is not a choice on this page, the path itself shows the install
; is per-user, and the absence of a UAC prompt is what says no rights are needed.
; Three ways of saying the same thing is what this page used to carry.
en.ScopePageCaption=Install location
zh.ScopePageCaption=选择安装位置
; Inno's SelectDirLabel3: "Setup will install [name] into the following folder."
; There is no inline "Install location" label beside the folder box - Inno's own page
; has none, and the hero and this sentence have already said what the box is.
en.ScopeLead=Setup will install DawnCapture into the following folder.
zh.ScopeLead=安装程序将把 DawnCapture 安装到以下文件夹中。
; Inno's DiskSpaceMBLabel, under the folder box. The value is the same {#AppSizeMB}
; token page 1 quotes, so the two pages cannot disagree; that sentence is
; WelcomeLine3, and this one has to change with it.
en.ScopeDiskSpace=At least {#AppSizeMB} MB of free disk space is required.
zh.ScopeDiskSpace=至少需要有 {#AppSizeMB} MB 的可用磁盘空间。
; Page 4 - the tasks, on one page, carrying Inno's own pair of strings for it: the
; caption is WizardSelectTasks, and the description - which is also the lead sentence
; the page draws - is SelectTasksDesc. That is the arrangement page 3 uses too.
;
; What used to be the lead here ("Setup is now ready to begin installing ...") is
; Inno's ReadyLabel1. That sentence belongs on the page where Install is clicked, which
; on Inno's native flow comes AFTER the choices; opening this page with it announced
; "ready to install" before the user had answered the one question the page asks.
;
; One row, and it is the only one that can be turned off. The Start menu entry is not
; listed: Inno's own tasks page shows only what the user can uncheck, and the [Icons]
; entry for it is unconditional, so listing it advertised a choice that did not exist.
en.TasksPageCaption=Additional tasks
zh.TasksPageCaption=附加任务
en.TasksPageSub=Select additional tasks
zh.TasksPageSub=选择附加任务
; Inno's SelectTasksDesc. One line in both languages, which this page needs: the row
; below sits one BodyStep under a BodyLineH box, so a lead that wrapped to two lines
; would leave only BodyGap between its second line and the row.
en.TasksLead=Which additional tasks should be performed?
zh.TasksLead=您想要安装程序执行哪些附加任务？
en.TasksDesktop=Create a desktop shortcut
zh.TasksDesktop=创建桌面快捷方式

; The closing line every cover page ends with. Same words as Inno's own ClickNext
; in both languages, so the wizard never says two different things about the
; same button.
en.ClickNextLine=Click Next to continue, or Cancel to exit Setup.
zh.ClickNextLine=点击“下一步”继续，或点击“取消”退出安装程序。

; Page 1's body, one sentence per line because the design colours the first line
; normally and the other two muted (prototype: .wz-lead + .wz-note). Inno's own
; WelcomeLabel2 is hidden - it is a single label, so it cannot carry two colours,
; and Inno appends its ClickNext sentence to it.
en.WelcomeLine1=Setup will now guide you through the installation of DawnCapture.
zh.WelcomeLine1=安装程序现在将引导你完成 DawnCapture 的安装。
en.WelcomeLine2=If DawnCapture is running, Setup closes it automatically before continuing.
zh.WelcomeLine2=如果 DawnCapture 正在运行，安装程序会在继续之前自动关闭它。
en.WelcomeLine3=Requires Windows 10 2004 (10.0.19041) or later. At least {#AppSizeMB} MB of free disk space is required.
zh.WelcomeLine3=需要 Windows 10 2004（10.0.19041）或更高版本；至少需要有 {#AppSizeMB} MB 的可用磁盘空间。
en.ScopeBrowse=&Browse...
zh.ScopeBrowse=浏览(&B)...
en.ScopeBrowsePrompt=Select the folder DawnCapture should be installed into
zh.ScopeBrowsePrompt=选择 DawnCapture 的安装文件夹

; Page 2 - an existing installation (conditional). Wording taken from the
; approved mock: the lead names the version, the second sentence names what
; continuing does, the third what is kept.
;
; The lead used to end with the scope the old copy was installed with ("for you
; only"). That is gone. This Setup no longer offers all-users at all, so the
; parenthetical could only ever repeat one word - and where the old copy actually
; lives is already on the hero's third line, which is what the scope was standing in
; for. The registry root is still read (see ReadPrevInstall); it just is not reported.
en.PrevPageCaption=An existing installation was found
zh.PrevPageCaption=检测到已安装的版本
en.PrevPageDesc=Setup removes the old build first, then installs this one. Your settings, logs and recordings are kept.
zh.PrevPageDesc=安装程序会先移除旧版本，再安装这一版；设置、日志与录制文件会保留。
en.PrevVersionRow=%1 → %2
zh.PrevVersionRow=%1 → %2
en.PrevFoundLead=DawnCapture %1 is already installed.
zh.PrevFoundLead=检测到已安装的 DawnCapture %1。
en.PrevRemovesOld=Continuing removes the old build silently and then installs %1.
zh.PrevRemovesOld=继续将先静默卸载旧版本，再安装 %1。
en.PrevKeepsData=Settings, logs and recordings are kept; the old program files and shortcuts are removed.
zh.PrevKeepsData=设置、日志与录制文件会保留；旧版的程序文件与快捷方式会被删除。
; Shown while a copy of the app is running, by both halves of the product.

; Page 2 uses it as a status line, and this is where that belongs: the page already says
; the old build is about to be removed, so the running copy is part of the same answer -
; and a page is something the user can still act on. Back, Cancel and quitting the app by
; hand all stay open until Next is clicked, which is what the modal box this replaced,
; sitting over the blank install page, could not offer. See PrevAppRefresh.
;
; The uninstaller uses the same sentence in a confirmation, because it is the same
; sentence: continuing closes the app first, and that is true of an uninstall too. It has
; no page to put it on - heading or not, an uninstall cannot host wizard pages - so it
; has to be a dialog there. See InitializeUninstall.
en.AppRunningNote=DawnCapture is running right now. Continuing closes it first; if it is recording, that recording is lost.
zh.AppRunningNote=DawnCapture 当前正在运行。继续将先结束它；若它正在录制，该次录制将会丢失。
; Page 2's slot again, replaced when the close does not take. Says what to do and leaves
; the user on the page, rather than sending them back to the start of the wizard.
en.PrevAppCloseFailed=DawnCapture could not be closed. Quit it by hand (right-click its tray icon and choose Exit), then click Next again.
zh.PrevAppCloseFailed=未能结束 DawnCapture。请手动退出它（右键单击托盘图标并选择“退出”），然后再次点击“下一步”。
; Install time, and the install stops. Only reachable if the app is running again after
; page 2 - or if there was no page 2 to begin with, because there was no previous
; installation to report. Nothing is asked here: the page that could ask has been left
; behind, so this only states the outcome and names the manual step.
en.PrevAppStillRunning=DawnCapture could not be closed, so Setup stopped. Close it by hand (right-click its tray icon and choose Exit) and run Setup again.
zh.PrevAppStillRunning=未能结束 DawnCapture，安装程序已停止。请手动退出它（右键单击托盘图标并选择“退出”），然后重新运行安装程序。
; The uninstall's equivalent, and it stops the uninstall: reaching it means the close was
; attempted and did not take, so the files the process holds open would survive the
; removal. Stopping beats half-removing.
en.UninstallAppCloseFailed=DawnCapture could not be closed, so the uninstall stopped. Close it by hand (right-click its tray icon and choose Exit) and uninstall again.
zh.UninstallAppCloseFailed=未能结束 DawnCapture，卸载已中止。请手动退出它（右键单击托盘图标并选择“退出”），然后重新卸载。
; Shown when the recorded uninstaller is on disk but Windows refuses to start it. A record
; whose uninstaller is simply gone is stale and passed over in silence - see
; PrepareToInstall. It carries the Win32 error code and the file name because "could not
; be run" on its own said nothing about which failure it was: measured on 6.7.3, Exec
; returns False only when CreateProcess fails, and puts that error in its result code.
en.PrevUninstallFailed=The uninstaller of the version already installed could not be started.%n%nWindows error: %1%nUninstaller: %2
zh.PrevUninstallFailed=无法启动已安装版本的卸载程序。%n%nWindows 错误码：%1%n卸载程序：%2
; Shown - and the install stops - when the previous build is still on disk after its
; uninstaller ran and Setup's own delete of the leftovers failed. Stopping is the point:
; installing beside a half-removed copy is the state PrepareToInstall exists to prevent.
en.PrevRemoveIncomplete=The previous version is still on disk, so Setup stopped instead of installing next to it. Delete this folder and run Setup again: %1
zh.PrevRemoveIncomplete=上一版本仍留在磁盘上，安装程序已停止，以免与残留文件并存。请删除该文件夹后重新运行安装程序：%1
; (The closing line moved to ClickNextLine, which every cover page shares.)

; Page 5's one added line. Inno draws the status line and the current file
; itself; this is only the note from the approved design, under the file line.
en.ProgressNote=Do not close this window — this takes about 15–30 seconds.
zh.ProgressNote=请不要关闭此窗口，全过程约 15–30 秒。
; Page 5's hero sub-line. The status wording inside the caption row is not here:
; Inno writes it from ExtractingLabel / InstallingLabel in [Messages] above.
en.ProgressSub=Installing {#AppVersion}
zh.ProgressSub=正在安装 {#AppVersion}

; Page 6's launch checkbox. Inno's own wording lives in Default.isl's
; [CustomMessages] as LaunchProgram (used by [Run] as {cm:LaunchProgram,…}), so
; the override belongs in this section, not in [Messages].
en.LaunchProgram=Launch DawnCapture now
zh.LaunchProgram=立即启动 DawnCapture

; The progress window's caption, deliberately the same words Inno's UninstallAppFullTitle
; puts on its confirm box, so the windows do not change identity halfway through. The
; messages for the form we used to draw were removed with that form - see
; InitializeUninstall.
en.UninstallDataTitle=DawnCapture Uninstall
zh.UninstallDataTitle=DawnCapture 卸载

; The one data question: whether to delete the program settings. %1 is the folder, folded
; into the body with %n%n because a message box has one block of plain text. No is the
; default, so a silent run - which never sees this - keeps everything.
;
; There is deliberately no question about the recordings: they are the user's own files,
; so an uninstall should not be able to destroy them at all.
en.UninstallAskConfig=Delete your program settings as well?%n%n%1%n%nChoose No to keep them: a later reinstall then carries on where you left off.
zh.UninstallAskConfig=同时删除程序配置吗？%n%n%1%n%n选择“否”将保留它们，以后重新安装可以直接继续使用。

; Second line of the two-line lockup on pages 1 and 6. The first line is the
; product name, which [Messages] puts into WelcomeLabel1/FinishedHeadingLabel.
en.WelcomeLockupLine={#AppVersion} · published by {#AppPublisher}
zh.WelcomeLockupLine={#AppVersion} · 由 {#AppPublisher} 发布
en.FinishLockupLine={#AppVersion} · installed
zh.FinishLockupLine={#AppVersion} · 安装完成
; The outcome sentence, drawn by [Code] because Inno's own label is hidden.
en.FinishLead=Setup has finished installing DawnCapture on your computer.
zh.FinishLead=安装程序已在你的计算机上安装了 DawnCapture。

; Page 6's body: the one selectable row and the hotkey line above the footer. The hotkey
; sentence is the same one the uninstall side uses, so the two cannot drift apart.
en.FinishLaunch=Launch DawnCapture now
zh.FinishLaunch=立即启动 DawnCapture
en.FinishHotkeyNote=Press Ctrl+Shift+F9 at any time to start recording.
zh.FinishHotkeyNote=随时按 Ctrl+Shift+F9 开始录制。


[Files]
; The three images [Code] draws are listed FIRST, before the payload, and that order is
; load-bearing rather than tidiness: they carry dontcopy, so they are extracted to the
; temp directory while the wizard is being built, and with SolidCompression a file can
; only be read by decompressing this section's block up to where it sits. Listed after the
; payload, that extraction decompressed the whole block first - measured, 16-32 seconds of
; nothing on screen before the wizard appeared. The durable alternative, if one of these
; ever has to sit lower, is the nocompression flag; nothing enforces the ordering, and
; this comment is the only thing that records it.
;
; Extracted at run time only, never installed: the hero lockup, the hairline under it and
; the corner mark. Every file in brand\ is used by this build - the fourth, wizard-small.png,
; is WizardSmallImageFile above.
Source: "brand\wizard-lockup-on-white.png"; Flags: dontcopy
Source: "brand\hero-rule.png"; Flags: dontcopy
Source: "brand\wizard-small-on-white.png"; Flags: dontcopy

; The whole publish output, verbatim. No file is excluded: PRIVACY-POLICY.md
; and THIRD-PARTY-NOTICES.md ship alongside the binaries on purpose.
;
; ignoreversion - an upgrade replaces the payload wholesale; per-file version
; comparison would leave stale binaries behind when a build is republished
; under the same version.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs


; No [Tasks] section, and the desktop entry does not use "Tasks: desktopicon": with the
; native tasks page skipped, TasksList comes up empty - measured - so that flag could
; never be satisfied, and mirroring the page's answer into TasksList.Checked[0] raised
; "List index out of bounds". The entry is driven by a Check function instead, which is
; evaluated while the entry is processed. Deliberate consequence: /MERGETASKS can no
; longer request the shortcut.
[Icons]
; The Start menu entry carries no Check and is not listed on page 4 - Inno's own tasks
; page only shows entries the user can turn off, so this one is created silently. The
; desktop entry is the one question that page asks, hence the Check.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Check: WantDesktopIcon


[Run]
; postinstall is the point: without it Setup processes the entry at the end of the install
; step, so the app launched while the wizard was still on the progress page. With it, the
; entry is processed when Finish is.
;
; The price is Inno's own launch checkbox on the finish page. Page 6 draws that row itself,
; so Inno's is hidden (see CurPageChanged) - and hiding is all that does: Inno's item is
; what decides, the entry runs at Finish iff it is still checked, and Check is not asked
; again after ssPostInstall. The visible row is kept in step with that item by
; SetLaunchRunItem. Check below therefore only says whether Inno offers the launch at all.
;
; skipifsilent is kept, so an unattended install never launches the app.
Filename: "{app}\{#AppExeName}"; Flags: postinstall nowait skipifsilent; Check: WantLaunchApp


; ----------------------------------------------------------------------------
; Deliberately absent
; ----------------------------------------------------------------------------
;
; [UninstallDelete] - the uninstaller never deletes user data by itself. [Code] asks about
; the settings folder and deletes it only after a Yes; the recordings are never deleted,
; because they are the user's own files, so they are not even offered. A silent uninstall
; - the upgrade path runs this uninstaller with /VERYSILENT - deletes nothing.
; Deleting the settings folder also drops the recording catalogue index, so the app
; rescans the Videos folder after a reinstall to show the recordings it kept.
;
; [Registry] - nothing to register. Toasts work because the app registers its own COM
; activator in HKCU at first launch, not because the installer wrote a key.
;
; AppMutex - the app's single-instance guard is a WinRT AppInstance key rather than a
; documented named mutex, so there is no stable name to hand to Inno Setup.
; CloseApplications covers the in-use case instead.
;
; [Code] must stay the last section in this file: everything after it is parsed as Pascal,
; so a plain ';' comment block down there is a syntax error.
;
; Two traps, both hit for real:
;   - a continuation line that STARTS with '[' is read as a section tag ("Invalid section
;     tag"), so keep bracketed argument lists on the line that opened them;
;   - Pascal comments do not nest: a brace comment ends at the first '}', and the rest of
;     that line is then compiled as code ("Identifier expected").


[Code]
var
  { Uninstall: whether to delete the program settings, held as an answer rather than as a
    control. It was a check box in a form of our own until that form was dropped for being
    the one window in the uninstall flow that belonged to neither Inno nor Windows - see
    InitializeUninstall. Recordings have no flag because they are never deleted: they are
    the user's files. }
  DeleteConfigData: Boolean;

  { Page 2 state. }
  PrevPage: TWizardPage;
  { Page 2's status line, one slot with two captions: the running-app note, and the
    failure to close it. Nothing but the caption, the colour and Visible ever changes. }
  PrevAppLine: TNewStaticText;
  PrevFound: Boolean;
  PrevVersion, PrevDir, PrevUninstaller: String;
  PrevAllUsers: Boolean;

  { Page 3 / 3b state. }
  ScopePage: TWizardPage;

  { Page 4's row and page 6's row: one native check box each.
    The answer lives in the two Booleans rather than in the controls, because [Icons]
    and [Run] read it while the install runs - by then the wizard is on its way out,
    and a dying form is not a place to keep a value. }
  TasksPage: TWizardPage;
  DesktopTaskCheck: TNewCheckBox;
  DesktopTaskChecked: Boolean;
  LaunchNowCheck: TNewCheckBox;
  LaunchNowChecked: Boolean;

  { Page 5's percentage, which Inno's own bar does not show. }
  ProgressPctLabel: TNewStaticText;
  ScopeClosingLine: TNewStaticText;
  ScopePathEdit: TNewEdit;
  ScopeBrowseButton: TNewButton;
  { Two occupants of one slot under the path row - see BuildScopePage: the disk-space
    note normally, the not-writable explanation while the folder cannot be written to.
    Nothing but which one is Visible ever changes. }
  ScopeNote: TNewStaticText;
  ScopeError: TNewStaticText;
  ScopeInitialised: Boolean;

{ Cover-page geometry, all unscaled. ContentMargin is the design's page padding;
  the hero is the 132px logo with the name and one muted sub-line beside it, and
  the rule under them separates the hero from the body. Every cover page uses the
  same numbers, which is the point of the prototype's .hero + .wz-hr pair.

  The hero is expressed as offsets from the logo's top edge rather than as
  absolute positions, because pages 1 and 2 centre that whole block vertically
  (the design does: "正文整组在剩余空间里居中") while pages 3, 4 and 5 anchor it at
  the top - their bodies fill the page. }
const
  ContentMargin = 24;
  LockupSize = 132;
  HeroTextX = 150;
  HeroNameDY = 40;    { name + sub-line are centred against the 132px logo }
  HeroSubDY = 74;
  HeroRuleDY = 148;   { just under the logo: 132 + 16 }
  BodyDY = 172;       { first body line, below the hero's rule }
  { Top edge of the footer row: the closing line on pages 1, 2, 3, 4 and 6, and
    page 5's "do not close this window" note - one value, so the row sits at exactly
    the same height on every page.

    An absolute constant rather than "measured up from the page's bottom", for the
    same reason HeroTopY is one: the wizard's own pages are 823 device px tall and
    a custom page's Surface is 815, so a bottom-relative value left this row 7
    device px apart between the two kinds of page.

    448 leaves 40 unscaled below it on the shorter page (488), which is what the
    tallest thing placed here needs. Every page's footer row is a one-line box
    (ClosingLineH) - page 4's included, now that its two-line plan chain is gone. }
  BottomRowY = 448;
  { The closing row is one line in both languages, so it gets a one-line box.
    Giving it BodyLineH instead left a two-line box whose bottom edge ran 7 device
    px past the page - measured, and invisible only because the text is top-aligned
    in it. }
  ClosingLineH = 20;
  { Where the hero starts on every page: the logo's top edge, unscaled.

    A constant, not a per-page value. It used to come from CoverTop, which centred
    "hero + body" as one block; because every page's body has its own height, the
    hero - and with it the rule, the one element all six pages share - landed 54
    unscaled px apart between the tallest page (3, rule at 204) and the shortest
    (5, rule at 258). Fixed here, the lockup and the rule sit at the same place on
    every page and only the sub-line text plus the body below the rule change.

    72 is the value every page's body still clears the closing row with: page 3's note
    block ends at 376 and page 4's last row at 364, against a footer row at 448 and a
    page 488 tall. }
  HeroTopY = 72;
  { The upper-right corner mark: the design's .wz-corner, which is padding 14px
    16px 0 around a 44x44 mark on a 700px canvas. This wizard is 745px wide, so
    44 -> 47, 16 -> 17, 14 -> 15. }
  CornerSize = 47;
  CornerInsetX = 17;
  CornerInsetY = 15;
  { Font sizes, converted from the design's canvas (700px wide) to this wizard
    (745px at WizardSizePercent=150): 22px name -> 17pt, 13px sub-line -> 10pt,
    12.5px body -> 10pt. Inno's default dialog font is 9pt, and WizardSizePercent
    scales the window only - never the fonts - which is why everything looked
    small once the canvas was enlarged. }
  HeroNameSize = 17;
  HeroSubSize = 10;
  BodySize = 10;
  { Body paragraphs. Every sentence is one line in Chinese and up to two in
    English, and a static text cannot report the height it wrapped to - measured:
    the Height stays whatever was set, whatever the caption and width, because
    Inno's AutoSize does not re-fit once SetBounds has pinned the control. So the
    box is sized for the longer language and the step keeps the gap in Chinese
    close to the design's 10px paragraph margin. The visible cost is a slightly
    airier body in Chinese; the alternative is clipped English. }
  BodyLineH = 40;
  BodyGap = 6;
  BodyStep = 46;      { BodyLineH + BodyGap }
  { The prototype's own colours: --text-muted and --text-faint. Delphi colours
    are $00BBGGRR, hence the byte order.

    --accent, --accent-soft and --warning-soft used to be declared here too. They are
    gone: every element that needed them is now either drawn by the platform - the row
    marks became native check boxes - or deleted outright, so the wizard's red comes
    only from the artwork in brand\. }
  ColorMuted = $00716962;
  ColorFaint = $00948C85;
  { The body's own text colour, named because page 2's status line is the one control
    that sets a colour, has it changed to red for a failure and then has to go back. }
  ColorText = $00000000;
  { The rule under the hero is brand\hero-rule.png, not a drawn control - see AddRule. }
  { Page 4's body rhythm lives with page 4's own constants below. }

  { The design's option row, the shape pages 4 and 6 each draw once: 12px of row
    padding, a 20px mark column, a 10px gap, so the title starts at 42; the row is 29
    tall. Scaled to this wizard's 745px canvas (x1.064): 12 -> 13, 42 -> 45,
    29 -> 31.

    Most of that is history now. Both rows are native check boxes, and a check box
    draws its own glyph, its own gap after it and its own vertical centring, so only
    the row's left edge and its height are still ours to set. RowStep, RowMarkW,
    RowTitleX and RowContentDY all went with the hand-drawn marks they described. }
  RowPadX = 13;           { where a row's mark sits }
  RowH = 31;              { the row's box; the check box centres itself inside it }

  { Page 3 (install location): a lead sentence, the location row and the note under
    it. Its body rides the welcome page's own rhythm - one BodyStep per line, 46 - so
    its three elements land on the same body grid page 1's paragraphs do:
    244, 290, 336.

    BodyGap alone is deliberately NOT what spaces the note under the path row.
    BodyGap is a box-to-box measure, and the eye never sees it: a BodyLineH box fills
    only half its height with one line of text, so two body lines end up 26 apart on
    screen. The path row is a control that fills its own box edge to edge, so spacing
    the text under it by BodyGap alone leaves a 6px slit against the folder box -
    which is exactly what it looked like. One BodyStep puts the note on the next grid
    line instead, leaving the 20px the control's own height does not account for.

    Page 3 is a .wz-content.cover like pages 1 and 2, so its body is anchored under
    the hero's rule at the shared offset - see BuildScopePage. Every value here is
    unscaled and every use of it is wrapped in ScaleX/ScaleY, because SetBounds does
    not scale. }
  PathH = 26;
  PathBrowseGap = 12;   { between the folder box and Browse; the box itself starts at 0 }
  BrowseW = 96;

  { Page 4 (additional tasks), unscaled, and it has no rhythm of its own: BodyLineH
    for the lead's box and BodyStep to the row, the same two values pages 1 and 3 use,
    and its row is the shared RowH. The closing line sits on BottomRowY, the footer row
    every page shares. }

  { Page 5 (progress): the design's .prog block - the bar, a caption row with the
    status on the left and the percentage on the right, and the current file.
    Inno draws all three itself; these numbers are the grid they are moved onto. }
  ProgBarH = 6;           { .bar height }
  ProgCapGap = 6;         { .prog .cap margin-top }
  ProgCapH = 18;
  ProgFileGap = 4;        { .prog .file margin-top }
  ProgPctW = 60;          { the percentage column, right-aligned in the caption row }

{ ---------------------------------------------------------------------------
  Pages 1 and 6: the in-page lockup.

  WizardImageFile is blanked in [Setup], which stops the drawing but does NOT
  change Inno's layout: the welcome/finish labels still start at the left edge of
  the image area. Measured on 6.7.3 at 200% DPI: Left = 427 = 213.5 unscaled,
  right edge 1153 = 576.5. So each control that belongs at the content margin is
  moved to ScaleX(ContentMargin) *with its right edge preserved* - no pixel
  constant is hard-coded, so this survives any DPI or font size. }

procedure PlaceAtMargin(C: TControl; const NewLeft: Integer);
begin
  C.Width := C.Width + (C.Left - NewLeft);
  C.Left := NewLeft;
end;

{ Draws the lockup on one page and hands the version line back, so the caller can
  line it up under that page's heading. }
function LoadPng(const Img: TBitmapImage; const Name: String): Boolean;
begin
  ExtractTemporaryFile(Name);
  { PngImage, not Bitmap: Bitmap.LoadFromFile only handles BMP/JPG and raises on
    a .png. Examples\CodeClasses.iss says so in a comment on its TBitmapImage
    block, and a trace through this code confirmed it - the load was the first
    statement that never returned. }
  Img.PngImage.LoadFromFile(ExpandConstant('{tmp}\') + Name);
  Result := True;
end;

function AddLockupImage(const Page: TNewNotebookPage; const X, Top: Integer): TBitmapImage;
begin
  Result := TBitmapImage.Create(WizardForm);
  Result.Parent := Page;
  Result.SetBounds(ScaleX(X), ScaleY(Top), ScaleX(LockupSize), ScaleY(LockupSize));
  Result.Stretch := True;
  { The in-page copy is pre-composited on white: TBitmapImage stretches with
    StretchBlt, which drops the alpha channel, so a transparent PNG shows the
    rounded corners as grey blocks. That is what the -on-white suffix means, and
    it is the reason these copies exist rather than the originals. }
  LoadPng(Result, 'wizard-lockup-on-white.png');
end;

{ The upper-right corner mark - the design's .wz-corner, drawn on every page.

  Drawn here rather than left to Inno's own WizardSmallBitmapImage, which carries
  the same artwork (WizardSmallImageFile) but cannot be relied on. Measured on
  6.7.3 at 167%: that control reports Visible=1 with a box of L=1136 T=25 W=78 H=78
  on every page, the welcome and finished pages included, and still paints nothing
  on those two - it is created before the notebook in Inno's form, so it sits
  behind the pages, and Inno only lifts it on the pages where it shows it itself.
  Adding BringToFront then made it vanish from the welcome page instead, so its
  z-order is not ours to control. A TBitmapImage parented to the page has none of
  that: it draws above the page's own background, exactly like the hero lockup.

  It draws on the wizard's own pages only. On a custom page's Surface it does not paint
  at all - measured: the image loads (174x174), the control reports Visible=1 with the
  box it was given, and nothing appears, at any position including a forced x=100 - and
  why was never established. Inno's own bitmap is the exact complement, covering the
  pages this one cannot; CurPageChanged holds both of them and the position they share.

  X is the page's content margin, as in every other helper here: 24 on the wizard's
  own pages, 0 on a custom page's Surface, which already carries the margin. The
  mark's own 17px inset is measured from the content's right edge, so it lands in
  the same place on both kinds of page.

  Page.Width is device pixels and SetBounds does not scale, so the arithmetic is
  device throughout and only the design constants are wrapped. }
function AddCornerMark(const Page: TNewNotebookPage; const X: Integer): TBitmapImage;
begin
  Result := TBitmapImage.Create(WizardForm);
  Result.Parent := Page;
  Result.SetBounds(Page.Width - ScaleX(X + CornerInsetX + CornerSize),
                   ScaleY(CornerInsetY), ScaleX(CornerSize), ScaleY(CornerSize));
  Result.Stretch := True;
  { The white-composited copy, for the same reason the hero lockup uses one:
    StretchBlt drops the alpha channel, and wizard-small.png is fully transparent
    outside its rounded tile with black RGB there (measured: ARGB(0,0,0,0) at
    1,1), so the raw file draws that area as a dark block behind the mark. }
  LoadPng(Result, 'wizard-small-on-white.png');
end;

{ The hairline under the hero: the design's .wz-hr. A 4x1 solid PNG stretched to
  one pixel tall is how it is drawn - TNewStaticText has no border, and a
  borderless TPanel with Color set does NOT work here: it compiled, ran, and
  painted nothing (checked by scanning the rendered page for a full-width
  non-white row and finding none), because the panel's colour is not honoured.

  It spans the page's full content width. The width comes from Page.Width, which
  is already in device pixels and so is used as-is: SetBounds does NOT scale -
  verified by probe on 6.7.3 at 167%, where a raw SurfaceWidth of 1163 landed as
  Width=1163 while an explicit ScaleY(24) landed as 40. Only the inset is scaled. }
procedure AddRule(const Page: TNewNotebookPage; const X, Y: Integer);
var
  Img: TBitmapImage;
begin
  Img := TBitmapImage.Create(WizardForm);
  Img.Parent := Page;
  Img.Stretch := True;
  Img.SetBounds(ScaleX(X), ScaleY(Y), Page.Width - ScaleX(2 * X), ScaleY(1));
  LoadPng(Img, 'hero-rule.png');
end;

function AddPageText(const Page: TNewNotebookPage; const X, Y, W, H: Integer;
                     const Text: String): TNewStaticText;
begin
  Result := TNewStaticText.Create(WizardForm);
  Result.Parent := Page;
  { A static text paints a rectangle of its own Color behind the caption, and there
    is no way to switch that off: TNewStaticText has no Transparent property - the
    compiler rejects it, measured - so a caller that puts text over one of the
    page's images must set Color to that image's colour. Left alone it paints white,
    which punched white holes in the option rows' tint and inside the badge pills.
    The radio and check controls used for the marks do not need this: they paint
    their background through, which the same rows show. }
  Result.Caption := Text;
  Result.SetBounds(ScaleX(X), ScaleY(Y), ScaleX(W), ScaleY(H));
end;

{ A paragraph that wraps inside a box of a given size, for text that is one line
  in Chinese but two in English.

  The order here is not cosmetic. Switching AutoSize off makes Inno re-fit the
  control to its CURRENT caption, so doing it after AddPageText - whose caption is
  still empty for a label that is filled later - collapses the box: a chain label
  asked to be 1022x33 came back as 6x29 and would have clipped its text. AutoSize
  is therefore cleared while the control is still empty, and the bounds are set
  last, after the real caption is in place. }
function AddWrappedText(const Page: TNewNotebookPage; const X, Y, W, H: Integer;
                        const Text: String): TNewStaticText;
begin
  Result := TNewStaticText.Create(WizardForm);
  Result.Parent := Page;
  Result.AutoSize := False;
  Result.WordWrap := True;
  Result.Caption := Text;
  Result.SetBounds(ScaleX(X), ScaleY(Y), ScaleX(W), ScaleY(H));
end;

{ The whole cover-page hero: logo, product name, one muted sub-line, rule. X is
  the page's own content margin - 24 on the wizard's own pages, 0 on a custom
  page, whose Surface already carries Inno's margin. Top is the logo's top edge,
  always HeroTopY: the hero must not move between pages, so its position is a
  constant rather than something each page works out. }
{ Control properties report device pixels, and SetBounds takes device pixels too -
  it does NOT scale. Probe on 6.7.3 at 167%: handing SetBounds a raw 1163 gave
  Width=1163, while an explicit ScaleY(24) gave 40. Every literal in this file is
  an unscaled design unit, so each one must be wrapped in ScaleX/ScaleY at the
  point of use; a raw unscaled value silently renders at 1/1.67 of its intended
  size, which is what made page 3's body sit at half its offsets.

  Conversions also run the other way: a value read out of a control is device
  pixels, and helpers such as AddPageText and AddHero take unscaled units, so it
  has to be divided back first - that is what this function is for. It is declared
  up here, ahead of the helpers and the page builders that call it.

  The divisor must NOT be ScaleY(1): that comes back as 1 at every scaling level,
  which made this a silent no-op, so the value was scaled a second time further
  down and every bottom-anchored line drifted off the page as the scaling grew -
  right at 100%, wrong at 150%, gone at 200%. ScaleY(100) is always the DPI-
  scaled 100, so it is the reliable factor. }
function Unscaled(const DevicePixels: Integer): Integer;
begin
  Result := DevicePixels * 100 div ScaleY(100);
end;

procedure AddHero(const Page: TNewNotebookPage; const X, Top: Integer; const SubLine: String);
var
  Line: TNewStaticText;
begin
  AddLockupImage(Page, X, Top);
  AddCornerMark(Page, X);

  Line := AddPageText(Page, X + HeroTextX, Top + HeroNameDY, 380, 28, '{#AppName}');
  Line.Font.Size := HeroNameSize;
  Line.Font.Style := [fsBold];
  Line.AutoSize := True;

  Line := AddPageText(Page, X + HeroTextX, Top + HeroSubDY, 420, 18, SubLine);
  Line.Font.Size := HeroSubSize;
  { --text-muted, not --text-faint: the design's .hero-vr uses the muted token,
    which is the same one its .wz-note body sentences use. Only page 2's extra
    identity line under this one is a step lighter - see BuildPrevPage. }
  Line.Font.Color := ColorMuted;

  AddRule(Page, X, Top + HeroRuleDY);
end;

procedure LayoutWelcomePage();
var
  Line: TNewStaticText;
  Top, PageW: Integer;
begin
  { Inno's own body label is hidden: the design gives the first sentence the
    normal colour and the other two the muted one (the prototype's .wz-lead and
    .wz-note), which one label cannot do - and Inno also appends its ClickNext
    sentence to that label's text, which the page draws separately and pinned to
    the bottom. Three of the page's own labels replace it. }
  WizardForm.WelcomeLabel2.Visible := False;

  { The hero sits at the same fixed place on every page - see HeroTopY - so only
    the sub-line above and the body below it differ. The body is three paragraphs,
    the last of which may wrap; the closing line stays pinned near the footer. }
  Top := HeroTopY;

  { The hero uses Inno's own WelcomeLabel1 as the product name - it already
    carries the heading font the design's hero-nm asks for - so only the logo,
    the sub-line and the rule are drawn here. }
  AddLockupImage(WizardForm.WelcomePage, ContentMargin, Top);

  WizardForm.WelcomeLabel1.AutoSize := True;
  WizardForm.WelcomeLabel1.Font.Size := HeroNameSize;
  PlaceAtMargin(WizardForm.WelcomeLabel1, ScaleX(ContentMargin + HeroTextX));
  WizardForm.WelcomeLabel1.Top := ScaleY(Top + HeroNameDY);

  { Fixed design position, NOT "under WelcomeLabel1": that label is auto-sized to
    the whole image area, so its bottom edge is not where its text ends. }
  Line := AddPageText(WizardForm.WelcomePage, ContentMargin + HeroTextX, Top + HeroSubDY,
                      420, 18, CustomMessage('WelcomeLockupLine'));
  Line.Font.Size := HeroSubSize;
  Line.Font.Color := ColorMuted;

  AddRule(WizardForm.WelcomePage, ContentMargin, Top + HeroRuleDY);
  AddCornerMark(WizardForm.WelcomePage, ContentMargin);

  { Body: first sentence in the normal colour, the other two muted, as the
    design's .wz-lead + .wz-note pair. AddWrappedText, not AddPageText: each box is
    sized for the longer language, and AddPageText's own AutoSize would have laid
    the English sentences out as one line running off the page. }
  PageW := Unscaled(WizardForm.WelcomePage.Width) - 2 * ContentMargin;

  Line := AddWrappedText(WizardForm.WelcomePage, ContentMargin, Top + BodyDY,
                         PageW, BodyLineH, CustomMessage('WelcomeLine1'));
  Line.Font.Size := BodySize;

  Line := AddWrappedText(WizardForm.WelcomePage, ContentMargin, Top + BodyDY + BodyStep,
                         PageW, BodyLineH, CustomMessage('WelcomeLine2'));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;

  Line := AddWrappedText(WizardForm.WelcomePage, ContentMargin, Top + BodyDY + 2 * BodyStep,
                         PageW, BodyLineH, CustomMessage('WelcomeLine3'));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;

  { The closing line, pinned to the bottom of the page above the footer divider
    as the design specifies - the copy Inno appends to the body cannot be moved,
    so the page carries its own. }
  Line := AddWrappedText(WizardForm.WelcomePage, ContentMargin,
                         BottomRowY,
                         PageW, ClosingLineH, CustomMessage('ClickNextLine'));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;
end;

procedure ScopeRefreshError();
begin
  { Back to the page's normal state: the disk-space note is the visible occupant of
    the slot under the path row, and the closing line names the Next button. The
    explanation and the closing line never share the page - the explanation wraps to
    two lines in English and takes the room the line would sit in. }
  ScopeNote.Visible := True;
  ScopeError.Visible := False;
  ScopeClosingLine.Visible := True;
end;

function DefaultUserDir(): String;
begin
  Result := ExpandConstant('{localappdata}') + '\Programs\{#AppName}';
end;

procedure ScopePathChanged(Sender: TObject);
begin
  { Typing or browsing to a different folder clears a stale "not writable" note, so
    the user reads the result of the next attempt rather than the previous one. }
  ScopeRefreshError();
end;

procedure ScopeBrowseClick(Sender: TObject);
var
  Chosen: String;
begin
  Chosen := RemoveBackslashUnlessRoot(Trim(ScopePathEdit.Text));
  if BrowseForFolder(CustomMessage('ScopeBrowsePrompt'), Chosen, True) then
  begin
    Chosen := RemoveBackslashUnlessRoot(Trim(Chosen));
    { The payload owns its own folder and the install folder is whatever this box
      holds, so keep the product folder name as the last segment. }
    if CompareText(ExtractFileName(Chosen), '{#AppName}') <> 0 then
      Chosen := Chosen + '\{#AppName}';
    ScopePathEdit.Text := Chosen;
  end;
end;

{ Pushes the launch row's answer into Inno's own list, which is the thing that actually
  decides whether the [Run] entry runs.

  Measured, and this is the mechanism three earlier rounds were spent on the wrong side
  of. A harness with four postinstall entries and a log of every Check call, in order:

    ssPostInstall      Check E0, E1, E2, E3 all called here - before the finish page
                       exists. One call each, and that is the only time Check runs.
    finish page open   CurPageChanged, after Inno has created its items from those
                       answers. Every item starts checked.
    Finish clicked     NextButtonClick.
    ssDone

    item left checked         -> ran
    item unchecked at page open -> skipped
    item unchecked on the Finish click -> skipped
    Check answering True and then False before the page opened -> still ran

  So Check decides only whether Inno offers the launch at all - it builds its item from
  that answer, and the item is what runs the entry at Finish. The row page 6 draws is our
  own control and writes nothing else, so the user's answer has to be copied here or it
  never arrives; that is why unchecking the row did not stop the app.

  The index is 0 because the RunList holds nothing but postinstall entries, and there is
  one. Guarded anyway: the list is filled during the install, and this runs from the
  finish page onwards. }
procedure SetLaunchRunItem(Value: Boolean);
begin
  if WizardForm.RunList.Items.Count > 0 then
    WizardForm.RunList.Checked[0] := Value;
end;

{ The check box owns the glyph and the click; this takes its answer to the two places
  that need it - the Boolean the [Run] check reads, and Inno's item. Called on the click,
  which is the earliest the answer can exist; NextButtonClick does it again on the way
  off the page, and that second one is what makes it stick. }
procedure LaunchNowChanged(Sender: TObject);
begin
  LaunchNowChecked := LaunchNowCheck.Checked;
  SetLaunchRunItem(LaunchNowChecked);
end;

{ [Run] asks this once, at ssPostInstall, before page 6 is shown. It is therefore a gate
  on whether Inno offers the launch at all, and not the user's answer: it reads a Boolean
  that is still at its initial True when Inno calls it. The answer itself travels through
  Inno's list item - see SetLaunchRunItem. Has to be a public function for the directive
  to find it. }
function WantLaunchApp(): Boolean;
begin
  Result := LaunchNowChecked;
end;

procedure LayoutFinishedPage();
var
  Line: TNewStaticText;
  Top, PageW, RowY: Integer;
begin
  { Page 6 mirrors page 1: the same hero, rule and centred body, with the design's
    .wz-note pinned near the footer - the hotkey line is the one thing worth
    taking away from the wizard. The body is the outcome sentence plus the one
    selectable row the design puts here. }
  PageW := Unscaled(WizardForm.FinishedPage.Width) - 2 * ContentMargin;
  Top := HeroTopY;

  AddLockupImage(WizardForm.FinishedPage, ContentMargin, Top);

  WizardForm.FinishedHeadingLabel.AutoSize := True;
  WizardForm.FinishedHeadingLabel.Font.Size := HeroNameSize;
  PlaceAtMargin(WizardForm.FinishedHeadingLabel, ScaleX(ContentMargin + HeroTextX));
  WizardForm.FinishedHeadingLabel.Top := ScaleY(Top + HeroNameDY);

  Line := AddPageText(WizardForm.FinishedPage, ContentMargin + HeroTextX,
                      Top + HeroSubDY, PageW - HeroTextX, 18, CustomMessage('FinishLockupLine'));
  Line.Font.Size := HeroSubSize;
  Line.Font.Color := ColorMuted;

  AddRule(WizardForm.FinishedPage, ContentMargin, Top + HeroRuleDY);
  AddCornerMark(WizardForm.FinishedPage, ContentMargin);

  { Inno's own FinishedLabel is hidden rather than reused. Setup appends its own
    "click Finish to exit Setup" sentence to that label's text the same way it
    appends "click Next" to page 1's body, and the design's finish page carries no
    such line - it says the outcome, the one row and the hotkey, and the single
    Finish button says the rest. Drawing the sentence here also gives it the body
    colour and a wrapped box in both languages. }
  WizardForm.FinishedLabel.Visible := False;

  Line := AddWrappedText(WizardForm.FinishedPage, ContentMargin, Top + BodyDY,
                         PageW, BodyLineH, CustomMessage('FinishLead'));
  Line.Font.Size := BodySize;

  { The one selectable row: the same native check box page 4 draws, rather than Inno's
    own RunList - that list wraps its entry in the themed box this design does not want,
    and it cannot carry the badge. The [Run] entry is gated by WantLaunchApp instead,
    so the choice still decides whether the app starts.

    No row fill, same as page 4: the flow answers with the check box alone. }
  RowY := Top + BodyDY + BodyLineH + BodyGap;

  LaunchNowCheck := TNewCheckBox.Create(WizardForm);
  LaunchNowCheck.Parent := WizardForm.FinishedPage;
  LaunchNowCheck.Caption := CustomMessage('FinishLaunch');
  LaunchNowCheck.Font.Size := BodySize;
  LaunchNowCheck.Checked := LaunchNowChecked;
  LaunchNowCheck.OnClick := @LaunchNowChanged;
  LaunchNowCheck.Left := ScaleX(ContentMargin + RowPadX);
  LaunchNowCheck.Top := ScaleY(RowY);
  LaunchNowCheck.Width := ScaleX(PageW - RowPadX);
  LaunchNowCheck.Height := ScaleY(RowH);

  { The hotkey line, pinned to the bottom of the page above the footer divider. }
  Line := AddWrappedText(WizardForm.FinishedPage, ContentMargin,
                         BottomRowY,
                         PageW, ClosingLineH, CustomMessage('FinishHotkeyNote'));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;
end;

{ First entry to page 3: UsePreviousAppDir is on, so on an upgrade DirEdit holds
  the folder the previous install used. Reading it here - rather than in
  InitializeWizard, where Setup has not settled DirEdit yet - keeps the page
  truthful instead of guessing a default. }
procedure InitScopePage();
begin
  { One question on this page now: where. It opens on the folder Setup already
    resolved - the per-user default on a fresh install, or the folder the previous
    install used on an upgrade, because that is where the payload belongs and where
    an upgrade is expected to overwrite. }
  ScopePathEdit.Text := WizardDirValue;

  { Two cases fall back to the per-user default instead:
      - WizardDirValue came back empty, which would leave the box blank; and
      - the previous install was an all-users one, whose folder under Program Files
        this per-user Setup can never write to. Opening on a path that is guaranteed
        to fail on Next would be a trap rather than a starting point. }
  if (ScopePathEdit.Text = '') or (PrevFound and PrevAllUsers) then
    ScopePathEdit.Text := DefaultUserDir();

  ScopeRefreshError();
end;

procedure BuildScopePage();
var
  AfterID, PageW, HeroTop, BodyTop, RowY: Integer;
  Line: TNewStaticText;
begin
  { Page 2 sits between 1 and 3 when it exists, so 3 follows whatever precedes
    it. Inno resolves the anchor when the page is created, which is why this is
    decided here and not by a fixed wpWelcome. }
  if PrevPage <> nil then
    AfterID := PrevPage.ID
  else
    AfterID := wpWelcome;

  { The description is the lead sentence rather than a string of its own: the band
    that would show it is hidden (see LayoutInnerChrome), and a second phrasing of
    the same instruction is exactly the duplication this page was cleaned of. If the
    band ever comes back, it repeats what the body says instead of contradicting it. }
  ScopePage := CreateCustomPage(AfterID, CustomMessage('ScopePageCaption'),
                                CustomMessage('ScopeLead'));

  { Cover layout, straight from the design's page 3: hero (logo + name + the sub
    line "选择安装位置") with the rule under it, the lead sentence, the location row,
    the note under it and the closing line. X is 0 because a custom page's Surface
    already sits at the content margin - LayoutInnerChrome pulled it back to
    ContentMargin.

    PageW is the usable width in unscaled pixels. Every SetBounds call below
    wraps its arguments in ScaleX/ScaleY: SetBounds does not scale (probe on 6.7.3
    at 167%: a raw 1163 landed as Width=1163, ScaleY(24) landed as 40), so a raw
    unscaled value silently renders at 1/1.67 of its intended size. }
  PageW := Unscaled(ScopePage.SurfaceWidth);
  HeroTop := HeroTopY;
  { The offset every other page uses: one rule plus the design's 24px margin below
    the hero. The body is deliberately NOT centred in the space that is left over -
    page 3's block is the shortest of the six, so centring it would put its first line
    ~57 lower than page 1's and break the one thing the cover layout holds constant,
    which is where the text sits relative to the rule. The closing line stays pinned
    to the footer row, as it is on every page. }
  BodyTop := HeroTop + HeroRuleDY + 24;

  AddHero(ScopePage.Surface, 0, HeroTop, CustomMessage('ScopePageCaption'));

  { Inno's SelectDirLabel3 sentence, in the same box every body paragraph on every
    page gets. AddWrappedText rather than AddPageText: the latter auto-sizes, and an
    auto-sized one-line box runs off the page the moment a translation is longer than
    English - see the note on the body paragraphs. }
  Line := AddWrappedText(ScopePage.Surface, 0, BodyTop, PageW, BodyLineH,
                         CustomMessage('ScopeLead'));
  Line.Font.Size := BodySize;

  { No option row: the scope is fixed, so the body is the lead sentence, the folder
    box and the note under it - the second and third lines of the body grid, one
    BodyStep below the one before (see the constants note above).

    The location row - the design's .pathrow minus its label: box and Browse on one
    line, across the page's content width. The "Install location" label that used to
    sit beside the box is gone. Inno's own page carries no such label, and by the time
    the eye reaches this row the hero and the sentence above have both said what the
    box is - a third statement only cost the box ~60 unscaled px of the room it needs
    to show a long path. }
  RowY := BodyTop + BodyStep;

  ScopePathEdit := TNewEdit.Create(ScopePage);
  ScopePathEdit.Parent := ScopePage.Surface;
  ScopePathEdit.SetBounds(0, ScaleY(RowY),
                          ScaleX(PageW - PathBrowseGap - BrowseW), ScaleY(PathH));
  ScopePathEdit.OnChange := @ScopePathChanged;

  ScopeBrowseButton := TNewButton.Create(ScopePage);
  ScopeBrowseButton.Parent := ScopePage.Surface;
  ScopeBrowseButton.SetBounds(ScaleX(PageW - BrowseW), ScaleY(RowY),
                              ScaleX(BrowseW), ScaleY(PathH));
  ScopeBrowseButton.Caption := CustomMessage('ScopeBrowse');
  ScopeBrowseButton.OnClick := @ScopeBrowseClick;

  { The one slot under the path row: the disk-space note normally, the not-writable
    explanation while the folder cannot be written to. One BodyStep below the row, not
    BodyGap - that lands it on the next line of the body grid instead of crowding the
    folder box, and the constants note above works through why the two measures are
    not interchangeable here. The box is BodyLineH, the standard two-line body box,
    which is what lets the explanation wrap in place without moving anything. The two
    labels share it and ScopeRefreshError is what swaps them.

    AddWrappedText, not a bare TNewStaticText: its AutoSize is cleared before any
    caption is set, and the explanation's caption only arrives later, from
    NextButtonClick. A static text left on AutoSize re-fits itself when that caption
    lands - the trap page 5's percentage label documents. }
  ScopeNote := AddWrappedText(ScopePage.Surface, 0, RowY + BodyStep, PageW, BodyLineH,
                              CustomMessage('ScopeDiskSpace'));
  ScopeNote.Font.Size := BodySize;
  ScopeNote.Font.Color := ColorMuted;

  ScopeError := AddWrappedText(ScopePage.Surface, 0, RowY + BodyStep, PageW, BodyLineH, '');
  ScopeError.Font.Color := clRed;
  ScopeError.Font.Size := BodySize;
  ScopeError.Visible := False;

  { The closing line, pinned near the footer on every page at the same offset: it
    names the buttons right below it, which is what the design's cover layout does.
    Surface.Height is device pixels, so it is divided back before being handed to
    AddPageText. }
  Line := AddPageText(ScopePage.Surface, 0,
                      BottomRowY,
                      PageW, ClosingLineH, CustomMessage('ClickNextLine'));
  ScopeClosingLine := Line;
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;
end;

{ ---------------------------------------------------------------------------
  Page 2: an existing installation (prototype page 2).

  Conditional: it exists only while the registry still carries the uninstall
  record for this AppId, and it is anchored to page 1, so the flow is
  1 -> 2 -> 3 when it is shown and 1 -> 3 when it is not.

  Detection runs in InitializeSetup because the page has to be built - or not -
  while the wizard is being constructed. The removal itself runs later, in
  PrepareToInstall, so nothing is deleted before the user has clicked through to
  Install. Three uninstall keys are checked because the same AppId can sit in
  HKCU (per-user), HKLM (all-users) or HKLM\WOW6432Node (all-users, 32-bit
  Setup on a 64-bit host). }

procedure ReadPrevInstall();
var
  Root: Integer;
  SubKey: String;
begin
  PrevVersion := '';
  PrevDir := '';
  PrevUninstaller := '';
  PrevAllUsers := False;

  SubKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdPlain}_is1';
  Root := HKEY_CURRENT_USER;
  if not RegKeyExists(Root, SubKey) then
  begin
    Root := HKEY_LOCAL_MACHINE;
    if not RegKeyExists(Root, SubKey) then
      SubKey := 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdPlain}_is1';
  end;
  if not RegKeyExists(Root, SubKey) then
    Exit;

  { The old copy's scope is not shown anywhere - see the [CustomMessages] note - but
    the root it was found under still decides one thing: an all-users copy lives under
    Program Files, which this per-user Setup cannot write to, so page 3 opens on the
    per-user default rather than on that path. }
  PrevAllUsers := (Root = HKEY_LOCAL_MACHINE);

  if not RegQueryStringValue(Root, SubKey, 'DisplayVersion', PrevVersion) then
    PrevVersion := '';
  { Both value names are written by Inno; "Inno Setup: App Path" is the
    documented one, InstallLocation is the fallback. }
  if not RegQueryStringValue(Root, SubKey, 'Inno Setup: App Path', PrevDir) then
    if not RegQueryStringValue(Root, SubKey, 'InstallLocation', PrevDir) then
      PrevDir := '';
  { InstallLocation is written with a trailing separator and 'Inno Setup: App Path'
    without one. PrepareToInstall compares and deletes this path, so the two spellings
    must not be allowed to name two different folders. }
  PrevDir := RemoveBackslashUnlessRoot(PrevDir);
  if not RegQueryStringValue(Root, SubKey, 'UninstallString', PrevUninstaller) then
    PrevUninstaller := '';
  PrevUninstaller := RemoveQuotes(PrevUninstaller);

  { A record only counts as a previous installation while something of it is still on
    disk. Records outlive their folders: deleting the folder by hand is the usual way,
    and the upgrade path in PrepareToInstall tells users to do exactly that when it
    stops on leftovers. A stale record is not a previous installation, and leaving one
    in place misleads three separate places - page 2 announces an install that is not
    there, page 3 opens the folder box on a path that does not exist, and
    PrepareToInstall goes after an uninstaller that is gone and reports the failure.
    Dropped here, once, rather than defended against in each of the three. }
  if (not FileExists(PrevUninstaller)) and (not DirExists(PrevDir)) then
  begin
    PrevVersion := '';
    PrevDir := '';
    PrevUninstaller := '';
  end;
end;

function InitializeSetup(): Boolean;
begin
  ReadPrevInstall();
  PrevFound := (PrevVersion <> '') or (PrevDir <> '');
  Result := True;
end;

procedure BuildPrevPage();
var
  Line: TNewStaticText;
  Top, PageW: Integer;
begin
  PrevPage := CreateCustomPage(wpWelcome, CustomMessage('PrevPageCaption'),
                               CustomMessage('PrevPageDesc'));

  { Same skeleton as page 1, so the same centring and the same body rhythm: three
    paragraphs sized for the longer language, and the closing line pinned to the
    bottom like page 1's. This page used to carry its own hardcoded 110 and 46/92
    steps and a device-pixel width - the width is what pushed the closing line off
    the page, since AddPageText scales what it is handed. }
  Top := HeroTopY;
  PageW := Unscaled(PrevPage.SurfaceWidth);

  { Inno's Surface already carries the page margin, so X is 0 here, and the
    hero's sub-line is the version row. }
  AddHero(PrevPage.Surface, 0, Top,
          FmtMessage(CustomMessage('PrevVersionRow'), [PrevVersion, '{#AppVersion}']));

  { Third lockup line: where the old copy lives. It is identity, not body, which
    is what keeps the body at three sentences like page 1. Full content width so a
    deep path is not clipped. }
  Line := AddPageText(PrevPage.Surface, HeroTextX, Top + HeroSubDY + 20,
                      PageW - HeroTextX, 16, PrevDir);
  Line.Font.Size := HeroSubSize;
  Line.Font.Color := ColorFaint;

  { Body: the same lead-then-muted-notes split as page 1. AddWrappedText, not
    AddPageText - see the BodyLineH note. }
  Line := AddWrappedText(PrevPage.Surface, 0, Top + BodyDY, PageW, BodyLineH,
                         FmtMessage(CustomMessage('PrevFoundLead'), [PrevVersion]));
  Line.Font.Size := BodySize;

  Line := AddWrappedText(PrevPage.Surface, 0, Top + BodyDY + BodyStep, PageW, BodyLineH,
                         FmtMessage(CustomMessage('PrevRemovesOld'), ['{#AppVersion}']));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;

  Line := AddWrappedText(PrevPage.Surface, 0, Top + BodyDY + 2 * BodyStep, PageW, BodyLineH,
                         CustomMessage('PrevKeepsData'));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;

  { The status line: the row after the body, in the body's own text colour rather than
    in the muted fine print the two notes above it use. This line reports the state of
    the machine, not advice about the upgrade, and it is the one line on the page the
    user may have to act on - so it should not read as small print.

    Empty and hidden until PrevAppRefresh fills it in, which happens on every arrival at
    this page and again from Next. AddWrappedText rather than AddPageText: it clears
    AutoSize while the control is still empty, so the box keeps the size it is given
    instead of re-fitting itself around whichever caption happens to be in it.

    Geometry: 72 + 172 + 3 * 46 = 382, the box ends at 422, and the footer row starts at
    448 on a page 488 unscaled tall - the same margins every other page clears. }
  PrevAppLine := AddWrappedText(PrevPage.Surface, 0, Top + BodyDY + 3 * BodyStep,
                                PageW, BodyLineH, '');
  PrevAppLine.Font.Size := BodySize;
  PrevAppLine.Font.Color := ColorText;
  PrevAppLine.Visible := False;

  { Same as page 1: the closing line belongs on the last row above the footer,
    not in the body's flow. }
  Line := AddWrappedText(PrevPage.Surface, 0,
                         BottomRowY,
                         PageW, ClosingLineH, CustomMessage('ClickNextLine'));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;
end;

{ Page 5: the one line the design adds to the progress page, under the current
  file. Positioned relative to FilenameLabel rather than at a fixed offset, so it
  follows Inno's own layout at any DPI. }
procedure BuildProgressPage();
var
  Line: TNewStaticText;
  Top, PageW, BodyTop: Integer;
begin
  { The design's page 5 is the only page with nothing to say but progress, so the
    body is three things and there is no lead sentence. Inno draws all three: the
    bar, the status line and the current file. They are not replaced - they are
    moved onto the design's grid, which is what the .prog block describes. }
  PageW := Unscaled(WizardForm.InstallingPage.Width);
  Top := HeroTopY;
  BodyTop := Top + HeroRuleDY + 24;

  AddHero(WizardForm.InstallingPage, 0, Top, CustomMessage('ProgressSub'));

  { The percentage is the one thing Inno's page does not have; it is filled in by
    CurInstallProgressChanged. }
  { ProgressGauge, not ProgressBar: the latter is the name the 6.x help suggests
    and the compiler rejects. Verified by compiling each candidate - ProgressGauge
    is the only one that resolves on 6.7.3. }
  WizardForm.ProgressGauge.SetBounds(0, ScaleY(BodyTop), ScaleX(PageW), ScaleY(ProgBarH));

  { StatusLabel, not ProgressLabel - and ProgressGauge, not ProgressBar: the names
    the 6.x help suggests are rejected by the compiler. All three were pinned down
    by compiling each candidate against 6.7.3; these are the ones that resolve. }
  WizardForm.StatusLabel.Left := 0;
  WizardForm.StatusLabel.Top := ScaleY(BodyTop + ProgBarH + ProgCapGap);
  WizardForm.StatusLabel.Font.Size := BodySize;
  WizardForm.StatusLabel.Font.Color := ColorMuted;

  { Built by hand rather than with AddPageText: its caption arrives later, from the
    progress callback, and a static text whose AutoSize is still on re-fits itself
    when the caption is set - which collapsed this box from 100px to 12 and left
    the percentage floating in the middle of the row instead of against the right
    edge. AutoSize is cleared while the control is still empty, bounds last. }
  ProgressPctLabel := TNewStaticText.Create(WizardForm);
  ProgressPctLabel.Parent := WizardForm.InstallingPage;
  ProgressPctLabel.AutoSize := False;
  ProgressPctLabel.Font.Size := BodySize;
  ProgressPctLabel.Font.Color := ColorMuted;
  ProgressPctLabel.Font.Name := 'Consolas';
  ProgressPctLabel.Alignment := taRightJustify;
  ProgressPctLabel.SetBounds(ScaleX(PageW - ProgPctW),
                             ScaleY(BodyTop + ProgBarH + ProgCapGap),
                             ScaleX(ProgPctW), ScaleY(ProgCapH));

  { The current file, in the monospace the design asks for: it is there for
    troubleshooting, and Setup writes it straight into FilenameLabel. }
  WizardForm.FilenameLabel.Left := 0;
  WizardForm.FilenameLabel.Top := ScaleY(BodyTop + ProgBarH + ProgCapGap + ProgCapH + ProgFileGap);
  WizardForm.FilenameLabel.Font.Size := HeroSubSize;
  WizardForm.FilenameLabel.Font.Name := 'Consolas';
  WizardForm.FilenameLabel.Font.Color := ColorFaint;

  { The one line that matters on this page: the design does not repeat "click
    Next" here, because there is no next step - it says do not close the window. }
  Line := AddWrappedText(WizardForm.InstallingPage, 0,
                         BottomRowY,
                         PageW, ClosingLineH, CustomMessage('ProgressNote'));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;
end;

{ Inno's progress callbacks. The bar is advanced by Setup itself; only the
  percentage beside it is ours, and it has to be recomputed rather than
  accumulated, because the extraction pass and the install pass each report their
  own range. }
procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if MaxProgress > 0 then
    ProgressPctLabel.Caption := IntToStr(CurProgress * 100 div MaxProgress) + '%'
  else
    ProgressPctLabel.Caption := '';
end;

{ Every inner page in the design is a cover page: the hero at the top, no wizard
  caption and no description. Inno always reserves a band for those two labels
  above the notebook that holds the actual page, and the notebook is inset
  horizontally by the same layout, so on 6.7.3 the inner pages are 1033x618 at
  200% DPI while the wizard's own welcome/finish pages are the full 1193x772.
  Measured, not guessed.

  Both are corrected on the notebook itself, because that is what carries them:
  the band is given back as height, and the horizontal inset is pulled back to
  the design's ContentMargin. Every number is read from Inno's own layout at run
  time, so this survives a different DPI or a different WizardSizePercent. }
procedure LayoutInnerChrome();
var
  Band, Pad: Integer;
begin
  WizardForm.PageNameLabel.Visible := False;
  WizardForm.PageDescriptionLabel.Visible := False;

  Band := WizardForm.InnerNotebook.Top;
  if Band > 0 then
  begin
    WizardForm.InnerNotebook.Top := WizardForm.InnerNotebook.Top - Band;
    WizardForm.InnerNotebook.Height := WizardForm.InnerNotebook.Height + Band;
  end;

  Pad := WizardForm.InnerNotebook.Left - ScaleX(ContentMargin);
  if Pad > 0 then
  begin
    WizardForm.InnerNotebook.Left := WizardForm.InnerNotebook.Left - Pad;
    WizardForm.InnerNotebook.Width := WizardForm.InnerNotebook.Width + 2 * Pad;
  end;
end;

{ The check box owns the glyph, the click and the answer; this only mirrors the answer
  into the Boolean [Icons] reads.

  This row used to be two static labels carrying a drawn '●'/'○', which is what made it
  mouse-only - TNewStaticText takes focus, but Inno exposes no key handler for it, and
  the file recorded that as a deliberate trade-off. The native control hands both back:
  Space toggles it, and its caption is what a screen reader announces. }
procedure DesktopTaskChanged(Sender: TObject);
begin
  DesktopTaskChecked := DesktopTaskCheck.Checked;
end;

{ [Icons] asks this while it processes the desktop entry, so it reads the answer
  page 4 collected. It has to be a public function for the directive to find it. }
function WantDesktopIcon(): Boolean;
begin
  Result := DesktopTaskChecked;
end;

procedure BuildTasksPage();
var
  Line: TNewStaticText;
  PageW, HeroTop, BodyTop, RowY: Integer;
begin
  { The description is the lead sentence, as on page 3: the band that would show it is
    hidden (see LayoutInnerChrome), and Inno's own tasks page puts SelectTasksDesc in
    exactly that slot - which is this string. }
  TasksPage := CreateCustomPage(ScopePage.ID, CustomMessage('TasksPageCaption'),
                                CustomMessage('TasksLead'));

  { Same cover rhythm as page 3: hero + rule, body anchored under the rule, the
    closing line pinned near the footer. Every coordinate is wrapped in ScaleX/ScaleY,
    because SetBounds does not scale - page 4 had exactly the mixed-basis bug page 3
    had, which put the checkbox on the hero's bottom edge and the lead sentence below
    the rule. }
  PageW := Unscaled(TasksPage.SurfaceWidth);
  HeroTop := HeroTopY;
  BodyTop := HeroTop + HeroRuleDY + 24;

  AddHero(TasksPage.Surface, 0, HeroTop, CustomMessage('TasksPageSub'));

  { Two lines reserved and wrapped, in the same box every other page's lead gets:
    the lead is one line in Chinese and two in English, and a single-line box would
    either clip the English or leave a gap in Chinese. AddWrappedText, not
    AddPageText - see its note. }
  Line := AddWrappedText(TasksPage.Surface, 0, BodyTop, PageW, BodyLineH,
                         CustomMessage('TasksLead'));
  Line.Font.Size := BodySize;

  { One BodyStep below the lead - the grid pages 1 and 3 use - rather than a pair of
    constants of this page's own, which put the row 8px lower than the same place on
    the other pages. }
  RowY := BodyTop + BodyStep;

  { The page's one row, and it is a real check box. That is what makes it read as
    something the user can turn off: the drawn dot looked like a status light, and
    nothing on the page said it could be clicked. The platform glyph does that job, and
    it brings keyboard and screen-reader support with it.

    On by default - see InitializeWizard. It starts on the mark column, so its glyph
    sits where the drawn dot used to, and it carries no row fill: the design's .opt.sel
    box reads as "this option is the chosen one", which is not what a lone check box
    should say. }
  DesktopTaskCheck := TNewCheckBox.Create(TasksPage);
  DesktopTaskCheck.Parent := TasksPage.Surface;
  DesktopTaskCheck.Caption := CustomMessage('TasksDesktop');
  DesktopTaskCheck.Font.Size := BodySize;
  DesktopTaskCheck.Checked := DesktopTaskChecked;
  DesktopTaskCheck.OnClick := @DesktopTaskChanged;
  DesktopTaskCheck.Left := ScaleX(RowPadX);
  DesktopTaskCheck.Top := ScaleY(RowY);
  DesktopTaskCheck.Width := ScaleX(PageW - RowPadX);
  DesktopTaskCheck.Height := ScaleY(RowH);

  { The closing line, the same one the other cover pages carry at this offset.

    It replaced the design's "what happens next" strip - see the [CustomMessages]
    note for why that went. Two things fall out of it: the label and the chain were
    what made page 4's footer read as a different component from the other five
    pages' closing lines, and removing them settles that; and TasksChainH, which
    reserved two lines for the English chain, went with them, so this row is now a
    one-line box like every other page's. }
  Line := AddPageText(TasksPage.Surface, 0,
                      BottomRowY,
                      PageW, ClosingLineH, CustomMessage('ClickNextLine'));
  Line.Font.Size := BodySize;
  Line.Font.Color := ColorMuted;
end;

procedure InitializeWizard();
begin
  LayoutInnerChrome();

  { Page 6's row is drawn by hand (see LayoutFinishedPage), so Inno's own RunList is
    hidden. It is not empty any more: the [Run] entry carries postinstall, which is
    the flag that makes it wait for Finish, and Inno fills this list with its own
    launch checkbox for it. That checkbox must not appear as a second control under
    the design's row - CurPageChanged hides it again on the finish page, where Inno's
    own update would otherwise show it.
    WizardForm.TasksList is deliberately not touched either - with the native tasks
    page skipped it comes up empty, and nothing reads it. }
  { The two selectable rows both start on - page 4's desktop shortcut and page 6's
    launch - and each is one click to turn off. The default is the safe side for both:
    a shortcut nobody asked for is visible and one click to delete, while a missing
    shortcut is invisible and has to be recovered by reinstalling. Assigned here
    rather than beside the pages, because both pages read their answer while they are
    built, below. }
  DesktopTaskChecked := True;
  LaunchNowChecked := True;
  WizardForm.RunList.Visible := False;

  if PrevFound then
    BuildPrevPage();
  BuildScopePage();
  BuildTasksPage();
  LayoutWelcomePage();
  LayoutFinishedPage();
  BuildProgressPage();
end;

{ Writability probe used by page 3 (and its error state, page 3b): the folder is
  created if missing and written into, so a path the user has no rights to fails
  with an explanation instead of a bare access-denied error halfway through the
  file copy. }
function IsWritableDir(const Dir: string): Boolean;
var
  ProbeFile: string;
begin
  Result := False;

  if not DirExists(Dir) then
  begin
    if not ForceDirectories(Dir) then
      Exit;
  end;

  ProbeFile := AddBackslash(Dir) + '.dawncapture_write_probe.tmp';
  if SaveStringToFile(ProbeFile, 'probe', False) then
  begin
    DeleteFile(ProbeFile);
    Result := True;
  end;
end;

{ The native tasks page is hidden because page 4 replaces it: same question,
  drawn by hand. Nothing depends on the TasksList it hosts any more - with this
  page skipped that list comes up empty, which is what made both "Tasks:
  desktopicon" and a Checked[0] mirror fail. The desktop entry is driven by
  WantDesktopIcon instead. }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpSelectTasks);
end;

{ The app's process state, and the two things the wizard does about it.

  They sit between the page builders and the wizard callbacks rather than next to
  PrepareToInstall, where they started: Pascal Script compiles in a single pass, so
  everything a function calls has to be defined above it - and both callbacks below use
  these. Page 2 reads the state on arrival (CurPageChanged) and acts on it from Next
  (NextButtonClick). }

{ True while a process named after the app is running.

  tasklist, because Pascal Scripting exposes no process API and this app is a WinUI one:
  there is no window class or helper process of ours to look for, and a window title
  would depend on the UI language. tasklist's own output is the answer - it prints a row
  carrying the image name when something matched, and only an INFO line when nothing
  did, so searching the captured text for the name is the test. The text arrives
  through a file: Exec cannot capture stdout, so cmd writes it to one in the temp
  directory Inno creates for Setup, which it also removes on exit. }
function IsAppRunning(): Boolean;
var
  TmpFile: String;
  Lines: TArrayOfString;
  Code, I: Integer;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\app-running.txt');

  if not Exec(ExpandConstant('{cmd}'),
              '/C tasklist /FI "IMAGENAME eq {#AppExeName}" /NH > "' + TmpFile + '"',
              '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Exit;

  if not LoadStringsFromFile(TmpFile, Lines) then
    Exit;

  for I := 0 to GetArrayLength(Lines) - 1 do
    if Pos(LowerCase('{#AppExeName}'), LowerCase(Lines[I])) > 0 then
    begin
      Result := True;
      Exit;
    end;
end;

{ Closes a copy of the app that is still running. True when it is not running any more,
  which includes the case where it never was.

  Force, not a polite window close: the app's own close button is wired to "minimise to
  the tray" by default, so a WM_CLOSE leaves the process - and every lock it holds -
  exactly where it was. Setup's own CloseApplications does not cover this either: it
  runs later, in the install step, and only knows the files it is about to write (the
  new folder), not the old copy.

  taskkill matches by image name, so it closes every DawnCapture.exe - including a copy
  the user happens to be running from somewhere else. Deliberate: a second live copy
  keeps locking files regardless of where it was started from, and the alternative -
  filtering by path - would need WMI or PowerShell on a code path that has to work
  before anything is installed.

  It closes the app, it does not assume it closed: each pass is a fresh tasklist check
  after a fresh kill, and a pass that finds nothing left is the only way out. Three
  passes cover both ways a single one can miss - the process needs a moment to actually
  go, and a transient failure clears on the next attempt. No Sleep between them, because
  taskkill and tasklist are process launches and that is delay enough; the loop needs no
  timer the scripting engine may not expose.

  It says nothing itself, which is what the Boolean is for. The two callers have
  different things to say and different ways of saying them: page 2 has a user in front
  of it and a slot in its layout, so a failure there is a red line and the user stays
  put; PrepareToInstall has no page left, so a failure there stops the install. The
  confirmation this used to raise lived in here, which is what put it over the blank
  install page with the whole wizard already behind it. }
function CloseRunningApp(): Boolean;
var
  Code, Pass: Integer;
begin
  Result := not IsAppRunning();
  if Result then
    Exit;

  for Pass := 1 to 3 do
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /F',
         '', SW_HIDE, ewWaitUntilTerminated, Code);
    if not IsAppRunning() then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ Page 2's status line, brought in line with the machine as it is right now.

  Re-checked on every arrival at the page rather than once, while it is built: the
  wizard is built before the user has clicked anything at all, and the app can be
  started or quit while the earlier pages are on screen. The same call runs after a
  close from Next, where its second job is to take the line back off the page.

  The colour is the page's own text colour, not the muted fine print the body notes
  above it use: this line reports a fact about the machine, and it is the one line here
  the user may have to act on. The failure caption, set from Next, goes red in the same
  box - which is the only reason ColorText has to be named at all. }
procedure PrevAppRefresh();
begin
  if IsAppRunning() then
  begin
    PrevAppLine.Caption := CustomMessage('AppRunningNote');
    PrevAppLine.Font.Color := ColorText;
    PrevAppLine.Visible := True;
  end
  else
    PrevAppLine.Visible := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  { Page 2 closes the app - here, on the page, before the wizard moves on. That is the
    whole reason it is not left to PrepareToInstall any more. This page has just said
    the old build is about to be removed, so the running copy belongs in the same
    breath; and while the page is still up, Back, Cancel and quitting the app by hand
    are all still available, which a modal box over the blank install page took away.

    A failure keeps the user here with the reason in the status line rather than failing
    the install: there is still a page to say it on, and still something to be done. }
  if (PrevPage <> nil) and (CurPageID = PrevPage.ID) then
  begin
    if CloseRunningApp() then
      { Not a plain Visible := False: the user may have quit the app by hand since the
        page opened, and PrevAppRefresh is what reads that state and takes the note off
        the page either way. }
      PrevAppRefresh()
    else
    begin
      PrevAppLine.Caption := CustomMessage('PrevAppCloseFailed');
      PrevAppLine.Font.Color := clRed;
      PrevAppLine.Visible := True;
      Result := False;
    end;
    Exit;
  end;

  { The finish page's launch row is read here, straight off the control.

    This is not what makes the launch respect the row - Inno's list item is, and it is
    already in step by now (SetLaunchRunItem). It is the last read before the entry runs,
    so a click handler that never fired cannot leave a stale answer behind: the control is
    the one thing the user definitely changed. }
  if CurPageID = wpFinished then
  begin
    LaunchNowChecked := LaunchNowCheck.Checked;
    SetLaunchRunItem(LaunchNowChecked);
    Exit;
  end;

  { Page 4's shortcut row, read off the control for the same reason page 6's is: [Icons]
    asks WantDesktopIcon while the files are being installed, which is after this, and a
    click handler that never fired would leave the Boolean at its initial True. Same
    wiring on both rows, so the same suspicion applies to both - and unlike the launch,
    this one fails silently: an unwanted shortcut is only noticed later. }
  if CurPageID = TasksPage.ID then
  begin
    DesktopTaskChecked := DesktopTaskCheck.Checked;
    Exit;
  end;

  if CurPageID <> ScopePage.ID then
    Exit;

  if IsWritableDir(ScopePathEdit.Text) then
  begin
    { Hand the chosen folder to Inno: the install folder comes from DirEdit, and
      with DisableDirPage=yes nothing else ever writes it. }
    WizardForm.DirEdit.Text := ScopePathEdit.Text;
    ScopeRefreshError();
    Exit;
  end;

  { Not writable. The usual cause is a system-owned folder that needs administrator
    rights this per-user Setup does not have, and there is no elevation path any more
    - Setup runs with PrivilegesRequired=lowest and is fixed for the lifetime of the
    process ([Setup]). So the only honest move is to say so and put the location back
    on the per-user default, which is always writable: the user is never left on a
    page whose Next cannot succeed.

    The folder is set before the note is shown, not after: assigning Text fires
    OnChange, which runs ScopePathChanged and puts the disk-space note back. Nothing
    navigates, so the page stays put and the next click re-tests the reset path. }
  ScopePathEdit.Text := DefaultUserDir();
  ScopeError.Caption := FmtMessage(CustomMessage('DirNotWritable'), [ScopePathEdit.Text]);
  ScopeNote.Visible := False;
  ScopeError.Visible := True;
  ScopeClosingLine.Visible := False;
  Result := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin

  { Buttons stay at Inno's native layout: < Back, Next, Cancel - Cancel on the
    far right, which is the Windows wizard convention. The design mock drew them
    the other way round; that has been deliberately dropped in favour of the
    platform order. }

  if (CurPageID = ScopePage.ID) and (not ScopeInitialised) then
  begin
    ScopeInitialised := True;
    InitScopePage();
  end;

  { Page 2 is a cover page like page 1, so it carries no caption or description
    header. Inno writes those two labels from the current page's Caption and
    Description on every page change, so they have to be cleared here rather
    than left unset when the page is built. }
  if (PrevPage <> nil) and (CurPageID = PrevPage.ID) then
  begin
    WizardForm.PageNameLabel.Caption := '';
    WizardForm.PageDescriptionLabel.Caption := '';

    { And the state of the app, re-read on arrival rather than settled once while the
      page was being built - see PrevAppRefresh. This is also what puts the line back
      when the user returns to the page after going forward and pressing Back. }
    PrevAppRefresh();
  end;

  { The design greys Cancel out on the progress page only: this install writes
    into the user profile and finishes in 15-30 seconds, so cancelling it would
    only leave a half-written folder behind. Re-enabled on every other page, so
    Back and Cancel keep their normal meaning elsewhere. To follow Inno's own
    behaviour instead - Cancel aborts and rolls back - delete these two lines. }
  WizardForm.CancelButton.Enabled := (CurPageID <> wpInstalling);

  { Inno re-shows its own launch checkbox on the finish page, because the [Run] entry
    carries postinstall (see the [Run] note). Page 6 draws that row itself, so Inno's is
    hidden again here - after Inno's own page update has run, which is what makes it
    stick.

    Hiding it hides nothing else: it is the control whose state Inno reads at Finish, so
    the row's answer is mirrored into it at the same moment. This runs after Inno has
    built the list from the Check answers it collected at ssPostInstall, which is why the
    mirror belongs here and not earlier. }
  if CurPageID = wpFinished then
  begin
    WizardForm.RunList.Visible := False;
    LaunchNowChecked := LaunchNowCheck.Checked;
    SetLaunchRunItem(LaunchNowChecked);
  end;

  { Inno's own corner bitmap is left visible here, and moved onto the design's line.

    Measured, and this is why that line is no longer a plain Visible := False: the two
    mechanisms cover disjoint sets of pages, so neither can be dropped.

      - AddCornerMark's own TBitmapImage paints on the wizard's native pages, 1 and 6, and
        does not paint at all on a custom page's Surface - not at any position, with the
        image loaded and Visible=1, verified by logging its bounds and bitmap and by
        forcing its left edge to 100. Why it fails there was not established.
      - WizardSmallBitmapImage is the exact complement: it paints nothing on pages 1 and 6,
        where it sits behind the notebook, and it paints on every inner page, custom pages
        included, where Inno lifts it above them.

    Inno's position is its own, though, not the design's: it lands flush against the
    window's right edge and is clipped by it, while the mark on pages 1 and 6 carries the
    design's inset. The bounds are therefore set here, and this runs after Inno's own page
    update - the same ordering that the RunList above needed to make its own change stick.
    Width and Height are set from the design constants too, because Inno's own size is its
    own and the two kinds of page would otherwise show the mark at two different sizes.

    Everything is device pixels, like AddCornerMark's arithmetic; only the design
    constants are wrapped. Left is measured from the window's right edge: the content
    margin the mark is inset from, plus the mark's own inset. }
  WizardForm.WizardSmallBitmapImage.Visible := True;
  WizardForm.WizardSmallBitmapImage.SetBounds(
    WizardForm.ClientWidth - ScaleX(CornerInsetX + ContentMargin + CornerSize),
    ScaleY(CornerInsetY), ScaleX(CornerSize), ScaleY(CornerSize));
end;

{ True while the folder still holds something that is unmistakably an install of ours.
  It is the gate on the delete below: PrevDir comes out of our own uninstall record,
  but a stale record - or a folder the uninstaller deliberately left because the user
  had put a file in it - must not turn into a DelTree of somebody else's data. }
function PrevDirIsOurs(): Boolean;
begin
  Result := FileExists(AddBackslash(PrevDir) + '{#AppExeName}')
         or FileExists(AddBackslash(PrevDir) + 'unins000.exe');
end;

{ Runs after the user has committed (clicked through to Install) and before any file is
  copied: whatever is left of the app is closed first, then the previous build's own
  uninstaller is run silently, so its program files and shortcuts go away and the new
  payload lands in a clean folder. Not strictly an upgrade path any more - step 1 below
  runs for every install.

  Three steps, in this order, and each one is why the upgrade that prompted this left a
  folder behind:

    1. Stop the app. A running copy locks its own exe and memory-maps every dll it has
       loaded, and the uninstaller cannot delete either - silently, because it runs
       /VERYSILENT /SUPPRESSMSGBOXES and Inno's uninstaller has no close-applications
       step of its own. Measured on a real upgrade: 89 files survived - the exe and 88
       dlls - while every data file went, which is the signature of a live process. It is
       also the one step here that can stop the install by itself - see CloseRunningApp,
       and page 2, which is where the user is meant to meet it.

    2. Run the previous uninstaller - the ones that are still on disk. A record that has
       outlived its uninstaller is nothing to run and nothing to report; the folder
       check in step 3 is what catches the cases that matter.

    3. Check that it finished the job. It was assumed to before, and the leftovers were
       never looked at again - by then the uninstall record had been consumed, so
       nothing else would ever clean that folder up.

  Leftovers that survive both are deleted by Setup itself. If even that fails the
  install stops: installing a second copy beside a half-removed one is the state this
  function exists to prevent.

  No data switch is passed. The uninstaller's data dialog is guarded by
  UninstallSilent(), which means a silent run deletes nothing at all - settings, logs,
  the catalogue and the recordings all survive the upgrade, which is what page 2
  promises. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';

  { Backstop, not the path the user normally meets.

    Page 2 is where the app is closed: with the user watching, with the reason in the
    page's own status line, and with Back, Cancel and a manual quit all still open. This
    runs anyway, because that page cannot cover every case - the user can launch the app
    again from the pages after it, a silent install has no pages at all, and a machine
    with no previous installation to report never saw page 2 in the first place.

    It runs before the uninstall record is looked at, and that placement matters for its
    own reason: it used to sit below the empty-record test, so an install with no record
    to read closed nothing and left the running app to Inno's own close-applications
    step. That step asks with the buttons "close them automatically" and "do not close
    them", and the second one continues the install anyway; it also only ever looks at
    the files Setup is about to write, which is the new folder, so the old copy was never
    its business to begin with.

    Nothing is reported from here. The page that could report has been left behind, and a
    second modal at this point is the thing that moving the check onto a page was meant
    to remove - so a failure stops the install instead, stating the outcome. }
  if not CloseRunningApp() then
  begin
    Result := CustomMessage('PrevAppStillRunning');
    Exit;
  end;

  { The recorded uninstaller is not always still on disk. When it is gone there is
    nothing to remove with it and nothing to report: ReadPrevInstall has already dropped
    the records that left nothing behind at all, and this covers the half-stale case,
    where the folder is still there but the uninstaller inside it is not. Launching it
    anyway is what produced "could not be started" on a machine where the file had
    already gone - and the folder check below is the part that actually matters. }
  if FileExists(PrevUninstaller) then
  begin
    { Note for whoever re-wraps this call: the "[IntToStr(Code), ...]" array must not be
      allowed to become the first thing on its line. Setup's section scanner is
      line-based and reads any line whose first non-blank character is "[" as a section
      header - measured, "Invalid section tag" - and [Code] being the last section does
      not exempt it. }
    if not Exec(PrevUninstaller,
                '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL',
                '', SW_HIDE, ewWaitUntilTerminated, Code) then
      SuppressibleMsgBox(FmtMessage(CustomMessage('PrevUninstallFailed'), [IntToStr(Code),
                                    PrevUninstaller]), mbError, MB_OK, IDOK);
  end;

  { Nothing to check, or nothing left that is ours to delete - the uninstaller did its
    job and took the folder with it. }
  if (PrevDir = '') or (not DirExists(PrevDir)) or (not PrevDirIsOurs()) then
    Exit;

  DelTree(PrevDir, True, True, True);

  if DirExists(PrevDir) then
    Result := FmtMessage(CustomMessage('PrevRemoveIncomplete'), [PrevDir]);
end;

{ ---------------------------------------------------------------------------
  Uninstall.

  Measured on Inno Setup 6.7.3, not assumed:

  - The uninstaller CANNOT host wizard pages. Calling CreateInputOptionPage or
    CreateCustomPage there aborts the uninstall with "Internal error: Cannot
    call ... function during Uninstall", in both silent and interactive runs. A
    standalone form made with CreateCustomForm CAN be used - Examples\
    CodeClasses.iss builds its own dialogs that way - and one was built here
    until it was replaced by message boxes; see InitializeUninstall.
  - The uninstaller's event list is five functions long - InitializeUninstall,
    InitializeUninstallProgressForm, CurUninstallStepChanged,
    UninstallNeedRestart and DeinitializeUninstall - so there is no progress
    callback to update anything during the removal, and no wizard.

  Rules, in the order that matters:

  - Silent/unattended uninstall (page 2 runs this with /VERYSILENT during an
    upgrade): nothing is asked and no user data is deleted. Program files and
    shortcuts are removed as usual - until the Result fix below, the silent path
    aborted before removing anything at all.
  - Interactive: the settings folder is asked about, and it is the only thing
    this uninstaller deletes that Inno would have left behind.
  - The recordings are never deleted, by anything, and are not asked about:
    they are the user's own files rather than the product's.
  - Refusing a question that can cancel aborts the uninstall. }

function InitializeUninstall(): Boolean;
var
  ConfigPath: String;
begin
  { Result is the go-ahead flag, and its default is False: returning it untouched
    makes the uninstaller log "InitializeUninstall returned False; aborting" and
    exit without removing anything. Every path below has to leave it True, the
    silent early return included - that bug shipped once and was caught by
    running a real silent uninstall: an upgrade left the previous build's files
    (and even a file the new payload no longer contained) in place, because the
    old uninstaller had aborted instead of uninstalling. }
  Result := True;

  { The app is closed first, before the dialog and before anything is removed.

    Inno's uninstaller has no close-applications step at all. Setup has CloseApplications,
    and page 2 above force-closes the app before an upgrade - but neither reaches a user
    who uninstalls from Settings with DawnCapture still in the tray, and that uninstall
    reports success while leaving behind everything it could not take.

    Measured on a real uninstall with the app running: 78 files survived, and they were
    exactly the modules the live process had loaded - the exe, coreclr.dll,
    Microsoft.ui.xaml.dll, Microsoft.Windows.SDK.NET.dll - while every unmapped file (the
    language folders, the notices) was removed. library.db survived the data dialog's own
    DelTree for the same reason, because SQLite still held it open. The same uninstall
    with the app closed removed all of it, which is what ruled out a permissions cause.

    Asked first, because the app is a recorder: a capture in progress dies with the
    process and nothing else warns about it. There is no page to say so on - the
    uninstaller cannot host wizard pages, see the section note above this function - so
    unlike page 2's status line this has to be a dialog. A silent uninstall, which is how
    an upgrade runs this uninstaller, gets the default answer and closes the app unasked.

    Either refusal aborts: continuing would delete what it can and leave the rest, which
    is the state this whole block exists to prevent. }
  if IsAppRunning() then
  begin
    if SuppressibleMsgBox(CustomMessage('AppRunningNote'), mbConfirmation, MB_OKCANCEL,
                          IDOK) <> IDOK then
    begin
      Result := False;
      Exit;
    end;

    if not CloseRunningApp() then
    begin
      SuppressibleMsgBox(CustomMessage('UninstallAppCloseFailed'), mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
  end;

  { A silent uninstall must ask nothing and delete nothing: page 2 runs this uninstaller
    with /VERYSILENT during an upgrade. The prompt below is not shown under /VERYSILENT -
    SuppressibleMsgBox returns its default, and that default is the safe answer - so this
    guard is the second line of defence rather than the only one. }
  if UninstallSilent() then
    Exit;

  { The data question, asked as a message box rather than inside a form of our own.

    A form was built here before this, laid out with the wizard's own tokens and previewed
    by rendering it, and it looked right on its own. It is gone on purpose: the uninstaller
    cannot host wizard pages, so a form is the only alternative to a message box, and a
    hand-drawn window sat next to three we cannot touch - Inno's confirm box, Inno's
    progress window and the system's completion box. A native question matches them by
    construction, and its buttons come out in the system's language.

    About the settings only. The recordings used to get a second question and it is gone
    deliberately: they are the user's files, so nothing here should be able to delete
    them. See UninstallAskConfig. }
  ConfigPath := ExpandConstant('{localappdata}\{#AppDataFolderName}');
  DeleteConfigData :=
    SuppressibleMsgBox(FmtMessage(CustomMessage('UninstallAskConfig'), [ConfigPath]),
                       mbConfirmation, MB_YESNO, IDNO) = IDYES;
end;

{ Inno's own uninstall progress window, given the same colours and type the install side's
  progress page uses. The window cannot be replaced and the bar inside it cannot be
  recoloured - that would take a custom VCL style, which restyles Setup too - so this is
  the frame around the bar and nothing more.

  The only moment that works: InitializeUninstall is too early, the window does not exist
  yet, and every other uninstall event is too late. There is no FilenameLabel here, unlike
  the install side's progress form - the compiler established that by rejecting the
  assignment - and no progress callback exists to drive a percentage, so none is drawn. }
procedure InitializeUninstallProgressForm();
begin
  UninstallProgressForm.Caption := CustomMessage('UninstallDataTitle');
  UninstallProgressForm.Color := clWhite;

  UninstallProgressForm.StatusLabel.Font.Size := BodySize;
  UninstallProgressForm.StatusLabel.Font.Color := ColorMuted;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Second line of defence: even if someone later flips a default, a silent uninstall
      still never deletes the settings. }
    if UninstallSilent() then
      Exit;

    { The only deletion this uninstaller performs, and the only thing it removes that Inno
      would not have removed by itself: the app's settings folder, and only when the user
      said so.

      Nothing else is deleted, and that is a decision rather than an omission - the
      recordings are the user's files and survive every uninstall. See UninstallAskConfig
      for why that question is not asked. }
    if DeleteConfigData then
      DelTree(ExpandConstant('{localappdata}\{#AppDataFolderName}'), True, True, True);
  end;
end;



