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

    /// <summary>录制指定窗口（HWND）。返回是否成功开始。</summary>
    Task<bool> StartWindowAsync(IntPtr window);

    /// <summary>直接录制主窗口所在显示器（桌面录制）。返回是否成功开始。</summary>
    Task<bool> StartDesktopAsync();

    /// <summary>拖拽选择屏幕区域并按选区裁剪录制。返回是否成功开始。</summary>
    Task<bool> StartRegionAsync();

    Task StopAsync();

    void Pause();

    void Resume();
}
