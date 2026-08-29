using Windows.Graphics;
using Windows.Graphics.Capture;

namespace DawnCapture.Models;

public enum RecordingSourceKind
{
    Screen,
    Window,
    Region
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

    /// <summary>用于屏幕/窗口捕获的 GraphicsCaptureItem，区域捕获时为 null。</summary>
    public GraphicsCaptureItem? Item { get; }

    /// <summary>区域捕获时的屏幕坐标区域，屏幕/窗口捕获时为 null。</summary>
    public RectInt32? Region { get; }
}
