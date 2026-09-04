using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace DawnCapture.Services;

public sealed class RecordingLibraryService : IRecordingLibraryService
{
    private readonly ISettingsService _settingsService;
    private readonly IRecordingCatalogService _catalogService;

    public RecordingLibraryService(
        ISettingsService settingsService,
        IRecordingCatalogService catalogService)
    {
        _settingsService = settingsService;
        _catalogService = catalogService;
    }

    public string GetOutputFolder()
    {
        return ResolveOutputFolder();
    }

    public async Task<IReadOnlyList<RecordingListItem>> LoadRecordingsAsync(
        CancellationToken cancellationToken = default)
    {
        string folder = ResolveOutputFolder();
        await _catalogService.SyncLibraryAsync(folder, cancellationToken);
        IReadOnlyList<RecordingCatalogEntry> entries = await _catalogService.GetRecordingsAsync(
            folder,
            cancellationToken);

        var items = new List<RecordingListItem>();
        foreach (RecordingCatalogEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RecordingListItem? item = await TryCreateListItemAsync(entry);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private string ResolveOutputFolder()
    {
        string folder = _settingsService.Current.OutputFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "DawnCapture");
        }

        return folder;
    }

    private static async Task<RecordingListItem?> TryCreateListItemAsync(RecordingCatalogEntry entry)
    {
        try
        {
            var fileInfo = new FileInfo(entry.FilePath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                return null;
            }

            DateTimeOffset updatedAt = entry.LastWriteTimeUtc;

            return new RecordingListItem
            {
                RecordingId = entry.Id,
                FilePath = entry.FilePath,
                Name = entry.DisplayName,
                FormatLabel = FormatMediaLabel(entry, fileInfo),
                UpdatedAtLabel = FormatUpdatedAt(updatedAt),
                SizeLabel = FormatSize(entry.FileSize),
                FileSizeBytes = entry.FileSize,
                UpdatedAt = updatedAt,
                IsAudio = entry.MediaKind == RecordingMediaKind.Audio,
                ThumbnailPng = entry.ThumbnailPng,
                ThumbnailWidth = entry.ThumbnailWidth,
                ThumbnailHeight = entry.ThumbnailHeight
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load recording metadata for '{entry.FilePath}'", ex);
            return null;
        }
    }

    private static string FormatMediaLabel(RecordingCatalogEntry entry, FileInfo fileInfo)
    {
        string unknownCodec = LocalizationService.GetString("Recordings_Codec_Unknown");
        string container = fileInfo.Extension.TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(container))
        {
            container = entry.MediaKind == RecordingMediaKind.Audio
                ? LocalizationService.GetString("Recordings_Media_Audio")
                : LocalizationService.GetString("Recordings_Media_Video");
        }

        var parts = new List<string> { container };

        if (entry.MediaKind == RecordingMediaKind.Audio)
        {
            parts.Add(string.IsNullOrWhiteSpace(entry.AudioCodec) ? unknownCodec : entry.AudioCodec);
            return string.Join(' ', parts);
        }

        parts.Add(string.IsNullOrWhiteSpace(entry.VideoCodec) ? unknownCodec : entry.VideoCodec);
        if (!string.IsNullOrWhiteSpace(entry.AudioCodec))
        {
            parts.Add(entry.AudioCodec);
        }

        return string.Join(' ', parts);
    }

    private static string FormatUpdatedAt(DateTimeOffset updatedAt)
    {
        DateTime local = updatedAt.LocalDateTime;
        string time = local.ToString("HH:mm", CultureInfo.CurrentCulture);

        if (local.Date == DateTime.Today)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                LocalizationService.GetString("Recordings_DateToday"),
                time);
        }

        if (local.Date == DateTime.Today.AddDays(-1))
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                LocalizationService.GetString("Recordings_DateYesterday"),
                time);
        }

        string pattern = LocalizationService.GetString("Recordings_DateTimeFormat");
        return local.ToString(pattern, CultureInfo.CurrentCulture);
    }

    private static string FormatSize(long bytes) => FileSizeFormatHelper.Format(bytes);
}