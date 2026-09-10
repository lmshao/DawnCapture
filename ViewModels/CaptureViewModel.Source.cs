using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.Views;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Capture;

namespace DawnCapture.ViewModels;

public partial class CaptureViewModel
{
    [ObservableProperty]
    private MonitorDisplay? _selectedDisplay;

    [ObservableProperty]
    private ImageSource? _selectedDisplayThumbnail;

    [ObservableProperty]
    private string _selectedDisplayBadge = string.Empty;

    [ObservableProperty]
    private bool _showDisplayPreview;

    [ObservableProperty]
    private CaptureModeKind _selectedMode = CaptureModeKind.FullScreen;

    [ObservableProperty]
    private string _eyebrow = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _sourceOptionsLabel = string.Empty;

    [ObservableProperty]
    private bool _showCursorOption = true;

    [ObservableProperty]
    private bool _showVideoPreview = true;

    [ObservableProperty]
    private bool _showAudioPreview;

    [ObservableProperty]
    private string _previewLabel = string.Empty;

    [ObservableProperty]
    private string _previewCaption = string.Empty;

    [ObservableProperty]
    private bool _showPreviewAction;

    [ObservableProperty]
    private string _previewActionLabel = string.Empty;

    [ObservableProperty]
    private bool _hasSource = true;

    [ObservableProperty]
    private bool _showMonitorGrid;

    [ObservableProperty]
    private bool _showEmptySource;

    [ObservableProperty]
    private bool _showRegionPreview;

    [ObservableProperty]
    private RegionCaptureTarget? _selectedRegion;

    [ObservableProperty]
    private ImageSource? _regionThumbnail;

    [ObservableProperty]
    private bool _isLoadingRegionThumbnail;

    [ObservableProperty]
    private string _regionBadge = string.Empty;

    [ObservableProperty]
    private bool _showWindowPreview;

    [ObservableProperty]
    private WindowCaptureTarget? _selectedWindow;

    [ObservableProperty]
    private ImageSource? _windowThumbnail;

    [ObservableProperty]
    private bool _isLoadingWindowThumbnail;

    [ObservableProperty]
    private string _windowBadge = string.Empty;

    [ObservableProperty]
    private string _emptySourceTitle = string.Empty;

    [ObservableProperty]
    private string _emptySourceDescription = string.Empty;

    [ObservableProperty]
    private string _emptySourceButton = string.Empty;

    [ObservableProperty]
    private bool _showVideoQuality = true;

    partial void OnSelectedDisplayChanged(MonitorDisplay? value)
    {
        _mainViewModel.ClearCaptureNotice();
        SelectedDisplayThumbnail = value?.Thumbnail;
        SelectedDisplayBadge = value?.BadgeLabel ?? string.Empty;
        SyncHasSource();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();
    }

    partial void OnSelectedModeChanged(CaptureModeKind value)
    {
        _mainViewModel.ClearCaptureNotice();
        if (value != CaptureModeKind.Window)
        {
            ClearSelectedWindow();
        }

        if (value != CaptureModeKind.Region)
        {
            ClearSelectedRegion();
        }

        ApplySelectedMode();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();
    }

    partial void OnSelectedRegionChanged(RegionCaptureTarget? value)
    {
        _mainViewModel.ClearCaptureNotice();
        RegionBadge = value?.Resolution ?? string.Empty;
        SyncHasSource();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();
    }

    partial void OnSelectedWindowChanged(WindowCaptureTarget? value)
    {
        _mainViewModel.ClearCaptureNotice();
        WindowBadge = value?.Resolution ?? string.Empty;
        SyncHasSource();
        UpdateHeaderCopy();
        UpdatePreviewCopy();
        UpdateOutputSummary();
    }

    private void ApplySelectedMode()
    {
        ShowAudioPreview = SelectedMode == CaptureModeKind.AudioOnly;
        ShowVideoPreview = SelectedMode != CaptureModeKind.AudioOnly;
        ShowCursorOption = SelectedMode != CaptureModeKind.AudioOnly;
        SourceOptionsLabel = SelectedMode == CaptureModeKind.AudioOnly
            ? LocalizationService.GetString("Capture_SourceLabel_Audio")
            : LocalizationService.GetString("Capture_SourceLabel_Video");
        ShowVideoQuality = SelectedMode != CaptureModeKind.AudioOnly;

        SyncHasSource();
        UpdateOutputSummary();
    }

    private void SyncHasSource()
    {
        HasSource = SelectedMode switch
        {
            CaptureModeKind.FullScreen => SelectedDisplay != null,
            CaptureModeKind.Window => SelectedWindow != null,
            CaptureModeKind.Region => SelectedRegion != null,
            CaptureModeKind.AudioOnly => true,
            _ => false
        };
    }

    private void CommitDisplay(MonitorDisplay display)
    {
        var monitor = Monitors.FirstOrDefault(m => m.Handle == display.Handle) ?? display;
        _monitorService.RefreshThumbnail(monitor);
        SelectedDisplay = monitor;
        SelectedDisplayThumbnail = monitor.Thumbnail;
        SelectedDisplayBadge = monitor.BadgeLabel;
        Log.Info($"Display committed: {monitor.Name} ({monitor.Resolution}), Handle=0x{monitor.Handle:X}");
    }

    private void InitializeMonitors()
    {
        Monitors.Clear();
        foreach (var monitor in _monitorService.GetMonitors())
        {
            Monitors.Add(monitor);
        }

        // Single monitor: skip the picker and go straight to the live-area preview.
        // Multiple monitors: start from the card grid so the user explicitly picks.
        SelectedDisplay = Monitors.Count == 1 ? Monitors[0] : null;
        Log.Info($"Monitors initialized: count={Monitors.Count}, autoSelected={SelectedDisplay?.Name ?? "none"}");
    }

    [RelayCommand]
    private void SelectDisplay(MonitorDisplay? display)
    {
        if (display == null)
        {
            return;
        }

        CommitDisplay(display);
    }

    [RelayCommand]
    private async Task PreviewActionAsync()
    {
        if (SelectedMode == CaptureModeKind.Window)
        {
            await PickWindowAsync();
            return;
        }

        if (SelectedMode == CaptureModeKind.Region)
        {
            await PickRegionAsync();
            return;
        }

        if (SelectedMode != CaptureModeKind.FullScreen || Monitors.Count <= 1)
        {
            return;
        }

        foreach (var monitor in Monitors)
        {
            _monitorService.RefreshThumbnail(monitor);
        }

        var picked = await DisplayPickerDialog.ShowAsync(Monitors.ToList(), SelectedDisplay);

        if (picked != null)
        {
            CommitDisplay(picked);
        }
    }

    [RelayCommand]
    private async Task ChooseSourceAsync()
    {
        if (SelectedMode == CaptureModeKind.Window)
        {
            await PickWindowAsync();
            return;
        }

        if (SelectedMode == CaptureModeKind.Region)
        {
            await PickRegionAsync();
        }
    }

    private async Task PickRegionAsync()
    {
        var region = await RegionPickerHelper.PickRegionAsync();

        if (region is null)
        {
            return;
        }

        await CommitRegionAsync(region.Value);
    }

    private async Task CommitRegionAsync(RectInt32 screenBounds)
    {
        ClearSelectedRegion();

        var normalized = RegionBoundsHelper.NormalizeForEncoding(screenBounds);
        if (normalized.Width != screenBounds.Width || normalized.Height != screenBounds.Height)
        {
            Log.Info(
                $"Region normalized for encoding: {screenBounds.Width}x{screenBounds.Height} -> {normalized.Width}x{normalized.Height}");
        }

        var target = new RegionCaptureTarget(normalized);
        SelectedRegion = target;
        Log.Info($"Region committed: {target.Summary}");

        _regionThumbnailCts?.Cancel();
        _regionThumbnailCts?.Dispose();
        _regionThumbnailCts = new CancellationTokenSource();
        var cts = _regionThumbnailCts;
        IsLoadingRegionThumbnail = true;
        RegionThumbnail = null;

        try
        {
            var thumbnail = await RegionPreviewHelper.CaptureThumbnailAsync(normalized, cancellationToken: cts.Token);
            if (!cts.IsCancellationRequested && ReferenceEquals(SelectedRegion, target))
            {
                RegionThumbnail = thumbnail;
                if (thumbnail is null)
                {
                    Log.Info($"Region preview thumbnail unavailable for {target.Summary}.");
                }
            }
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsLoadingRegionThumbnail = false;
            }
        }
    }

    private void ClearSelectedRegion()
    {
        _regionThumbnailCts?.Cancel();
        _regionThumbnailCts?.Dispose();
        _regionThumbnailCts = null;

        SelectedRegion = null;
        RegionThumbnail = null;
        RegionBadge = string.Empty;
        IsLoadingRegionThumbnail = false;
        SyncHasSource();
    }

    private async Task PickWindowAsync()
    {
        var item = await WindowCaptureHelper.PickWindowAsync();

        if (item is null)
        {
            return;
        }

        await CommitWindowAsync(item);
    }

    private async Task CommitWindowAsync(GraphicsCaptureItem item)
    {
        ClearSelectedWindow();

        var target = new WindowCaptureTarget(item);
        target.Closed += OnSelectedWindowClosed;
        SelectedWindow = target;
        Log.Info($"Window committed: {target.DisplayName} ({target.Resolution})");

        _windowThumbnailCts?.Cancel();
        _windowThumbnailCts?.Dispose();
        _windowThumbnailCts = new CancellationTokenSource();
        var cts = _windowThumbnailCts;
        IsLoadingWindowThumbnail = true;
        WindowThumbnail = null;

        try
        {
            var thumbnail = await WindowPreviewHelper.CaptureThumbnailAsync(item, cancellationToken: cts.Token);
            if (!cts.IsCancellationRequested && ReferenceEquals(SelectedWindow, target))
            {
                WindowThumbnail = thumbnail;
                if (thumbnail is null)
                {
                    Log.Info($"Window preview thumbnail unavailable for {target.DisplayName}.");
                }
            }
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsLoadingWindowThumbnail = false;
            }
        }
    }

    private void OnSelectedWindowClosed(object? sender, EventArgs e)
    {
        Log.Info("Selected window closed.");
        ClearSelectedWindow();
        ShowCaptureNotice(
            LocalizationService.GetString("AppStatus_WindowClosed"),
            CaptureNoticeSeverity.Warning);
        UpdateHeaderCopy();
        UpdatePreviewCopy();
    }

    private void ClearSelectedWindow()
    {
        _windowThumbnailCts?.Cancel();
        _windowThumbnailCts?.Dispose();
        _windowThumbnailCts = null;

        if (SelectedWindow is not null)
        {
            SelectedWindow.Closed -= OnSelectedWindowClosed;
            SelectedWindow.Dispose();
        }

        SelectedWindow = null;
        WindowThumbnail = null;
        WindowBadge = string.Empty;
        IsLoadingWindowThumbnail = false;
        SyncHasSource();
    }

    private void UpdateHeaderCopy()
    {
        if (IsRecording)
        {
            Eyebrow = LocalizationService.GetString("Capture_Eyebrow_Recording");
            Title = LocalizationService.GetString("Capture_Title_Recording");
            Description = LocalizationService.GetString("Capture_Desc_Recording");
            return;
        }

        Eyebrow = HasSource || SelectedMode == CaptureModeKind.AudioOnly
            ? LocalizationService.GetString("Capture_Eyebrow_Ready")
            : LocalizationService.GetString("Capture_Eyebrow_SourceRequired");
        Title = HasSource || SelectedMode == CaptureModeKind.AudioOnly
            ? LocalizationService.GetString("Capture_Title_SourceReady")
            : LocalizationService.GetString("Capture_Title_Default");
        Description = HasSource || SelectedMode == CaptureModeKind.AudioOnly
            ? LocalizationService.GetString("Capture_Desc_Default")
            : LocalizationService.GetString("Capture_Desc_NoSource");
    }

    private void UpdatePreviewCopy()
    {
        ControllerSource = SelectedMode switch
        {
            CaptureModeKind.AudioOnly => LocalizationService.GetString("Capture_Source_AudioOnly"),
            CaptureModeKind.Region when HasSource => SelectedRegion?.Summary ?? LocalizationService.GetString("Capture_Preview_RegionPlaceholder"),
            CaptureModeKind.Window when HasSource => $"{SelectedWindow?.DisplayName} / {SelectedWindow?.Resolution}",
            CaptureModeKind.FullScreen when HasSource => $"{SelectedDisplay?.Name} / {SelectedDisplay?.Resolution}",
            _ => LocalizationService.GetString("Capture_Source_None")
        };

        if (SelectedMode == CaptureModeKind.AudioOnly)
        {
            PreviewLabel = BuildAudioSourceSummary();
            PreviewCaption = IsRecording
                ? IsPaused
                    ? LocalizationService.GetString("Status_Paused")
                    : LocalizationService.GetString("Capture_Preview_AudioRecording")
                : LocalizationService.GetString("Capture_Preview_AudioLevels");
            ShowPreviewAction = false;
            ShowMonitorGrid = false;
            ShowDisplayPreview = false;
            ShowEmptySource = false;
            ShowRegionPreview = false;
            ShowWindowPreview = false;
            UpdateAudioStatusBadges();
            return;
        }

        ShowMonitorGrid = SelectedMode == CaptureModeKind.FullScreen && !HasSource;
        ShowDisplayPreview = SelectedMode == CaptureModeKind.FullScreen && HasSource;
        ShowEmptySource = (SelectedMode == CaptureModeKind.Window || SelectedMode == CaptureModeKind.Region) && !HasSource;
        ShowRegionPreview = SelectedMode == CaptureModeKind.Region && HasSource;
        ShowWindowPreview = SelectedMode == CaptureModeKind.Window && HasSource;

        if (SelectedMode == CaptureModeKind.FullScreen)
        {
            PreviewLabel = HasSource
                ? $"{SelectedDisplay?.Name} / {SelectedDisplay?.Resolution}"
                : LocalizationService.GetString("Capture_Preview_ChooseDisplay");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_FullScreenCaption")
                : LocalizationService.GetString("Capture_Preview_SelectDisplayHint");
            ShowPreviewAction = HasSource && Monitors.Count > 1;
            PreviewActionLabel = LocalizationService.GetString("Capture_Action_ChangeDisplay");
        }
        else if (SelectedMode == CaptureModeKind.Window)
        {
            PreviewLabel = HasSource
                ? $"{SelectedWindow?.DisplayName} / {SelectedWindow?.Resolution}"
                : LocalizationService.GetString("Capture_Preview_NoWindow");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_WindowCaption")
                : LocalizationService.GetString("Capture_Preview_WindowPickerHint");
            if (IsRecording)
            {
                PreviewCaption = LocalizationService.GetString("Capture_Preview_WindowRecordingHint");
            }
            ShowPreviewAction = HasSource;
            PreviewActionLabel = LocalizationService.GetString("Capture_Action_ChangeWindow");
            EmptySourceTitle = LocalizationService.GetString("Capture_EmptyWindow_Title");
            EmptySourceDescription = LocalizationService.GetString("Capture_EmptyWindow_Desc");
            EmptySourceButton = LocalizationService.GetString("Capture_Action_ChooseWindow");
        }
        else if (SelectedMode == CaptureModeKind.Region)
        {
            PreviewLabel = HasSource
                ? string.Format(
                    LocalizationService.GetString("Capture_Preview_RegionDetailFormat"),
                    SelectedRegion?.Resolution,
                    SelectedRegion?.ScreenBounds.X,
                    SelectedRegion?.ScreenBounds.Y)
                : LocalizationService.GetString("Capture_Preview_NoRegion");
            PreviewCaption = HasSource
                ? LocalizationService.GetString("Capture_Preview_RegionCaption")
                : LocalizationService.GetString("Capture_Preview_SelectRegionFirst");
            if (IsRecording)
            {
                PreviewCaption = LocalizationService.GetString("Capture_Preview_RegionRecordingHint");
            }
            ShowPreviewAction = HasSource;
            PreviewActionLabel = LocalizationService.GetString("Capture_Action_EditRegion");
            EmptySourceTitle = LocalizationService.GetString("Capture_EmptyRegion_Title");
            EmptySourceDescription = LocalizationService.GetString("Capture_EmptyRegion_Desc");
            EmptySourceButton = LocalizationService.GetString("Capture_Action_SelectRegion");
        }
    }
}
