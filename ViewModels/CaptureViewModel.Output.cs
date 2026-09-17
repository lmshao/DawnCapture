// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace DawnCapture.ViewModels;

public partial class CaptureViewModel
{
    [ObservableProperty]
    private bool _showCursor = true;

    [ObservableProperty]
    private string _destinationFolderSummary = string.Empty;

    [ObservableProperty]
    private string _presetLabel = string.Empty;

    [ObservableProperty]
    private string _audioQualityLabel = string.Empty;

    [ObservableProperty]
    private string _outputFootnoteSpecs = string.Empty;

    [ObservableProperty]
    private string _outputFootnoteHint = string.Empty;

    [ObservableProperty]
    private string _outputFootnoteTooltip = string.Empty;

    [ObservableProperty]
    private int _capturePresetIndex = 1;

    [ObservableProperty]
    private int _audioQualityIndex;

    partial void OnShowCursorChanged(bool value)
    {
        if (_suppressSettingsSave)
        {
            return;
        }

        if (_settingsService.Current.CaptureCursor == value)
        {
            return;
        }

        _settingsService.Current.CaptureCursor = value;
        _settingsService.Save();
    }

    partial void OnCapturePresetIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedPresetTierLabel));
        ApplyFlyoutSelection(PresetFlyout, value);

        if (_suppressSettingsSave)
        {
            return;
        }

        if (value >= 3)
        {
            return;
        }

        RecordingSettingsHelper.ApplyCaptureQualityPreset(_settingsService.Current, value);
        _settingsService.Save();
        UpdateOutputSummary();
    }

    [RelayCommand]
    private void SelectCapturePreset(int index)
    {
        if (index < 0 || index >= PresetOptions.Count)
        {
            return;
        }

        CapturePresetIndex = index;
    }

    [RelayCommand]
    private void SelectAudioQuality(int index)
    {
        if (index < 0 || index >= AudioQualityOptions.Count)
        {
            return;
        }

        AudioQualityIndex = index;
    }

    partial void OnAudioQualityIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedAudioQualityTierLabel));
        ApplyFlyoutSelection(AudioQualityFlyout, value);

        if (_suppressSettingsSave)
        {
            return;
        }

        _settingsService.Current.AudioQualityIndex = value;
        _settingsService.Save();
        UpdateOutputSummary();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) =>
        ApplyFromSettings(_settingsService.Current);

    private void ApplyFromSettings(AppSettings settings)
    {
        _suppressSettingsSave = true;
        try
        {
            ShowCursor = settings.CaptureCursor;
            CapturePresetIndex = RecordingSettingsHelper.ResolveCapturePresetIndex(settings);
            OnPropertyChanged(nameof(SelectedPresetTierLabel));
            AudioQualityIndex = settings.AudioQualityIndex;
            OnPropertyChanged(nameof(SelectedAudioQualityTierLabel));
            MicrophoneEnabled = settings.MicrophoneEnabled;
            SystemAudioEnabled = settings.SystemAudioEnabled;
            ApplyFlyoutSelection(PresetFlyout, CapturePresetIndex);
            ApplyFlyoutSelection(AudioQualityFlyout, AudioQualityIndex);
            UpdateOutputSummary();
        }
        finally
        {
            _suppressSettingsSave = false;
        }
    }

    private MenuFlyout CreatePresetFlyout()
    {
        var flyout = new MenuFlyout();
        for (int i = 0; i < PresetOptions.Count; i++)
        {
            int index = i;
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = PresetOptions[index].FullLabel,
                Command = SelectCapturePresetCommand,
                CommandParameter = index
            });
        }

        ApplyFlyoutSelection(flyout, CapturePresetIndex);
        return flyout;
    }

    private MenuFlyout CreateAudioQualityFlyout()
    {
        var flyout = new MenuFlyout();
        for (int i = 0; i < AudioQualityOptions.Count; i++)
        {
            int index = i;
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = AudioQualityOptions[index].FullLabel,
                Command = SelectAudioQualityCommand,
                CommandParameter = index
            });
        }

        ApplyFlyoutSelection(flyout, AudioQualityIndex);
        return flyout;
    }

    private static void ApplyFlyoutSelection(MenuFlyout flyout, int selectedIndex)
    {
        for (int i = 0; i < flyout.Items.Count; i++)
        {
            if (flyout.Items[i] is MenuFlyoutItem item)
            {
                item.Icon = i == selectedIndex ? CreateFlyoutCheckIcon() : null;
            }
        }
    }

    private static FontIcon CreateFlyoutCheckIcon() =>
        new() { Glyph = "\uE73E", FontSize = 12 };

    public IReadOnlyList<DockComboOption> PresetOptions { get; } =
    [
        new(
            LocalizationService.GetString("Preset_Tier_SmallerFiles"),
            LocalizationService.GetString("Preset_Option_SmallerFiles")),
        new(
            LocalizationService.GetString("Preset_Tier_Balanced"),
            LocalizationService.GetString("Preset_Option_Balanced")),
        new(
            LocalizationService.GetString("Preset_Tier_SmootherMotion"),
            LocalizationService.GetString("Preset_Option_SmootherMotion")),
        new(
            LocalizationService.GetString("Preset_Tier_Custom"),
            LocalizationService.GetString("Preset_Option_Custom"))
    ];

    public MenuFlyout PresetFlyout { get; }

    public string SelectedPresetTierLabel =>
        PresetOptions[Math.Clamp(CapturePresetIndex, 0, PresetOptions.Count - 1)].TierLabel;

    public IReadOnlyList<DockComboOption> AudioQualityOptions { get; } =
    [
        new(
            LocalizationService.GetString("AudioQuality_Tier_Standard"),
            LocalizationService.GetString("AudioQuality_Option_Standard")),
        new(
            LocalizationService.GetString("AudioQuality_Tier_High"),
            LocalizationService.GetString("AudioQuality_Option_High")),
        new(
            LocalizationService.GetString("AudioQuality_Tier_Best"),
            LocalizationService.GetString("AudioQuality_Option_Best"))
    ];

    public MenuFlyout AudioQualityFlyout { get; }

    public string SelectedAudioQualityTierLabel =>
        AudioQualityOptions[Math.Clamp(AudioQualityIndex, 0, AudioQualityOptions.Count - 1)].TierLabel;

    private void OnMainViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.OutputFolderSummary) or nameof(MainViewModel.OutputFolderFull))
        {
            DestinationFolderSummary = BuildDestinationFolderSummary(_mainViewModel.OutputFolderFull);
        }
    }

    private static string BuildDestinationFolderSummary(string folder) =>
        PathDisplayHelper.CompactDockFolderSummary(
            folder,
            OutputFolderHelper.DefaultPath,
            LocalizationService.GetString("OutputFolder_DefaultSummary"));

    private void UpdateOutputSummary()
    {
        var settings = _settingsService.Current;
        int captureIndex = RecordingSettingsHelper.ResolveCapturePresetIndex(settings);
        int audioKbps = RecordingAudioOptions.BitrateFromQualityIndex(settings.AudioQualityIndex);

        if (SelectedMode == CaptureModeKind.AudioOnly)
        {
            OutputFootnoteSpecs = RecordingOutputSummaryHelper.FormatAudioSpecs(audioKbps);
            OutputFootnoteHint = " · " + InlineAudioQualityHint(settings.AudioQualityIndex);
            OutputFootnoteTooltip = OutputFootnoteSpecs + OutputFootnoteHint;
            return;
        }

        string? resolution = GetCaptureResolution();
        if (string.IsNullOrEmpty(resolution))
        {
            OutputFootnoteSpecs = LocalizationService.GetString("Dock_Output_SelectSource");
            OutputFootnoteHint = string.Empty;
            OutputFootnoteTooltip = OutputFootnoteSpecs;
            return;
        }

        string codec = RecordingSettingsHelper.VideoCodecLabel(settings.VideoCodecIndex);
        (int Width, int Height)? dimensions = GetCaptureDimensions();
        int width = dimensions?.Width ?? RecordingSettingsHelper.ReferenceWidth;
        int height = dimensions?.Height ?? RecordingSettingsHelper.ReferenceHeight;
        int effectiveVideoKbps = RecordingSettingsHelper.ResolvePreviewVideoBitrateKbps(settings, width, height);
        bool approximateVideoBitrate = RecordingSettingsHelper.UsesApproximateVideoBitrate(settings);

        OutputFootnoteSpecs = RecordingOutputSummaryHelper.FormatVideoSpecs(
            codec,
            resolution,
            width: null,
            height: null,
            settings.FrameRate,
            effectiveVideoKbps,
            audioKbps,
            includeAudio: true,
            approximateVideoBitrate: approximateVideoBitrate);
        OutputFootnoteHint = " · " + InlinePresetHint(captureIndex);
        OutputFootnoteTooltip = OutputFootnoteSpecs + OutputFootnoteHint;
    }

    private string? GetCaptureResolution() => SelectedMode switch
    {
        CaptureModeKind.FullScreen => SelectedDisplay?.Resolution,
        CaptureModeKind.Window => SelectedWindow?.Resolution,
        CaptureModeKind.Region => SelectedRegion?.Resolution,
        _ => null
    };

    private (int Width, int Height)? GetCaptureDimensions() => SelectedMode switch
    {
        CaptureModeKind.FullScreen when SelectedDisplay is { } display =>
            (display.Width, display.Height),
        CaptureModeKind.Window when SelectedWindow is { } window =>
            (window.Width, window.Height),
        CaptureModeKind.Region when SelectedRegion is { } region =>
            (region.Width, region.Height),
        _ => null
    };

    private static string InlinePresetHint(int captureIndex)
    {
        string text = captureIndex switch
        {
            0 => LocalizationService.GetString("Preset_Friendly_SmallerFiles"),
            2 => LocalizationService.GetString("Preset_Friendly_SmootherMotion"),
            3 => LocalizationService.GetString("Preset_Friendly_Custom"),
            _ => LocalizationService.GetString("Preset_Friendly_Balanced")
        };

        return InlineHintPhrase(text);
    }

    private static string InlineAudioQualityHint(int audioQualityIndex)
    {
        string text = audioQualityIndex switch
        {
            0 => LocalizationService.GetString("AudioQuality_Option_Standard_Short"),
            2 => LocalizationService.GetString("AudioQuality_Option_Best_Short"),
            _ => LocalizationService.GetString("AudioQuality_Option_High_Short")
        };

        return InlineHintPhrase(text);
    }

    private static string InlineHintPhrase(string text) =>
        text.Replace(" · ", LocalizationService.GetString("Dock_Output_HintSeparator"), StringComparison.Ordinal);
}
