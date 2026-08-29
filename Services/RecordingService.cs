using System;
using System.Threading.Tasks;
using DawnCapture.Models;

namespace DawnCapture.Services;

/// <summary>
/// 录制服务占位实现，Phase 2 将替换为基于 Windows.Graphics.Capture 的真实实现。
/// </summary>
public sealed class RecordingService : IRecordingService
{
    public RecordingState State => RecordingState.Idle;

    public TimeSpan Elapsed => TimeSpan.Zero;

    public event EventHandler<RecordingState>? StateChanged;

    public event EventHandler<string>? RecordingFailed;

    public Task<bool> PickAndStartAsync()
    {
        return Task.FromResult(false);
    }

    public Task StopAsync()
    {
        return Task.CompletedTask;
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }
}
