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
            // 设置文件损坏时退回默认值，不让应用崩溃。
            Current = new AppSettings();
        }
    }

    public void Save()
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
        }
        catch
        {
            // 保存失败不应影响录制主流程。
        }
    }
}
