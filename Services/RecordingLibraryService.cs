using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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

    public async Task<bool> RenameRecordingAsync(
        Guid recordingId,
        string filePath,
        string newBaseName,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory) || !File.Exists(filePath))
        {
            return false;
        }

        string extension = Path.GetExtension(filePath);
        string baseName = newBaseName.Trim();
        if (extension.Length > 0 && baseName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^extension.Length];
        }

        if (baseName.Length == 0)
        {
            return false;
        }

        string newPath = Path.Combine(directory, baseName + extension);
        if (string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            await Task.Run(() => File.Move(filePath, newPath), cancellationToken);
            await _catalogService.UpdateDisplayNameAsync(recordingId, baseName, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to rename recording '{filePath}' to '{newPath}'", ex);
            return false;
        }
    }

    public async Task<bool> DeleteRecordingAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() => DeleteToRecycleBin(filePath), cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to delete recording '{filePath}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Sends the file to the Recycle Bin via SHFileOperation (FOF_ALLOWUNDO).
    /// Errors are reported through the return value only — no native UI is shown.
    /// </summary>
    private static void DeleteToRecycleBin(string filePath)
    {
        var operation = new RecycleBinOperation
        {
            Function = RecycleBinOperation.FO_DELETE,
            From = filePath + "\0",
            Flags = RecycleBinOperation.FOF_ALLOWUNDO
                  | RecycleBinOperation.FOF_NOCONFIRMATION
                  | RecycleBinOperation.FOF_SILENT
                  | RecycleBinOperation.FOF_NOERRORUI
        };

        int result = SHFileOperationW(ref operation);
        if (result != 0)
        {
            throw new IOException($"Recycle Bin delete failed with error 0x{result:X8}.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RecycleBinOperation
    {
        public const uint FO_DELETE = 0x0003;
        public const ushort FOF_ALLOWUNDO = 0x0040;
        public const ushort FOF_NOCONFIRMATION = 0x0010;
        public const ushort FOF_SILENT = 0x0004;
        public const ushort FOF_NOERRORUI = 0x0400;

        public IntPtr Owner;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Aborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHFileOperationW(ref RecycleBinOperation operation);

    private string ResolveOutputFolder()
    {
        return OutputFolderHelper.Resolve(_settingsService);
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
