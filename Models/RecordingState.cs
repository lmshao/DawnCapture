// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

namespace DawnCapture.Models;

public enum RecordingState
{
    Idle,
    PickingSource,
    Recording,
    Paused,
    Stopping
}
