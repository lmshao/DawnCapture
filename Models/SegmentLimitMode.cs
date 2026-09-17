// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

namespace DawnCapture.Models;

/// <summary>
/// How a recording is split into segments while auto-segmentation is on.
/// </summary>
public enum SegmentLimitMode
{
    Duration,
    Size
}
