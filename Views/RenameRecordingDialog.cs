// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using DawnCapture.Helpers;
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
    private readonly string _extension;

    private RenameRecordingDialog(string filePath, string currentBaseName)
    {
        _filePath = filePath;
        _extension = Path.GetExtension(filePath);

        _nameBox = new TextBox
        {
            Text = currentBaseName,
            MaxLength = 200
        };

        var extensionLabel = new TextBlock
        {
            Text = _extension,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 0, 10),
            Foreground = (Brush)Application.Current.Resources["DawnTextFaintBrush"],
            IsHitTestVisible = false
        };

        var nameRow = new Grid();
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_nameBox, 0);
        Grid.SetColumn(extensionLabel, 1);
        nameRow.Children.Add(_nameBox);
        nameRow.Children.Add(extensionLabel);

        _errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["DawnDangerBrush"],
            FontSize = 12,
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        Content = new StackPanel
        {
            MinWidth = ContentDialogHelper.StandardContentMinWidth,
            Spacing = 8,
            Children =
                {
                    nameRow,
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

    public static async Task<string?> ShowAsync(string filePath, string currentBaseName)
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return null;
        }

        var dialog = new RenameRecordingDialog(filePath, currentBaseName)
        {
            XamlRoot = xamlRoot,
            Title = LocalizationService.GetString("Recordings_RenameTitle"),
            PrimaryButtonText = LocalizationService.GetString("Recordings_RenamePrimary"),
            SecondaryButtonText = ContentDialogHelper.CancelText,
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
