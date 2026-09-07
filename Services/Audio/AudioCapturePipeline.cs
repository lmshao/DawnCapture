using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;
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

    private volatile float _peakLevel;

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
                    return CreateSample(queued.Buffer, queued.Timestamp);
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
