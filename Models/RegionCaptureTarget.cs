// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using Windows.Graphics;

namespace DawnCapture.Models;

public sealed class RegionCaptureTarget
{
    public RegionCaptureTarget(RectInt32 screenBounds)
    {
        ScreenBounds = screenBounds;
    }

    public RectInt32 ScreenBounds { get; }

    public int Width => ScreenBounds.Width;

    public int Height => ScreenBounds.Height;

    public string Resolution => $"{Width} x {Height}";

    public string PositionLabel => $"X {ScreenBounds.X} / Y {ScreenBounds.Y}";

    public string Summary => $"{Resolution} / {PositionLabel}";
}
