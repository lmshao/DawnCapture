using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using System;

namespace DawnCapture.Models;

public partial class RecordingListItem : ObservableObject
{
    public required Guid RecordingId { get; init; }

    public required string FilePath { get; init; }

    public required string Name { get; init; }

    public required string FormatLabel { get; init; }

    public required string UpdatedAtLabel { get; init; }

    public required string SizeLabel { get; init; }

    public required long FileSizeBytes { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsAudio { get; init; }

    public byte[]? ThumbnailPng { get; init; }

    public int? ThumbnailWidth { get; init; }

    public int? ThumbnailHeight { get; init; }

    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _hasThumbnail;

    public bool ShowVideoThumbnail => !IsAudio && HasThumbnail;

    public bool ShowVideoPlaceholder => !IsAudio && !HasThumbnail;

    partial void OnHasThumbnailChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowVideoThumbnail));
        OnPropertyChanged(nameof(ShowVideoPlaceholder));
    }
}
