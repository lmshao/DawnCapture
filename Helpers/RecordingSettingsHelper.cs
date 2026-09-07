using DawnCapture.Models;

namespace DawnCapture.Helpers;

public static class RecordingSettingsHelper
{
    public const int DefaultBitrateKbps = 8000;

    public static int BitrateFromQualityIndex(int index) => index switch
    {
        0 => 5000,
        2 => 12000,
        _ => DefaultBitrateKbps
    };

    public static int FrameRateFromSettingsIndex(int frameRateIndex, int currentFrameRate) =>
        frameRateIndex switch
        {
            1 => 60,
            0 => 30,
            _ => currentFrameRate is 30 or 60 ? currentFrameRate : 30
        };

    public static int FrameRateToSettingsIndex(int frameRate) => frameRate switch
    {
        >= 60 => 1,
        30 => 0,
        _ => 2
    };

    public static void ApplyCaptureQualityPreset(AppSettings settings, int captureQualityIndex)
    {
        switch (captureQualityIndex)
        {
            case 0:
                settings.QualityIndex = 0;
                settings.FrameRate = 30;
                settings.BitrateKbps = BitrateFromQualityIndex(0);
                break;
            case 2:
                settings.QualityIndex = 2;
                settings.FrameRate = 60;
                settings.BitrateKbps = BitrateFromQualityIndex(2);
                break;
            default:
                settings.QualityIndex = 1;
                settings.FrameRate = 30;
                settings.BitrateKbps = BitrateFromQualityIndex(1);
                break;
        }
    }

    public static int GetCaptureQualityIndex(AppSettings settings)
    {
        if (settings.FrameRate >= 60)
        {
            return 2;
        }

        return settings.QualityIndex == 0 ? 0 : 1;
    }

    public static void ApplySettingsQualityIndex(AppSettings settings, int qualityIndex)
    {
        settings.QualityIndex = qualityIndex;
        settings.BitrateKbps = BitrateFromQualityIndex(qualityIndex);
    }

    public static string VideoCodecLabel(int codecIndex) =>
        codecIndex == 1 ? "H.265" : "H.264";

    public static void ApplyFromSettings(AppSettings target, AppSettings source)
    {
        target.OutputFolder = source.OutputFolder;
        target.FrameRate = source.FrameRate;
        target.BitrateKbps = source.BitrateKbps;
        target.CaptureCursor = source.CaptureCursor;
        target.Language = source.Language;
        target.QualityIndex = source.QualityIndex;
        target.AudioQualityIndex = source.AudioQualityIndex;
        target.VideoCodecIndex = source.VideoCodecIndex;
        target.CountdownEnabled = source.CountdownEnabled;
        target.NotificationEnabled = source.NotificationEnabled;
    }
}
