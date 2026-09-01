using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using DawnCapture.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using Point = Windows.Foundation.Point;

namespace DawnCapture.Views;

public sealed partial class RegionPickerWindow : Window
{
    private const double MinimumSelectionSize = 16;
    private const double HandleHitSize = 10;

    private readonly TaskCompletionSource<RectInt32?> _tcs = new();
    private readonly Task _desktopImageTask;

    private Point _anchor;
    private Rect _selection;
    private Rect _snapshot;
    private DragMode _dragMode;
    private bool _hasSelection;

    public RegionPickerWindow(RectInt32 virtualScreenBounds)
    {
        InitializeComponent();

        Title = LocalizationService.GetString("Window_RegionPickerTitle");
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        AppWindow.MoveAndResize(virtualScreenBounds);

        Log.Debug($"RegionPickerWindow created: VirtualScreen={virtualScreenBounds.Width}x{virtualScreenBounds.Height} @({virtualScreenBounds.X},{virtualScreenBounds.Y})");
        _desktopImageTask = LoadDesktopImageAsync(virtualScreenBounds);
    }

    public async Task<RectInt32?> PickAsync()
    {
        await _desktopImageTask;
        Activate();
        return await _tcs.Task;
    }

    private async Task LoadDesktopImageAsync(RectInt32 bounds)
    {
        try
        {
            var bitmap = await Task.Run(() => CaptureScreen(bounds));
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            bitmap.Dispose();

            var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(stream.ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            randomAccessStream.Seek(0);
            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(randomAccessStream);
            DesktopImage.Source = bitmapImage;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load the desktop screenshot", ex);
        }
    }

    private static Bitmap CaptureScreen(RectInt32 bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            bounds.X,
            bounds.Y,
            0,
            0,
            new System.Drawing.Size(bounds.Width, bounds.Height),
            CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsToolbarSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Root.Focus(FocusState.Programmatic);
        Root.CapturePointer(e.Pointer);

        _anchor = point.Position;
        _snapshot = _selection;

        var hit = HitTest(_anchor);
        if (!_hasSelection || hit == DragMode.None)
        {
            _dragMode = DragMode.Draw;
            _selection = new Rect(_anchor.X, _anchor.Y, 0, 0);
            _hasSelection = true;
        }
        else
        {
            _dragMode = hit;
        }

        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }

        var position = e.GetCurrentPoint(Root).Position;
        double deltaX = position.X - _anchor.X;
        double deltaY = position.Y - _anchor.Y;

        if (_dragMode == DragMode.Draw)
        {
            _selection = NormalizeRect(_anchor, position);
        }
        else if (_dragMode == DragMode.Move)
        {
            _selection = new Rect(
                Clamp(_snapshot.X + deltaX, 0, Root.ActualWidth - _snapshot.Width),
                Clamp(_snapshot.Y + deltaY, 0, Root.ActualHeight - _snapshot.Height),
                _snapshot.Width,
                _snapshot.Height);
        }
        else
        {
            _selection = ResizeSelection(_snapshot, _dragMode, deltaX, deltaY);
        }

        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }

        Root.ReleasePointerCapture(e.Pointer);
        _dragMode = DragMode.None;

        if (_selection.Width < MinimumSelectionSize || _selection.Height < MinimumSelectionSize)
        {
            _hasSelection = false;
        }

        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void UpdateSelectionVisuals()
    {
        if (!_hasSelection)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            InfoBorder.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            HintText.Visibility = Visibility.Visible;
            SetHandlesVisibility(Visibility.Collapsed);
            SetShadesVisibility(Visibility.Collapsed);
            return;
        }

        double x = _selection.X;
        double y = _selection.Y;
        double width = _selection.Width;
        double height = _selection.Height;

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
        SelectionBorder.Visibility = Visibility.Visible;

        UpdateShades(x, y, width, height);
        UpdateHandles(x, y, width, height);
        UpdateInfo(x, y, width, height);

        HintText.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = _dragMode == DragMode.Draw
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateShades(double x, double y, double width, double height)
    {
        double rootWidth = Root.ActualWidth;
        double rootHeight = Root.ActualHeight;

        ShadeTop.Visibility = Visibility.Visible;
        Canvas.SetLeft(ShadeTop, 0);
        Canvas.SetTop(ShadeTop, 0);
        ShadeTop.Width = rootWidth;
        ShadeTop.Height = Math.Max(0, y);

        ShadeBottom.Visibility = Visibility.Visible;
        Canvas.SetLeft(ShadeBottom, 0);
        Canvas.SetTop(ShadeBottom, y + height);
        ShadeBottom.Width = rootWidth;
        ShadeBottom.Height = Math.Max(0, rootHeight - y - height);

        ShadeLeft.Visibility = Visibility.Visible;
        Canvas.SetLeft(ShadeLeft, 0);
        Canvas.SetTop(ShadeLeft, y);
        ShadeLeft.Width = Math.Max(0, x);
        ShadeLeft.Height = height;

        ShadeRight.Visibility = Visibility.Visible;
        Canvas.SetLeft(ShadeRight, x + width);
        Canvas.SetTop(ShadeRight, y);
        ShadeRight.Width = Math.Max(0, rootWidth - x - width);
        ShadeRight.Height = height;
    }

    private void UpdateHandles(double x, double y, double width, double height)
    {
        double right = x + width;
        double bottom = y + height;
        double middleX = x + width / 2;
        double middleY = y + height / 2;

        SetHandlePosition(HandleNorthWest, x, y);
        SetHandlePosition(HandleNorth, middleX, y);
        SetHandlePosition(HandleNorthEast, right, y);
        SetHandlePosition(HandleWest, x, middleY);
        SetHandlePosition(HandleEast, right, middleY);
        SetHandlePosition(HandleSouthWest, x, bottom);
        SetHandlePosition(HandleSouth, middleX, bottom);
        SetHandlePosition(HandleSouthEast, right, bottom);
        SetHandlesVisibility(Visibility.Visible);
    }

    private void UpdateInfo(double x, double y, double width, double height)
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        int screenX = AppWindow.Position.X + (int)Math.Round(x * scale);
        int screenY = AppWindow.Position.Y + (int)Math.Round(y * scale);
        int physicalWidth = (int)Math.Round(width * scale);
        int physicalHeight = (int)Math.Round(height * scale);

        InfoText.Text = $"X: {screenX}  Y: {screenY}   {physicalWidth} × {physicalHeight}";
        InfoBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(InfoBorder, x);
        Canvas.SetTop(InfoBorder, y >= 34 ? y - 30 : y + height + 6);
    }

    private DragMode HitTest(Point point)
    {
        if (!_hasSelection)
        {
            return DragMode.None;
        }

        double left = _selection.X;
        double top = _selection.Y;
        double right = _selection.X + _selection.Width;
        double bottom = _selection.Y + _selection.Height;
        double middleX = left + _selection.Width / 2;
        double middleY = top + _selection.Height / 2;

        if (IsNear(point, left, top)) return DragMode.NorthWest;
        if (IsNear(point, middleX, top)) return DragMode.North;
        if (IsNear(point, right, top)) return DragMode.NorthEast;
        if (IsNear(point, left, middleY)) return DragMode.West;
        if (IsNear(point, right, middleY)) return DragMode.East;
        if (IsNear(point, left, bottom)) return DragMode.SouthWest;
        if (IsNear(point, middleX, bottom)) return DragMode.South;
        if (IsNear(point, right, bottom)) return DragMode.SouthEast;

        return point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom
            ? DragMode.Move
            : DragMode.None;
    }

    private Rect ResizeSelection(Rect source, DragMode mode, double deltaX, double deltaY)
    {
        double left = source.X;
        double top = source.Y;
        double right = source.X + source.Width;
        double bottom = source.Y + source.Height;

        if (mode is DragMode.NorthWest or DragMode.West or DragMode.SouthWest)
        {
            left = Clamp(source.X + deltaX, 0, right - MinimumSelectionSize);
        }

        if (mode is DragMode.NorthEast or DragMode.East or DragMode.SouthEast)
        {
            right = Clamp(right + deltaX, left + MinimumSelectionSize, Root.ActualWidth);
        }

        if (mode is DragMode.NorthWest or DragMode.North or DragMode.NorthEast)
        {
            top = Clamp(source.Y + deltaY, 0, bottom - MinimumSelectionSize);
        }

        if (mode is DragMode.SouthWest or DragMode.South or DragMode.SouthEast)
        {
            bottom = Clamp(bottom + deltaY, top + MinimumSelectionSize, Root.ActualHeight);
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private static Rect NormalizeRect(Point first, Point second)
    {
        return new Rect(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(first.X - second.X),
            Math.Abs(first.Y - second.Y));
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static bool IsNear(Point point, double x, double y)
    {
        return Math.Abs(point.X - x) <= HandleHitSize
            && Math.Abs(point.Y - y) <= HandleHitSize;
    }

    private static void SetHandlePosition(FrameworkElement handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - handle.Width / 2);
        Canvas.SetTop(handle, y - handle.Height / 2);
    }

    private void SetHandlesVisibility(Visibility visibility)
    {
        HandleNorthWest.Visibility = visibility;
        HandleNorth.Visibility = visibility;
        HandleNorthEast.Visibility = visibility;
        HandleWest.Visibility = visibility;
        HandleEast.Visibility = visibility;
        HandleSouthWest.Visibility = visibility;
        HandleSouth.Visibility = visibility;
        HandleSouthEast.Visibility = visibility;
    }

    private void SetShadesVisibility(Visibility visibility)
    {
        ShadeTop.Visibility = visibility;
        ShadeLeft.Visibility = visibility;
        ShadeRight.Visibility = visibility;
        ShadeBottom.Visibility = visibility;
    }

    private bool IsToolbarSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, Toolbar))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void CompleteSelection()
    {
        if (!_hasSelection)
        {
            return;
        }

        double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        double x = _selection.X;
        double y = _selection.Y;

        int px = AppWindow.Position.X + (int)Math.Round(x * scale);
        int py = AppWindow.Position.Y + (int)Math.Round(y * scale);
        int width = (int)Math.Round(_selection.Width * scale);
        int height = (int)Math.Round(_selection.Height * scale);

        if (width < MinimumSelectionSize || height < MinimumSelectionSize)
        {
            return;
        }

        var region = new RectInt32
        {
            X = px,
            Y = py,
            Width = width,
            Height = height
        };

        Log.Debug($"Region selection completed: {region.Width}x{region.Height} @({region.X},{region.Y})");
        _tcs.TrySetResult(region);
        Close();
    }

    private void Root_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_hasSelection && HitTest(e.GetPosition(Root)) == DragMode.Move)
        {
            CompleteSelection();
            e.Handled = true;
        }
    }

    private void OnEnterInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_hasSelection)
        {
            CompleteSelection();
            args.Handled = true;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        CompleteSelection();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Cancel();
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Cancel();
    }

    private void Cancel()
    {
        Log.Debug("Region selection canceled because Escape was pressed or the selection was too small.");
        _tcs.TrySetResult(null);
        Close();
    }

    private enum DragMode
    {
        None,
        Draw,
        Move,
        NorthWest,
        North,
        NorthEast,
        West,
        East,
        SouthWest,
        South,
        SouthEast
    }
}
