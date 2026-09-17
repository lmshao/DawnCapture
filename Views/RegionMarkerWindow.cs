// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace DawnCapture.Views;

public sealed class RegionMarkerWindow
{
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsPopup = 0x80000000;
    private const uint SsBitmap = 0x0000000E;
    private const uint LwaColorKey = 0x00000001;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const uint WmClose = 0x0010;
    private const uint StmSetImage = 0x0172;
    private const int ImageBitmap = 0;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int MagentaColorRef = 0x00FF00FF;
    private const int PurpleColorRef = 0x00FC92A8;
    private const int BorderThickness = 1;
    private const int DashLength = 6;
    private const int DashGap = 4;

    private IntPtr _hwnd;
    private IntPtr _bitmap;

    public RegionMarkerWindow(RectInt32 region)
    {
        int width = Math.Max(1, region.Width);
        int height = Math.Max(1, region.Height);

        _bitmap = CreateMarkerBitmap(width, height);
        _hwnd = CreateWindowEx(
            WsExTopmost | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate,
            "STATIC",
            string.Empty,
            WsPopup | SsBitmap,
            region.X,
            region.Y,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            DeleteObject(_bitmap);
            _bitmap = IntPtr.Zero;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        SendMessage(_hwnd, StmSetImage, new IntPtr(ImageBitmap), _bitmap);
        SetLayeredWindowAttributes(_hwnd, MagentaColorRef, 255, LwaColorKey);
        SetWindowDisplayAffinity(_hwnd, WdaExcludeFromCapture);
        ShowWindow(_hwnd, SwShowNoActivate);
        SetWindowPos(
            _hwnd,
            new IntPtr(-1),
            region.X,
            region.Y,
            width,
            height,
            SwpNoActivate | SwpShowWindow);
    }

    public void Close()
    {
        IntPtr hwnd = _hwnd;
        IntPtr bitmap = _bitmap;
        _hwnd = IntPtr.Zero;
        _bitmap = IntPtr.Zero;

        if (hwnd != IntPtr.Zero)
        {
            SendMessage(hwnd, StmSetImage, new IntPtr(ImageBitmap), IntPtr.Zero);
            PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        if (bitmap != IntPtr.Zero)
        {
            DeleteObject(bitmap);
        }
    }

    private static IntPtr CreateMarkerBitmap(int width, int height)
    {
        IntPtr screenContext = GetDC(IntPtr.Zero);
        IntPtr memoryContext = CreateCompatibleDC(screenContext);
        IntPtr bitmap = CreateCompatibleBitmap(screenContext, width, height);
        IntPtr previousBitmap = SelectObject(memoryContext, bitmap);

        try
        {
            var client = new NativeRect { Right = width, Bottom = height };
            IntPtr backgroundBrush = CreateSolidBrush(MagentaColorRef);
            FillRect(memoryContext, ref client, backgroundBrush);
            DeleteObject(backgroundBrush);

            IntPtr borderBrush = CreateSolidBrush(PurpleColorRef);
            DrawHorizontalDashes(memoryContext, borderBrush, width, 0);
            DrawHorizontalDashes(
                memoryContext,
                borderBrush,
                width,
                Math.Max(0, height - BorderThickness));
            DrawVerticalDashes(memoryContext, borderBrush, height, 0);
            DrawVerticalDashes(
                memoryContext,
                borderBrush,
                height,
                Math.Max(0, width - BorderThickness));
            DeleteObject(borderBrush);
        }
        finally
        {
            SelectObject(memoryContext, previousBitmap);
            DeleteDC(memoryContext);
            ReleaseDC(IntPtr.Zero, screenContext);
        }

        return bitmap;
    }

    private static void DrawHorizontalDashes(IntPtr deviceContext, IntPtr brush, int width, int y)
    {
        for (int x = 0; x < width; x += DashLength + DashGap)
        {
            var dash = new NativeRect
            {
                Left = x,
                Top = y,
                Right = Math.Min(width, x + DashLength),
                Bottom = y + BorderThickness
            };
            FillRect(deviceContext, ref dash, brush);
        }
    }

    private static void DrawVerticalDashes(IntPtr deviceContext, IntPtr brush, int height, int x)
    {
        for (int y = 0; y < height; y += DashLength + DashGap)
        {
            var dash = new NativeRect
            {
                Left = x,
                Top = y,
                Right = x + BorderThickness,
                Bottom = Math.Min(height, y + DashLength)
            };
            FillRect(deviceContext, ref dash, brush);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr deviceContext, ref NativeRect rect, IntPtr brush);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr hwnd,
        int colorKey,
        byte alpha,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

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
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
