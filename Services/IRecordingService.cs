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

    /// <summary>先选择屏幕，再在屏幕上拖拽选择区域，按选区裁剪录制。返回是否成功开始。</summary>
    Task<bool> PickScreenAndStartRegionAsync();

    Task StopAsync();

    void Pause();

    void Resume();
}
