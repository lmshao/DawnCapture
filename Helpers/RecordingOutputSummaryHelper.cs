using System;
using System.Collections.Generic;
using System.Globalization;
using DawnCapture.Models;
using DawnCapture.Services;

namespace DawnCapture.Helpers;

public static class RecordingOutputSummaryHelper
{
    public static string FormatCatalogEntry(RecordingCatalogEntry entry)
    {
        if (entry.MediaKind == RecordingMediaKind.Audio)
        {
            return FormatAudioSpecs(RecordingMediaProbe.SanitizeAacBitrateKbps(entry.BitrateKbps));
        }

        bool includeAudio = !string.IsNullOrWhiteSpace(entry.AudioCodec);
        return FormatVideoSpecs(
            entry.VideoCodec,
            resolution: null,
            entry.Width,
            entry.Height,
            entry.FrameRate,
            entry.BitrateKbps,
            RecordingMediaProbe.SanitizeAacBitrateKbps(entry.AudioBitrateKbps),
            includeAudio,
            approximateVideoBitrate: false);
    }

    public static string FormatVideoSpecs(
        string? videoCodec,
        string? resolution,
        int? width,
        int? height,
        double? frameRate,
        int? videoBitrateKbps,
        int? audioBitrateKbps,
        bool includeAudio = true,
        bool approximateVideoBitrate = false)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(videoCodec))
        {
            parts.Add(NormalizeVideoCodec(videoCodec));
        }

        string resolved = !string.IsNullOrWhiteSpace(resolution)
            ? resolution
            : FormatResolution(width, height);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            parts.Add(resolved);
        }

        if (frameRate is > 0)
        {
            parts.Add($"{FormatFrameRate(frameRate.Value)} fps");
        }

        if (videoBitrateKbps is > 0)
        {
            parts.Add(FormatVideoBitrateMbps(videoBitrateKbps.Value, approximateVideoBitrate));
        }

        if (includeAudio)
        {
            return CombineVideoAndAudioSections(parts, audioBitrateKbps);
        }

        return parts.Count > 0
            ? FormatVideoSection(parts)
            : LocalizationService.GetString("Recordings_Codec_Unknown");
    }

    private static string CombineVideoAndAudioSections(IReadOnlyList<string> videoParts, int? audioBitrateKbps)
    {
        string audioSection = FormatAudioSection(audioBitrateKbps);
        if (videoParts.Count == 0)
        {
            return audioSection;
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.GetString("Output_Summary_VideoWithAudio"),
            FormatVideoSection(videoParts),
            audioSection);
    }

    private static string FormatVideoSection(IReadOnlyList<string> videoParts)
    {
        string videoSpecs = string.Join(" · ", videoParts);
        return string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.GetString("Output_Summary_VideoSection"),
            videoSpecs);
    }

    private static string FormatAudioSection(int? audioBitrateKbps)
    {
        var audioParts = new List<string> { "AAC", $"{RecordingAudioOptions.DefaultSampleRateKhz} kHz" };
        if (audioBitrateKbps is > 0)
        {
            audioParts.Add($"{audioBitrateKbps} kbps");
        }

        string audioSpecs = string.Join(" · ", audioParts);
        return string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.GetString("Output_Summary_AudioSection"),
            audioSpecs);
    }

    private static string FormatVideoBitrateMbps(int videoBitrateKbps, bool approximate)
    {
        int megabits = (videoBitrateKbps + 500) / 1000;
        megabits = Math.Max(megabits, 1);
        return approximate ? $"~{megabits} Mbps" : $"{megabits} Mbps";
    }

    public static string FormatAudioSpecs(int? audioBitrateKbps)
    {
        if (audioBitrateKbps is > 0)
        {
            return string.Format(
                LocalizationService.GetString("Dock_Output_AudioFormat"),
                RecordingAudioOptions.DefaultSampleRateKhz,
                audioBitrateKbps.Value);
        }

        return $"AAC · {RecordingAudioOptions.DefaultSampleRateKhz} kHz";
    }

    public static string FormatResolution(int? width, int? height)
    {
        if (width is > 0 && height is > 0)
        {
            return $"{width} x {height}";
        }

        return string.Empty;
    }

    public static string NormalizeVideoCodec(string codec)
    {
        if (codec.Contains("265", StringComparison.Ordinal) || codec.Contains("HEVC", StringComparison.OrdinalIgnoreCase))
        {
            return "H.265/HEVC";
        }

        if (codec.Contains("264", StringComparison.Ordinal) || codec.Contains("AVC", StringComparison.OrdinalIgnoreCase))
        {
            return "H.264/AVC";
        }

        return codec;
    }

    private static string FormatFrameRate(double frameRate)
    {
        return Math.Abs(frameRate - Math.Round(frameRate)) < 0.01
            ? ((int)Math.Round(frameRate)).ToString(CultureInfo.InvariantCulture)
            : frameRate.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
