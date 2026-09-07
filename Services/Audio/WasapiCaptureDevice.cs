using System;
using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DawnCapture.Services.Audio;

internal sealed class WasapiCaptureDevice : IDisposable
{
    private readonly bool _loopback;
    private readonly ConcurrentQueue<float[]> _monoFrames = new();

    private WasapiCapture? _capture;
    private int _inputSampleRate = AudioFormat.SampleRate;

    public WasapiCaptureDevice(bool loopback)
    {
        _loopback = loopback;
    }

    public int InputSampleRate => _inputSampleRate;

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
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.StopRecording();
        _capture.Dispose();
        _capture = null;
        DrainQueue();
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
