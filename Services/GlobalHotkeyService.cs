// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using DawnCapture.Helpers;
using DawnCapture.Models;
using Microsoft.UI.Dispatching;

namespace DawnCapture.Services;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GwlpWndProc = -4;
    private const uint WmHotkey = 0x0312;

    public const int HotkeyToggleRecording = 1;
    public const int HotkeyTogglePause = 2;
    private const int HotkeyProbe = 99;

    private IntPtr _hwnd;
    private DispatcherQueue _dispatcherQueue = null!;
    private Action _onToggleRecording = () => { };
    private Action _onTogglePause = () => { };
    private readonly WndProcDelegate _wndProcDelegate;
    private IntPtr _originalWndProc;
    private bool _hookInstalled;
    private HotkeyBinding _toggleRecording = HotkeyBinding.ToggleRecordingDefault;
    private HotkeyBinding _togglePause = HotkeyBinding.TogglePauseDefault;
    private bool _recordingRegistered;
    private bool _pauseRegistered;
    private bool _captureSuspended;

    public bool IsToggleRecordingRegistered => _recordingRegistered;

    public bool IsTogglePauseRegistered => _pauseRegistered;

    public GlobalHotkeyService()
    {
        _wndProcDelegate = HandleWindowMessage;
    }

    public void Attach(
        IntPtr hwnd,
        DispatcherQueue dispatcherQueue,
        Action onToggleRecording,
        Action onTogglePause)
    {
        _hwnd = hwnd;
        _dispatcherQueue = dispatcherQueue;
        _onToggleRecording = onToggleRecording;
        _onTogglePause = onTogglePause;
        EnsureHookInstalled();
    }

    public void SuspendForCapture()
    {
        _captureSuspended = true;
        UnregisterCurrentBindings();
    }

    public bool Apply(HotkeyBinding toggleRecording, HotkeyBinding togglePause)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return false;
        }

        _captureSuspended = false;
        UnregisterCurrentBindings();
        _toggleRecording = toggleRecording;
        _togglePause = togglePause;

        _recordingRegistered = TryRegisterBinding(HotkeyToggleRecording, _toggleRecording);
        _pauseRegistered = TryRegisterBinding(HotkeyTogglePause, _togglePause);

        if (!_recordingRegistered)
        {
            Log.Info($"Global hotkey registration failed for toggle recording: {HotkeyHelper.FormatDisplay(_toggleRecording)}");
        }

        if (!_pauseRegistered)
        {
            Log.Info($"Global hotkey registration failed for toggle pause: {HotkeyHelper.FormatDisplay(_togglePause)}");
        }

        return _recordingRegistered || _pauseRegistered;
    }

    public bool Probe(HotkeyBinding binding, HotkeyCaptureTarget editingTarget = HotkeyCaptureTarget.None)
    {
        if (_hwnd == IntPtr.Zero || binding.IsEmpty)
        {
            return false;
        }

        if (BindingMatchesEditingTarget(binding, editingTarget))
        {
            return true;
        }

        if (BindingMatchesActive(binding, HotkeyToggleRecording, _toggleRecording, _recordingRegistered) ||
            BindingMatchesActive(binding, HotkeyTogglePause, _togglePause, _pauseRegistered))
        {
            return true;
        }

        bool registered = RegisterHotKey(
            _hwnd,
            HotkeyProbe,
            HotkeyHelper.ToRegisterHotKeyModifiers(binding.Modifiers),
            binding.VirtualKey);

        if (registered)
        {
            UnregisterHotKey(_hwnd, HotkeyProbe);
        }

        return registered;
    }

    public void Dispose()
    {
        UnregisterCurrentBindings();

        if (_hookInstalled && _originalWndProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GwlpWndProc, _originalWndProc);
            _hookInstalled = false;
            _originalWndProc = IntPtr.Zero;
        }
    }

    private bool TryRegisterBinding(int hotkeyId, HotkeyBinding binding)
    {
        if (binding.IsEmpty)
        {
            return false;
        }

        return RegisterHotKey(
            _hwnd,
            hotkeyId,
            HotkeyHelper.ToRegisterHotKeyModifiers(binding.Modifiers),
            binding.VirtualKey);
    }

    private void UnregisterCurrentBindings()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_recordingRegistered)
        {
            UnregisterHotKey(_hwnd, HotkeyToggleRecording);
            _recordingRegistered = false;
        }

        if (_pauseRegistered)
        {
            UnregisterHotKey(_hwnd, HotkeyTogglePause);
            _pauseRegistered = false;
        }
    }

    private static bool BindingMatchesActive(
        HotkeyBinding candidate,
        int hotkeyId,
        HotkeyBinding activeBinding,
        bool isRegistered)
    {
        return isRegistered && candidate.Equals(activeBinding);
    }

    private bool BindingMatchesEditingTarget(HotkeyBinding candidate, HotkeyCaptureTarget editingTarget)
    {
        return editingTarget switch
        {
            HotkeyCaptureTarget.ToggleRecording => candidate.Equals(_toggleRecording),
            HotkeyCaptureTarget.TogglePause => candidate.Equals(_togglePause),
            _ => false
        };
    }

    private void EnsureHookInstalled()
    {
        if (_hookInstalled || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _originalWndProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        _hookInstalled = _originalWndProc != IntPtr.Zero;
        if (!_hookInstalled)
        {
            Log.Info("Global hotkey window hook installation failed.");
        }
    }

    private IntPtr HandleWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey && !_captureSuspended)
        {
            switch (wParam.ToInt32())
            {
                case HotkeyToggleRecording:
                    EnqueueAction(_onToggleRecording);
                    return IntPtr.Zero;
                case HotkeyTogglePause:
                    EnqueueAction(_onTogglePause);
                    return IntPtr.Zero;
            }
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void EnqueueAction(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }

        return SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
