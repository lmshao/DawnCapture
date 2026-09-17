// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace DawnCapture.Services.Audio;

/// <summary>
/// Accumulates interleaved stereo PCM at a native sample rate and produces fixed-size 48 kHz
/// chunks without discarding unconsumed input between mixer ticks. Both channels advance on one
/// shared read index so left and right stay in phase, and every count here is in frames - a frame
/// is one L/R pair, two floats. That convention is deliberate: mixing frames and samples up is
/// the easiest way to break this class.
/// </summary>
internal sealed class StereoResampleBuffer
{
    private readonly List<float> _buffer = new();
    private readonly int _inputSampleRate;
    private double _readIndex;

    public StereoResampleBuffer(int inputSampleRate)
    {
        _inputSampleRate = inputSampleRate;
    }

    /// <summary>Native frames currently held (each frame is one L/R pair).</summary>
    private int FrameCount => _buffer.Count / AudioFormat.Channels;

    public void Append(ReadOnlySpan<float> interleavedStereo)
    {
        if (interleavedStereo.Length == 0)
        {
            return;
        }

        for (var i = 0; i < interleavedStereo.Length; i++)
        {
            _buffer.Add(interleavedStereo[i]);
        }
    }

    public void Clear()
    {
        _buffer.Clear();
        _readIndex = 0;
    }

    /// <summary>
    /// Fills <paramref name="output"/> with interleaved 48 kHz frames. Returns false while the
    /// buffer does not hold enough input, which is what keeps a source that has not started
    /// delivering yet silent instead of shifting the shared timeline.
    /// </summary>
    public bool TryReadStereo48k(Span<float> output)
    {
        if (output.Length < AudioFormat.Channels)
        {
            return false;
        }

        var outputFrames = output.Length / AudioFormat.Channels;
        var step = (double)_inputSampleRate / AudioFormat.SampleRate;
        var lastSourceFrame = _readIndex + (outputFrames - 1) * step;
        if (FrameCount <= (int)lastSourceFrame)
        {
            return false;
        }

        for (var frame = 0; frame < outputFrames; frame++)
        {
            var sourcePosition = _readIndex + frame * step;
            var index = (int)sourcePosition;
            var fraction = (float)(sourcePosition - index);
            var hasNext = index + 1 < FrameCount;
            var currentBase = index * AudioFormat.Channels;
            var nextBase = currentBase + AudioFormat.Channels;
            var outputBase = frame * AudioFormat.Channels;

            for (var channel = 0; channel < AudioFormat.Channels; channel++)
            {
                var sample = _buffer[currentBase + channel];
                if (hasNext)
                {
                    sample += (_buffer[nextBase + channel] - sample) * fraction;
                }

                output[outputBase + channel] = sample;
            }
        }

        _readIndex += outputFrames * step;
        var dropFrames = (int)_readIndex;
        if (dropFrames > 0)
        {
            _buffer.RemoveRange(0, dropFrames * AudioFormat.Channels);
            _readIndex -= dropFrames;
        }

        return true;
    }
}
