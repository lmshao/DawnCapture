using DawnCapture.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI;

namespace DawnCapture.Helpers;

public static class DialogHelper
{
    private const double CardMaxWidth = ContentDialogHelper.StandardContentMinWidth;
    private static readonly SemaphoreSlim DialogGate = new(1, 1);

    public static Task ShowErrorAsync(string message, string? title = null) =>
        ShowCompactDialogAsync(
            message,
            title ?? LocalizationService.GetString("Recordings_ErrorTitle"),
            showCancel: false,
            confirmText: LocalizationService.GetString("Recordings_Ok"));

    public static async Task<bool> ShowConfirmAsync(
        string message,
        string? title = null,
        string? confirmText = null,
        string? cancelText = null)
    {
        bool? result = await ShowCompactDialogAsync(
            message,
            title ?? LocalizationService.GetString("Recordings_ErrorTitle"),
            showCancel: true,
            confirmText: confirmText ?? LocalizationService.GetString("Recordings_Ok"),
            cancelText: cancelText ?? ContentDialogHelper.CancelText);

        return result == true;
    }

    private static async Task<bool?> ShowCompactDialogAsync(
        string message,
        string title,
        bool showCancel,
        string confirmText,
        string? cancelText = null)
    {
        if (App.MainWindow?.Content is not FrameworkElement root || root.XamlRoot is not { } xamlRoot)
        {
            return showCancel ? false : null;
        }

        await DialogGate.WaitAsync();
        try
        {
            return await ShowCompactDialogCoreAsync(
                root,
                xamlRoot,
                message,
                title,
                showCancel,
                confirmText,
                cancelText);
        }
        finally
        {
            DialogGate.Release();
        }
    }

    private static async Task<bool?> ShowCompactDialogCoreAsync(
        FrameworkElement root,
        XamlRoot xamlRoot,
        string message,
        string title,
        bool showCancel,
        string confirmText,
        string? cancelText)
    {
        var popup = new Popup
        {
            XamlRoot = xamlRoot,
            ShouldConstrainToRootBounds = true
        };

        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsTabStop = true
        };

        var card = new Border
        {
            Background = GetThemeBrush("LayerFillColorDefaultBrush", "DawnSurfaceMutedBrush"),
            BorderBrush = GetThemeBrush("ControlElevationBorderBrush", "DawnStrokeBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 18, 20, 16),
            MinWidth = CardMaxWidth,
            MaxWidth = CardMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var stack = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush", "DawnTextBrush")
        });

        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "DawnTextMutedBrush")
        });

        var tcs = new TaskCompletionSource<bool?>();
        var completed = false;

        void Complete(bool? result)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            popup.IsOpen = false;
            tcs.TrySetResult(result);
        }

        if (showCancel)
        {
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var cancelButton = CreateDialogButton(cancelText ?? ContentDialogHelper.CancelText);
            cancelButton.Click += (_, _) => Complete(false);
            buttonRow.Children.Add(cancelButton);

            var confirmButton = CreateDialogButton(confirmText, isPrimary: true);
            confirmButton.Click += (_, _) => Complete(true);
            buttonRow.Children.Add(confirmButton);

            stack.Children.Add(buttonRow);
        }
        else
        {
            var okButton = CreateDialogButton(confirmText, isPrimary: true);
            okButton.Margin = new Thickness(0, 4, 0, 0);
            okButton.Click += (_, _) => Complete(true);
            stack.Children.Add(okButton);
        }

        card.Child = stack;
        overlay.Children.Add(card);

        void UpdateOverlaySize(object? sender, SizeChangedEventArgs e)
        {
            overlay.Width = e.NewSize.Width;
            overlay.Height = e.NewSize.Height;
        }

        overlay.Width = root.ActualWidth;
        overlay.Height = root.ActualHeight;
        root.SizeChanged += UpdateOverlaySize;

        overlay.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, overlay))
            {
                Complete(showCancel ? false : true);
            }
        };

        overlay.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                Complete(showCancel ? false : true);
                e.Handled = true;
            }
        };

        popup.Closed += (_, _) =>
        {
            root.SizeChanged -= UpdateOverlaySize;
            Complete(showCancel ? false : true);
        };

        popup.Child = overlay;
        popup.IsOpen = true;
        overlay.Focus(FocusState.Programmatic);

        return await tcs.Task;
    }

    private static Button CreateDialogButton(string label, bool isPrimary = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 88,
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        if (isPrimary &&
            Application.Current.Resources.TryGetValue("AccentButtonStyle", out object? style) &&
            style is Style accentStyle)
        {
            button.Style = accentStyle;
        }

        return button;
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
}
