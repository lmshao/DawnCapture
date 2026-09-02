using Microsoft.UI.Xaml.Media;

namespace DawnCapture.Models;

public class MonitorDisplay
{
    public string Name { get; init; } = string.Empty;
    public string Resolution { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
    public ImageSource? Thumbnail { get; init; }
}
