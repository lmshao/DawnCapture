using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;

namespace DawnCapture.Services;

public sealed partial class RecordingService
{
    /// <summary>
    /// Serializes a segment switch against <see cref="StopAsync"/>. Both move the media
    /// objects and the output path, so they must not overlap. The encoder sample pump
    /// never takes this gate - it only compares the calling source against
    /// <see cref="_mediaStreamSource"/> - which is what keeps the gate deadlock-free.
    /// </summary>
    private readonly SemaphoreSlim _pipelineGate = new(1, 1);

    private readonly object _segmentSync = new();
    private readonly List<Task> _retirementTasks = [];
    private MediaPipeline? _activePipeline;
    private volatile bool _segmentRotating;
    private volatile bool _segmentSwitchFailed;
    private int _segmentIndex = 1;
    private long _segmentBaseTicks;
    private long _segmentStartFrames;
    private long _segmentStartAudioSamples;
    private int _maxDurationStopRequested;
    private bool _segmentEnabledForRecording;
    private SegmentLimitMode _segmentLimitModeForRecording;
    private int _segmentMinutesForRecording;
    private int _segmentSizeMbForRecording;
    private int _maxRecordingMinutesForRecording;

    /// <summary>
    /// 1-based number of the file currently being written, or 0 when this recording is
    /// not segmented. The recording dock uses it to label the current segment.
    /// </summary>
    public int CurrentSegmentNumber =>
        _segmentEnabledForRecording ? Volatile.Read(ref _segmentIndex) : 0;

    /// <summary>
    /// Snapshots the limits once per recording, so the segment thresholds stay
    /// consistent with the encoder settings even if the settings page is edited while
    /// recording. Also resets the counters that segment bookkeeping is based on.
    /// </summary>
    private void CaptureRecordingLimits()
    {
        var settings = _settings.Current;
        _segmentEnabledForRecording = settings.SegmentEnabled;
        _segmentLimitModeForRecording = settings.SegmentLimitMode;
        _segmentMinutesForRecording = settings.SegmentMinutes;
        _segmentSizeMbForRecording = settings.SegmentSizeMb;
        _maxRecordingMinutesForRecording = settings.MaxRecordingMinutes;

        _framesWritten = 0;
        _audioSamplesWritten = 0;
        _segmentIndex = 1;
        _segmentBaseTicks = 0;
        _segmentStartFrames = 0;
        _segmentStartAudioSamples = 0;
        _segmentRotating = false;
        _segmentSwitchFailed = false;
        _activePipeline = null;
        lock (_segmentSync)
        {
            _retirementTasks.Clear();
        }

        Interlocked.Exchange(ref _maxDurationStopRequested, 0);
    }

    /// <summary>Recorded time since the current segment started; paused time is excluded.</summary>
    private TimeSpan SegmentElapsed =>
        _stopwatch.Elapsed - TimeSpan.FromTicks(Volatile.Read(ref _segmentBaseTicks));

    private bool IsMaxDurationReached()
    {
        return _maxRecordingMinutesForRecording > 0
            && _stopwatch.Elapsed >= TimeSpan.FromMinutes(_maxRecordingMinutesForRecording);
    }

    private bool IsSegmentSwitchDue()
    {
        if (!_segmentEnabledForRecording)
        {
            return false;
        }

        if (_segmentLimitModeForRecording == SegmentLimitMode.Size)
        {
            if (_segmentSizeMbForRecording <= 0)
            {
                return false;
            }

            ulong threshold = (ulong)_segmentSizeMbForRecording * 1024UL * 1024UL;
            return (Volatile.Read(ref _outputStream)?.Size ?? 0) >= threshold;
        }

        return _segmentMinutesForRecording > 0
            && SegmentElapsed >= TimeSpan.FromMinutes(_segmentMinutesForRecording);
    }

    /// <summary>
    /// Runs on the encoder sample pump. Both limits need work that cannot happen inside
    /// the callback: stopping has to await the transcode task, which is the very task
    /// awaiting this callback.
    /// </summary>
    private void EvaluateRecordingLimits()
    {
        if (IsMaxDurationReached())
        {
            if (Interlocked.CompareExchange(ref _maxDurationStopRequested, 1, 0) == 0)
            {
                Log.Info(
                    $"Maximum recording length reached ({_maxRecordingMinutesForRecording} min); stopping and saving.");
                RaiseNotice(LocalizationService.GetString("Notice_MaxDurationReached"));
                _ = Task.Run(StopAsync);
            }

            return;
        }

        if (IsSegmentSwitchDue())
        {
            RequestSegmentSwitch();
        }
    }

    private void RequestSegmentSwitch()
    {
        if (_segmentSwitchFailed)
        {
            return;
        }

        lock (_segmentSync)
        {
            if (_segmentRotating)
            {
                return;
            }

            _segmentRotating = true;
        }

        _ = Task.Run(SwitchSegmentAsync);
    }

    /// <summary>
    /// Replaces the running encoder with one bound to the next file. The replacement is
    /// built and prepared first and the hand-over is then a pointer swap, so not a single
    /// sample is dropped between the two files; the file that is retired on the way
    /// finishes in the background.
    /// </summary>
    private async Task SwitchSegmentAsync()
    {
        await _pipelineGate.WaitAsync();
        try
        {
            if (!_isRecording || _isStopping || !_segmentEnabledForRecording || _segmentSwitchFailed)
            {
                return;
            }

            int nextSegment = Volatile.Read(ref _segmentIndex) + 1;
            var candidate = _currentSourceKind == RecordingSourceKind.Audio
                ? await BuildAudioPipelineAsync(_audioOptions, nextSegment)
                : await BuildVideoPipelineAsync(_encodeSize, _audioOptions, nextSegment, reuseEffectiveCodec: true);

            if (candidate is null)
            {
                // Nothing was disturbed: this recording simply keeps writing to its
                // current file. Waiting for a retry would hammer the encoder, so a
                // failure disables auto-segment for the rest of the recording.
                _segmentSwitchFailed = true;
                Log.Error("Unable to prepare the next segment; auto-segment is off for this recording.");
                RaiseNotice(LocalizationService.GetString("Notice_SegmentUnavailable"));
                return;
            }

            if (!_isRecording || _isStopping)
            {
                // A stop landed while the replacement was being prepared.
                ReleasePipeline(candidate);
                TryDeleteOutputFile(candidate.OutputPath);
                return;
            }

            long retiredFrames = _framesWritten;
            long retiredSamples = _audioSamplesWritten;
            long retiredStartFrames = _segmentStartFrames;
            long retiredStartSamples = _segmentStartAudioSamples;
            TimeSpan retiredDuration = SegmentElapsed;
            var retiring = _activePipeline;

            _activePipeline = candidate;
            _segmentStartFrames = retiredFrames;
            _segmentStartAudioSamples = retiredSamples;
            Volatile.Write(ref _segmentIndex, nextSegment);
            ActivatePipeline(candidate, _stopwatch.Elapsed);
            _audioPipeline?.RebaseTimestamps();
            lock (_frameLock)
            {
                _hasLastVideoPts = false;
                _lastVideoPts = TimeSpan.Zero;
            }

            Log.Info($"Segment {nextSegment} started; the previous file is finishing in the background.");

            if (retiring is not null)
            {
                RegisterRetirement(FinishRetiredSegmentAsync(
                    retiring,
                    nextSegment - 1,
                    retiredFrames - retiredStartFrames,
                    retiredSamples - retiredStartSamples,
                    retiredDuration));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Segment switch failed", ex);
            _segmentSwitchFailed = true;
        }
        finally
        {
            _segmentRotating = false;
            _pipelineGate.Release();
        }
    }

    /// <summary>
    /// Lets a replaced pipeline drain to its end, then registers the file it wrote. The
    /// pump already answers this pipeline's sample requests with null, which is what
    /// makes its transcode finish.
    /// </summary>
    private async Task FinishRetiredSegmentAsync(
        MediaPipeline pipeline,
        int segmentNumber,
        long frames,
        long audioSamples,
        TimeSpan duration)
    {
        try
        {
            if (pipeline.Task is not null)
            {
                try
                {
                    await pipeline.Task.WaitAsync(TranscodeFinalizeTimeout);
                }
                catch (TimeoutException)
                {
                    Log.Error(
                        $"Segment {segmentNumber} finalize timed out after {frames} frames / {audioSamples} audio samples.");
                    pipeline.Cancellation?.Cancel();
                    TryDeleteOutputFile(pipeline.OutputPath);
                    return;
                }
                catch
                {
                    // Finalization errors are reported by the task continuation.
                }
            }

            bool isAudioOnly = _currentSourceKind == RecordingSourceKind.Audio;
            bool hasOutput = isAudioOnly ? audioSamples > 0 : frames > 0;
            if (!hasOutput)
            {
                TryDeleteOutputFile(pipeline.OutputPath);
                return;
            }

            long sizeBytes = 0;
            try
            {
                sizeBytes = new FileInfo(pipeline.OutputPath).Length;
            }
            catch
            {
                // Only used for the log line below.
            }

            Log.Info(
                $"Segment {segmentNumber} finished: {duration.TotalSeconds:0.0}s, {sizeBytes / (1024 * 1024)} MB, {frames} frames / {audioSamples} audio samples.");
            await TryRegisterRecordingAsync(pipeline.OutputPath, duration, _currentSourceKind, notify: false);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to finish segment {segmentNumber}", ex);
        }
        finally
        {
            ReleasePipeline(pipeline);
        }
    }

    private void RegisterRetirement(Task task)
    {
        lock (_segmentSync)
        {
            _retirementTasks.RemoveAll(static pending => pending.IsCompleted);
            _retirementTasks.Add(task);
        }
    }

    /// <summary>Awaits every file that is still being finalized in the background.</summary>
    private async Task WaitForRetirementsAsync()
    {
        Task[] pending;
        lock (_segmentSync)
        {
            pending = [.. _retirementTasks];
        }

        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(TranscodeFinalizeTimeout);
        }
        catch (TimeoutException)
        {
            Log.Error("A retired segment did not finish in time; the recording stops without waiting any longer.");
        }
        catch
        {
            // Reported by the individual retirement.
        }
    }

    /// <summary>
    /// Releases the encoder objects of the active file. Mirrors the matching part of
    /// <see cref="CleanupCaptureAsync"/>.
    /// </summary>
    private void ReleaseMediaObjects()
    {
        var pipeline = _activePipeline;
        _activePipeline = null;
        Volatile.Write(ref _mediaStreamSource, null);
        _transcoder = null;
        _outputStream = null;
        _currentOutputPath = null;
        _transcodeTask = null;
        _transcodeCancellation = null;

        if (pipeline is not null)
        {
            ReleasePipeline(pipeline);
        }
    }

    private void ReleasePipeline(MediaPipeline pipeline)
    {
        try
        {
            pipeline.Stream.Dispose();
        }
        catch
        {
            // Ignore cleanup exceptions.
        }

        DetachSource(pipeline.Source);
        pipeline.Cancellation?.Dispose();
        pipeline.Cancellation = null;
        pipeline.Task = null;
    }
}
