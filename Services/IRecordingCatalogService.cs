using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;

namespace DawnCapture.Services;

public interface IRecordingCatalogService
{
    Task SyncLibraryAsync(string outputFolder, CancellationToken cancellationToken = default);

    Task RegisterRecordingAsync(
        string filePath,
        DateTimeOffset recordedAt,
        TimeSpan duration,
        RecordingMediaKind mediaKind,
        RecordingSourceKind? sourceKind = null,
        RecordingEncodingInfo? encoding = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecordingCatalogEntry>> GetRecordingsAsync(
        string outputFolder,
        CancellationToken cancellationToken = default);

    Task SaveThumbnailAsync(
        Guid recordingId,
        byte[] thumbnailPng,
        int width,
        int height,
        CancellationToken cancellationToken = default);
}