using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using DawnCapture.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views;

public sealed partial class RecordingSourceDialog : ContentDialog
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;

    public RecordingSourceDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshWindowList();
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    public RecordingMode SelectedMode { get; private set; } = RecordingMode.Desktop;

    public IntPtr SelectedWindow { get; private set; }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (WindowModeRadio.IsChecked == true)
        {
            SelectedMode = RecordingMode.Window;
            DesktopHint.Visibility = Visibility.Collapsed;
            RegionHint.Visibility = Visibility.Collapsed;
            WindowHint.Visibility = Visibility.Visible;
            WindowList.Visibility = Visibility.Visible;
            if (WindowList.Items.Count > 0 && WindowList.SelectedItem is null)
            {
                WindowList.SelectedIndex = 0;
            }
        }
        else if (RegionModeRadio.IsChecked == true)
        {
            SelectedMode = RecordingMode.Region;
            DesktopHint.Visibility = Visibility.Collapsed;
            RegionHint.Visibility = Visibility.Visible;
            WindowHint.Visibility = Visibility.Collapsed;
            WindowList.Visibility = Visibility.Collapsed;
        }
        else
        {
            SelectedMode = RecordingMode.Desktop;
            DesktopHint.Visibility = Visibility.Visible;
            RegionHint.Visibility = Visibility.Collapsed;
            WindowHint.Visibility = Visibility.Collapsed;
            WindowList.Visibility = Visibility.Collapsed;
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SelectedMode == RecordingMode.Window)
        {
            if (WindowList.SelectedItem is not WindowItem item)
            {
                args.Cancel = true;
                WindowHint.Text = "请先选择一个窗口";
                return;
            }

            SelectedWindow = item.Hwnd;
        }
    }

    private void RefreshWindowList()
    {
        var windows = new List<WindowItem>();
        EnumWindows((hwnd, lParam) =>
        {
            if (IsRecordableWindow(hwnd))
            {
                var title = GetWindowTitle(hwnd);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    windows.Add(new WindowItem(title, hwnd));
                }
            }

            return true;
        }, IntPtr.Zero);

        WindowList.ItemsSource = windows;
        if (windows.Count == 0)
        {
            WindowHint.Text = "未找到可录制的窗口";
        }
        else if (WindowModeRadio.IsChecked == true)
        {
            WindowList.SelectedIndex = 0;
        }
    }

    private static bool IsRecordableWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd))
        {
            return false;
        }

        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        if ((exStyle & WsExToolWindow) != 0)
        {
            return false;
        }

        if (GetWindowTextLength(hwnd) == 0)
        {
            return false;
        }

        // 跳过被 DWM 隐藏（cloaked）的窗口，例如其它虚拟桌面的窗口。
        int cloaked = 0;
        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        return true;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    public sealed record WindowItem(string Title, IntPtr Hwnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
}
