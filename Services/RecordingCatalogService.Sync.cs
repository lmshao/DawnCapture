using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using Microsoft.Data.Sqlite;

namespace DawnCapture.Services;

public sealed partial class RecordingCatalogService
{
    private async Task SyncLibraryCoreAsync(string outputFolder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSchema();
        await MigrateLegacyJsonCatalogIfNeededAsync();

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
        var pendingEntries = new List<RecordingCatalogEntry>();
        var identityUpdates = new List<(RecordingCatalogEntry Entry, FileInfo FileInfo)>();

        foreach (string filePath in EnumerateCandidateFiles(folder, dbByPath.Keys))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessScannedFileAsync(
                filePath,
                dbByPath,
                dbByHash,
                seenIds,
                seenHashes,
                pendingEntries,
                identityUpdates,
                cancellationToken);
        }

        FlushSyncChanges(identityUpdates, pendingEntries, duplicateIds);
        PruneMissingEntries(folder, seenIds, seenHashes);
    }

    private void FlushSyncChanges(
        List<(RecordingCatalogEntry Entry, FileInfo FileInfo)> identityUpdates,
        List<RecordingCatalogEntry> entries,
        IReadOnlyList<Guid> duplicateIds)
    {
        if (identityUpdates.Count == 0 && entries.Count == 0 && duplicateIds.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach ((RecordingCatalogEntry entry, FileInfo fileInfo) in identityUpdates)
        {
            UpdateFileIdentity(connection, transaction, entry, fileInfo);
        }

        foreach (RecordingCatalogEntry entry in entries)
        {
            UpsertEntry(connection, transaction, entry);
        }

        DeleteEntriesByIds(connection, transaction, duplicateIds);
        transaction.Commit();
    }

    private async Task ProcessScannedFileAsync(
        string filePath,
        Dictionary<string, RecordingCatalogEntry> dbByPath,
        Dictionary<string, RecordingCatalogEntry> dbByHash,
        HashSet<Guid> seenIds,
        HashSet<string> seenHashes,
        List<RecordingCatalogEntry> pendingEntries,
        List<(RecordingCatalogEntry Entry, FileInfo FileInfo)> identityUpdates,
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
            await BackfillMissingMetadataAsync(existingByPath, fileInfo, pendingEntries);
            seenIds.Add(existingByPath.Id);
            seenHashes.Add(existingByPath.FileHash);
            return;
        }

        string hash = await RecordingFileHashHelper.ComputeSha256HexAsync(fileInfo.FullName, cancellationToken);

        if (existingByPath is not null)
        {
            if (string.Equals(existingByPath.FileHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                identityUpdates.Add((existingByPath, fileInfo));
                await BackfillMissingMetadataAsync(existingByPath, fileInfo, pendingEntries);
            }
            else
            {
                await RefreshEntryFromFileAsync(existingByPath, fileInfo, hash, pendingEntries);
            }

            seenIds.Add(existingByPath.Id);
            seenHashes.Add(hash);
            return;
        }

        if (dbByHash.TryGetValue(hash, out RecordingCatalogEntry? existingByHash))
        {
            identityUpdates.Add((existingByHash, fileInfo));
            await BackfillMissingMetadataAsync(existingByHash, fileInfo, pendingEntries);
            seenIds.Add(existingByHash.Id);
            seenHashes.Add(hash);
            return;
        }

        bool isAudio = IsAudioExtension(fileInfo.Extension);
        var mediaKind = isAudio ? RecordingMediaKind.Audio : RecordingMediaKind.Video;
        var probe = await RecordingMediaProbe.ProbeAsync(fileInfo.FullName, mediaKind);

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
            probe.VideoBitrateKbps,
            probe.AudioBitrateKbps,
            existingId: null,
            existingDisplayName: null,
            existingThumbnail: null,
            existingThumbnailWidth: null,
            existingThumbnailHeight: null);

        pendingEntries.Add(imported);
        seenIds.Add(imported.Id);
        seenHashes.Add(hash);
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
        var staleIds = LoadEntryRefs()
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

    private async Task BackfillMissingMetadataAsync(
        RecordingCatalogEntry existing,
        FileInfo fileInfo,
        List<RecordingCatalogEntry> pendingEntries)
    {
        var probe = await RecordingMediaProbe.ProbeAsync(fileInfo.FullName, existing.MediaKind);

        string? videoCodec = existing.VideoCodec ?? probe.VideoCodec;
        string? audioCodec = existing.AudioCodec ?? probe.AudioCodec;
        bool hasAudio = !string.IsNullOrWhiteSpace(audioCodec);
        int? bitrateKbps = existing.MediaKind == RecordingMediaKind.Audio
            ? probe.AudioBitrateKbps ?? RecordingMediaProbe.SanitizeAacBitrateKbps(existing.BitrateKbps)
            : probe.VideoBitrateKbps ?? existing.BitrateKbps;
        int? audioBitrateKbps = existing.MediaKind == RecordingMediaKind.Video && hasAudio
            ? probe.AudioBitrateKbps ?? RecordingMediaProbe.SanitizeAacBitrateKbps(existing.AudioBitrateKbps)
            : existing.AudioBitrateKbps;

        if (string.Equals(existing.VideoCodec, videoCodec, StringComparison.Ordinal)
            && string.Equals(existing.AudioCodec, audioCodec, StringComparison.Ordinal)
            && existing.AudioBitrateKbps == audioBitrateKbps
            && existing.BitrateKbps == bitrateKbps)
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
                BitrateKbps = bitrateKbps,
                AudioBitrateKbps = audioBitrateKbps
            },
            existing.Width,
            existing.Height,
            probe.VideoCodec,
            probe.AudioCodec,
            probe.VideoBitrateKbps,
            probe.AudioBitrateKbps,
            existing.Id,
            existing.DisplayName,
            existing.ThumbnailPng,
            existing.ThumbnailWidth,
            existing.ThumbnailHeight);

        pendingEntries.Add(updated);
    }

    private async Task RefreshEntryFromFileAsync(
        RecordingCatalogEntry existing,
        FileInfo fileInfo,
        string hash,
        List<RecordingCatalogEntry> pendingEntries)
    {
        var probe = await RecordingMediaProbe.ProbeAsync(fileInfo.FullName, existing.MediaKind);

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
                BitrateKbps = existing.MediaKind == RecordingMediaKind.Audio
                    ? probe.AudioBitrateKbps ?? existing.BitrateKbps
                    : probe.VideoBitrateKbps ?? existing.BitrateKbps,
                AudioBitrateKbps = probe.AudioBitrateKbps ?? existing.AudioBitrateKbps
            },
            probe.Width,
            probe.Height,
            probe.VideoCodec,
            probe.AudioCodec,
            probe.VideoBitrateKbps,
            probe.AudioBitrateKbps,
            existing.Id,
            existing.DisplayName,
            existingThumbnail: null,
            existingThumbnailWidth: null,
            existingThumbnailHeight: null);

        pendingEntries.Add(refreshed);
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

    private static void DeleteEntriesByIds(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        foreach (Guid id in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM recordings WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.ExecuteNonQuery();
        }
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

    private async Task MigrateLegacyJsonCatalogIfNeededAsync()
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
                string hash = await RecordingFileHashHelper.ComputeSha256HexAsync(legacy.FilePath);
                var probe = await RecordingMediaProbe.ProbeAsync(legacy.FilePath, legacy.MediaKind);
                var entry = BuildEntryFromFile(
                    fileInfo,
                    hash,
                    legacy.RecordedAt,
                    legacy.MediaKind,
                    legacy.SourceKind,
                    legacy.DurationMs,
                    encoding: null,
                    probe.Width,
                    probe.Height,
                    probe.VideoCodec,
                    probe.AudioCodec,
                    probe.VideoBitrateKbps,
                    probe.AudioBitrateKbps,
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
