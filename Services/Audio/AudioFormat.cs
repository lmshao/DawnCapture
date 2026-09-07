namespace DawnCapture.Services.Audio;

internal static class AudioFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;
    public const int SamplesPerChunk = 960;
    public const int BytesPerChunk = SamplesPerChunk * Channels * (BitsPerSample / 8);
}
