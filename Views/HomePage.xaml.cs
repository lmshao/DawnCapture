using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views;

public sealed partial class HomePage : Page
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;

    private HomeViewModel _viewModel = null!;
    private bool _suppressWindowSelection;

    public HomePage()
    {
        InitializeComponent();

        _viewModel = Ioc.Default.GetRequiredService<HomeViewModel>();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.IsRecording))
            {
                UpdateStartButtons();
            }
        };
    }

    private void UpdateStartButtons()
    {
        bool idle = !_viewModel.IsRecording;
        DesktopButton.IsEnabled = idle;
        WindowButton.IsEnabled = idle;
        RegionButton.IsEnabled = idle;
    }

    private async void DesktopButton_Click(object sender, RoutedEventArgs e)
    {
        await StartAsync(RecordingMode.Desktop, IntPtr.Zero);
    }

    private async void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindowList();
        WindowList.Visibility = Visibility.Visible;
    }

    private async void RegionButton_Click(object sender, RoutedEventArgs e)
    {
        await StartAsync(RecordingMode.Region, IntPtr.Zero);
    }

    private async void WindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWindowSelection || WindowList.SelectedItem is not WindowItem item)
        {
            return;
        }

        _suppressWindowSelection = true;
        WindowList.Visibility = Visibility.Collapsed;
        await StartAsync(RecordingMode.Window, item.Hwnd);
        _suppressWindowSelection = false;
    }

    private async Task StartAsync(RecordingMode mode, IntPtr window)
    {
        UpdateStartButtons();
        try
        {
            await _viewModel.StartRecordingAsync(mode, window);
        }
        catch (Exception ex)
        {
            Log.Error("启动录制失败", ex);
            _viewModel.StatusText = string.Format(
                LocalizationService.GetString("Error_StartRecording"),
                ex.Message);
        }
        finally
        {
            UpdateStartButtons();
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
        if (windows.Count > 0)
        {
            _suppressWindowSelection = true;
            WindowList.SelectedIndex = -1;
            _suppressWindowSelection = false;
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
