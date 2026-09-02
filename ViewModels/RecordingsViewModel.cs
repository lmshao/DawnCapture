using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Models;
using DawnCapture.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace DawnCapture.ViewModels;

public partial class RecordingsViewModel : ObservableObject
{
    public RecordingsViewModel()
    {
        Recordings = new ObservableCollection<RecordingListItem>(
        [
            new RecordingListItem
            {
                Name = "Product demo",
                DateDuration = "Today 09:42 / 02:18",
                TypeLabel = LocalizationService.GetString("Recordings_Type_Full"),
                SizeLabel = "148 MB",
                IsAudio = false
            },
            new RecordingListItem
            {
                Name = "Region recording",
                DateDuration = "Yesterday 22:16 / 00:47",
                TypeLabel = LocalizationService.GetString("Recordings_Type_Region"),
                SizeLabel = "39 MB",
                IsAudio = false
            },
            new RecordingListItem
            {
                Name = "Window recording",
                DateDuration = "Aug 31 / 05:03",
                TypeLabel = LocalizationService.GetString("Recordings_Type_Window"),
                SizeLabel = "312 MB",
                IsAudio = false
            },
            new RecordingListItem
            {
                Name = "Meeting audio",
                DateDuration = "Aug 30 / 08:14",
                TypeLabel = LocalizationService.GetString("Recordings_Type_Audio"),
                SizeLabel = "12 MB",
                IsAudio = true
            }
        ]);

        _allRecordings = Recordings.ToList();
        VideoCount = _allRecordings.Count(x => !x.IsAudio);
        AudioCount = _allRecordings.Count(x => x.IsAudio);
        TotalSizeLabel = "511 MB";
    }

    private readonly System.Collections.Generic.List<RecordingListItem> _allRecordings;

    public ObservableCollection<RecordingListItem> Recordings { get; }

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _videoCount;

    [ObservableProperty]
    private int _audioCount;

    [ObservableProperty]
    private string _totalSizeLabel = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        Recordings.Clear();
        string query = value.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allRecordings
            : _allRecordings.Where(x => x.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase));

        foreach (var item in filtered)
        {
            Recordings.Add(item);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        // UI shell only.
    }
}
