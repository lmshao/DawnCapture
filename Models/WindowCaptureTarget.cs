using System;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;

namespace DawnCapture.Models;

public sealed class WindowCaptureTarget : IDisposable
{
    private readonly TypedEventHandler<GraphicsCaptureItem, object> _closedHandler;

    public WindowCaptureTarget(GraphicsCaptureItem item)
    {
        Item = item;
        DisplayName = item.DisplayName;
        Width = item.Size.Width;
        Height = item.Size.Height;
        _closedHandler = (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        item.Closed += _closedHandler;
    }

    public GraphicsCaptureItem Item { get; }

    public string DisplayName { get; }

    public int Width { get; }

    public int Height { get; }

    public string Resolution => $"{Width} x {Height}";

    public event EventHandler? Closed;

    public void Dispose()
    {
        Item.Closed -= _closedHandler;
    }
}
