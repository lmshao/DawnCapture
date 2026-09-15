using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;
using Windows.Media.Core;

namespace DawnCapture.Services;

public interface IAudioCapturePipeline : IDisposable
{
    bool HasAudio { get; }

    Task StartAsync(RecordingAudioOptions options, Stopwatch clock, CancellationToken cancellationToken);

    void Stop();

    void SetPaused(bool paused);

    void BeginFlush();

    /// <summary>
    /// Makes the next delivered sample the new zero of the timeline, so a segment file
    /// starts at the beginning instead of carrying the offset of the one before it. The
    /// queued audio is kept, which is what makes the two files continuous.
    /// </summary>
    void RebaseTimestamps();

    MediaStreamSample? TryCreateSample();

    /// <summary>Smoothed audio peak level in the range 0..1.</summary>
    double PeakLevel { get; }
}
