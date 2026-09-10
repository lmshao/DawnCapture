using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.ViewModels;
using DawnCapture.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DawnCapture;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Border> _navMarks = new();
    private readonly Dictionary<string, TextBlock> _navLabels = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private string _currentNav = "Capture";
    private IGlobalHotkeyService? _globalHotkeyService;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = Ioc.Default.GetRequiredService<MainViewModel>();
        InitializeComponent();
        WindowIconHelper.Apply(this);
        ConfigureWindowMetrics();
        RegisterNavElements();
        NavigateTo("Capture", force: true);

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CaptureNoticeText)
                or nameof(MainViewModel.CaptureNoticeSeverity))
            {
                UpdateCaptureNoticeBar();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsStorageLow))
            {
                UpdateStorageAvailabilityBrush();
            }
        };
        Title = "DawnCapture";
        UpdateCaptureNoticeBar();
        UpdateStorageAvailabilityBrush();
        ViewModel.NavigationRequested += (_, tag) => NavigateTo(tag);
        RegisterGlobalHotkeys();
        AppWindow.Closing += OnMainWindowClosing;
        Closed += (_, _) => _globalHotkeyService?.Dispose();
    }

    private bool _closingAfterStop;
    private bool _forceExit;

    public void HideToTray()
    {
        AppWindow.Hide();
    }

    public void RestoreFromTray()
    {
        AppWindow.Show(true);
        Activate();
    }

    public async Task TryRequestExitAsync()
    {
        _forceExit = true;
        try
        {
            if (!await ConfirmStopRecordingIfNeededAsync())
            {
                return;
            }

            Close();
        }
        finally
        {
            _forceExit = false;
        }
    }

    /// <summary>
    /// Close (X): minimize to tray, or exit — with recording confirmation only on exit.
    /// </summary>
    private async void OnMainWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closingAfterStop)
        {
            args.Cancel = true;
            return;
        }

        var settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
        if (!_forceExit &&
            settingsService.Current.CloseMainWindowAction == CloseMainWindowAction.MinimizeToTray)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }

        if (!await ConfirmStopRecordingIfNeededAsync())
        {
            args.Cancel = true;
            return;
        }
    }

    private async Task<bool> ConfirmStopRecordingIfNeededAsync()
    {
        var recordingService = Ioc.Default.GetRequiredService<IRecordingService>();
        if (recordingService.State is not (RecordingState.Recording or RecordingState.Paused))
        {
            return true;
        }

        if (!await CloseRecordingDialog.ConfirmStopAndSaveAsync())
        {
            return false;
        }

        _closingAfterStop = true;
        try
        {
            await recordingService.StopAsync();
            return true;
        }
        finally
        {
            _closingAfterStop = false;
        }
    }

    private void RegisterGlobalHotkeys()
    {
        var captureViewModel = Ioc.Default.GetRequiredService<CaptureViewModel>();
        var settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
        _globalHotkeyService = Ioc.Default.GetRequiredService<IGlobalHotkeyService>();
        _globalHotkeyService.Attach(
            WindowNative.GetWindowHandle(this),
            DispatcherQueue,
            () => captureViewModel.ToggleRecordingCommand.Execute(null),
            () => captureViewModel.TogglePauseCommand.Execute(null));

        if (_globalHotkeyService.Apply(
                settingsService.Current.HotkeyToggleRecording,
                settingsService.Current.HotkeyTogglePause))
        {
            Log.Info("Global hotkeys applied from settings.");
        }
    }

    private void RegisterNavElements()
    {
        _navMarks["Capture"] = NavCaptureMark;
        _navMarks["Recordings"] = NavRecordingsMark;
        _navMarks["Settings"] = NavSettingsMark;
        _navMarks["About"] = NavAboutMark;

        _navLabels["Capture"] = NavCaptureLabel;
        _navLabels["Recordings"] = NavRecordingsLabel;
        _navLabels["Settings"] = NavSettingsLabel;
        _navLabels["About"] = NavAboutLabel;

        _navButtons["Capture"] = NavCaptureBtn;
        _navButtons["Recordings"] = NavRecordingsBtn;
        _navButtons["Settings"] = NavSettingsBtn;
        _navButtons["About"] = NavAboutBtn;
    }

    private void ConfigureWindowMetrics()
    {
        // AppWindow sizes are physical pixels, while XAML layout units are DIPs.
        // Scale the 1120x720 design metrics by the window DPI so the app renders
        // identically at any scale factor (e.g. 150% on a 4K display).
        var scale = GetDpiScale();
        var widthPx = (int)Math.Round(LayoutMetrics.AppWidth * scale);
        var heightPx = (int)Math.Round(LayoutMetrics.AppHeight * scale);

        AppWindow.Resize(new SizeInt32(widthPx, heightPx));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.PreferredMinimumWidth = widthPx;
            presenter.PreferredMinimumHeight = heightPx;
        }
    }

    private double GetDpiScale()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        return GetDpiForWindow(hwnd) / 96.0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            NavigateTo(tag);
        }
    }

    private void NavigateTo(string tag, bool force = false)
    {
        if (!force && tag == _currentNav)
        {
            return;
        }

        _currentNav = tag;
        UpdateNavSelection();

        Type pageType = tag switch
        {
            "Capture" => typeof(CapturePage),
            "Recordings" => typeof(RecordingsPage),
            "Settings" => typeof(SettingsPage),
            "About" => typeof(AboutPage),
            _ => typeof(CapturePage)
        };

        if (force || ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void UpdateNavSelection()
    {
        var accent = (Brush)Application.Current.Resources["DawnAccentBrush"];
        var textBrush = (Brush)Application.Current.Resources["DawnTextBrush"];
        var mutedBrush = (Brush)Application.Current.Resources["DawnTextMutedBrush"];
        var hoverBrush = (Brush)Application.Current.Resources["DawnSurfaceHoverBrush"];
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        foreach (var pair in _navMarks)
        {
            bool selected = pair.Key == _currentNav;
            pair.Value.Background = selected ? accent : transparent;

            if (_navLabels.TryGetValue(pair.Key, out TextBlock? label))
            {
                label.Foreground = selected ? textBrush : mutedBrush;
            }

            if (_navButtons.TryGetValue(pair.Key, out Button? button))
            {
                button.Background = selected ? hoverBrush : transparent;
            }
        }
    }

    private void UpdateStorageAvailabilityBrush()
    {
        var faint = (Brush)Application.Current.Resources["DawnTextFaintBrush"];
        var danger = (Brush)Application.Current.Resources["DawnDangerBrush"];
        StorageAvailableLabel.Foreground = ViewModel.IsStorageLow ? danger : faint;
    }

    private void StorageFooter_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.Content is CapturePage capturePage)
        {
            capturePage.ShowFolderFlyout();
        }
    }

    private void UpdateCaptureNoticeBar()
    {
        string? message = ViewModel.CaptureNoticeText;
        bool open = !string.IsNullOrEmpty(message);
        CaptureNoticeBar.IsOpen = open;
        if (!open)
        {
            return;
        }

        CaptureNoticeBar.Message = message;
        CaptureNoticeBar.Severity = ViewModel.CaptureNoticeSeverity switch
        {
            CaptureNoticeSeverity.Error => InfoBarSeverity.Error,
            CaptureNoticeSeverity.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Informational
        };
    }

    private void CaptureNoticeBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.ClearCaptureNotice();
    }
}
