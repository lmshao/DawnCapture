// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;

namespace DawnCapture.Models;

public enum RecordingMediaKind
{
    Video,
    Audio
}

public sealed class RecordingEncodingInfo
{
    public string? VideoCodec { get; init; }

    public string? AudioCodec { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public int? BitrateKbps { get; init; }

    public int? AudioBitrateKbps { get; init; }
}

public sealed class RecordingCatalogEntry
{
    public required Guid Id { get; init; }

    public required string FilePath { get; init; }

    public required string DisplayName { get; init; }

    public required string FileHash { get; init; }

    public required long FileSize { get; init; }

    public required DateTimeOffset LastWriteTimeUtc { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required RecordingMediaKind MediaKind { get; init; }

    public RecordingSourceKind? SourceKind { get; init; }

    public long? DurationMs { get; init; }

    public string? VideoCodec { get; init; }

    public string? AudioCodec { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public int? BitrateKbps { get; init; }

    public int? AudioBitrateKbps { get; init; }

    public byte[]? ThumbnailPng { get; init; }

    public int? ThumbnailWidth { get; init; }

    public int? ThumbnailHeight { get; init; }
}
