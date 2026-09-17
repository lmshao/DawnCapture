// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

namespace DawnCapture.Models;

public sealed class RecordingAudioOptions
{
    public bool EnableMicrophone { get; init; }

    public bool EnableSystemAudio { get; init; }

    /// <summary>Endpoint id to capture from; null or empty follows the system default device.</summary>
    public string? MicrophoneDeviceId { get; init; }

    public string? SystemAudioDeviceId { get; init; }

    public int BitrateKbps { get; init; } = 128;

    public int SampleRate { get; init; } = 48000;

    public const int DefaultSampleRateKhz = 48;

    public int Channels { get; init; } = 2;

    public bool HasAnySource => EnableMicrophone || EnableSystemAudio;

    public static int BitrateFromQualityIndex(int index) => index switch
    {
        0 => 128,
        2 => 256,
        _ => 192
    };
}
