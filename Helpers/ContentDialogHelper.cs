// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DawnCapture.Helpers;

public static class ContentDialogHelper
{
    public const double StandardContentMinWidth = 320;

    public const double WideContentMinWidth = 360;

    public static string CancelText => LocalizationService.GetString("Common_Cancel");

    public static void WireDangerPrimary(ContentDialog dialog)
    {
        dialog.Loaded += (_, _) => TintPrimaryButtonAsDanger(dialog);
    }

    public static void TintPrimaryButtonAsDanger(ContentDialog dialog)
    {
        if (VisualTreeHelper.GetChild(dialog, 0) is not FrameworkElement root ||
            root.FindName("PrimaryButton") is not Button primary)
        {
            return;
        }

        primary.Background = (Brush)Application.Current.Resources["DawnDangerBrush"];
        primary.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
    }
}
