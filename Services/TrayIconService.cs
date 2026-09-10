using System;
using System.Runtime.InteropServices;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DawnCapture.Services;

public sealed class TrayIconService : ITrayIconService
{
    private const int TrayIconId = 1;
    private const int CmdShow = 1001;
    private const int CmdToggleRecording = 1002;
    private const int CmdTogglePause = 1003;
    private const int CmdExit = 1004;

    private const int WmTrayIcon = NativeMethods.WM_USER + 1;
    private const string WindowClassName = "DawnCapture.TrayMessageWindow";

    private readonly IRecordingService _recordingService;
    private readonly ISettingsService _settingsService;
    private readonly CaptureViewModel _captureViewModel;

    private readonly NativeMethods.WndProc _windowProc;
    private MainWindow? _mainWindow;
    private DispatcherQueue? _dispatcherQueue;
    private IntPtr _messageWindow;
    private IntPtr _iconHandle;
    private bool _iconAdded;
    private RecordingState _recordingState = RecordingState.Idle;
    private bool _disposed;

    public TrayIconService(
        IRecordingService recordingService,
        ISettingsService settingsService,
        CaptureViewModel captureViewModel)
    {
        _recordingService = recordingService;
        _settingsService = settingsService;
        _captureViewModel = captureViewModel;
        _windowProc = WindowProc;
        _recordingService.StateChanged += OnRecordingStateChanged;
    }

    public void Attach(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _dispatcherQueue = mainWindow.DispatcherQueue;
        EnsureMessageWindow();
        EnsureTrayIcon();
        UpdateTrayTip();
        mainWindow.Closed += (_, _) => Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recordingService.StateChanged -= OnRecordingStateChanged;

        if (_iconAdded)
        {
            var data = CreateNotifyData(NIF_ICON);
            _ = NativeMethods.Shell_NotifyIcon(NIM_DELETE, ref data);
            _iconAdded = false;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        if (_messageWindow != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_messageWindow);
            _messageWindow = IntPtr.Zero;
        }
    }

    private void OnRecordingStateChanged(object? sender, RecordingState state)
    {
        _recordingState = state;
        _dispatcherQueue?.TryEnqueue(UpdateTrayTip);
    }

    private void EnsureMessageWindow()
    {
        if (_messageWindow != IntPtr.Zero)
        {
            return;
        }

        NativeMethods.WNDCLASSEX wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _windowProc,
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = WindowClassName
        };

        if (NativeMethods.RegisterClassEx(ref wc) == 0 &&
            Marshal.GetLastWin32Error() != 1410)
        {
            Log.Error("RegisterClassEx failed for tray message window");
            return;
        }

        _messageWindow = NativeMethods.CreateWindowEx(
            0,
            WindowClassName,
            WindowClassName,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.HWND_MESSAGE,
            IntPtr.Zero,
            wc.hInstance,
            IntPtr.Zero);
    }

    private void EnsureTrayIcon()
    {
        if (_iconAdded || _messageWindow == IntPtr.Zero)
        {
            return;
        }

        _iconHandle = LoadAppIcon();
        var data = CreateNotifyData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.uCallbackMessage = WmTrayIcon;
        data.hIcon = _iconHandle;
        data.szTip = BuildTooltip();

        if (!NativeMethods.Shell_NotifyIcon(NIM_ADD, ref data))
        {
            Log.Error("Shell_NotifyIcon NIM_ADD failed");
            return;
        }

        _iconAdded = true;
        data.uVersion = NOTIFYICON_VERSION_4;
        _ = NativeMethods.Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private void UpdateTrayTip()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = CreateNotifyData(NIF_TIP);
        data.szTip = BuildTooltip();
        _ = NativeMethods.Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATA CreateNotifyData(uint flags)
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _messageWindow,
            uID = TrayIconId,
            uFlags = flags
        };
    }

    private static IntPtr LoadAppIcon()
    {
        string iconPath = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            PackageAssets.AppIcon.Replace('/', System.IO.Path.DirectorySeparatorChar));

        IntPtr handle = NativeMethods.LoadImage(
            IntPtr.Zero,
            iconPath,
            NativeMethods.IMAGE_ICON,
            0,
            0,
            NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);

        return handle == IntPtr.Zero ? NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION) : handle;
    }

    private string BuildTooltip()
    {
        return _recordingState switch
        {
            RecordingState.Recording => LocalizationService.GetString("Tray_Tooltip_Recording"),
            RecordingState.Paused => LocalizationService.GetString("Tray_Tooltip_Paused"),
            _ => LocalizationService.GetString("Tray_Tooltip_Idle")
        };
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmTrayIcon)
        {
            int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            switch (mouseMsg)
            {
                case NativeMethods.WM_LBUTTONDBLCLK:
                    EnqueueShowMainWindow();
                    return IntPtr.Zero;
                case NativeMethods.WM_RBUTTONUP:
                    ShowContextMenu();
                    return IntPtr.Zero;
            }
        }
        else if (msg == NativeMethods.WM_COMMAND)
        {
            int command = wParam.ToInt32() & 0xFFFF;
            HandleMenuCommand(command);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, CmdShow, LocalizationService.GetString("Tray_Menu_Show"));

        string toggleLabel = _recordingState is RecordingState.Recording or RecordingState.Paused
            ? LocalizationService.GetString("Tray_Menu_StopRecording")
            : LocalizationService.GetString("Tray_Menu_StartRecording");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, CmdToggleRecording, toggleLabel);

        if (_recordingState is RecordingState.Recording or RecordingState.Paused)
        {
            string pauseLabel = _recordingState == RecordingState.Paused
                ? LocalizationService.GetString("Tray_Menu_Resume")
                : LocalizationService.GetString("Tray_Menu_Pause");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, CmdTogglePause, pauseLabel);
        }

        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, CmdExit, LocalizationService.GetString("Tray_Menu_Exit"));

        NativeMethods.GetCursorPos(out NativeMethods.POINT point);
        NativeMethods.SetForegroundWindow(_messageWindow);
        int selected = NativeMethods.TrackPopupMenu(
            menu,
            NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
            point.X,
            point.Y,
            0,
            _messageWindow,
            IntPtr.Zero);
        NativeMethods.DestroyMenu(menu);

        if (selected != 0)
        {
            HandleMenuCommand(selected);
        }
    }

    private void HandleMenuCommand(int command)
    {
        switch (command)
        {
            case CmdShow:
                EnqueueShowMainWindow();
                break;
            case CmdToggleRecording:
                EnqueueToggleRecording();
                break;
            case CmdTogglePause:
                EnqueueTogglePause();
                break;
            case CmdExit:
                EnqueueExit();
                break;
        }
    }

    private void EnqueueShowMainWindow()
    {
        _dispatcherQueue?.TryEnqueue(() => _mainWindow?.RestoreFromTray());
    }

    private void EnqueueToggleRecording()
    {
        _dispatcherQueue?.TryEnqueue(() => _captureViewModel.ToggleRecordingCommand.Execute(null));
    }

    private void EnqueueTogglePause()
    {
        _dispatcherQueue?.TryEnqueue(() => _captureViewModel.TogglePauseCommand.Execute(null));
    }

    private void EnqueueExit()
    {
        _dispatcherQueue?.TryEnqueue(async () =>
        {
            if (_mainWindow is not null)
            {
                await _mainWindow.TryRequestExitAsync();
            }
        });
    }

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NOTIFYICON_VERSION_4 = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private static class NativeMethods
    {
        public const int WM_USER = 0x0400;
        public const int WM_COMMAND = 0x0111;
        public const int WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_RBUTTONUP = 0x0205;
        public const int IMAGE_ICON = 1;
        public const int LR_LOADFROMFILE = 0x00000010;
        public const int LR_DEFAULTSIZE = 0x00000040;
        public const int IDI_APPLICATION = 32512;
        public const uint MF_STRING = 0x00000000;
        public const uint MF_SEPARATOR = 0x00000800;
        public const uint TPM_RETURNCMD = 0x0100;
        public const uint TPM_RIGHTBUTTON = 0x0002;
        public static readonly IntPtr HWND_MESSAGE = new(-3);

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadImage(
            IntPtr hInst,
            string name,
            int type,
            int cx,
            int cy,
            int fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr LoadIcon(IntPtr hInstance, int lpIconName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int TrackPopupMenu(
            IntPtr hMenu,
            uint uFlags,
            int x,
            int y,
            int nReserved,
            IntPtr hWnd,
            IntPtr prcRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
