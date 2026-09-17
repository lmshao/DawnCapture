// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;

namespace DawnCapture.Views;

/// <summary>
/// Confirms deletion of a recording file; the file is moved to the Recycle Bin.
/// The default button is Cancel so Enter never deletes by accident; the
/// primary button is tinted with the danger color once the dialog opens.
/// </summary>
public sealed class DeleteRecordingDialog : ContentDialog
{
    private DeleteRecordingDialog()
    {
    }

    public static async Task<bool> ConfirmAsync(RecordingListItem item)
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return false;
        }

        var message = new TextBlock
        {
            Text = string.Format(
                LocalizationService.GetString("Recordings_DeleteMessage"),
                item.Name),
            TextWrapping = TextWrapping.WrapWholeWords,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        var restoreHint = new TextBlock
        {
            Text = LocalizationService.GetString("Recordings_DeleteRestoreHint"),
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = (Brush)Application.Current.Resources["DawnTextMutedBrush"]
        };
        var size = new TextBlock
        {
            Text = string.Format(
                LocalizationService.GetString("Recordings_DeleteSize"),
                item.SizeLabel),
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = (Brush)Application.Current.Resources["DawnTextFaintBrush"],
            FontSize = 12
        };

        var dialog = new DeleteRecordingDialog
        {
            XamlRoot = xamlRoot,
            Title = LocalizationService.GetString("Recordings_DeleteTitle"),
            Content = new StackPanel
            {
                MinWidth = ContentDialogHelper.StandardContentMinWidth,
                Spacing = 6,
                Children =
                    {
                        message,
                        restoreHint,
                        size
                    }
            },
            PrimaryButtonText = LocalizationService.GetString("Recordings_DeletePrimary"),
            SecondaryButtonText = ContentDialogHelper.CancelText,
            DefaultButton = ContentDialogButton.Secondary
        };

        ContentDialogHelper.WireDangerPrimary(dialog);

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
