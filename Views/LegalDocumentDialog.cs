using System;
using System.Threading.Tasks;
using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views;

/// <summary>
/// Shows a shipped legal document (third-party notices, privacy policy)
/// inside the app instead of launching an external program, so the content
/// is readable on every machine regardless of file associations.
/// </summary>
public sealed class LegalDocumentDialog : ContentDialog
{
    private const double MaxBodyHeight = 420;
    private const double MaxBodyWidth = 560;

    private LegalDocumentDialog()
    {
    }

    public static async Task ShowAsync(string title, string content)
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        var body = new TextBlock
        {
            Text = content,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            MaxWidth = MaxBodyWidth
        };

        var dialog = new LegalDocumentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = MaxBodyHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body
            },
            PrimaryButtonText = LocalizationService.GetString("Recordings_Ok"),
            DefaultButton = ContentDialogButton.Primary
        };

        await dialog.ShowAsync();
    }
}
