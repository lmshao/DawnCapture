using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace DawnCapture.Views;

public sealed partial class CapturePage : Page
{
    private readonly Dictionary<CaptureModeKind, Border> _modeMarks = new();
    private readonly Dictionary<CaptureModeKind, Button> _modeButtons = new();

    public CaptureViewModel ViewModel { get; }

    public CapturePage()
    {
        ViewModel = Ioc.Default.GetRequiredService<CaptureViewModel>();
        InitializeComponent();
        Workspace.SizeChanged += Workspace_SizeChanged;
        BuildModeButtons();
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
        ViewModel.Monitors.CollectionChanged += OnMonitorsCollectionChanged;
        UpdateModeSelection();
    }

    private void OnMonitorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _monitorSpatialCardCount = -1;
        if (PreviewColumn.ActualWidth > 0)
        {
            UpdatePreviewLayout(Workspace.ActualHeight);
        }
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

        UpdateMonitorSpatialLayout(contentWidth, contentHeight);
    }

    private int _monitorSpatialCardCount = -1;

    /// <summary>
    /// Places monitor pickers on a canvas using Win32 virtual-desktop coordinates
    /// (left/right/up/down relative layout), scaled to fit the preview frame.
    /// </summary>
    private void UpdateMonitorSpatialLayout(double contentWidth, double contentHeight)
    {
        if (ViewModel.Monitors.Count == 0 || contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        if (_monitorSpatialCardCount != ViewModel.Monitors.Count)
        {
            RebuildMonitorSpatialCanvas();
        }

        var desktop = MonitorLayoutHelper.GetDesktopBounds(ViewModel.Monitors);
        MonitorSpatialCanvas.Width = desktop.Width;
        MonitorSpatialCanvas.Height = desktop.Height;
        MonitorDesktopViewbox.MaxWidth = contentWidth;
        MonitorDesktopViewbox.MaxHeight = contentHeight;
    }

    private void RebuildMonitorSpatialCanvas()
    {
        MonitorSpatialCanvas.Children.Clear();
        var desktop = MonitorLayoutHelper.GetDesktopBounds(ViewModel.Monitors);
        var edgeStroke = MonitorLayoutHelper.GetPreviewEdgeStrokeThickness(desktop);
        var monitorOutline = GetMonitorPreviewOutlineBrush();

        foreach (var monitor in ViewModel.Monitors)
        {
            var (left, top) = MonitorLayoutHelper.GetCanvasPosition(monitor, desktop);

            var thumbnailHost = new Border
            {
                Background = (Brush)Application.Current.Resources["DawnSurfaceMutedBrush"],
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var thumbnail = new Image { Stretch = Stretch.UniformToFill };
            thumbnail.SetBinding(Image.SourceProperty, new Binding
            {
                Source = monitor,
                Path = new PropertyPath(nameof(MonitorDisplay.Thumbnail)),
                Mode = BindingMode.OneWay
            });
            thumbnailHost.Child = thumbnail;

            var nameText = new TextBlock
            {
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Text = monitor.Name,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var detailText = new TextBlock
            {
                Margin = new Thickness(0, 3, 0, 0),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["DawnTextFaintBrush"],
                Text = monitor.Detail,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var labelPanel = new StackPanel { Spacing = 0 };
            labelPanel.Children.Add(nameText);
            labelPanel.Children.Add(detailText);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(thumbnailHost, 0);
            Grid.SetRow(labelPanel, 1);
            content.Children.Add(thumbnailHost);
            content.Children.Add(labelPanel);

            var button = new Button
            {
                Padding = new Thickness(8, 6, 8, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Background = GetThemeBrush("LayerFillColorDefaultBrush", "DawnSurfaceMutedBrush"),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Content = content,
                Command = ViewModel.SelectDisplayCommand,
                CommandParameter = monitor
            };

            var frame = new Border
            {
                Width = monitor.Width,
                Height = monitor.Height,
                Background = GetThemeBrush("CardBackgroundFillColorDefaultBrush", "LayerFillColorDefaultBrush"),
                BorderBrush = monitorOutline,
                BorderThickness = new Thickness(edgeStroke),
                CornerRadius = new CornerRadius(0),
                Child = button
            };

            Canvas.SetLeft(frame, left);
            Canvas.SetTop(frame, top);
            MonitorSpatialCanvas.Children.Add(frame);
        }

        _monitorSpatialCardCount = ViewModel.Monitors.Count;
    }

    private static Brush GetThemeBrush(string primaryKey, string fallbackKey)
    {
        if (Application.Current.Resources.TryGetValue(primaryKey, out object? primary) && primary is Brush primaryBrush)
        {
            return primaryBrush;
        }

        return Application.Current.Resources[fallbackKey] as Brush
            ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private static Brush GetMonitorPreviewOutlineBrush()
    {
        ReadOnlySpan<string> keys =
        [
            "ControlStrongStrokeColorDefaultBrush",
            "CardStrokeColorDefaultBrush",
            "TextFillColorSecondaryBrush",
            "ControlStrokeColorDefaultBrush"
        ];

        foreach (var key in keys)
        {
            if (Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush)
            {
                return brush;
            }
        }

        return GetThemeBrush("DawnTextMutedBrush", "DawnStrokeStrongBrush");
    }

}
