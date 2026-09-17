// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;
using Windows.Media.Core;

namespace DawnCapture.Services;

public interface IAudioCapturePipeline : IDisposable
{
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

    /// <summary>True while the mix is over full scale, so the audio is being hard clipped.</summary>
    bool IsClipping { get; }

    /// <summary>Samples over full scale so far in this recording.</summary>
    long ClippedSamples { get; }

    /// <summary>
    /// Raised once when the mix starts clipping, from the mixer thread. Used for the one-time
    /// notice: the user needs to know which levels to turn down while it is still happening.
    /// </summary>
    event Action? ClippingStarted;
}
