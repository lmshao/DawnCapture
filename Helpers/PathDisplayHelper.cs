// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;

namespace DawnCapture.Helpers;

public static class PathDisplayHelper
{
    public static string MiddleEllipsis(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
        {
            return path;
        }

        string root = path.Length >= 3 && path[1] == ':' && path[2] == '\\'
            ? path[..3]
            : string.Empty;

        string[] parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return path;
        }

        string trailing = parts.Length >= 2
            ? string.Join('\\', parts[^2], parts[^1])
            : parts[^1];

        string compact = root + "…\\" + trailing;
        return compact.Length <= maxLength ? compact : root + "…\\" + parts[^1];
    }

    public static string CompactFolderSummary(string path, string defaultFolder, string defaultLabel)
    {
        if (string.Equals(path, defaultFolder, StringComparison.OrdinalIgnoreCase))
        {
            return defaultLabel;
        }

        return MiddleEllipsis(path, 38);
    }

    public static string CompactDockFolderSummary(string path, string defaultFolder, string defaultLabel)
    {
        if (string.Equals(path, defaultFolder, StringComparison.OrdinalIgnoreCase))
        {
            return defaultLabel;
        }

        return MiddleEllipsis(path, 26);
    }
}
