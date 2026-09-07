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
                UpdateOutlineBrush();
            }
            else if (e.PropertyName == nameof(CaptureViewModel.HasSource) ||
                     e.PropertyName == nameof(CaptureViewModel.SelectedDisplay) ||
                     e.PropertyName == nameof(CaptureViewModel.SelectedWindow))
            {
                UpdateOutlineBrush();
            }
            else if (e.PropertyName == nameof(CaptureViewModel.RecordButtonLabel) &&
                     string.IsNullOrEmpty(ViewModel.RecordButtonLabel))
            {
                ViewModel.RecordButtonLabel = LocalizationService.GetString("Dock_StartRecording");
            }
        };

        ViewModel.RecordButtonLabel = LocalizationService.GetString("Dock_StartRecording");
        UpdateModeSelection();
        UpdateOutlineBrush();
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

    private void UpdateOutlineBrush()
    {
        var accent = (Brush)Application.Current.Resources["DawnAccentBrush"];
        var stroke = (Brush)Application.Current.Resources["DawnStrokeStrongBrush"];
        bool committed = ViewModel.SelectedMode != CaptureModeKind.AudioOnly && ViewModel.HasSource;
        CaptureOutline.BorderBrush = committed ? accent : stroke;
    }

    public void ShowFolderFlyout() => FolderFlyout.ShowAt(SaveToButton);

    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = PreviewColumn.ActualWidth;
        var height = Workspace.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var sixteenNineHeight = width * 9.0 / 16.0;

        // Preserve the designed layout at normal sizes. Once the window grows
        // substantially taller than the 1120x720 design (e.g. maximized on 4K),
        // cap the preview card to a strict 16:9 landscape aspect.
        const double maximizedThreshold = 560;
        if (height > maximizedThreshold && height > sixteenNineHeight)
        {
            PreviewCard.Height = sixteenNineHeight;
            PreviewCard.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            PreviewCard.Height = double.NaN;
            PreviewCard.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }
}
