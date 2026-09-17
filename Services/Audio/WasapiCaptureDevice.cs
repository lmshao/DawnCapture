// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

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
    private readonly string? _deviceId;
    private readonly ConcurrentQueue<float[]> _stereoFrames = new();

    private WasapiCapture? _capture;
    private int _inputSampleRate = AudioFormat.SampleRate;
    private volatile bool _active;
    private int _restarting;
    private long _framesDelivered;

    /// <param name="deviceId">
    /// Endpoint id chosen in the settings; null or empty follows the system default device.
    /// </param>
    public WasapiCaptureDevice(bool loopback, string? deviceId = null)
    {
        _loopback = loopback;
        _deviceId = deviceId;
    }

    /// <summary>
    /// True when a configured device could not be opened and the default one was used instead,
    /// so the recording keeps its sound from somewhere else. The pipeline reports it once.
    /// </summary>
    public bool FellBackToDefaultDevice { get; private set; }

    private string Label => _loopback ? "Loopback" : "Microphone";

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
            _capture = CreateCapture();

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
            // A device listed as active can still refuse to open - another application holding it
            // exclusively, or a driver glitch. Using the default endpoint is then what the user
            // asked for: a source, not silence.
            if (TryStartOnDefaultDevice())
            {
                return;
            }

            // No capture device available (e.g. no microphone attached).
            // Degrade gracefully: the pipeline records other sources or silence.
            Log.Info($"{Label} unavailable: {ex.Message}");
            _capture?.Dispose();
            _capture = null;
        }
    }

    /// <summary>
    /// Last resort for a configured endpoint that cannot be opened at all. Every retry inside this
    /// method stays on the default endpoint, so a format fallback cannot silently go back to the
    /// device that just failed.
    /// </summary>
    private bool TryStartOnDefaultDevice()
    {
        if (string.IsNullOrEmpty(_deviceId) || FellBackToDefaultDevice)
        {
            return false;
        }

        try
        {
            _capture?.Dispose();
            _capture = CreateCapture(forceDefault: true);
            if (!TryStartAtTargetSampleRate(forceDefault: true))
            {
                StartAtDeviceMixFormat();
            }

            _capture.RecordingStopped += OnRecordingStopped;
            _active = true;
            FellBackToDefaultDevice = true;
            Log.Info($"{Label} could not be opened; using the default device instead.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"{Label} default device also failed: {ex.Message}");
            _capture?.Dispose();
            _capture = null;
            return false;
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

    /// <summary>Hands over one interleaved stereo packet (L, R, L, R...).</summary>
    public bool TryTakeStereoFrame(out float[] stereoFrame)
    {
        return _stereoFrames.TryDequeue(out stereoFrame!);
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

        var stereo = new float[frameCount * AudioFormat.Channels];
        PcmAudioConverter.DecodeToFloatStereo(buffer, waveFormat, frameCount, stereo);
        _stereoFrames.Enqueue(stereo);
        Interlocked.Increment(ref _framesDelivered);
    }

    private void DrainQueue()
    {
        while (_stereoFrames.TryDequeue(out _))
        {
        }
    }

    private bool TryStartAtTargetSampleRate(bool forceDefault = false)
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
            _capture = CreateCapture(forceDefault);

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

    /// <summary>
    /// Opens the endpoint chosen in the settings, or the system default when none was chosen.
    /// A saved id can be gone (unplugged headset, re-imaged machine); that must not leave the
    /// recording silent, so it falls back to the default endpoint and says so.
    /// </summary>
    private WasapiCapture CreateCapture(bool forceDefault = false)
    {
        if (forceDefault || string.IsNullOrEmpty(_deviceId))
        {
            return CreateDefaultCapture();
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(_deviceId);
            if (device is not null && device.State == DeviceState.Active)
            {
                Log.Info($"{Label} using the selected device: {device.FriendlyName}");
                return _loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
            }

            Log.Info($"{Label} selected device is not active; using the default device.");
        }
        catch (Exception ex)
        {
            Log.Info($"{Label} selected device could not be opened ({ex.Message}); using the default device.");
        }

        FellBackToDefaultDevice = true;
        return CreateDefaultCapture();
    }

    private WasapiCapture CreateDefaultCapture() =>
        _loopback ? new WasapiLoopbackCapture() : new WasapiCapture();

    private static WaveFormat CreateTargetWaveFormat()
    {
        return WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
    }
}
