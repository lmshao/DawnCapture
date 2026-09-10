using System;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views;

/// <summary>
/// Asks what to do with an active recording when the main window is closing:
/// stop and save, or keep recording (which cancels the close).
/// The default button is the safest choice, so Enter never aborts a recording.
/// </summary>
public sealed class CloseRecordingDialog : ContentDialog
{
    private CloseRecordingDialog()
    {
    }

    /// <summary>
    /// Returns true when the user wants to stop the recording and close the
    /// window; false keeps the window open and the recording running.
    /// </summary>
    public static async Task<bool> ConfirmStopAndSaveAsync()
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return false;
        }

        var dialog = new CloseRecordingDialog
        {
            XamlRoot = xamlRoot,
            Title = LocalizationService.GetString("MainWindow_CloseRecordingTitle"),
            Content = new TextBlock
            {
                Text = LocalizationService.GetString("MainWindow_CloseRecordingMessage"),
                TextWrapping = TextWrapping.WrapWholeWords,
                MinWidth = ContentDialogHelper.StandardContentMinWidth
            },
            PrimaryButtonText = LocalizationService.GetString("MainWindow_CloseRecordingStopAndSave"),
            SecondaryButtonText = LocalizationService.GetString("MainWindow_CloseRecordingContinue"),
            CloseButtonText = ContentDialogHelper.CancelText,
            DefaultButton = ContentDialogButton.Secondary
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
