using System;
using Windows.Graphics;

namespace DawnCapture.Helpers;

public static class RegionBoundsHelper
{
    public const int MinimumEncodeSize = 2;

    public static RectInt32 NormalizeForEncoding(RectInt32 region)
    {
        return new RectInt32
        {
            X = region.X,
            Y = region.Y,
            Width = NormalizeDimension(region.Width),
            Height = NormalizeDimension(region.Height)
        };
    }

    public static int NormalizeDimension(int value)
    {
        return Math.Max(MinimumEncodeSize, value & ~1);
    }
}
