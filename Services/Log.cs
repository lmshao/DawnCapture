using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace DawnCapture.Services;

public static class Log
{
    private static readonly object Sync = new();
    private static string? _logFile;

    public static void Init()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DawnCapture",
                "logs");
            Directory.CreateDirectory(directory);
            _logFile = Path.Combine(directory, $"dawncapture_{DateTime.Now:yyyyMMdd}.log");
        }
        catch
        {
            // Log initialization failures must not prevent the application from running.
        }
    }

    public static string FilePath => _logFile ?? string.Empty;

    public static void Info(
        string message,
        [CallerMemberName] string? member = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        Write("INFO", message, null, member, file, line);
    }

    [Conditional("DEBUG")]
    public static void Debug(
        string message,
        [CallerMemberName] string? member = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        Write("DEBUG", message, null, member, file, line);
    }

    public static void Error(
        string message,
        Exception? exception = null,
        [CallerMemberName] string? member = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        Write("ERROR", message, exception, member, file, line);
    }

    private static void Write(
        string level,
        string message,
        Exception? exception,
        string? member,
        string? file,
        int line)
    {
        try
        {
            lock (Sync)
            {
                if (_logFile is null)
                {
                    Init();
                }

                if (_logFile is null)
                {
                    return;
                }

                string source = $"{Path.GetFileName(file)}:{line} {member}";
                string text = exception is null
                    ? message
                    : $"{message} | {exception.GetType().Name}: {exception.Message}";

                File.AppendAllText(
                    _logFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {source} {text}{Environment.NewLine}");
            }
        }
        catch
        {
            // Silently ignore log write failures.
        }
    }
}

public static class LocalizationService
{
    public const string SystemLanguage = "system";
    public const string SimplifiedChinese = "zh-CN";
    public const string English = "en-US";

    private static ResourceLoader? _resourceLoader;

    public static void ApplyLanguage(string? language)
    {
        string? languageOverride = language switch
        {
            SimplifiedChinese => SimplifiedChinese,
            English => English,
            _ => null
        };

        if (languageOverride is not null)
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageOverride;
        }

        _resourceLoader = null;
    }

    public static string GetString(string key)
    {
        _resourceLoader ??= new ResourceLoader();
        return _resourceLoader.GetString(key);
    }
}
