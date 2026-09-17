// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using NAudio.Wave;

namespace DawnCapture.Services.Audio;

internal static class PcmAudioConverter
{
    /// <summary>
    /// Decodes one device packet into interleaved stereo float frames (L, R, L, R...). A mono
    /// source - the usual microphone - is copied to both channels; widths beyond stereo are only
    /// seen when the capture had to fall back to the device mix format, and are folded down with
    /// the matrix below. Nothing is ever averaged into mono: that is what used to destroy stereo.
    /// </summary>
    public static void DecodeToFloatStereo(
        ReadOnlySpan<byte> data,
        WaveFormat format,
        int frameCount,
        float[] output)
    {
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;
        bool isPcm16 = format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16;
        bool isPcm32 = format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32;

        if (!isFloat && !isPcm16 && !isPcm32)
        {
            Log.Debug(
                $"Unsupported audio format: encoding={format.Encoding}, bits={format.BitsPerSample}, channels={format.Channels}.");
            Array.Clear(output);
            return;
        }

        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = format.BitsPerSample / 8;
        var frameStride = channels * bytesPerSample;
        var frames = Math.Min(
            Math.Min(frameCount, data.Length / Math.Max(1, frameStride)),
            output.Length / AudioFormat.Channels);

        for (var frame = 0; frame < frames; frame++)
        {
            var baseOffset = frame * frameStride;
            float left;
            float right;

            if (channels == 1)
            {
                left = right = ReadSample(data, baseOffset, isFloat, isPcm16);
            }
            else
            {
                left = ReadSample(data, baseOffset, isFloat, isPcm16);
                right = ReadSample(data, baseOffset + bytesPerSample, isFloat, isPcm16);

                // Surround fallback only; channel order follows the Windows convention
                // (FL, FR, FC, LFE, BL, BR): center and backs at -3 dB, LFE dropped so low
                // frequencies cannot pile up.
                if (channels >= 3)
                {
                    var center = ReadSample(data, baseOffset + (2 * bytesPerSample), isFloat, isPcm16) * 0.7071f;
                    left += center;
                    right += center;
                }

                if (channels >= 5)
                {
                    left += ReadSample(data, baseOffset + (4 * bytesPerSample), isFloat, isPcm16) * 0.7071f;
                }

                if (channels >= 6)
                {
                    right += ReadSample(data, baseOffset + (5 * bytesPerSample), isFloat, isPcm16) * 0.7071f;
                }
            }

            var outputOffset = frame * AudioFormat.Channels;
            output[outputOffset] = left;
            output[outputOffset + 1] = right;
        }

        for (var i = frames * AudioFormat.Channels; i < output.Length; i++)
        {
            output[i] = 0f;
        }
    }

    private static float ReadSample(ReadOnlySpan<byte> data, int offset, bool isFloat, bool isPcm16)
    {
        if (isFloat)
        {
            return BitConverter.ToSingle(data.Slice(offset, sizeof(float)));
        }

        return isPcm16
            ? BitConverter.ToInt16(data.Slice(offset, sizeof(short))) / 32768f
            : BitConverter.ToInt32(data.Slice(offset, sizeof(int))) / 2147483648f;
    }

    /// <summary>
    /// Converts the finished float mix into the 16-bit block the encoder consumes, and returns how
    /// many samples were over full scale. This is the only point in the chain where the signal is
    /// limited: everything upstream keeps full precision, so a source is never clipped before it
    /// has had the chance to cancel against another one.
    /// </summary>
    public static int ClampAndConvertToInt16(ReadOnlySpan<float> accumulator, Span<short> output)
    {
        var count = Math.Min(accumulator.Length, output.Length);
        var overFullScale = 0;

        for (var i = 0; i < count; i++)
        {
            var sample = accumulator[i];
            if (sample > 1f)
            {
                sample = 1f;
                overFullScale++;
            }
            else if (sample < -1f)
            {
                sample = -1f;
                overFullScale++;
            }

            output[i] = (short)(sample * 32767f);
        }

        return overFullScale;
    }

    /// <summary>Largest absolute sample in a block: the level meter and the clipping check.</summary>
    public static float MaxAbs(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = Math.Abs(samples[i]);
            if (value > peak)
            {
                peak = value;
            }
        }

        return peak;
    }

}
