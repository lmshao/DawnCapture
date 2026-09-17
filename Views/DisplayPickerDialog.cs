// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DawnCapture.Views;

public sealed class DisplayPickerDialog : ContentDialog
{
    private readonly StackPanel _optionsPanel = new() { Spacing = 8 };
    private readonly Dictionary<MonitorDisplay, Border> _optionCards = new();
    private MonitorDisplay? _selected;

    private DisplayPickerDialog()
    {
        PrimaryButtonClick += (_, args) =>
        {
            if (_selected == null)
            {
                args.Cancel = true;
            }
        };

        var description = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = (Brush)Application.Current.Resources["DawnTextMutedBrush"]
        };

        Content = new StackPanel
        {
            MinWidth = ContentDialogHelper.WideContentMinWidth,
            Spacing = 12,
            Children =
            {
                description,
                new ScrollViewer
                {
                    MaxHeight = 320,
                    Content = _optionsPanel
                }
            }
        };

        DescriptionBlock = description;
    }

    private TextBlock DescriptionBlock { get; }

    public static async Task<MonitorDisplay?> ShowAsync(
        IReadOnlyList<MonitorDisplay> monitors,
        MonitorDisplay? current)
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return null;
        }

        var dialog = new DisplayPickerDialog
        {
            XamlRoot = xamlRoot,
            Title = LocalizationService.GetString("DisplayDialog_Title"),
            PrimaryButtonText = LocalizationService.GetString("DisplayDialog_Use"),
            SecondaryButtonText = ContentDialogHelper.CancelText,
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.DescriptionBlock.Text = LocalizationService.GetString("DisplayDialog_Desc");
        dialog.BuildOptions(monitors, current);

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? dialog._selected : null;
    }

    private void BuildOptions(IReadOnlyList<MonitorDisplay> monitors, MonitorDisplay? current)
    {
        _optionsPanel.Children.Clear();
        _optionCards.Clear();

        foreach (var monitor in monitors)
        {
            var card = CreateOptionCard(monitor);
            _optionCards[monitor] = card;
            _optionsPanel.Children.Add(card);
        }

        if (current != null)
        {
            SelectOption(monitors.FirstOrDefault(m => m.Handle == current.Handle) ?? current);
        }
        else
        {
            IsPrimaryButtonEnabled = false;
        }
    }

    private Border CreateOptionCard(MonitorDisplay monitor)
    {
        var stroke = (Brush)Application.Current.Resources["DawnStrokeBrush"];
        var hover = (Brush)Application.Current.Resources["DawnSurfaceHoverBrush"];

        var image = new Image
        {
            Source = monitor.Thumbnail,
            Stretch = Stretch.UniformToFill
        };

        var thumb = new Border
        {
            Height = 54,
            Background = (Brush)Application.Current.Resources["DawnSurfaceMutedBrush"],
            BorderBrush = (Brush)Application.Current.Resources["DawnStrokeStrongBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = image
        };

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = monitor.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = monitor.Detail,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["DawnTextFaintBrush"]
                }
            }
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(thumb, 0);
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(thumb);
        grid.Children.Add(textPanel);

        var card = new Border
        {
            Padding = new Thickness(10),
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Tag = monitor,
            Child = grid
        };

        card.PointerEntered += (_, _) =>
        {
            if (!ReferenceEquals(_selected, monitor))
            {
                card.Background = hover;
            }
        };

        card.PointerExited += (_, _) =>
        {
            if (!ReferenceEquals(_selected, monitor))
            {
                card.Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"];
            }
        };

        card.Tapped += (_, _) => SelectOption(monitor);

        return card;
    }

    private void SelectOption(MonitorDisplay monitor)
    {
        _selected = monitor;
        IsPrimaryButtonEnabled = true;

        var accent = (Brush)Application.Current.Resources["DawnAccentBrush"];
        var defaultBg = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"];
        var stroke = (Brush)Application.Current.Resources["DawnStrokeBrush"];

        foreach (var pair in _optionCards)
        {
            bool selected = pair.Key.Handle == monitor.Handle;
            pair.Value.BorderBrush = selected ? accent : stroke;
            pair.Value.Background = selected
                ? (Brush)Application.Current.Resources["DawnSurfaceHoverBrush"]
                : defaultBg;
        }
    }
}
