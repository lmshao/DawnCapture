using System;
using System.IO;
using System.Threading.Tasks;
using DawnCapture.Models;
using NAudio.Wave;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace DawnCapture.Helpers;

internal sealed class RecordingMediaProbeResult
{
    public long? DurationMs { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? VideoCodec { get; init; }

    public string? AudioCodec { get; init; }

    public int? VideoBitrateKbps { get; init; }

    public int? AudioBitrateKbps { get; init; }
}

internal static class RecordingMediaProbe
{
    private const int MinPlausibleAacBitrateKbps = 32;
    private const int MaxPlausibleAacBitrateKbps = 512;

    public static async Task<RecordingMediaProbeResult> ProbeAsync(string filePath, RecordingMediaKind mediaKind)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            if (mediaKind == RecordingMediaKind.Audio)
            {
                MusicProperties music = await file.Properties.GetMusicPropertiesAsync();
                long? durationMs = music.Duration.TotalMilliseconds > 0
                    ? (long)music.Duration.TotalMilliseconds
                    : null;
                string? musicCodec = InferAudioCodecFromExtension(filePath);
                int? musicBitrateKbps = ResolveMusicBitrateKbps(music) ?? ProbeEncodedAudioBitrateKbps(filePath);
                return new RecordingMediaProbeResult
                {
                    DurationMs = durationMs,
                    AudioCodec = musicCodec,
                    AudioBitrateKbps = musicBitrateKbps
                };
            }

            VideoProperties video = await file.Properties.GetVideoPropertiesAsync();
            MusicProperties musicTrack = await file.Properties.GetMusicPropertiesAsync();
            long? videoDurationMs = video.Duration.TotalMilliseconds > 0
                ? (long)video.Duration.TotalMilliseconds
                : null;
            int? width = video.Width > 0 ? (int)video.Width : null;
            int? height = video.Height > 0 ? (int)video.Height : null;

            string? audioCodec = HasAudioExtension(filePath) ? InferAudioCodecFromExtension(filePath) : null;
            int? audioBitrateKbps = ResolveMusicBitrateKbps(musicTrack)
                ?? ProbeEncodedAudioBitrateKbps(filePath);
            bool hasAudio = !string.IsNullOrWhiteSpace(audioCodec) && audioBitrateKbps is > 0;
            int? videoBitrateKbps = ResolveVideoBitrateKbps(video, audioBitrateKbps, hasAudio);

            if (!hasAudio)
            {
                audioCodec = null;
                audioBitrateKbps = null;
            }

            return new RecordingMediaProbeResult
            {
                DurationMs = videoDurationMs,
                Width = width,
                Height = height,
                VideoCodec = "H.264/AVC",
                AudioCodec = audioCodec,
                VideoBitrateKbps = videoBitrateKbps,
                AudioBitrateKbps = audioBitrateKbps
            };
        }
        catch
        {
            return new RecordingMediaProbeResult();
        }
    }

    public static DateTimeOffset ResolveRecordedAt(FileInfo fileInfo)
    {
        DateTime recordedUtc = fileInfo.LastWriteTimeUtc > fileInfo.CreationTimeUtc
            ? fileInfo.LastWriteTimeUtc
            : fileInfo.CreationTimeUtc;
        return new DateTimeOffset(DateTime.SpecifyKind(recordedUtc, DateTimeKind.Utc));
    }

    private static int? ResolveVideoBitrateKbps(VideoProperties video, int? audioBitrateKbps, bool hasAudio)
    {
        if (video.Bitrate <= 0)
        {
            return null;
        }

        int totalKbps = (int)(video.Bitrate / 1000);
        if (!hasAudio || audioBitrateKbps is null or <= 0)
        {
            return totalKbps;
        }

        return totalKbps > audioBitrateKbps.Value
            ? totalKbps - audioBitrateKbps.Value
            : null;
    }

    private static int? ProbeEncodedAudioBitrateKbps(string filePath)
    {
        if (!HasAudioExtension(filePath))
        {
            return null;
        }

        try
        {
            using var reader = new MediaFoundationReader(filePath);
            if (reader.WaveFormat.Channels <= 0)
            {
                return null;
            }

            int bytesPerSecond = reader.WaveFormat.AverageBytesPerSecond;
            int? bitrateKbps = bytesPerSecond > 0 ? bytesPerSecond * 8 / 1000 : null;
            return IsPlausibleAacBitrateKbps(bitrateKbps) ? bitrateKbps : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? ResolveMusicBitrateKbps(MusicProperties music)
    {
        if (music.Bitrate <= 0)
        {
            return null;
        }

        int? bitrateKbps = (int)(music.Bitrate / 1000);
        return IsPlausibleAacBitrateKbps(bitrateKbps) ? bitrateKbps : null;
    }

    private static bool IsPlausibleAacBitrateKbps(int? bitrateKbps)
    {
        return bitrateKbps is >= MinPlausibleAacBitrateKbps and <= MaxPlausibleAacBitrateKbps;
    }

    internal static int? SanitizeAacBitrateKbps(int? bitrateKbps)
    {
        return IsPlausibleAacBitrateKbps(bitrateKbps) ? bitrateKbps : null;
    }

    private static string? InferAudioCodecFromExtension(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            ? "AAC"
            : null;
    }

    private static bool HasAudioExtension(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase);
    }
}
