using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

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
            // 日志初始化失败不应影响应用运行。
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
            // 写日志失败时静默忽略。
        }
    }
}
