using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Capture;

namespace DawnCapture.Helpers;

public static class WindowPreviewHelper
{
    private const int DefaultMaxWidth = 480;

    public static Task<ImageSource?> CaptureThumbnailAsync(
        GraphicsCaptureItem item,
        int maxWidth = DefaultMaxWidth,
        CancellationToken cancellationToken = default)
    {
        return GraphicsCaptureSnapshotHelper.CaptureAsync(item, maxWidth, cancellationToken);
    }
}
