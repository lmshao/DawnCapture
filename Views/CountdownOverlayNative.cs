// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace DawnCapture.Views;

public sealed class CountdownOverlayNative : IDisposable
{
    private const int OverlayWidth = 200;
    private const int OverlayHeight = 200;
    private const int VkEscape = 0x1B;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsPopup = 0x80000000;
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const byte UlwAlpha = 2;

    private readonly RectInt32 _position;
    private IntPtr _hwnd;

    public CountdownOverlayNative(RectInt32 monitorBounds)
    {
        _position = ComputeOverlayBounds(monitorBounds);
        _hwnd = CreateOverlayWindow(_position);
        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public static RectInt32 ComputeOverlayBounds(RectInt32 monitorBounds)
    {
        int width = Math.Min(OverlayWidth, monitorBounds.Width);
        int height = Math.Min(OverlayHeight, monitorBounds.Height);
        return new RectInt32
        {
            X = monitorBounds.X + Math.Max(0, (monitorBounds.Width - width) / 2),
            Y = monitorBounds.Y + Math.Max(0, (monitorBounds.Height - height) / 2),
            Width = width,
            Height = height
        };
    }

    public async Task<bool> RunCountdownAsync(int seconds, CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            for (int remaining = seconds; remaining >= 1; remaining--)
            {
                UpdateDigit(remaining.ToString());
                await DelayWithEscapeWatchAsync(1000, linkedCts.Token);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        IntPtr hwnd = _hwnd;
        _hwnd = IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            DestroyWindow(hwnd);
        }
    }

    private void UpdateDigit(string digit)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        using var bitmap = RenderDigitBitmap(digit, _position.Width, _position.Height);
        UpdateLayeredBitmap(_hwnd, bitmap, _position.X, _position.Y);
    }

    private static Bitmap RenderDigitBitmap(string digit, int width, int height)
    {
        var uiSettings = new UISettings();
        Windows.UI.Color accent = uiSettings.GetColorValue(UIColorType.Accent);
        var accentColor = System.Drawing.Color.FromArgb(255, accent.R, accent.G, accent.B);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var font = new Font("Segoe UI Semibold", 96, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(accentColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        graphics.DrawString(digit, font, brush, new RectangleF(0, 0, width, height), format);
        return bitmap;
    }

    private static IntPtr CreateOverlayWindow(RectInt32 bounds)
    {
        IntPtr hwnd = CreateWindowEx(
            WsExLayered | WsExTopmost | WsExTransparent | WsExToolWindow | WsExNoActivate,
            "STATIC",
            string.Empty,
            WsPopup,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        ShowWindow(hwnd, 4); // SW_SHOWNOACTIVATE
        SetWindowPos(
            hwnd,
            new IntPtr(-1),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            0x0010 | 0x0040); // SWP_NOACTIVATE | SWP_SHOWWINDOW

        return hwnd;
    }

    private static void UpdateLayeredBitmap(IntPtr hwnd, Bitmap bitmap, int x, int y)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
        IntPtr previousBitmap = SelectObject(memoryDc, hBitmap);

        try
        {
            var size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
            var sourcePoint = new NativePoint();
            var topPoint = new NativePoint { X = x, Y = y };
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };

            UpdateLayeredWindow(
                hwnd,
                screenDc,
                ref topPoint,
                ref size,
                memoryDc,
                ref sourcePoint,
                0,
                ref blend,
                UlwAlpha);
        }
        finally
        {
            SelectObject(memoryDc, previousBitmap);
            DeleteObject(hBitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static async Task DelayWithEscapeWatchAsync(int milliseconds, CancellationToken token)
    {
        int elapsed = 0;
        const int stepMs = 50;

        while (elapsed < milliseconds)
        {
            token.ThrowIfCancellationRequested();

            if (IsEscapeDown())
            {
                throw new OperationCanceledException();
            }

            int waitMs = Math.Min(stepMs, milliseconds - elapsed);
            await Task.Delay(waitMs, token);
            elapsed += waitMs;
        }
    }

    private static bool IsEscapeDown() => (GetAsyncKeyState(VkEscape) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize pSize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        int crKey,
        ref BlendFunction pBlend,
        byte dwFlags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
