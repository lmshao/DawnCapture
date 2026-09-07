using System.Globalization;
using DawnCapture.Services;

namespace DawnCapture.Helpers;

public static class FileSizeFormatHelper
{
    public static string Format(long bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;

        if (bytes >= gb)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                LocalizationService.GetString("Recordings_Size_GB"),
                bytes / gb);
        }

        if (bytes >= mb)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                LocalizationService.GetString("Recordings_Size_MB"),
                bytes / mb);
        }

        if (bytes >= kb)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                LocalizationService.GetString("Recordings_Size_KB"),
                bytes / kb);
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.GetString("Recordings_Size_B"),
            bytes);
    }
}
