using System;
using NAudio.Wave;

namespace DawnCapture.Services.Audio;

internal static class PcmAudioConverter
{
    public static float[] DecodeToFloatMono(
        ReadOnlySpan<byte> data,
        WaveFormat format,
        int frameCount)
    {
        var samples = new float[frameCount];
        if (frameCount == 0)
        {
            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var floats = MemoryMarshalCastToFloatArray(data, frameCount * format.Channels);
            DownmixToMono(floats, format.Channels, samples);
            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var pcm16 = MemoryMarshalCastToInt16Array(data, frameCount * format.Channels);
            DownmixPcm16ToMono(pcm16, format.Channels, samples);
            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32)
        {
            var pcm32 = MemoryMarshalCastToInt32Array(data, frameCount * format.Channels);
            DownmixPcm32ToMono(pcm32, format.Channels, samples);
            return samples;
        }

        Log.Debug(
            $"Unsupported audio format: encoding={format.Encoding}, bits={format.BitsPerSample}, channels={format.Channels}.");
        Array.Clear(samples);
        return samples;
    }

    public static void WriteMonoToStereo48k(ReadOnlySpan<float> mono, Span<short> stereoOutput)
    {
        var frameCount = Math.Min(mono.Length, stereoOutput.Length / AudioFormat.Channels);
        for (var i = 0; i < frameCount; i++)
        {
            var pcm = FloatToInt16(mono[i]);
            var offset = i * AudioFormat.Channels;
            stereoOutput[offset] = pcm;
            stereoOutput[offset + 1] = pcm;
        }
    }

    public static void MixStereo(Span<short> target, ReadOnlySpan<short> source)
    {
        var count = Math.Min(target.Length, source.Length);
        for (var i = 0; i < count; i++)
        {
            var mixed = target[i] + source[i];
            target[i] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
        }
    }

    public static void ClearStereo(Span<short> buffer) => buffer.Clear();

    private static void DownmixToMono(float[] input, int channels, float[] monoOutput)
    {
        for (var frame = 0; frame < monoOutput.Length; frame++)
        {
            var sum = 0f;
            var baseIndex = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += input[baseIndex + channel];
            }

            monoOutput[frame] = sum / channels;
        }
    }

    private static void DownmixPcm16ToMono(short[] input, int channels, float[] monoOutput)
    {
        for (var frame = 0; frame < monoOutput.Length; frame++)
        {
            var sum = 0f;
            var baseIndex = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += input[baseIndex + channel] / 32768f;
            }

            monoOutput[frame] = sum / channels;
        }
    }

    private static void DownmixPcm32ToMono(int[] input, int channels, float[] monoOutput)
    {
        for (var frame = 0; frame < monoOutput.Length; frame++)
        {
            var sum = 0f;
            var baseIndex = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += input[baseIndex + channel] / 2147483648f;
            }

            monoOutput[frame] = sum / channels;
        }
    }

    private static short FloatToInt16(float sample)
    {
        sample = Math.Clamp(sample, -1f, 1f);
        return (short)(sample * 32767f);
    }

    private static float[] MemoryMarshalCastToFloatArray(ReadOnlySpan<byte> data, int count)
    {
        var result = new float[count];
        var max = Math.Min(count, data.Length / sizeof(float));
        for (var i = 0; i < max; i++)
        {
            result[i] = BitConverter.ToSingle(data.Slice(i * sizeof(float), sizeof(float)));
        }

        return result;
    }

    private static short[] MemoryMarshalCastToInt16Array(ReadOnlySpan<byte> data, int count)
    {
        var result = new short[count];
        var max = Math.Min(count, data.Length / sizeof(short));
        for (var i = 0; i < max; i++)
        {
            result[i] = BitConverter.ToInt16(data.Slice(i * sizeof(short), sizeof(short)));
        }

        return result;
    }

    private static int[] MemoryMarshalCastToInt32Array(ReadOnlySpan<byte> data, int count)
    {
        var result = new int[count];
        var max = Math.Min(count, data.Length / sizeof(int));
        for (var i = 0; i < max; i++)
        {
            result[i] = BitConverter.ToInt32(data.Slice(i * sizeof(int), sizeof(int)));
        }

        return result;
    }
}
