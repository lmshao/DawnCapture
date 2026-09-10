using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.Services.Audio;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DawnCapture.Services;

public sealed partial class RecordingService
{
    private async Task<bool> CreateMediaObjectsAsync(SizeInt32 size, RecordingAudioOptions audioOptions)
    {
        Log.Debug($"Creating media objects: OutputSize={size.Width}x{size.Height}, FrameRate={_settings.Current.FrameRate}, Bitrate={_settings.Current.BitrateKbps}Kbps, AudioMic={audioOptions.EnableMicrophone}, AudioSystem={audioOptions.EnableSystemAudio}, AudioBitrate={audioOptions.BitrateKbps}Kbps");
        try
        {
            _effectiveVideoCodecIndex = _settings.Current.VideoCodecIndex;
            _effectiveVideoBitrateKbps = RecordingSettingsHelper.ResolveEffectiveVideoBitrateKbps(
                _settings.Current,
                size.Width,
                size.Height);
            Log.Debug($"Effective video bitrate={_effectiveVideoBitrateKbps}Kbps (mode={_settings.Current.BitrateMode})");
            var videoProperties = VideoEncodingProperties.CreateUncompressed(
                MediaEncodingSubtypes.Bgra8,
                (uint)size.Width,
                (uint)size.Height);

            var descriptor = new VideoStreamDescriptor(videoProperties);
            AudioStreamDescriptor? audioDescriptor = null;
            if (audioOptions.HasAnySource)
            {
                var pcmProperties = AudioEncodingProperties.CreatePcm(
                    (uint)audioOptions.SampleRate,
                    (uint)audioOptions.Channels,
                    (uint)AudioFormat.BitsPerSample);
                audioDescriptor = new AudioStreamDescriptor(pcmProperties);
            }

            _mediaStreamSource = audioDescriptor is null
                ? new MediaStreamSource(descriptor)
                : new MediaStreamSource(descriptor, audioDescriptor);

            _startingStreamCount = 0;
            _mediaStreamSource.BufferTime = TimeSpan.Zero;
            _mediaStreamSource.Starting += OnMediaStreamSourceStarting;
            _mediaStreamSource.SampleRequested += OnMediaStreamSourceSampleRequested;

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = (uint)size.Width;
            profile.Video.Height = (uint)size.Height;
            profile.Video.Bitrate = (uint)(_effectiveVideoBitrateKbps * 1000);
            profile.Video.FrameRate.Numerator = (uint)_settings.Current.FrameRate;
            profile.Video.FrameRate.Denominator = 1;
            profile.Video.PixelAspectRatio.Numerator = 1;
            profile.Video.PixelAspectRatio.Denominator = 1;

            if (audioOptions.HasAnySource)
            {
                profile.Audio = AudioEncodingProperties.CreateAac(
                    (uint)audioOptions.SampleRate,
                    (uint)audioOptions.Channels,
                    (uint)(audioOptions.BitrateKbps * 1000));
            }

            var folder = OutputFolderHelper.Resolve(_settings.Current.OutputFolder);
            if (!await CreateOutputFileAsync(folder, ".mp4"))
            {
                return false;
            }

            _transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };

            bool requestedHevc = _settings.Current.VideoCodecIndex == 1;
            if (requestedHevc)
            {
                profile.Video.Subtype = MediaEncodingSubtypes.Hevc;
            }

            var prepared = await TryPrepareVideoTranscodeAsync(profile);
            if (prepared is null && requestedHevc)
            {
                Log.Info("HEVC transcode preparation failed; retrying with H.264.");
                profile.Video.Subtype = MediaEncodingSubtypes.H264;
                _effectiveVideoCodecIndex = 0;
                if (!await RecreateOutputFileAsync(folder, ".mp4"))
                {
                    return false;
                }

                prepared = await TryPrepareVideoTranscodeAsync(profile);
                if (prepared is not null)
                {
                    RaiseNotice(LocalizationService.GetString("Notice_HevcFallback"));
                }
            }

            if (prepared is null)
            {
                TryDeleteOutputFile(_currentOutputPath);
                RaiseFailed(LocalizationService.GetString("Failure_TranscodePrepare"));
                return false;
            }

            // One token per recording, so a stop or a failed start can cancel the
            // encoder instead of abandoning it mid-write (see CleanupCaptureAsync).
            _transcodeCancellation?.Dispose();
            _transcodeCancellation = new CancellationTokenSource();
            var transcodeToken = _transcodeCancellation.Token;

            var transcodeResult = prepared;
            _transcodeTask = Task.Run(
                async () => await transcodeResult.TranscodeAsync().AsTask(transcodeToken),
                transcodeToken);

            _ = _transcodeTask.ContinueWith(
                t =>
                {
                    if (t.IsFaulted && t.Exception is not null)
                    {
                        RaiseFailed(t.Exception.GetBaseException().Message);
                    }
                },
                TaskScheduler.Default);

            return true;
        }
        catch (Exception ex)
        {
            TryDeleteOutputFile(_currentOutputPath);
            RaiseFailed(ex.Message);
            return false;
        }
    }

    private async Task<bool> PrepareAudioOnlyMediaObjectsAsync(RecordingAudioOptions audioOptions)
    {
        Log.Debug(
            $"Creating audio-only media objects: AudioMic={audioOptions.EnableMicrophone}, AudioSystem={audioOptions.EnableSystemAudio}, AudioBitrate={audioOptions.BitrateKbps}Kbps");
        try
        {
            _framesWritten = 0;
            _audioSamplesWritten = 0;
            _isPaused = false;
            _closedEvent.Reset();

            var pcmProperties = AudioEncodingProperties.CreatePcm(
                (uint)audioOptions.SampleRate,
                (uint)audioOptions.Channels,
                (uint)AudioFormat.BitsPerSample);
            var audioDescriptor = new AudioStreamDescriptor(pcmProperties);

            _mediaStreamSource = new MediaStreamSource(audioDescriptor);
            _startingStreamCount = 0;
            _mediaStreamSource.BufferTime = TimeSpan.Zero;
            _mediaStreamSource.Starting += OnMediaStreamSourceStarting;
            _mediaStreamSource.SampleRequested += OnMediaStreamSourceSampleRequested;

            var profile = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
            profile.Audio = AudioEncodingProperties.CreateAac(
                (uint)audioOptions.SampleRate,
                (uint)audioOptions.Channels,
                (uint)(audioOptions.BitrateKbps * 1000));
            _audioEncodingProfile = profile;

            var folder = OutputFolderHelper.Resolve(_settings.Current.OutputFolder);
            Directory.CreateDirectory(folder);
            var outputPath = Path.Combine(folder, $"DawnCapture_{DateTime.Now:yyyyMMdd_HHmmss}.m4a");
            _currentOutputPath = outputPath;

            File.Create(outputPath).Dispose();
            var outputFile = await StorageFile.GetFileFromPathAsync(outputPath);
            _outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);

            return true;
        }
        catch (Exception ex)
        {
            TryDeleteOutputFile(_currentOutputPath);
            RaiseFailed(ex.Message);
            return false;
        }
    }

    private void StartAudioOnlyTranscode()
    {
        if (_mediaStreamSource is null || _outputStream is null || _audioEncodingProfile is null)
        {
            Log.Error("StartAudioOnlyTranscode called before audio media objects were prepared.");
            RaiseFailed(LocalizationService.GetString("Failure_NoAudioSamples"));
            return;
        }

        _transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };

        _transcodeCancellation?.Dispose();
        _transcodeCancellation = new CancellationTokenSource();
        var transcodeToken = _transcodeCancellation.Token;

        _transcodeTask = Task.Run(
            async () =>
            {
                var prepared = await _transcoder
                    .PrepareMediaStreamSourceTranscodeAsync(
                        _mediaStreamSource,
                        _outputStream,
                        _audioEncodingProfile)
                    .AsTask(transcodeToken);
                await prepared.TranscodeAsync().AsTask(transcodeToken);
            },
            transcodeToken);

        _ = _transcodeTask.ContinueWith(
            t =>
            {
                if (t.IsFaulted && t.Exception is not null)
                {
                    RaiseFailed(t.Exception.GetBaseException().Message);
                }
            },
            TaskScheduler.Default);
    }

    private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
    {
        _startingStreamCount++;
        args.Request.SetActualStartPosition(TimeSpan.Zero);
    }

    private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        if (!_isRecording && !_isStopping)
        {
            args.Request.Sample = null;
            return;
        }

        if (args.Request.StreamDescriptor is AudioStreamDescriptor)
        {
            args.Request.Sample = _audioPipeline?.TryCreateSample();
            if (args.Request.Sample is not null)
            {
                _audioSamplesWritten++;
            }

            return;
        }

        if (_isPaused && !_isStopping)
        {
            args.Request.Sample = null;
            return;
        }

        try
        {
            if (!WaitForVideoSample(out var frame, out var timestamp, out var duration) || frame is null)
            {
                args.Request.Sample = null;
                return;
            }

            using (frame)
            {
                var surface = frame.Surface;
                var croppedSurface = default(IDirect3DSurface);
                var compositeSurface = default(IDirect3DSurface);

                if (_windowLetterboxActive)
                {
                    compositeSurface = CompositeWindowFrame(surface, frame.ContentSize);
                    if (compositeSurface is null)
                    {
                        args.Request.Sample = null;
                        return;
                    }

                    surface = compositeSurface;
                }
                else if (_cropRect is { } crop)
                {
                    croppedSurface = CropSurface(surface, crop);
                    if (croppedSurface is null)
                    {
                        args.Request.Sample = null;
                        return;
                    }

                    surface = croppedSurface;
                }

                var sample = MediaStreamSample.CreateFromDirect3D11Surface(surface, timestamp);
                sample.Duration = duration;
                args.Request.Sample = sample;
                _framesWritten++;

                if (croppedSurface is not null)
                {
                    // The sample owns a surface reference, so release the extra reference held by this method.
                    croppedSurface.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            RaiseFailed(ex.Message);
            args.Request.Sample = null;
        }
    }

    private async Task<bool> CreateOutputFileAsync(string folder, string extension)
    {
        Directory.CreateDirectory(folder);
        var outputPath = Path.Combine(folder, $"DawnCapture_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
        _currentOutputPath = outputPath;

        // StorageFile.GetFileFromPathAsync requires the file to exist.
        File.Create(outputPath).Dispose();
        var outputFile = await StorageFile.GetFileFromPathAsync(outputPath);
        _outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
        return true;
    }

    private async Task<bool> RecreateOutputFileAsync(string folder, string extension)
    {
        if (_outputStream is not null)
        {
            try
            {
                _outputStream.Dispose();
            }
            catch
            {
                // Ignore cleanup exceptions.
            }

            _outputStream = null;
        }

        TryDeleteOutputFile(_currentOutputPath);
        return await CreateOutputFileAsync(folder, extension);
    }

    private async Task<PrepareTranscodeResult?> TryPrepareVideoTranscodeAsync(MediaEncodingProfile profile)
    {
        try
        {
            return await _transcoder!.PrepareMediaStreamSourceTranscodeAsync(
                _mediaStreamSource!,
                _outputStream!,
                profile);
        }
        catch (Exception ex)
        {
            Log.Info($"Video transcode prepare failed: {ex.Message}");
            return null;
        }
    }

    private static void TryDeleteOutputFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"Failed to delete output file '{path}': {ex.Message}");
        }
    }
}
