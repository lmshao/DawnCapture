using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using Microsoft.Data.Sqlite;

namespace DawnCapture.Services;

public sealed class RecordingCatalogService : IRecordingCatalogService
{
    // Only formats produced by the recorder today. Extend this list when new
    // output formats are added to the capture pipeline.
    private static readonly string[] SupportedExtensions = [".mp4", ".m4a"];

    private readonly object _sync = new();
    private bool _schemaInitialized;

    public Task SyncLibraryAsync(string outputFolder, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SyncLibraryCore(outputFolder, cancellationToken), cancellationToken);
    }

    public Task RegisterRecordingAsync(
        string filePath,
        DateTimeOffset recordedAt,
        TimeSpan duration,
        RecordingMediaKind mediaKind,
        RecordingSourceKind? sourceKind = null,
        RecordingEncodingInfo? encoding = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSchema();

            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            string hash = await RecordingFileHashHelper.ComputeSha256HexAsync(fullPath, cancellationToken);
            var probe = await RecordingMediaProbe.ProbeAsync(fullPath, mediaKind);
            RecordingCatalogEntry? existing = FindExistingEntry(fullPath, hash);

            var entry = BuildEntryFromFile(
                fileInfo,
                hash,
                recordedAt,
                mediaKind,
                sourceKind,
                duration > TimeSpan.Zero ? (long)duration.TotalMilliseconds : probe.DurationMs,
                encoding,
                probe.Width,
                probe.Height,
                probe.VideoCodec,
                probe.AudioCodec,
                existingId: existing?.Id,
                existingDisplayName: existing?.DisplayName,
                existingThumbnail: existing?.ThumbnailPng,
                existingThumbnailWidth: existing?.ThumbnailWidth,
                existingThumbnailHeight: existing?.ThumbnailHeight);

            UpsertEntry(entry);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<RecordingCatalogEntry>> GetRecordingsAsync(
        string outputFolder,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSchema();

            string folder = Path.GetFullPath(outputFolder).TrimEnd('\\', '/');
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, file_path, display_name, file_hash, file_size, last_write_time_utc,
                       recorded_at, media_kind, source_kind, duration_ms, video_codec, audio_codec,
                       width, height, frame_rate, bitrate_kbps, thumbnail, thumbnail_width, thumbnail_height
                FROM recordings
                WHERE file_path LIKE $folder || '%'
                ORDER BY recorded_at DESC;
                """;
            command.Parameters.AddWithValue("$folder", folder);

            var results = new List<RecordingCatalogEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadEntry(reader));
            }

            return (IReadOnlyList<RecordingCatalogEntry>)results;
        }, cancellationToken);
    }

    public Task SaveThumbnailAsync(
        Guid recordingId,
        byte[] thumbnailPng,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSchema();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE recordings
                SET thumbnail = $thumbnail,
                    thumbnail_width = $width,
                    thumbnail_height = $height,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", recordingId.ToString());
            command.Parameters.AddWithValue("$thumbnail", thumbnailPng);
            command.Parameters.AddWithValue("$width", width);
            command.Parameters.AddWithValue("$height", height);
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }, cancellationToken);
    }

    public Task UpdateDisplayNameAsync(
        Guid recordingId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSchema();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE recordings
                SET display_name = $displayName,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", recordingId.ToString());
            command.Parameters.AddWithValue("$displayName", displayName);
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }, cancellationToken);
    }

    private void SyncLibraryCore(string outputFolder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSchema();
        MigrateLegacyJsonCatalogIfNeeded();

        string folder = Path.GetFullPath(outputFolder);
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            return;
        }

        List<RecordingCatalogEntry> dbEntries = LoadAllEntries()
            .Where(entry => IsInFolder(entry.FilePath, folder))
            .ToList();

        List<Guid> duplicateIds = FindDuplicateEntryIds(dbEntries);
        if (duplicateIds.Count > 0)
        {
            DeleteEntriesByIds(duplicateIds);
            dbEntries = dbEntries
                .Where(entry => !duplicateIds.Contains(entry.Id))
                .ToList();
            Log.Info($"Removed {duplicateIds.Count} duplicate catalog entries during sync.");
        }

        var dbByPath = dbEntries.ToDictionary(
            entry => NormalizePath(entry.FilePath),
            StringComparer.OrdinalIgnoreCase);
        var dbByHash = dbEntries.ToDictionary(
            entry => entry.FileHash,
            StringComparer.OrdinalIgnoreCase);

        var seenIds = new HashSet<Guid>();
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in EnumerateCandidateFiles(folder, dbByPath.Keys))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessScannedFile(filePath, dbByPath, dbByHash, seenIds, seenHashes, cancellationToken);
        }

        PruneMissingEntries(folder, seenIds, seenHashes);
    }

    private void ProcessScannedFile(
        string filePath,
        Dictionary<string, RecordingCatalogEntry> dbByPath,
        Dictionary<string, RecordingCatalogEntry> dbByHash,
        HashSet<Guid> seenIds,
        HashSet<string> seenHashes,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            return;
        }

        string normalizedPath = NormalizePath(fileInfo.FullName);
        dbByPath.TryGetValue(normalizedPath, out RecordingCatalogEntry? existingByPath);

        if (existingByPath is not null
            && RecordingFileHashHelper.IsQuickMatch(
                fileInfo,
                existingByPath.FileSize,
                existingByPath.LastWriteTimeUtc.UtcDateTime))
        {
            BackfillMissingCodecs(existingByPath, fileInfo);
            seenIds.Add(existingByPath.Id);
            seenHashes.Add(existingByPath.FileHash);
            return;
        }

        string hash = RecordingFileHashHelper.ComputeSha256HexAsync(fileInfo.FullName, cancellationToken)
            .GetAwaiter().GetResult();

        if (existingByPath is not null)
        {
            if (string.Equals(existingByPath.FileHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                UpdateFileIdentity(existingByPath, fileInfo);
                BackfillMissingCodecs(existingByPath, fileInfo);
            }
            else
            {
                RefreshEntryFromFile(existingByPath, fileInfo, hash);
            }

            seenIds.Add(existingByPath.Id);
            seenHashes.Add(hash);
            return;
        }

        if (dbByHash.TryGetValue(hash, out RecordingCatalogEntry? existingByHash))
        {
            UpdateFileIdentity(existingByHash, fileInfo);
            seenIds.Add(existingByHash.Id);
            seenHashes.Add(hash);
            return;
        }

        bool isAudio = IsAudioExtension(fileInfo.Extension);
        var mediaKind = isAudio ? RecordingMediaKind.Audio : RecordingMediaKind.Video;
        var probe = RecordingMediaProbe.ProbeAsync(fileInfo.FullName, mediaKind)
            .GetAwaiter().GetResult();

        if (probe.DurationMs is null
            && probe.Width is null
            && probe.Height is null
            && string.IsNullOrWhiteSpace(probe.VideoCodec)
            && string.IsNullOrWhiteSpace(probe.AudioCodec))
        {
            // Not recognizable as media — outside the scope of this library.
            return;
        }

        var imported = BuildEntryFromFile(
            fileInfo,
            hash,
            RecordingMediaProbe.ResolveRecordedAt(fileInfo),
            mediaKind,
            sourceKind: null,
            probe.DurationMs,
            encoding: null,
            probe.Width,
            probe.Height,
            probe.VideoCodec,
            probe.AudioCodec,
            existingId: null,
            existingDisplayName: null,
            existingThumbnail: null,
            existingThumbnailWidth: null,
            existingThumbnailHeight: null);

        UpsertEntry(imported);
        seenIds.Add(imported.Id);
        seenHashes.Add(hash);
    }


    private static void CleanupLegacySidecars(string folder)
    {
        try
        {
            foreach (string sidecarPath in Directory.EnumerateFiles(folder, "*.dawncapture.json"))
            {
                File.Delete(sidecarPath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to cleanup legacy sidecars in '{folder}': {ex.Message}");
        }
    }
    private static IEnumerable<string> EnumerateCandidateFiles(string folder, IEnumerable<string> knownPaths)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in knownPaths)
        {
            if (File.Exists(path))
            {
                paths.Add(path);
            }
        }

        foreach (string extension in SupportedExtensions)
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(folder, $"*{extension}"))
                {
                    paths.Add(path);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to enumerate recordings in '{folder}'", ex);
            }
        }

        return paths;
    }

    private void PruneMissingEntries(string folder, HashSet<Guid> seenIds, HashSet<string> seenHashes)
    {
        var staleIds = LoadAllEntries()
            .Where(entry => IsInFolder(entry.FilePath, folder))
            .Where(entry => !seenIds.Contains(entry.Id) && !seenHashes.Contains(entry.FileHash))
            .Select(entry => entry.Id)
            .ToList();

        if (staleIds.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (Guid id in staleIds)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM recordings WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static RecordingCatalogEntry BuildEntryFromFile(
        FileInfo fileInfo,
        string hash,
        DateTimeOffset recordedAt,
        RecordingMediaKind mediaKind,
        RecordingSourceKind? sourceKind,
        long? durationMs,
        RecordingEncodingInfo? encoding,
        int? probedWidth,
        int? probedHeight,
        string? probedVideoCodec,
        string? probedAudioCodec,
        Guid? existingId,
        string? existingDisplayName,
        byte[]? existingThumbnail,
        int? existingThumbnailWidth,
        int? existingThumbnailHeight)
    {
        return new RecordingCatalogEntry
        {
            Id = existingId ?? Guid.NewGuid(),
            FilePath = fileInfo.FullName,
            DisplayName = existingDisplayName ?? Path.GetFileNameWithoutExtension(fileInfo.Name),
            FileHash = hash,
            FileSize = fileInfo.Length,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            RecordedAt = recordedAt,
            MediaKind = mediaKind,
            SourceKind = sourceKind,
            DurationMs = durationMs,
            VideoCodec = encoding?.VideoCodec ?? probedVideoCodec,
            AudioCodec = encoding?.AudioCodec ?? probedAudioCodec,
            Width = encoding?.Width ?? probedWidth,
            Height = encoding?.Height ?? probedHeight,
            FrameRate = encoding?.FrameRate,
            BitrateKbps = encoding?.BitrateKbps,
            ThumbnailPng = existingThumbnail,
            ThumbnailWidth = existingThumbnailWidth,
            ThumbnailHeight = existingThumbnailHeight
        };
    }

    private void UpsertEntry(RecordingCatalogEntry entry)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO recordings (
                id, file_path, display_name, file_hash, file_size, last_write_time_utc,
                recorded_at, media_kind, source_kind, duration_ms, video_codec, audio_codec,
                width, height, frame_rate, bitrate_kbps, thumbnail, thumbnail_width, thumbnail_height, updated_at)
            VALUES (
                $id, $filePath, $displayName, $fileHash, $fileSize, $lastWriteTimeUtc,
                $recordedAt, $mediaKind, $sourceKind, $durationMs, $videoCodec, $audioCodec,
                $width, $height, $frameRate, $bitrateKbps, $thumbnail, $thumbnailWidth, $thumbnailHeight, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                file_path = excluded.file_path,
                display_name = excluded.display_name,
                file_hash = excluded.file_hash,
                file_size = excluded.file_size,
                last_write_time_utc = excluded.last_write_time_utc,
                recorded_at = excluded.recorded_at,
                media_kind = excluded.media_kind,
                source_kind = excluded.source_kind,
                duration_ms = excluded.duration_ms,
                video_codec = excluded.video_codec,
                audio_codec = excluded.audio_codec,
                width = excluded.width,
                height = excluded.height,
                frame_rate = excluded.frame_rate,
                bitrate_kbps = excluded.bitrate_kbps,
                thumbnail = COALESCE(excluded.thumbnail, recordings.thumbnail),
                thumbnail_width = COALESCE(excluded.thumbnail_width, recordings.thumbnail_width),
                thumbnail_height = COALESCE(excluded.thumbnail_height, recordings.thumbnail_height),
                updated_at = excluded.updated_at;
            """;
        BindEntryParameters(command, entry);
        command.ExecuteNonQuery();
    }

    private void UpdateFileIdentity(RecordingCatalogEntry existing, FileInfo fileInfo)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE recordings
            SET file_path = $filePath,
                file_size = $fileSize,
                last_write_time_utc = $lastWriteTimeUtc,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", existing.Id.ToString());
        command.Parameters.AddWithValue("$filePath", fileInfo.FullName);
        command.Parameters.AddWithValue("$fileSize", fileInfo.Length);
        command.Parameters.AddWithValue("$lastWriteTimeUtc", fileInfo.LastWriteTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private void BackfillMissingCodecs(RecordingCatalogEntry existing, FileInfo fileInfo)
    {
        bool needsVideoCodec = existing.MediaKind == RecordingMediaKind.Video
            && string.IsNullOrWhiteSpace(existing.VideoCodec);
        bool needsAudioCodec = string.IsNullOrWhiteSpace(existing.AudioCodec);
        if (!needsVideoCodec && !needsAudioCodec)
        {
            return;
        }

        var probe = RecordingMediaProbe.ProbeAsync(fileInfo.FullName, existing.MediaKind)
            .GetAwaiter().GetResult();

        string? videoCodec = existing.VideoCodec ?? probe.VideoCodec;
        string? audioCodec = existing.AudioCodec ?? probe.AudioCodec;
        if (string.Equals(existing.VideoCodec, videoCodec, StringComparison.Ordinal)
            && string.Equals(existing.AudioCodec, audioCodec, StringComparison.Ordinal))
        {
            return;
        }

        var updated = BuildEntryFromFile(
            fileInfo,
            existing.FileHash,
            existing.RecordedAt,
            existing.MediaKind,
            existing.SourceKind,
            existing.DurationMs,
            new RecordingEncodingInfo
            {
                VideoCodec = videoCodec,
                AudioCodec = audioCodec,
                Width = existing.Width,
                Height = existing.Height,
                FrameRate = existing.FrameRate,
                BitrateKbps = existing.BitrateKbps
            },
            existing.Width,
            existing.Height,
            probe.VideoCodec,
            probe.AudioCodec,
            existing.Id,
            existing.DisplayName,
            existing.ThumbnailPng,
            existing.ThumbnailWidth,
            existing.ThumbnailHeight);

        UpsertEntry(updated);
    }

    private void RefreshEntryFromFile(RecordingCatalogEntry existing, FileInfo fileInfo, string hash)
    {
        var probe = RecordingMediaProbe.ProbeAsync(fileInfo.FullName, existing.MediaKind)
            .GetAwaiter().GetResult();

        var refreshed = BuildEntryFromFile(
            fileInfo,
            hash,
            existing.RecordedAt,
            existing.MediaKind,
            existing.SourceKind,
            probe.DurationMs ?? existing.DurationMs,
            new RecordingEncodingInfo
            {
                VideoCodec = existing.VideoCodec ?? probe.VideoCodec,
                AudioCodec = existing.AudioCodec ?? probe.AudioCodec,
                Width = probe.Width ?? existing.Width,
                Height = probe.Height ?? existing.Height,
                FrameRate = existing.FrameRate,
                BitrateKbps = existing.BitrateKbps
            },
            probe.Width,
            probe.Height,
            probe.VideoCodec,
            probe.AudioCodec,
            existing.Id,
            existing.DisplayName,
            existingThumbnail: null,
            existingThumbnailWidth: null,
            existingThumbnailHeight: null);

        UpsertEntry(refreshed);
    }

    private List<RecordingCatalogEntry> LoadAllEntries()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_path, display_name, file_hash, file_size, last_write_time_utc,
                   recorded_at, media_kind, source_kind, duration_ms, video_codec, audio_codec,
                   width, height, frame_rate, bitrate_kbps, thumbnail, thumbnail_width, thumbnail_height
            FROM recordings;
            """;

        var results = new List<RecordingCatalogEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEntry(reader));
        }

        return results;
    }

    private static RecordingCatalogEntry ReadEntry(SqliteDataReader reader)
    {
        return new RecordingCatalogEntry
        {
            Id = Guid.Parse(reader.GetString(0)),
            FilePath = reader.GetString(1),
            DisplayName = reader.GetString(2),
            FileHash = reader.GetString(3),
            FileSize = reader.GetInt64(4),
            LastWriteTimeUtc = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            RecordedAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            MediaKind = (RecordingMediaKind)reader.GetInt32(7),
            SourceKind = reader.IsDBNull(8) ? null : (RecordingSourceKind)reader.GetInt32(8),
            DurationMs = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            VideoCodec = reader.IsDBNull(10) ? null : reader.GetString(10),
            AudioCodec = reader.IsDBNull(11) ? null : reader.GetString(11),
            Width = reader.IsDBNull(12) ? null : reader.GetInt32(12),
            Height = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            FrameRate = reader.IsDBNull(14) ? null : reader.GetDouble(14),
            BitrateKbps = reader.IsDBNull(15) ? null : reader.GetInt32(15),
            ThumbnailPng = reader.IsDBNull(16) ? null : (byte[])reader.GetValue(16),
            ThumbnailWidth = reader.IsDBNull(17) ? null : reader.GetInt32(17),
            ThumbnailHeight = reader.IsDBNull(18) ? null : reader.GetInt32(18)
        };
    }

    private static void BindEntryParameters(SqliteCommand command, RecordingCatalogEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$filePath", entry.FilePath);
        command.Parameters.AddWithValue("$displayName", entry.DisplayName);
        command.Parameters.AddWithValue("$fileHash", entry.FileHash);
        command.Parameters.AddWithValue("$fileSize", entry.FileSize);
        command.Parameters.AddWithValue("$lastWriteTimeUtc", entry.LastWriteTimeUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$recordedAt", entry.RecordedAt.ToString("O"));
        command.Parameters.AddWithValue("$mediaKind", (int)entry.MediaKind);
        command.Parameters.AddWithValue("$sourceKind", entry.SourceKind.HasValue ? (int)entry.SourceKind.Value : DBNull.Value);
        command.Parameters.AddWithValue("$durationMs", entry.DurationMs ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$videoCodec", entry.VideoCodec ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$audioCodec", entry.AudioCodec ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$width", entry.Width ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$height", entry.Height ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$frameRate", entry.FrameRate ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$bitrateKbps", entry.BitrateKbps ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$thumbnail", entry.ThumbnailPng ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$thumbnailWidth", entry.ThumbnailWidth ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$thumbnailHeight", entry.ThumbnailHeight ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
    }

    private void EnsureSchema()
    {
        lock (_sync)
        {
            if (_schemaInitialized)
            {
                return;
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS recordings (
                    id TEXT PRIMARY KEY NOT NULL,
                    file_path TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    file_hash TEXT NOT NULL,
                    file_size INTEGER NOT NULL,
                    last_write_time_utc TEXT NOT NULL,
                    recorded_at TEXT NOT NULL,
                    media_kind INTEGER NOT NULL,
                    source_kind INTEGER NULL,
                    duration_ms INTEGER NULL,
                    video_codec TEXT NULL,
                    audio_codec TEXT NULL,
                    width INTEGER NULL,
                    height INTEGER NULL,
                    frame_rate REAL NULL,
                    bitrate_kbps INTEGER NULL,
                    thumbnail BLOB NULL,
                    thumbnail_width INTEGER NULL,
                    thumbnail_height INTEGER NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_recordings_hash ON recordings(file_hash);
                CREATE INDEX IF NOT EXISTS idx_recordings_path ON recordings(file_path);
                """;
            command.ExecuteNonQuery();
            _schemaInitialized = true;
        }
    }

    private static SqliteConnection OpenConnection()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DawnCapture");
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "library.db")}");
        connection.Open();
        return connection;
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private RecordingCatalogEntry? FindExistingEntry(string fullPath, string hash)
    {
        string normalizedPath = NormalizePath(fullPath);
        foreach (RecordingCatalogEntry entry in LoadAllEntries())
        {
            if (string.Equals(NormalizePath(entry.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.FileHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static List<Guid> FindDuplicateEntryIds(IReadOnlyList<RecordingCatalogEntry> entries)
    {
        var duplicateIds = new HashSet<Guid>();
        var byPath = new Dictionary<string, RecordingCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (RecordingCatalogEntry entry in entries.OrderByDescending(e => e.RecordedAt))
        {
            string path = NormalizePath(entry.FilePath);
            if (byPath.TryGetValue(path, out RecordingCatalogEntry? existing))
            {
                RecordingCatalogEntry keeper = PickPreferredEntry(existing, entry);
                RecordingCatalogEntry stale = keeper.Id == existing.Id ? entry : existing;
                byPath[path] = keeper;
                duplicateIds.Add(stale.Id);
            }
            else
            {
                byPath[path] = entry;
            }
        }

        var byHash = new Dictionary<string, RecordingCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (RecordingCatalogEntry entry in byPath.Values.OrderByDescending(e => e.RecordedAt))
        {
            if (byHash.TryGetValue(entry.FileHash, out RecordingCatalogEntry? existing))
            {
                RecordingCatalogEntry keeper = PickPreferredEntry(existing, entry);
                RecordingCatalogEntry stale = keeper.Id == existing.Id ? entry : existing;
                byHash[entry.FileHash] = keeper;
                duplicateIds.Add(stale.Id);
            }
            else
            {
                byHash[entry.FileHash] = entry;
            }
        }

        return duplicateIds.ToList();
    }

    private static RecordingCatalogEntry PickPreferredEntry(
        RecordingCatalogEntry existing,
        RecordingCatalogEntry candidate)
    {
        if (existing.ThumbnailPng is { Length: > 0 } && candidate.ThumbnailPng is not { Length: > 0 })
        {
            return existing;
        }

        if (candidate.ThumbnailPng is { Length: > 0 } && existing.ThumbnailPng is not { Length: > 0 })
        {
            return candidate;
        }

        return candidate.RecordedAt >= existing.RecordedAt ? candidate : existing;
    }

    private void DeleteEntriesByIds(IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (Guid id in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM recordings WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static bool IsInFolder(string filePath, string folder)
    {
        string normalizedFolder = Path.GetFullPath(folder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedFile = Path.GetFullPath(filePath);
        return normalizedFile.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedFile.StartsWith(normalizedFolder + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAudioExtension(string extension)
    {
        return extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    private void MigrateLegacyJsonCatalogIfNeeded()
    {
        string jsonPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DawnCapture",
            "recordings.json");
        if (!File.Exists(jsonPath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(jsonPath);
            var legacyEntries = JsonSerializer.Deserialize<List<LegacyCatalogEntry>>(json);
            if (legacyEntries is null || legacyEntries.Count == 0)
            {
                return;
            }

            foreach (LegacyCatalogEntry legacy in legacyEntries)
            {
                if (!File.Exists(legacy.FilePath))
                {
                    continue;
                }

                var fileInfo = new FileInfo(legacy.FilePath);
                string hash = RecordingFileHashHelper.ComputeSha256HexAsync(legacy.FilePath)
                    .GetAwaiter().GetResult();
                var entry = BuildEntryFromFile(
                    fileInfo,
                    hash,
                    legacy.RecordedAt,
                    legacy.MediaKind,
                    legacy.SourceKind,
                    legacy.DurationMs,
                    encoding: null,
                    probedWidth: null,
                    probedHeight: null,
                    probedVideoCodec: null,
                    probedAudioCodec: null,
                    existingId: legacy.Id,
                    existingDisplayName: Path.GetFileNameWithoutExtension(fileInfo.Name),
                    existingThumbnail: null,
                    existingThumbnailWidth: null,
                    existingThumbnailHeight: null);
                UpsertEntry(entry);
            }

            File.Move(jsonPath, jsonPath + ".bak", overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to migrate legacy recordings.json catalog", ex);
        }
    }

    private sealed class LegacyCatalogEntry
    {
        public Guid Id { get; set; }

        public string FilePath { get; set; } = string.Empty;

        public DateTimeOffset RecordedAt { get; set; }

        public RecordingMediaKind MediaKind { get; set; }

        public RecordingSourceKind? SourceKind { get; set; }

        public long? DurationMs { get; set; }
    }
}
