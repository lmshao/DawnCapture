// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Text.Json;
using DawnCapture.Models;

namespace DawnCapture.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly string _filePath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DawnCapture");
        _filePath = Path.Combine(directory, "settings.json");
        Load();
    }

    public AppSettings Current { get; private set; } = new();

    /// <summary>
    /// True when the settings file could not be read and was replaced by defaults. The shell
    /// reports it once so the user is not left wondering where the configuration went.
    /// </summary>
    public bool SettingsWereReset { get; private set; }

    /// <summary>Where the unreadable settings file was moved, when that happened.</summary>
    public string? PreservedSettingsPath { get; private set; }

    public event EventHandler? SettingsChanged;

    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                Current = new AppSettings();
                return;
            }

            var json = File.ReadAllText(_filePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Never replace the user's settings silently: keep the unreadable file, log the
            // reason, and let the shell say what happened on this launch.
            Log.Error($"Failed to read settings from '{_filePath}'; falling back to defaults.", ex);
            PreservedSettingsPath = TryPreserveUnreadableFile();
            SettingsWereReset = true;
            Current = new AppSettings();
        }
    }

    private string? TryPreserveUnreadableFile()
    {
        try
        {
            string preserved = $"{_filePath}.unreadable-{DateTime.Now:yyyyMMdd_HHmmss}";
            File.Move(_filePath, preserved, overwrite: true);
            return preserved;
        }
        catch (Exception ex)
        {
            Log.Info($"Could not preserve the unreadable settings file: {ex.Message}");
            return null;
        }
    }

    public bool Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });

            // Write to a temp file and swap atomically so an interrupted write
            // never leaves a corrupt settings.json behind.
            string tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save settings to '{_filePath}'", ex);
            return false;
        }
    }

    public bool ResetToDefaults()
    {
        AppSettings previous = Current;
        Current = new AppSettings();
        if (Save())
        {
            return true;
        }

        // Keep memory and disk consistent when the reset cannot be persisted.
        Current = previous;
        return false;
    }
}
