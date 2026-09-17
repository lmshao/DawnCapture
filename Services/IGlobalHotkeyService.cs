// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using DawnCapture.Models;
using System;
namespace DawnCapture.Services;

public interface IGlobalHotkeyService : IDisposable
{
    void Attach(IntPtr hwnd, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Action onToggleRecording, Action onTogglePause);

    bool Apply(HotkeyBinding toggleRecording, HotkeyBinding togglePause);

    void SuspendForCapture();

    bool Probe(HotkeyBinding binding, HotkeyCaptureTarget editingTarget = HotkeyCaptureTarget.None);

    bool IsToggleRecordingRegistered { get; }

    bool IsTogglePauseRegistered { get; }
}
