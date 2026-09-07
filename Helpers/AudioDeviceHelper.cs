using NAudio.CoreAudioApi;
using DawnCapture.Services;

namespace DawnCapture.Helpers;

public static class AudioDeviceHelper
{
    public static bool HasActiveCaptureDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Count > 0;
        }
        catch (System.Exception ex)
        {
            Log.Info($"Microphone detection failed: {ex.Message}");
            return false;
        }
    }

    public static bool HasActiveRenderDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Count > 0;
        }
        catch (System.Exception ex)
        {
            Log.Info($"System audio detection failed: {ex.Message}");
            return false;
        }
    }
}
