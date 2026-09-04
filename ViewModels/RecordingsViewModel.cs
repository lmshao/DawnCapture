using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DawnCapture.ViewModels;

public partial class RecordingsViewModel : ObservableObject
{
    private readonly IRecordingLibraryService _libraryService;
    private readonly IRecordingCatalogService _catalogService;
    private readonly IRecordingService _recordingService;
    private List<RecordingListItem> _allRecordings = [];
    private CancellationTokenSource? _thumbnailLoadCts;

    public RecordingsViewModel(
        IRecordingLibraryService libraryService,
        IRecordingCatalogService catalogService,
        IRecordingService recordingService)
    {
        _libraryService = libraryService;
        _catalogService = catalogService;
        _recordingService = recordingService;
        Recordings = new ObservableCollection<RecordingListItem>();
        _recordingService.StateChanged += OnRecordingStateChanged;
    }

    public ObservableCollection<RecordingListItem> Recordings { get; }

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _videoCount;

    [ObservableProperty]
    private int _audioCount;

    [ObservableProperty]
    private string _videoCountLabel = string.Empty;

    [ObservableProperty]
    private string _audioCountLabel = string.Empty;

    [ObservableProperty]
    private string _totalSizeLabel = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasRecordings;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private RecordingSortField _sortField = RecordingSortField.UpdatedAt;

    [ObservableProperty]
    private bool _sortDescending = true;

    public string NameSortIndicator => GetSortIndicator(RecordingSortField.Name);

    public string UpdatedSortIndicator => GetSortIndicator(RecordingSortField.UpdatedAt);

    public string SizeSortIndicator => GetSortIndicator(RecordingSortField.FileSize);

    public string NameColumnHeader =>
        LocalizationService.GetString("Recordings_Column_Name") + NameSortIndicator;

    public string UpdatedColumnHeader =>
        LocalizationService.GetString("Recordings_Column_Updated") + UpdatedSortIndicator;

    public string SizeColumnHeader =>
        LocalizationService.GetString("Recordings_Column_Size") + SizeSortIndicator;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSortFieldChanged(RecordingSortField value)
    {
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(UpdatedSortIndicator));
        OnPropertyChanged(nameof(SizeSortIndicator));
        OnPropertyChanged(nameof(NameColumnHeader));
        OnPropertyChanged(nameof(UpdatedColumnHeader));
        OnPropertyChanged(nameof(SizeColumnHeader));
        ApplyFilter();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(UpdatedSortIndicator));
        OnPropertyChanged(nameof(SizeSortIndicator));
        OnPropertyChanged(nameof(NameColumnHeader));
        OnPropertyChanged(nameof(UpdatedColumnHeader));
        OnPropertyChanged(nameof(SizeColumnHeader));
        ApplyFilter();
    }

    [RelayCommand]
    private void SortByName() => ToggleSort(RecordingSortField.Name, defaultDescending: false);

    [RelayCommand]
    private void SortByUpdatedAt() => ToggleSort(RecordingSortField.UpdatedAt, defaultDescending: true);

    [RelayCommand]
    private void SortBySize() => ToggleSort(RecordingSortField.FileSize, defaultDescending: true);

    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            _thumbnailLoadCts?.Cancel();
            _thumbnailLoadCts?.Dispose();
            _thumbnailLoadCts = new CancellationTokenSource();
            var thumbnailToken = _thumbnailLoadCts.Token;

            _allRecordings = (await _libraryService.LoadRecordingsAsync()).ToList();
            UpdateSummary();
            ApplyFilter();
            QueueThumbnailLoads(thumbnailToken);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        string folder = _libraryService.GetOutputFolder();
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open recordings folder '{folder}'", ex);
        }
    }

    [RelayCommand]
    private void PlayRecording(RecordingListItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.FilePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to play recording '{item.FilePath}'", ex);
        }
    }

    private void OnRecordingStateChanged(object? sender, RecordingState state)
    {
        if (state == RecordingState.Idle)
        {
            _ = RefreshAsync();
        }
    }

    private void UpdateSummary()
    {
        VideoCount = _allRecordings.Count(x => !x.IsAudio);
        AudioCount = _allRecordings.Count(x => x.IsAudio);
        VideoCountLabel = string.Format(
            LocalizationService.GetString("Recordings_VideosCount"),
            VideoCount);
        AudioCountLabel = string.Format(
            LocalizationService.GetString("Recordings_AudioCount"),
            AudioCount);

        long totalBytes = _allRecordings.Sum(x => x.FileSizeBytes);
        TotalSizeLabel = string.Format(
            LocalizationService.GetString("Recordings_TotalSize"),
            FileSizeFormatHelper.Format(totalBytes));
        IsEmpty = _allRecordings.Count == 0;
        HasRecordings = !IsEmpty;
    }

    private void ApplyFilter()
    {
        Recordings.Clear();
        string query = SearchQuery.Trim();
        IEnumerable<RecordingListItem> filtered = string.IsNullOrEmpty(query)
            ? _allRecordings
            : _allRecordings.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        filtered = SortField switch
        {
            RecordingSortField.Name => SortDescending
                ? filtered.OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                : filtered.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
            RecordingSortField.FileSize => SortDescending
                ? filtered.OrderByDescending(x => x.FileSizeBytes)
                : filtered.OrderBy(x => x.FileSizeBytes),
            _ => SortDescending
                ? filtered.OrderByDescending(x => x.UpdatedAt)
                : filtered.OrderBy(x => x.UpdatedAt)
        };

        foreach (RecordingListItem item in filtered)
        {
            Recordings.Add(item);
        }
    }

    private void ToggleSort(RecordingSortField field, bool defaultDescending)
    {
        if (SortField == field)
        {
            SortDescending = !SortDescending;
            return;
        }

        SortField = field;
        SortDescending = defaultDescending;
    }

    private string GetSortIndicator(RecordingSortField field)
    {
        if (SortField != field)
        {
            return string.Empty;
        }

        return SortDescending ? " ↓" : " ↑";
    }

    private void QueueThumbnailLoads(CancellationToken cancellationToken)
    {
        foreach (RecordingListItem item in _allRecordings)
        {
            if (item.IsAudio || item.HasThumbnail)
            {
                continue;
            }

            _ = LoadThumbnailAsync(item, cancellationToken);
        }
    }

    private async Task LoadThumbnailAsync(RecordingListItem item, CancellationToken cancellationToken)
    {
        try
        {
            DispatcherQueue? dispatcher = App.MainWindow?.DispatcherQueue;
            if (dispatcher is null)
            {
                return;
            }

            if (item.ThumbnailPng is { Length: > 0 })
            {
                byte[] cached = item.ThumbnailPng;
                dispatcher.TryEnqueue(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    item.Thumbnail = RecordingThumbnailHelper.CreateBitmapFromPng(cached);
                    item.HasThumbnail = true;
                });
                return;
            }

            ThumbnailPixelData? pixelData = await RecordingThumbnailHelper.LoadPixelDataAsync(
                item.FilePath,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || pixelData is null)
            {
                return;
            }

            ThumbnailPixelData pixels = pixelData.Value;
            byte[] pngBytes = RecordingThumbnailHelper.CreatePngBytes(pixels);
            await _catalogService.SaveThumbnailAsync(
                item.RecordingId,
                pngBytes,
                pixels.Width,
                pixels.Height,
                cancellationToken);

            dispatcher.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                item.Thumbnail = RecordingThumbnailHelper.CreateWriteableBitmap(pixels);
                item.HasThumbnail = true;
            });
        }
        catch (OperationCanceledException)
        {
            // Refresh replaced the in-flight thumbnail request.
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to load thumbnail for '{item.FilePath}': {ex}");
        }
    }

}
