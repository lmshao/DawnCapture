// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace DawnCapture.Services.Audio;

/// <summary>
/// Accumulates mono PCM at a native sample rate and produces fixed-size 48 kHz chunks
/// without discarding unconsumed input between mixer ticks.
/// </summary>
internal sealed class MonoResampleBuffer
{
    private readonly List<float> _buffer = new();
    private readonly int _inputSampleRate;
    private double _readIndex;

    public MonoResampleBuffer(int inputSampleRate)
    {
        _inputSampleRate = inputSampleRate;
    }

    public void Append(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            _buffer.Add(samples[i]);
        }
    }

    public void Clear()
    {
        _buffer.Clear();
        _readIndex = 0;
    }

    public bool TryReadMono48k(Span<float> output)
    {
        if (output.Length == 0)
        {
            return false;
        }

        var step = (double)_inputSampleRate / AudioFormat.SampleRate;
        var lastSourceIndex = _readIndex + (output.Length - 1) * step;
        if (_buffer.Count <= (int)lastSourceIndex)
        {
            return false;
        }

        for (var i = 0; i < output.Length; i++)
        {
            var sourcePosition = _readIndex + (i * step);
            var index = (int)sourcePosition;
            var fraction = (float)(sourcePosition - index);
            var sample = _buffer[index];
            if (index + 1 < _buffer.Count)
            {
                sample += (_buffer[index + 1] - _buffer[index]) * fraction;
            }

            output[i] = sample;
        }

        _readIndex += output.Length * step;
        var drop = (int)_readIndex;
        if (drop > 0)
        {
            _buffer.RemoveRange(0, drop);
            _readIndex -= drop;
        }

        return true;
    }
}
