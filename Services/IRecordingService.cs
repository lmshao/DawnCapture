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

    /// <summary>弹出系统选择器让用户选择窗口，然后开始录制。返回是否成功开始。</summary>
    Task<bool> PickAndStartAsync();

    /// <summary>直接录制主窗口所在显示器（桌面录制）。返回是否成功开始。</summary>
    Task<bool> StartDesktopAsync();

    /// <summary>拖拽选择屏幕区域并按选区裁剪录制。返回是否成功开始。</summary>
    Task<bool> StartRegionAsync();

    Task StopAsync();

    void Pause();

    void Resume();
}
