// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace DawnCapture.Helpers;

/// <summary>
/// Lightweight crash detection: exception hooks stamp a marker file, and the
/// next launch consumes it to notify the user. No dumps, no telemetry.
///
/// A recording in progress stamps the same file, which is what makes a forced kill, a log
/// off or a power loss visible: none of those raise an exception, so without the marker the
/// user would just find an unplayable file and no explanation.
/// </summary>
public static class CrashMarkerHelper
{
    /// <summary>Marker kind for a recording that was still being written.</summary>
    public const string RecordingKind = "recording";

    /// <summary>Marker kind for an unhandled exception.</summary>
    public const string CrashKind = "crash";

    private const char FieldSeparator = '|';

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
            if (File.Exists(MarkerPath) && IsRecordingMarker(File.ReadAllText(MarkerPath)))
            {
                // "Your recording was not saved" is the more useful message, and the
                // exception itself has already been written to the log.
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, $"{DateTime.Now:O} | {source}");
        }
        catch
        {
            // Never mask the original exception with marker failures.
        }
    }

    /// <summary>
    /// Stamps "a recording is being written to this file", so that a forced kill, a log off
    /// or a power loss can be explained on the next launch. Replaces a previous recording
    /// marker, which is what makes the last file of a segmented recording the reported one.
    /// </summary>
    public static void WriteRecording(string outputPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(
                MarkerPath,
                $"{RecordingKind}{FieldSeparator}{outputPath}{FieldSeparator}{DateTime.Now:O}");
        }
        catch
        {
            // Losing the marker only costs a notification.
        }
    }

    /// <summary>
    /// Removes the recording marker once its file has been finalized. A crash marker is left
    /// alone: it records something the user still has to be told about.
    /// </summary>
    public static void ClearRecording()
    {
        try
        {
            if (File.Exists(MarkerPath) && IsRecordingMarker(File.ReadAllText(MarkerPath)))
            {
                File.Delete(MarkerPath);
            }
        }
        catch
        {
            // The next launch reports it instead.
        }
    }

    /// <summary>
    /// Consumes the marker: returns what the previous session left behind and deletes the
    /// marker so the next launch starts clean. Returns null when the previous session exited
    /// normally.
    /// </summary>
    public static SessionMarker? TryConsume()
    {
        try
        {
            if (!File.Exists(MarkerPath))
            {
                return null;
            }

            string content = File.ReadAllText(MarkerPath).Trim();
            File.Delete(MarkerPath);

            if (content.Length == 0)
            {
                return new SessionMarker(CrashKind, "unknown", null);
            }

            if (!IsRecordingMarker(content))
            {
                return new SessionMarker(CrashKind, content, null);
            }

            // recording|<output path>|<started at>. A Windows path cannot contain the
            // separator, so the split is unambiguous.
            string[] fields = content.Split(FieldSeparator);
            return new SessionMarker(
                RecordingKind,
                fields.Length > 2 ? fields[2] : "unknown",
                fields.Length > 1 ? fields[1] : string.Empty);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsRecordingMarker(string content) =>
        content.StartsWith(RecordingKind + FieldSeparator, StringComparison.Ordinal);
}

/// <summary>What the previous session left behind, as consumed by <see cref="CrashMarkerHelper.TryConsume"/>.</summary>
/// <param name="Kind">Either <see cref="CrashMarkerHelper.RecordingKind"/> or <see cref="CrashMarkerHelper.CrashKind"/>.</param>
/// <param name="Detail">For a crash, the timestamp and source; for a recording, when it started.</param>
/// <param name="OutputPath">The file an unfinished recording was writing, when known.</param>
public sealed record SessionMarker(string Kind, string Detail, string? OutputPath);
