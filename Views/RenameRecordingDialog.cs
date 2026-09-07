using DawnCapture.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DawnCapture.Views;

/// <summary>
/// Renames the recording file on disk. Returns the new base name (without extension),
/// or null when the user cancels.
/// </summary>
public sealed class RenameRecordingDialog : ContentDialog
{
    private readonly TextBox _nameBox;
    private readonly TextBlock _errorText;
    private readonly string _filePath;

    private RenameRecordingDialog(string filePath, string currentName)
    {
        _filePath = filePath;

        _nameBox = new TextBox
        {
            Text = currentName,
            MaxLength = 200
        };

        _errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["DawnDangerBrush"],
            FontSize = 12,
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        Content = new StackPanel
        {
            MinWidth = 320,
            Spacing = 8,
            Children =
                {
                    _nameBox,
                    _errorText
                }
        };

        PrimaryButtonClick += OnPrimaryButtonClick;
        Opened += (_, _) =>
        {
            _nameBox.Focus(FocusState.Programmatic);
            _nameBox.SelectAll();
        };
    }

    public string? RenamedName { get; private set; }

    public static async Task<string?> ShowAsync(string filePath, string currentName)
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return null;
        }

        var dialog = new RenameRecordingDialog(filePath, currentName)
        {
            XamlRoot = xamlRoot,
            Title = LocalizationService.GetString("Recordings_RenameTitle"),
            PrimaryButtonText = LocalizationService.GetString("Recordings_RenamePrimary"),
            SecondaryButtonText = LocalizationService.GetString("Recordings_DeleteCancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? dialog.RenamedName : null;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string? error = ValidateName(_nameBox.Text);
        if (error is not null)
        {
            _errorText.Text = error;
            _errorText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        RenamedName = StripExtension(_nameBox.Text.Trim());
        _errorText.Visibility = Visibility.Collapsed;
    }

    private string? ValidateName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return LocalizationService.GetString("Recordings_RenameEmpty");
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        int index = trimmed.IndexOfAny(invalidChars);
        if (index >= 0)
        {
            return string.Format(
                LocalizationService.GetString("Recordings_RenameInvalidChars"),
                invalidChars[index]);
        }

        string? directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        string candidate = Path.Combine(directory, StripExtension(trimmed) + Path.GetExtension(_filePath));
        if (File.Exists(candidate) && !string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.GetString("Recordings_RenameConflict");
        }

        return null;
    }

    private string StripExtension(string name)
    {
        string extension = Path.GetExtension(_filePath);
        return extension.Length > 0 && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                   ? name[..^extension.Length]
                   : name;
    }
}
