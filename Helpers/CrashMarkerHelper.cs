using System;
using System.IO;

namespace DawnCapture.Helpers;

/// <summary>
/// Lightweight crash detection: exception hooks stamp a marker file, and the
/// next launch consumes it to notify the user. No dumps, no telemetry.
/// </summary>
public static class CrashMarkerHelper
{
    private static readonly string MarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DawnCapture",
        "last-crash.txt");

    /// <summary>
    /// Stamps the crash marker from an unhandled-exception hook. Failures are
    /// swallowed so they never mask the original exception.
    /// </summary>
    public static void Write(string source)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, $"{DateTime.Now:O} | {source}");
        }
        catch
        {
            // Never mask the original exception with marker failures.
        }
    }

    /// <summary>
    /// Consumes the marker: returns the recorded crash timestamp (or
    /// "unknown") if the previous session ended abnormally, and deletes the
    /// marker so the next launch starts clean. Returns null when the previous
    /// session exited normally.
    /// </summary>
    public static string? TryConsume()
    {
        try
        {
            if (!File.Exists(MarkerPath))
            {
                return null;
            }

            string content = File.ReadAllText(MarkerPath).Trim();
            File.Delete(MarkerPath);
            return content.Length > 0 ? content : "unknown";
        }
        catch
        {
            return null;
        }
    }
}
