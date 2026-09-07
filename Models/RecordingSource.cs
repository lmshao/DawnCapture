using Windows.Graphics;
using Windows.Graphics.Capture;

namespace DawnCapture.Models;

public enum RecordingSourceKind
{
    Screen,
    Window,
    Region,
    Audio
}

public sealed class RecordingSource
{
    public RecordingSource(RecordingSourceKind kind, GraphicsCaptureItem? item = null, RectInt32? region = null)
    {
        Kind = kind;
        Item = item;
        Region = region;
    }

    public RecordingSourceKind Kind { get; }

    /// <summary>The GraphicsCaptureItem used for display or window capture; null for region capture.</summary>
    public GraphicsCaptureItem? Item { get; }

    /// <summary>The screen-coordinate bounds used for region capture; null for display or window capture.</summary>
    public RectInt32? Region { get; }
}
