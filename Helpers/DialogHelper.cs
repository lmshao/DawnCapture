using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace DawnCapture.Helpers;

public static class DialogHelper
{
    public static async Task ShowErrorAsync(string message, string? title = null)
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title ?? LocalizationService.GetString("Recordings_ErrorTitle"),
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.WrapWholeWords
            },
            CloseButtonText = LocalizationService.GetString("Recordings_Ok")
        };

        await dialog.ShowAsync();
    }
}