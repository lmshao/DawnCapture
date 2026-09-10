using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using Microsoft.Data.Sqlite;

namespace DawnCapture.Services;

public sealed partial class RecordingCatalogService : IRecordingCatalogService
{
    // Only formats produced by the recorder today. Extend this list when new
    // output formats are added to the capture pipeline.
    private static readonly string[] SupportedExtensions = [".mp4", ".m4a"];

    private readonly object _sync = new();
    private bool _schemaInitialized;

    public Task SyncLibraryAsync(string outputFolder, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            async () => await SyncLibraryCoreAsync(outputFolder, cancellationToken),
            cancellationToken);
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
            ExistingEntryRef? existing = FindExistingEntry(fullPath, hash);

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
                probe.VideoBitrateKbps,
                probe.AudioBitrateKbps,
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
            if (folder.EndsWith(':'))
            {
                folder += '\\';
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, file_path, display_name, file_hash, file_size, last_write_time_utc,
                       recorded_at, media_kind, source_kind, duration_ms, video_codec, audio_codec,
                       width, height, frame_rate, bitrate_kbps, audio_bitrate_kbps
                FROM recordings
                WHERE file_path = $folder OR file_path LIKE $folder || '\' || '%'
                ORDER BY recorded_at DESC;
                """;
            command.Parameters.AddWithValue("$folder", folder);

            var results = new List<RecordingCatalogEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadEntrySummary(reader));
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

    public Task<byte[]?> GetThumbnailPngAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSchema();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT thumbnail FROM recordings WHERE id = $id;";
            command.Parameters.AddWithValue("$id", recordingId.ToString());
            return command.ExecuteScalar() as byte[];
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
        int? probedVideoBitrateKbps,
        int? probedAudioBitrateKbps,
        Guid? existingId,
        string? existingDisplayName,
        byte[]? existingThumbnail,
        int? existingThumbnailWidth,
        int? existingThumbnailHeight)
    {
        bool hasAudio = !string.IsNullOrWhiteSpace(encoding?.AudioCodec ?? probedAudioCodec);
        int? bitrateKbps = mediaKind == RecordingMediaKind.Audio
            ? probedAudioBitrateKbps
                ?? RecordingMediaProbe.SanitizeAacBitrateKbps(encoding?.BitrateKbps)
                ?? RecordingMediaProbe.SanitizeAacBitrateKbps(encoding?.AudioBitrateKbps)
            : probedVideoBitrateKbps ?? encoding?.BitrateKbps;
        int? audioBitrateKbps = hasAudio && mediaKind == RecordingMediaKind.Video
            ? probedAudioBitrateKbps ?? RecordingMediaProbe.SanitizeAacBitrateKbps(encoding?.AudioBitrateKbps)
            : null;

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
            BitrateKbps = bitrateKbps,
            AudioBitrateKbps = hasAudio && mediaKind == RecordingMediaKind.Video ? audioBitrateKbps : null,
            ThumbnailPng = existingThumbnail,
            ThumbnailWidth = existingThumbnailWidth,
            ThumbnailHeight = existingThumbnailHeight
        };
    }

    private void UpsertEntry(RecordingCatalogEntry entry)
    {
        using var connection = OpenConnection();
        UpsertEntry(connection, transaction: null, entry);
    }

    private static void UpsertEntry(SqliteConnection connection, SqliteTransaction? transaction, RecordingCatalogEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO recordings (
                id, file_path, display_name, file_hash, file_size, last_write_time_utc,
                recorded_at, media_kind, source_kind, duration_ms, video_codec, audio_codec,
                width, height, frame_rate, bitrate_kbps, audio_bitrate_kbps, thumbnail, thumbnail_width, thumbnail_height, updated_at)
            VALUES (
                $id, $filePath, $displayName, $fileHash, $fileSize, $lastWriteTimeUtc,
                $recordedAt, $mediaKind, $sourceKind, $durationMs, $videoCodec, $audioCodec,
                $width, $height, $frameRate, $bitrateKbps, $audioBitrateKbps, $thumbnail, $thumbnailWidth, $thumbnailHeight, $updatedAt)
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
                audio_bitrate_kbps = COALESCE(excluded.audio_bitrate_kbps, recordings.audio_bitrate_kbps),
                thumbnail = COALESCE(excluded.thumbnail, recordings.thumbnail),
                thumbnail_width = COALESCE(excluded.thumbnail_width, recordings.thumbnail_width),
                thumbnail_height = COALESCE(excluded.thumbnail_height, recordings.thumbnail_height),
                updated_at = excluded.updated_at;
            """;
        BindEntryParameters(command, entry);
        command.ExecuteNonQuery();
    }

    private static void UpdateFileIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RecordingCatalogEntry existing,
        FileInfo fileInfo)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
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

    private List<RecordingCatalogEntry> LoadAllEntries()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, file_path, display_name, file_hash, file_size, last_write_time_utc,
                   recorded_at, media_kind, source_kind, duration_ms, video_codec, audio_codec,
                   width, height, frame_rate, bitrate_kbps, audio_bitrate_kbps, thumbnail, thumbnail_width, thumbnail_height
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

    private List<EntryRef> LoadEntryRefs()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, file_path, file_hash FROM recordings;";

        var results = new List<EntryRef>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new EntryRef(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return results;
    }

    private readonly record struct EntryRef(Guid Id, string FilePath, string FileHash);

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
            AudioBitrateKbps = reader.IsDBNull(16) ? null : reader.GetInt32(16),
            ThumbnailPng = reader.IsDBNull(17) ? null : (byte[])reader.GetValue(17),
            ThumbnailWidth = reader.IsDBNull(18) ? null : reader.GetInt32(18),
            ThumbnailHeight = reader.IsDBNull(19) ? null : reader.GetInt32(19)
        };
    }

    private static RecordingCatalogEntry ReadEntrySummary(SqliteDataReader reader)
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
            AudioBitrateKbps = reader.IsDBNull(16) ? null : reader.GetInt32(16)
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
        command.Parameters.AddWithValue("$audioBitrateKbps", entry.AudioBitrateKbps ?? (object)DBNull.Value);
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
            TryAddColumn(connection, "audio_bitrate_kbps", "INTEGER NULL");
            _schemaInitialized = true;
        }
    }

    private static void TryAddColumn(SqliteConnection connection, string columnName, string columnType)
    {
        using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText = "PRAGMA table_info(recordings);";
        using var reader = infoCommand.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE recordings ADD COLUMN {columnName} {columnType};";
        alterCommand.ExecuteNonQuery();
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

    private ExistingEntryRef? FindExistingEntry(string fullPath, string hash)
    {
        string normalizedPath = NormalizePath(fullPath);
        return FindEntryByIdentity("file_path", normalizedPath)
            ?? FindEntryByIdentity("file_hash", hash);
    }

    private static ExistingEntryRef? FindEntryByIdentity(string column, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, display_name, thumbnail, thumbnail_width, thumbnail_height
            FROM recordings
            WHERE {column} = $value COLLATE NOCASE
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$value", value);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ExistingEntryRef(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4));
    }

    private sealed record ExistingEntryRef(
        Guid Id,
        string DisplayName,
        byte[]? ThumbnailPng,
        int? ThumbnailWidth,
        int? ThumbnailHeight);
}
