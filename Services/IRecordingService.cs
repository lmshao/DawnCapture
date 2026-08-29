using System;
using System.Threading.Tasks;
using DawnCapture.Models;

namespace DawnCapture.Services;

public interface IRecordingService
{
    RecordingState State { get; }

    TimeSpan Elapsed { get; }

    event EventHandler<RecordingState>? StateChanged;

    event EventHandler<string>? RecordingFailed;

    /// <summary>弹出系统选择器让用户选择屏幕/窗口，然后开始录制。返回是否成功开始。</summary>
    Task<bool> PickAndStartAsync();

    Task StopAsync();

    void Pause();

    void Resume();
}
