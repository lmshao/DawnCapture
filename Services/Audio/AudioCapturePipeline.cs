using System;
using System.Collections.Concurrent;
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
    private long _lastMicFrames;
    private long _lastLoopbackFrames;

    /// <summary>20 ms chunks without an audible peak before a log line is worth writing (~4 s).</summary>
    private const int QuietChunksBeforeNotice = 200;

    /// <summary>Below this smoothed peak the mix counts as silent.</summary>
    private const float AudiblePeakThreshold = 0.005f;

    public bool HasAudio { get; private set; }

    public double PeakLevel => _peakLevel;

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

        if (options.EnableMicrophone)
        {
            _microphone = new WasapiCaptureDevice(loopback: false);
            _microphone.Start();
        }

        if (options.EnableSystemAudio)
        {
            _loopback = new WasapiCaptureDevice(loopback: true);
            _loopback.Start();
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
            $"Audio pipeline started: mic={options.EnableMicrophone}, system={options.EnableSystemAudio}, bitrate={options.BitrateKbps}kbps.");
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
    }

    public void Stop()
    {
        _running = false;
        _mixerThread?.Join(TimeSpan.FromSeconds(2));
        _mixerThread = null;

        _microphone?.Dispose();
        _microphone = null;

        _loopback?.Dispose();
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
        var micResampler = _microphone is not null
            ? new MonoResampleBuffer(_microphone.InputSampleRate)
            : null;
        var loopResampler = _loopback is not null
            ? new MonoResampleBuffer(_loopback.InputSampleRate)
            : null;

        var monoScratch = new float[AudioFormat.SamplesPerChunk];
        var sourceStereo = new short[AudioFormat.SamplesPerChunk * AudioFormat.Channels];
        var stereoScratch = new short[AudioFormat.SamplesPerChunk * AudioFormat.Channels];
        var mixBuffer = new byte[AudioFormat.BytesPerChunk];
        var nextChunkAt = TimeSpan.Zero;

        while (_running)
        {
            try
            {
                AppendDeviceFrames(_microphone, micResampler);
                AppendDeviceFrames(_loopback, loopResampler);

                if (_paused)
                {
                    micResampler?.Clear();
                    loopResampler?.Clear();
                    Thread.Sleep(20);
                    nextChunkAt = _clock?.Elapsed ?? TimeSpan.Zero;
                    continue;
                }

                var elapsed = _clock?.Elapsed ?? TimeSpan.Zero;
                var producedChunks = 0;
                while (_running && elapsed >= nextChunkAt && producedChunks < 100)
                {
                    PcmAudioConverter.ClearStereo(stereoScratch);

                    if (micResampler?.TryReadMono48k(monoScratch) == true)
                    {
                        PcmAudioConverter.ClearStereo(sourceStereo);
                        PcmAudioConverter.WriteMonoToStereo48k(monoScratch, sourceStereo);
                        PcmAudioConverter.MixStereo(stereoScratch, sourceStereo);
                    }

                    if (loopResampler?.TryReadMono48k(monoScratch) == true)
                    {
                        PcmAudioConverter.ClearStereo(sourceStereo);
                        PcmAudioConverter.WriteMonoToStereo48k(monoScratch, sourceStereo);
                        PcmAudioConverter.MixStereo(stereoScratch, sourceStereo);
                    }

                    Buffer.BlockCopy(stereoScratch, 0, mixBuffer, 0, mixBuffer.Length);
                    _peakLevel = Math.Max(_peakLevel * 0.7f, ComputePeak(stereoScratch));
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
        long micFrames = _microphone?.FramesDelivered ?? 0;
        long loopbackFrames = _loopback?.FramesDelivered ?? 0;
        Log.Info($"Mix silent {QuietChunksBeforeNotice / 50.0:0.0}s (mic frames: {micFrames - _lastMicFrames}, loopback frames: {loopbackFrames - _lastLoopbackFrames}).");
        _lastMicFrames = micFrames;
        _lastLoopbackFrames = loopbackFrames;
    }

    private static float ComputePeak(short[] samples)
    {
        float peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float normalized = Math.Abs(samples[i] / 32768f);
            if (normalized > peak)
            {
                peak = normalized;
            }
        }

        return peak;
    }

    private static void AppendDeviceFrames(WasapiCaptureDevice? device, MonoResampleBuffer? resampler)
    {
        if (device is null || resampler is null)
        {
            return;
        }

        while (device.TryTakeMonoFrame(out var mono))
        {
            resampler.Append(mono);
        }
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
