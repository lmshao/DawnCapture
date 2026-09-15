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
    /// <summary>
    /// One encoder bound to one output file. Building a pipeline touches no service
    /// state, which is what lets a segment switch prepare its replacement while the
    /// current file is still being written.
    /// </summary>
    private sealed class MediaPipeline
    {
        public required MediaStreamSource Source { get; init; }

        public required MediaTranscoder Transcoder { get; init; }

        public required IRandomAccessStream Stream { get; init; }

        public required string OutputPath { get; init; }

        public required PrepareTranscodeResult Prepared { get; init; }

        public required int CodecIndex { get; init; }

        public required int BitrateKbps { get; init; }

        public CancellationTokenSource? Cancellation { get; set; }

        public Task? Task { get; set; }
    }

    private async Task<MediaPipeline?> BuildVideoPipelineAsync(
        SizeInt32 size,
        RecordingAudioOptions audioOptions,
        int segmentNumber,
        bool reuseEffectiveCodec)
    {
        Log.Debug($"Creating media objects: OutputSize={size.Width}x{size.Height}, FrameRate={_settings.Current.FrameRate}, Bitrate={_settings.Current.BitrateKbps}Kbps, AudioMic={audioOptions.EnableMicrophone}, AudioSystem={audioOptions.EnableSystemAudio}, AudioBitrate={audioOptions.BitrateKbps}Kbps");

        int previousEffectiveCodecIndex = _effectiveVideoCodecIndex;
        int codecIndex = _settings.Current.VideoCodecIndex;
        int bitrateKbps = RecordingSettingsHelper.ResolveEffectiveVideoBitrateKbps(
            _settings.Current,
            size.Width,
            size.Height);
        Log.Debug($"Effective video bitrate={bitrateKbps}Kbps (mode={_settings.Current.BitrateMode})");

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

        var source = audioDescriptor is null
            ? new MediaStreamSource(descriptor)
            : new MediaStreamSource(descriptor, audioDescriptor);
        _startingStreamCount = 0;
        source.BufferTime = TimeSpan.Zero;
        source.Starting += OnMediaStreamSourceStarting;
        source.SampleRequested += OnMediaStreamSourceSampleRequested;

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)size.Width;
        profile.Video.Height = (uint)size.Height;
        profile.Video.Bitrate = (uint)(bitrateKbps * 1000);
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

        // A segment switch reuses the codec the first segment settled on, so a HEVC
        // fallback is neither retried nor reported again for every segment.
        bool requestedHevc =
            (reuseEffectiveCodec ? previousEffectiveCodecIndex : _settings.Current.VideoCodecIndex) == 1;
        if (requestedHevc)
        {
            profile.Video.Subtype = MediaEncodingSubtypes.Hevc;
        }

        var folder = OutputFolderHelper.Resolve(_settings.Current.OutputFolder);
        var opened = await OpenOutputAsync(folder, ".mp4", segmentNumber);
        if (opened is null)
        {
            DetachSource(source);
            return null;
        }

        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var stream = opened.Value.Stream;
        var outputPath = opened.Value.Path;
        var prepared = await TryPrepareTranscodeAsync(transcoder, source, stream, profile);
        if (prepared is null && requestedHevc)
        {
            Log.Info("HEVC transcode preparation failed; retrying with H.264.");
            profile.Video.Subtype = MediaEncodingSubtypes.H264;
            codecIndex = 0;
            stream.Dispose();
            TryDeleteOutputFile(outputPath);

            var reopened = await OpenOutputAsync(folder, ".mp4", segmentNumber);
            if (reopened is null)
            {
                DetachSource(source);
                return null;
            }

            stream = reopened.Value.Stream;
            outputPath = reopened.Value.Path;
            prepared = await TryPrepareTranscodeAsync(transcoder, source, stream, profile);
            if (prepared is not null)
            {
                RaiseNotice(LocalizationService.GetString("Notice_HevcFallback"));
            }
        }

        if (prepared is null)
        {
            DetachSource(source);
            stream.Dispose();
            TryDeleteOutputFile(outputPath);
            return null;
        }

        return new MediaPipeline
        {
            Source = source,
            Transcoder = transcoder,
            Stream = stream,
            OutputPath = outputPath,
            Prepared = prepared,
            CodecIndex = codecIndex,
            BitrateKbps = bitrateKbps
        };
    }

    private async Task<MediaPipeline?> BuildAudioPipelineAsync(
        RecordingAudioOptions audioOptions,
        int segmentNumber)
    {
        Log.Debug(
            $"Creating audio-only media objects: AudioMic={audioOptions.EnableMicrophone}, AudioSystem={audioOptions.EnableSystemAudio}, AudioBitrate={audioOptions.BitrateKbps}Kbps");

        var pcmProperties = AudioEncodingProperties.CreatePcm(
            (uint)audioOptions.SampleRate,
            (uint)audioOptions.Channels,
            (uint)AudioFormat.BitsPerSample);
        var audioDescriptor = new AudioStreamDescriptor(pcmProperties);

        var source = new MediaStreamSource(audioDescriptor);
        _startingStreamCount = 0;
        source.BufferTime = TimeSpan.Zero;
        source.Starting += OnMediaStreamSourceStarting;
        source.SampleRequested += OnMediaStreamSourceSampleRequested;

        var profile = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High);
        profile.Audio = AudioEncodingProperties.CreateAac(
            (uint)audioOptions.SampleRate,
            (uint)audioOptions.Channels,
            (uint)(audioOptions.BitrateKbps * 1000));

        var folder = OutputFolderHelper.Resolve(_settings.Current.OutputFolder);
        var opened = await OpenOutputAsync(folder, ".m4a", segmentNumber);
        if (opened is null)
        {
            DetachSource(source);
            return null;
        }

        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var stream = opened.Value.Stream;
        var prepared = await TryPrepareTranscodeAsync(transcoder, source, stream, profile);
        if (prepared is null)
        {
            DetachSource(source);
            stream.Dispose();
            TryDeleteOutputFile(opened.Value.Path);
            return null;
        }

        return new MediaPipeline
        {
            Source = source,
            Transcoder = transcoder,
            Stream = stream,
            OutputPath = opened.Value.Path,
            Prepared = prepared,
            CodecIndex = _effectiveVideoCodecIndex,
            BitrateKbps = 0
        };
    }

    /// <summary>
    /// Makes a prepared pipeline the one the sample pump feeds. The source is assigned
    /// before the transcode starts, so the pump already recognises it as the active one
    /// when the first sample request arrives - it must never answer that request with a
    /// null sample.
    /// </summary>
    private void ActivatePipeline(MediaPipeline pipeline, TimeSpan segmentBase)
    {
        _activePipeline = pipeline;
        Volatile.Write(ref _mediaStreamSource, pipeline.Source);
        _transcoder = pipeline.Transcoder;
        _outputStream = pipeline.Stream;
        _currentOutputPath = pipeline.OutputPath;
        _effectiveVideoCodecIndex = pipeline.CodecIndex;
        _effectiveVideoBitrateKbps = pipeline.BitrateKbps;

        Volatile.Write(ref _segmentBaseTicks, segmentBase.Ticks);

        pipeline.Cancellation?.Dispose();
        pipeline.Cancellation = new CancellationTokenSource();
        var token = pipeline.Cancellation.Token;
        var prepared = pipeline.Prepared;

        pipeline.Task = Task.Run(
            async () => await prepared.TranscodeAsync().AsTask(token),
            token);

        _transcodeCancellation = pipeline.Cancellation;
        _transcodeTask = pipeline.Task;

        _ = pipeline.Task.ContinueWith(
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

        if (!ReferenceEquals(sender, Volatile.Read(ref _mediaStreamSource)))
        {
            // A source that has been retired only needs to drain to its end.
            args.Request.Sample = null;
            return;
        }

        if (!_isPaused && !_isStopping)
        {
            EvaluateRecordingLimits();
            if (!_isRecording || _isStopping)
            {
                args.Request.Sample = null;
                return;
            }
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

    private async Task<(string Path, IRandomAccessStream Stream)?> OpenOutputAsync(
        string folder,
        string extension,
        int segmentNumber)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var outputPath = BuildOutputPath(folder, extension, segmentNumber);

            // StorageFile.GetFileFromPathAsync requires the file to exist.
            File.Create(outputPath).Dispose();
            var outputFile = await StorageFile.GetFileFromPathAsync(outputPath);
            var stream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
            return (outputPath, stream);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open output file in '{folder}'", ex);
            return null;
        }
    }

    private static async Task<PrepareTranscodeResult?> TryPrepareTranscodeAsync(
        MediaTranscoder transcoder,
        MediaStreamSource source,
        IRandomAccessStream stream,
        MediaEncodingProfile profile)
    {
        try
        {
            return await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
        }
        catch (Exception ex)
        {
            Log.Info($"Transcode prepare failed: {ex.Message}");
            return null;
        }
    }

    private void DetachSource(MediaStreamSource source)
    {
        source.Starting -= OnMediaStreamSourceStarting;
        source.SampleRequested -= OnMediaStreamSourceSampleRequested;
    }

    /// <summary>
    /// Builds the output path. A segmented recording carries its part number so the
    /// files of one recording sort together and can be told apart.
    /// </summary>
    private string BuildOutputPath(string folder, string extension, int segmentNumber)
    {
        string part = _segmentEnabledForRecording ? $"_p{segmentNumber}" : string.Empty;
        return Path.Combine(folder, $"DawnCapture_{DateTime.Now:yyyyMMdd_HHmmss}{part}{extension}");
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
