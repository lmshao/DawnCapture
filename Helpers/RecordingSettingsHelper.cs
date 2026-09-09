using System;

using DawnCapture.Models;



namespace DawnCapture.Helpers;



public static class RecordingSettingsHelper

{

    public const int DefaultBitrateKbps = 8000;

    public const int ReferenceWidth = 1920;

    public const int ReferenceHeight = 1080;

    public const int MinAdaptiveBitrateKbps = 2000;

    public const int MaxAdaptiveBitrateKbps = 40000;

    public const int MinFixedBitrateKbps = 1000;

    public const int MaxFixedBitrateKbps = 100000;



    private const double PixelScaleExponent = 0.85;

    private const double FrameRateReference = 30.0;

    private const double FrameRateScaleExponent = 0.55;



    public static int BitrateFromQualityIndex(int index) => index switch

    {

        0 => 5000,

        2 => 12000,

        _ => DefaultBitrateKbps

    };



    public static int FrameRateFromSettingsIndex(int frameRateIndex) =>

        frameRateIndex == 1 ? 60 : 30;



    public static int FrameRateToSettingsIndex(int frameRate) =>

        frameRate >= 60 ? 1 : 0;



    public static int NormalizeFrameRate(int frameRate) =>

        frameRate >= 60 ? 60 : 30;



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



    public static int ResolveCapturePresetIndex(AppSettings settings)

    {

        if (settings.FrameRate >= 60 && settings.QualityIndex == 2 && MatchesPresetBitrate(settings, 2))

        {

            return 2;

        }



        if (settings.FrameRate == 30 && settings.QualityIndex == 0 && MatchesPresetBitrate(settings, 0))

        {

            return 0;

        }



        if (settings.FrameRate == 30 && settings.QualityIndex == 1 && MatchesPresetBitrate(settings, 1))

        {

            return 1;

        }



        return 3;

    }



    public static void ApplySettingsQualityIndex(AppSettings settings, int qualityIndex)

    {

        settings.QualityIndex = qualityIndex;

        settings.BitrateKbps = BitrateFromQualityIndex(qualityIndex);

    }



    public static int ScaleAdaptiveBitrate(

        int anchorKbps,

        int width,

        int height,

        int frameRate,

        bool hevc)

    {

        if (width <= 0 || height <= 0)

        {

            return anchorKbps;

        }



        double pixelRatio = (width * (double)height) / (ReferenceWidth * (double)ReferenceHeight);

        double pixelScale = Math.Pow(pixelRatio, PixelScaleExponent);

        double frameScale = Math.Pow(Math.Max(frameRate, 1) / FrameRateReference, FrameRateScaleExponent);

        double codecScale = hevc ? 0.7 : 1.0;

        int scaled = (int)Math.Round(anchorKbps * pixelScale * frameScale * codecScale);

        return Math.Clamp(scaled, MinAdaptiveBitrateKbps, MaxAdaptiveBitrateKbps);

    }



    public static int ResolveEffectiveVideoBitrateKbps(AppSettings settings, int width, int height)

    {

        if (settings.BitrateMode == VideoBitrateMode.Fixed)

        {

            return Math.Clamp(settings.BitrateKbps, MinFixedBitrateKbps, MaxFixedBitrateKbps);

        }



        int anchorKbps = BitrateFromQualityIndex(settings.QualityIndex);

        return ScaleAdaptiveBitrate(

            anchorKbps,

            width,

            height,

            settings.FrameRate,

            settings.VideoCodecIndex == 1);

    }



    public static int ResolvePreviewVideoBitrateKbps(AppSettings settings, int width, int height)

    {

        if (width <= 0 || height <= 0)

        {

            width = ReferenceWidth;

            height = ReferenceHeight;

        }



        return ResolveEffectiveVideoBitrateKbps(settings, width, height);

    }



    public static bool UsesApproximateVideoBitrate(AppSettings settings) =>

        settings.BitrateMode == VideoBitrateMode.Adaptive;



    public static string VideoCodecLabel(int codecIndex) =>

        codecIndex == 1 ? "H.265/HEVC" : "H.264/AVC";



    public static void ApplyFromSettings(AppSettings target, AppSettings source)

    {

        target.OutputFolder = source.OutputFolder;

        target.FrameRate = source.FrameRate;

        target.BitrateKbps = source.BitrateKbps;

        target.BitrateMode = source.BitrateMode;

        target.CaptureCursor = source.CaptureCursor;

        target.Language = source.Language;

        target.QualityIndex = source.QualityIndex;

        target.AudioQualityIndex = source.AudioQualityIndex;

        target.VideoCodecIndex = source.VideoCodecIndex;

        target.CountdownEnabled = source.CountdownEnabled;

        target.NotificationEnabled = source.NotificationEnabled;

    }



    private static bool MatchesPresetBitrate(AppSettings settings, int presetIndex)

    {

        if (settings.BitrateMode == VideoBitrateMode.Adaptive)

        {

            return true;

        }



        int presetQualityIndex = presetIndex switch

        {

            0 => 0,

            2 => 2,

            _ => 1

        };



        return settings.BitrateKbps == BitrateFromQualityIndex(presetQualityIndex);

    }

}


