using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace DawnCapture.Views;

public sealed partial class CapturePage : Page
{
    private readonly Dictionary<CaptureModeKind, Border> _modeMarks = new();
    private readonly Dictionary<CaptureModeKind, Button> _modeButtons = new();

    public CaptureViewModel ViewModel { get; }

    public MainViewModel MainViewModel { get; }

    public CapturePage()
    {
        ViewModel = Ioc.Default.GetRequiredService<CaptureViewModel>();
        MainViewModel = Ioc.Default.GetRequiredService<MainViewModel>();
        InitializeComponent();
        Workspace.SizeChanged += Workspace_SizeChanged;
        BuildModeButtons();
        MainViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CaptureNoticeText)
                or nameof(MainViewModel.CaptureNoticeSeverity))
            {
                UpdateCaptureNoticeBar();
            }
        };
        UpdateCaptureNoticeBar();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CaptureViewModel.SelectedMode))
            {
                UpdateModeSelection();
            }
            else if (e.PropertyName == nameof(CaptureViewModel.RecordButtonLabel) &&
                     string.IsNullOrEmpty(ViewModel.RecordButtonLabel))
            {
                ViewModel.RecordButtonLabel = LocalizationService.GetString("Dock_StartRecording");
            }
        };

        ViewModel.RecordButtonLabel = LocalizationService.GetString("Dock_StartRecording");
        UpdateModeSelection();
    }

    private void BuildModeButtons()
    {
        AddModeButton(CaptureModeKind.FullScreen, "\uE740",
            LocalizationService.GetString("Capture_Mode_FullScreen"),
            LocalizationService.GetString("Capture_Mode_FullScreen_Desc"));
        AddModeButton(CaptureModeKind.Window, "\uEB9F",
            LocalizationService.GetString("Capture_Mode_Window"),
            LocalizationService.GetString("Capture_Mode_Window_Desc"));
        AddModeButton(CaptureModeKind.Region, "\uE7C3",
            LocalizationService.GetString("Capture_Mode_Region"),
            LocalizationService.GetString("Capture_Mode_Region_Desc"));
        AddModeButton(CaptureModeKind.AudioOnly, "\uE720",
            LocalizationService.GetString("Capture_Mode_Audio"),
            LocalizationService.GetString("Capture_Mode_Audio_Desc"));
    }

    private void AddModeButton(CaptureModeKind mode, string glyph, string title, string description)
    {
        var mark = new Border
        {
            Width = 3,
            Height = 32,
            CornerRadius = new CornerRadius(0, 3, 3, 0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["DawnTextMutedBrush"]
        };

        var iconHost = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            BorderBrush = (Brush)Application.Current.Resources["DawnStrokeBrush"],
            BorderThickness = new Thickness(1),
            Child = icon
        };

        var titleBlock = new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 };
        var descBlock = new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["DawnTextFaintBrush"]
        };

        var textPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(titleBlock);
        textPanel.Children.Add(descBlock);

        var chevron = new FontIcon
        {
            Glyph = "\uE76C",
            FontSize = 18,
            Foreground = (Brush)Application.Current.Resources["DawnTextFaintBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid
        {
            ColumnSpacing = 9,
            Padding = new Thickness(0, 5, 0, 5),
            MinHeight = 58
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

        Grid.SetColumn(mark, 0);
        Grid.SetColumn(iconHost, 1);
        Grid.SetColumn(textPanel, 2);
        Grid.SetColumn(chevron, 3);

        grid.Children.Add(mark);
        grid.Children.Add(iconHost);
        grid.Children.Add(textPanel);
        grid.Children.Add(chevron);

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 58,
            Content = grid
        };

        button.Click += (_, _) => ViewModel.SelectModeCommand.Execute(mode);

        _modeMarks[mode] = mark;
        _modeButtons[mode] = button;
        ModeButtonsPanel.Children.Add(button);
    }

    private void UpdateModeSelection()
    {
        var accent = (Brush)Application.Current.Resources["DawnAccentBrush"];
        var hover = (Brush)Application.Current.Resources["DawnSurfaceHoverBrush"];
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        foreach (var pair in _modeMarks)
        {
            bool selected = pair.Key == ViewModel.SelectedMode;
            pair.Value.Background = selected ? accent : transparent;
            _modeButtons[pair.Key].Background = selected ? hover : transparent;
        }
    }

    public void ShowFolderFlyout() => FolderFlyout.ShowAt(SaveToButton);

    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Height > 0)
        {
            UpdatePreviewLayout(e.NewSize.Height);
        }
    }

    /// <summary>
    /// All capture modes share one preview footprint: the outline is always
    /// an exact 16:9 frame (matching full-screen capture). The frame is
    /// sized with "contain" semantics — constrained by both the available
    /// width and height, so stretching the window in either direction never
    /// clips it; surplus space becomes centered letterbox.
    /// Audio-only mode reuses the same frame with the waveform centered.
    /// </summary>
    private void UpdatePreviewLayout(double workspaceHeight)
    {
        double width = PreviewColumn.ActualWidth;
        if (width <= 0 || double.IsNaN(width))
        {
            return;
        }

        // Vertical budget around the content area (PreviewCard):
        // card padding top/bottom 2×2 + card border 1×1 + header 20 + caption 22 + row spacings 2×2.
        const double cardChrome = 2 + 2 + 1 + 1 + 20 + 22 + 2 + 2;
        // Horizontal budget: card padding left/right 8×2 + card border 1×1
        // + outline padding 2×2 + outline border 1×1.
        const double outlineChrome = 8 + 8 + 1 + 1 + 2 + 2 + 1 + 1;
        const double outlinePadBorder = 2 + 2 + 1 + 1;

        // Fallback for the footnote row before it has been laid out:
        // FontSize 10 single line plus its top margin.
        double footnoteHeight = OutputFootnote.ActualHeight;
        if (double.IsNaN(footnoteHeight) || footnoteHeight <= 0)
        {
            footnoteHeight = 20;
        }

        double maxContentWidth = Math.Max(width - outlineChrome, 0);
        double maxContentHeight = Math.Max(
            workspaceHeight - footnoteHeight - cardChrome - outlinePadBorder,
            0);

        // Contain: keep 16:9 but never exceed the available area in either axis.
        double contentWidth = Math.Min(maxContentWidth, maxContentHeight * 16.0 / 9.0);
        double contentHeight = contentWidth * 9.0 / 16.0;
        double outlineHeight = contentHeight + outlinePadBorder;

        CaptureOutline.Width = contentWidth + outlinePadBorder;
        CaptureOutline.Height = outlineHeight;
        CaptureOutline.HorizontalAlignment = HorizontalAlignment.Center;

        PreviewCard.Height = cardChrome + outlineHeight;
        PreviewCard.VerticalAlignment = VerticalAlignment.Center;
    }

    private void UpdateCaptureNoticeBar()
    {
        string? message = MainViewModel.CaptureNoticeText;
        bool open = !string.IsNullOrEmpty(message);
        CaptureNoticeBar.IsOpen = open;
        if (!open)
        {
            return;
        }

        CaptureNoticeBar.Message = message;
        CaptureNoticeBar.Severity = MainViewModel.CaptureNoticeSeverity switch
        {
            CaptureNoticeSeverity.Error => InfoBarSeverity.Error,
            CaptureNoticeSeverity.Warning => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Informational
        };
    }

    private void CaptureNoticeBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        MainViewModel.ClearCaptureNotice();
    }
}
