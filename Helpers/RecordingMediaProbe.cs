using System;
using System.IO;
using System.Threading.Tasks;
using DawnCapture.Models;
using NAudio.Wave;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace DawnCapture.Helpers;

internal static class RecordingMediaProbe
{
    public static async Task<(long? DurationMs, int? Width, int? Height, string? VideoCodec, string? AudioCodec)>
        ProbeAsync(string filePath, RecordingMediaKind mediaKind)
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
                return (durationMs, null, null, null, musicCodec);
            }

            VideoProperties video = await file.Properties.GetVideoPropertiesAsync();
            long? videoDurationMs = video.Duration.TotalMilliseconds > 0
                ? (long)video.Duration.TotalMilliseconds
                : null;
            int? width = video.Width > 0 ? (int)video.Width : null;
            int? height = video.Height > 0 ? (int)video.Height : null;
            string? audioCodec = ProbeVideoAudioCodec(filePath);
            return (videoDurationMs, width, height, "H.264", audioCodec);
        }
        catch
        {
            return (null, null, null, null, null);
        }
    }

    public static DateTimeOffset ResolveRecordedAt(FileInfo fileInfo)
    {
        DateTime recordedUtc = fileInfo.LastWriteTimeUtc > fileInfo.CreationTimeUtc
            ? fileInfo.LastWriteTimeUtc
            : fileInfo.CreationTimeUtc;
        return new DateTimeOffset(DateTime.SpecifyKind(recordedUtc, DateTimeKind.Utc));
    }

    private static string? ProbeVideoAudioCodec(string filePath)
    {
        if (!HasAudioExtension(filePath))
        {
            return null;
        }

        try
        {
            using var reader = new MediaFoundationReader(filePath);
            return reader.WaveFormat.Channels > 0 ? "AAC" : null;
        }
        catch
        {
            return null;
        }
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