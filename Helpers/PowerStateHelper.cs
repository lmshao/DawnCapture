// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace DawnCapture.Helpers;

/// <summary>
/// Power management while recording: prevents system sleep and display off.
/// SetThreadExecutionState is per-thread and resets automatically when the process exits.
/// </summary>
public static class PowerStateHelper
{
    private const int EsContinuous = unchecked((int)0x80000000);
    private const int EsSystemRequired = 0x00000001;
    private const int EsDisplayRequired = 0x00000002;

    [DllImport("kernel32.dll")]
    private static extern int SetThreadExecutionState(int flags);

    public static void PreventSleepDuringRecording()
        => SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);

    public static void AllowSleep()
        => SetThreadExecutionState(EsContinuous);
}
