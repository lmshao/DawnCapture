// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services;
using Windows.Media.Core;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace DawnCapture.Services.Audio;

public sealed class AudioCapturePipeline : IAudioCapturePipeline
{
    /// <summary>
    /// One live capture source plus the buffers the mixer needs for it. The resampler is built
    /// from the rate the device actually opened at, so it must be created after Start().
    /// </summary>
    private sealed class SourceRuntime
    {
        public SourceRuntime(WasapiCaptureDevice device, string label)
        {
            Device = device;
            Label = label;
            Resampler = new StereoResampleBuffer(device.InputSampleRate);
        }

        public WasapiCaptureDevice Device { get; }

        public string Label { get; }

        public StereoResampleBuffer Resampler { get; }

        /// <summary>Scratch for one 48 kHz chunk, reused on every mix tick.</summary>
        public float[] Scratch { get; } = new float[AudioFormat.SamplesPerChunk * AudioFormat.Channels];

        /// <summary>Peak of the last chunk this source contributed (per-source metering, stage 2).</summary>
        public float Peak { get; set; }

        /// <summary>Frames already reported by the silence log.</summary>
        public long ReportedFrames { get; set; }
    }

    private readonly List<SourceRuntime> _sources = new();
    private WasapiCaptureDevice? _microphone;
    private WasapiCaptureDevice? _loopback;
    private Thread? _mixerThread;
    private volatile bool _running;
    private volatile bool _paused;

    private readonly ConcurrentQueue<(byte[] Buffer, TimeSpan Timestamp)> _outputQueue = new();
    private readonly long _chunkDurationTicks = TimeSpan.TicksPerSecond * AudioFormat.SamplesPerChunk / AudioFormat.SampleRate;
    private Stopwatch? _clock;
    private volatile bool _flushMode;
    private TimeSpan _timestampOrigin;
    private volatile bool _rebasePending;

    private volatile float _peakLevel;
    private long _quietChunks;
    private bool _silenceNoticed;

    private int _clippedRun;
    private int _cleanRun;
    private long _clippedChunks;
    private long _clippedSamples;
    private volatile bool _isClipping;

    /// <summary>20 ms chunks without an audible peak before a log line is worth writing (~4 s).</summary>
    private const int QuietChunksBeforeNotice = 200;

    /// <summary>Below this smoothed peak the mix counts as silent.</summary>
    private const float AudiblePeakThreshold = 0.005f;

    /// <summary>
    /// Applied to every source while two sources are recording. Both are scaled by the same factor,
    /// so the voice/music balance is untouched and only the overall level drops by 3 dB - the point
    /// is headroom: with the microphone peaking at 0.43 (-7 dBFS, a typical speaking level) the sum
    /// still fits inside full scale. The user can compensate with the two Windows level sliders,
    /// both of which act before quantization. A single source is never attenuated.
    /// </summary>
    private const float DualSourceGain = 0.7f;

    /// <summary>Clipped chunks needed before the overload is reported (3 x 20 ms).</summary>
    private const int ClippingChunksToSet = 3;

    /// <summary>Clean chunks that clear the report again (50 x 20 ms = 1 s).</summary>
    private const int ClippingChunksToClear = 50;

    /// <summary>False when the caller asked for no source at all; drives the sample pump.</summary>
    private bool HasAudio { get; set; }

    public double PeakLevel => _peakLevel;

    public bool IsClipping => _isClipping;

    public long ClippedSamples => Interlocked.Read(ref _clippedSamples);

    public event Action? ClippingStarted;

    public async Task StartAsync(RecordingAudioOptions options, Stopwatch clock, CancellationToken cancellationToken)
    {
        HasAudio = options.HasAnySource;
        if (!HasAudio)
        {
            return;
        }

        if (options.EnableMicrophone)
        {
            await LogMicrophoneAccessAsync();
        }

        _flushMode = false;
        _clock = clock;
        DrainOutputQueue();

        _sources.Clear();
        ResetClippingState();

        if (options.EnableMicrophone)
        {
            _microphone = new WasapiCaptureDevice(loopback: false, options.MicrophoneDeviceId);
            _microphone.Start();
            _sources.Add(new SourceRuntime(_microphone, "mic"));
        }

        if (options.EnableSystemAudio)
        {
            _loopback = new WasapiCaptureDevice(loopback: true, options.SystemAudioDeviceId);
            _loopback.Start();
            _sources.Add(new SourceRuntime(_loopback, "system"));
        }

        _running = true;
        _mixerThread = new Thread(MixerLoop)
        {
            IsBackground = true,
            Name = "AudioMixer",
            Priority = ThreadPriority.AboveNormal
        };
        _mixerThread.Start();

        Log.Info(
            $"Audio pipeline started: mic={options.EnableMicrophone}, system={options.EnableSystemAudio}, " +
            $"bitrate={options.BitrateKbps}kbps, sources={_sources.Count}, gain={SourceGain:0.0}.");
    }

    public void ReportStartupWarnings(RecordingAudioOptions options, Action<string> raiseNotice)
    {
        if (options.EnableMicrophone && (_microphone is null || !_microphone.IsActive))
        {
            raiseNotice(LocalizationService.GetString("Failure_MicrophoneAccess"));
        }

        if (options.EnableSystemAudio && (_loopback is null || !_loopback.IsActive))
        {
            raiseNotice(LocalizationService.GetString("Notice_SystemAudioUnavailable"));
        }

        // A chosen device was not there, so the default one is recording instead. The user has
        // to know: the sound is still being captured, but from a different endpoint.
        if (_microphone?.FellBackToDefaultDevice == true || _loopback?.FellBackToDefaultDevice == true)
        {
            raiseNotice(LocalizationService.GetString("Notice_AudioDeviceMissing"));
        }
    }

    public void Stop()
    {
        _running = false;
        _mixerThread?.Join(TimeSpan.FromSeconds(2));
        _mixerThread = null;

        foreach (var source in _sources)
        {
            source.Device.Dispose();
        }

        _sources.Clear();
        _microphone = null;
        _loopback = null;

        DrainOutputQueue();
        HasAudio = false;
    }

    public void SetPaused(bool paused) => _paused = paused;

    public void BeginFlush() => _flushMode = true;

    public void RebaseTimestamps() => _rebasePending = true;

    public MediaStreamSample? TryCreateSample()
    {
        if (!HasAudio || (_paused && !_flushMode))
        {
            return null;
        }

        while (_running || !_outputQueue.IsEmpty || _flushMode)
        {
            if (_outputQueue.TryPeek(out var next))
            {
                if (!_flushMode && _clock is not null &&
                    _clock.Elapsed + TimeSpan.FromMilliseconds(2) < next.Timestamp)
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (_outputQueue.TryDequeue(out var queued))
                {
                    if (_rebasePending)
                    {
                        _timestampOrigin = queued.Timestamp;
                        _rebasePending = false;
                    }

                    return CreateSample(queued.Buffer, queued.Timestamp - _timestampOrigin);
                }
            }
            else if (!_running || _flushMode)
            {
                return null;
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        return null;
    }

    public void Dispose()
    {
        Stop();
    }

    private void MixerLoop()
    {
        var accumulator = new float[AudioFormat.SamplesPerChunk * AudioFormat.Channels];
        var blockSamples = new short[AudioFormat.SamplesPerChunk * AudioFormat.Channels];
        var mixBuffer = new byte[AudioFormat.BytesPerChunk];
        var nextChunkAt = TimeSpan.Zero;

        while (_running)
        {
            try
            {
                foreach (var source in _sources)
                {
                    AppendDeviceFrames(source);
                }

                if (_paused)
                {
                    foreach (var source in _sources)
                    {
                        source.Resampler.Clear();
                    }

                    Thread.Sleep(20);
                    nextChunkAt = _clock?.Elapsed ?? TimeSpan.Zero;
                    continue;
                }

                var elapsed = _clock?.Elapsed ?? TimeSpan.Zero;
                var producedChunks = 0;
                var gain = SourceGain;

                while (_running && elapsed >= nextChunkAt && producedChunks < 100)
                {
                    accumulator.AsSpan().Clear();

                    foreach (var source in _sources)
                    {
                        if (!source.Resampler.TryReadStereo48k(source.Scratch))
                        {
                            // Nothing from this source in this chunk: it stays silent, the grid
                            // does not move, which is what keeps a late source on the timeline.
                            source.Peak = 0;
                            continue;
                        }

                        var scratch = source.Scratch.AsSpan();
                        if (gain != 1f)
                        {
                            for (var i = 0; i < scratch.Length; i++)
                            {
                                scratch[i] *= gain;
                            }
                        }

                        source.Peak = PcmAudioConverter.MaxAbs(scratch);

                        var mixed = accumulator.AsSpan();
                        for (var i = 0; i < mixed.Length; i++)
                        {
                            mixed[i] += scratch[i];
                        }
                    }

                    // One pass over the finished float mix: level, overload, and the single place
                    // where the signal is limited to what the encoder can store.
                    var peak = PcmAudioConverter.MaxAbs(accumulator);
                    var overFullScale = PcmAudioConverter.ClampAndConvertToInt16(accumulator, blockSamples);
                    UpdateClipping(peak, overFullScale);
                    _peakLevel = Math.Max(_peakLevel * 0.7f, Math.Min(1f, peak));

                    Buffer.BlockCopy(blockSamples, 0, mixBuffer, 0, mixBuffer.Length);
                    _outputQueue.Enqueue((mixBuffer, nextChunkAt));
                    mixBuffer = new byte[AudioFormat.BytesPerChunk];
                    WatchSilence();

                    nextChunkAt += TimeSpan.FromTicks(_chunkDurationTicks);
                    producedChunks++;
                    elapsed = _clock?.Elapsed ?? TimeSpan.Zero;
                }

                if (elapsed > nextChunkAt + TimeSpan.FromTicks(_chunkDurationTicks * 100))
                {
                    var alignedTicks = (elapsed.Ticks / _chunkDurationTicks) * _chunkDurationTicks;
                    nextChunkAt = TimeSpan.FromTicks(alignedTicks);
                }

                Thread.Sleep(1);
            }
            catch (Exception ex)
            {
                Log.Error("Audio mixer loop failed", ex);
                _running = false;
            }
        }
    }

    /// <summary>
    /// Logs when the mix stops being audible and when it becomes audible again, together
    /// with how many frames each device delivered meanwhile. That is what makes "was the
    /// sound captured while the session was locked" answerable from the log alone: silence
    /// with frames still arriving means nothing was playing, silence with no frames at all
    /// means the device itself stopped delivering. A gap between tracks is a normal reason
    /// for the former, so the message states the evidence rather than judging it.
    /// </summary>
    private void WatchSilence()
    {
        if (_peakLevel > AudiblePeakThreshold)
        {
            _quietChunks = 0;
            if (_silenceNoticed)
            {
                _silenceNoticed = false;
                Log.Info("Audio is audible again.");
            }

            return;
        }

        if (++_quietChunks < QuietChunksBeforeNotice || _silenceNoticed)
        {
            return;
        }

        _silenceNoticed = true;
        var frames = string.Empty;
        foreach (var source in _sources)
        {
            long delivered = source.Device.FramesDelivered;
            if (frames.Length > 0)
            {
                frames += ", ";
            }

            frames += $"{source.Label} frames: {delivered - source.ReportedFrames}";
            source.ReportedFrames = delivered;
        }

        Log.Info($"Mix silent {QuietChunksBeforeNotice / 50.0:0.0}s ({frames}).");
    }

    /// <summary>
    /// Every source carries the same factor while two of them are recording, which keeps their
    /// balance intact; a single source is passed through untouched.
    /// </summary>
    private float SourceGain => _sources.Count > 1 ? DualSourceGain : 1f;

    private static void AppendDeviceFrames(SourceRuntime source)
    {
        while (source.Device.TryTakeStereoFrame(out var frames))
        {
            source.Resampler.Append(frames);
        }
    }

    private void ResetClippingState()
    {
        _clippedRun = 0;
        _cleanRun = 0;
        _clippedChunks = 0;
        _isClipping = false;
        Interlocked.Exchange(ref _clippedSamples, 0);
    }

    /// <summary>
    /// Tracks overload from the finished float mix, before it is limited. Detected here rather than
    /// on the 16-bit block on purpose: by the time that block exists the excess is already gone, and
    /// the whole point of the indicator is to tell the user which level to turn down.
    /// </summary>
    private void UpdateClipping(float peak, int overFullScaleSamples)
    {
        if (peak > 1f)
        {
            _clippedChunks++;
            if (overFullScaleSamples > 0)
            {
                Interlocked.Add(ref _clippedSamples, overFullScaleSamples);
            }

            _cleanRun = 0;
            if (!_isClipping && ++_clippedRun >= ClippingChunksToSet)
            {
                _isClipping = true;
                Log.Info(
                    $"Audio clipping detected: block peak {peak:0.000}, {ClippedSamples} samples over full scale so far.");
                ClippingStarted?.Invoke();
            }

            return;
        }

        _clippedRun = 0;
        if (!_isClipping)
        {
            return;
        }

        if (++_cleanRun < ClippingChunksToClear)
        {
            return;
        }

        _isClipping = false;
        _cleanRun = 0;
        Log.Info($"Audio clipping ended: {_clippedChunks} clipped chunks, {ClippedSamples} samples over full scale.");
    }

    private static MediaStreamSample CreateSample(byte[] pcmData, TimeSpan timestamp)
    {
        var duration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * AudioFormat.SamplesPerChunk / AudioFormat.SampleRate);
        var buffer = pcmData.AsBuffer();
        var sample = MediaStreamSample.CreateFromBuffer(buffer, timestamp);
        sample.Duration = duration;
        return sample;
    }

    private void DrainOutputQueue()
    {
        while (_outputQueue.TryDequeue(out _))
        {
        }
    }

    private static async Task LogMicrophoneAccessAsync()
    {
        try
        {
            var access = await AppCapability.Create("microphone").RequestAccessAsync();
            Log.Info($"Microphone AppCapability access: {access}");
            if (access == AppCapabilityAccessStatus.DeniedByUser ||
                access == AppCapabilityAccessStatus.DeniedBySystem)
            {
                Log.Info("Microphone privacy gate denied; WASAPI will still be attempted for unpackaged capture.");
            }
        }
        catch (Exception ex)
        {
            Log.Info($"Microphone AppCapability check skipped: {ex.Message}");
        }
    }
}
