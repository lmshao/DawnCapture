using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DawnCapture.Services.Audio;

internal sealed class WasapiCaptureDevice : IDisposable
{
    private readonly bool _loopback;
    private readonly ConcurrentQueue<float[]> _monoFrames = new();

    private WasapiCapture? _capture;
    private int _inputSampleRate = AudioFormat.SampleRate;
    private volatile bool _active;
    private int _restarting;
    private long _framesDelivered;

    public WasapiCaptureDevice(bool loopback)
    {
        _loopback = loopback;
    }

    /// <summary>
    /// Frames handed over since this device started. The mixer uses it to tell "nothing is
    /// playing" (frames keep arriving, the mix is silent) apart from "the device stopped"
    /// (no frames at all), which a lock, an unlock or an endpoint switch can cause.
    /// </summary>
    public long FramesDelivered => Interlocked.Read(ref _framesDelivered);

    public int InputSampleRate => _inputSampleRate;

    public bool IsActive => _capture is not null;

    public void Start()
    {
        if (_capture is not null)
        {
            return;
        }

        try
        {
            _capture = _loopback
                ? new WasapiLoopbackCapture()
                : new WasapiCapture();

            if (!TryStartAtTargetSampleRate())
            {
                StartAtDeviceMixFormat();
            }

            // WASAPI ends the capture when the device is invalidated or the session is
            // reconfigured - a lock and an unlock are both typical triggers. Without a
            // handler that happens silently and the rest of the recording has no sound.
            _capture.RecordingStopped += OnRecordingStopped;
            _active = true;
        }
        catch (Exception ex)
        {
            // No capture device available (e.g. no microphone attached).
            // Degrade gracefully: the pipeline records other sources or silence.
            Log.Info($"{(_loopback ? "Loopback" : "Microphone")} unavailable: {ex.Message}");
            _capture?.Dispose();
            _capture = null;
        }
    }

    public void Stop()
    {
        _active = false;
        DisposeCapture();
    }

    private void DisposeCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null)
        {
            return;
        }

        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;

        try
        {
            capture.StopRecording();
        }
        catch
        {
            // The device may already be gone.
        }

        capture.Dispose();
        DrainQueue();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!_active)
        {
            return;
        }

        Log.Info($"{(_loopback ? "Loopback" : "Microphone")} capture ended unexpectedly: {e.Exception?.Message}");

        if (Interlocked.CompareExchange(ref _restarting, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(RestartAfterUnexpectedStop);
    }

    /// <summary>
    /// Reopens the device so the rest of the recording keeps its sound. Runs off the
    /// capture thread, which is ending at this point.
    /// </summary>
    private void RestartAfterUnexpectedStop()
    {
        try
        {
            DisposeCapture();
            if (!_active)
            {
                return;
            }

            Start();
            if (_active)
            {
                Log.Info($"{(_loopback ? "Loopback" : "Microphone")} capture restarted.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"{(_loopback ? "Loopback" : "Microphone")} capture restart failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _restarting, 0);
        }
    }

    public bool TryTakeMonoFrame(out float[] monoFrame)
    {
        return _monoFrames.TryDequeue(out monoFrame!);
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || e.BytesRecorded <= 0)
        {
            return;
        }

        var waveFormat = _capture.WaveFormat;
        var frameCount = e.BytesRecorded / waveFormat.BlockAlign;
        if (frameCount <= 0)
        {
            return;
        }

        var buffer = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);

        var mono = PcmAudioConverter.DecodeToFloatMono(buffer, waveFormat, frameCount);
        _monoFrames.Enqueue(mono);
        Interlocked.Increment(ref _framesDelivered);
    }

    private void DrainQueue()
    {
        while (_monoFrames.TryDequeue(out _))
        {
        }
    }

    private bool TryStartAtTargetSampleRate()
    {
        if (_capture is null)
        {
            return false;
        }

        _capture.WaveFormat = CreateTargetWaveFormat();
        _capture.DataAvailable += OnDataAvailable;

        try
        {
            _capture.StartRecording();
            _inputSampleRate = _capture.WaveFormat.SampleRate;
            Log.Debug(
                $"{(_loopback ? "Loopback" : "Microphone")} capture started via NAudio at target format: " +
                $"{_inputSampleRate}Hz, channels={_capture.WaveFormat.Channels}, encoding={_capture.WaveFormat.Encoding}.");
            return true;
        }
        catch (Exception ex)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Dispose();
            _capture = _loopback
                ? new WasapiLoopbackCapture()
                : new WasapiCapture();

            Log.Info(
                $"{(_loopback ? "Loopback" : "Microphone")} could not open at {AudioFormat.SampleRate}Hz " +
                $"(WASAPI AutoConvertPcm); falling back to device mix format. {ex.Message}");
            return false;
        }
    }

    private void StartAtDeviceMixFormat()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
        _inputSampleRate = _capture.WaveFormat.SampleRate;

        Log.Debug(
            $"{(_loopback ? "Loopback" : "Microphone")} capture started via NAudio at device mix format: " +
            $"{_inputSampleRate}Hz, channels={_capture.WaveFormat.Channels}, encoding={_capture.WaveFormat.Encoding}.");
    }

    private static WaveFormat CreateTargetWaveFormat()
    {
        return WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
    }
}
