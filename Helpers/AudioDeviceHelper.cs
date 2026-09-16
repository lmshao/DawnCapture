using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using DawnCapture.Services;

namespace DawnCapture.Helpers;

public static class AudioDeviceHelper
{
    /// <summary>An endpoint the user can pick; the id is what gets stored in the settings.</summary>
    public sealed record AudioDeviceInfo(string Id, string Name);

    /// <summary>
    /// Endpoints that can be used right now, or null when the enumeration itself failed. The two
    /// must stay apart: "there are none" is a fact the settings page reports and acts on, while a
    /// failed query must not throw away the user's saved device.
    /// </summary>
    public static IReadOnlyList<AudioDeviceInfo>? GetActiveCaptureDevices() =>
        GetActiveDevices(DataFlow.Capture, "capture");

    public static IReadOnlyList<AudioDeviceInfo>? GetActiveRenderDevices() =>
        GetActiveDevices(DataFlow.Render, "render");

    private static IReadOnlyList<AudioDeviceInfo>? GetActiveDevices(DataFlow flow, string label)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = new List<AudioDeviceInfo>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            }

            return devices;
        }
        catch (Exception ex)
        {
            Log.Info($"Audio device enumeration ({label}) failed: {ex.Message}");
            return null;
        }
    }

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
