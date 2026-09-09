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
        catch
        {
            // Fall back to defaults when the settings file is corrupt.
            Current = new AppSettings();
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
            File.WriteAllText(_filePath, json);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save settings to '{_filePath}'", ex);
            return false;
        }
    }
}
