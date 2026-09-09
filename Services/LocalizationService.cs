using System;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace DawnCapture.Services;

public static class LocalizationService
{
    public const string SystemLanguage = "system";
    public const string SimplifiedChinese = "zh-CN";
    public const string English = "en-US";

    /// <summary>Fixed native labels for language picker entries (not localized via RESW).</summary>
    public const string SimplifiedChineseDisplayName = "简体中文";
    public const string EnglishDisplayName = "English";

    private static ResourceLoader? _resourceLoader;

    public static void ApplyLanguage(string? language)
    {
        ApplicationLanguages.PrimaryLanguageOverride = ResolveLanguage(language);
        _resourceLoader = null;
    }

    /// <summary>
    /// Resolves the configured language to a concrete locale. Explicit zh-CN
    /// and en-US selections pass through; "system" (or any unknown value)
    /// follows the system UI language when it is Chinese and otherwise falls
    /// back to English, so non-Chinese systems never see Chinese text.
    /// </summary>
    private static string ResolveLanguage(string? language)
    {
        if (language == SimplifiedChinese)
        {
            return SimplifiedChinese;
        }

        if (language == English)
        {
            return English;
        }

        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChinese
            : English;
    }

    /// <summary>
    /// Resolves a localized string by its bare resource key.
    ///
    /// Key naming rule (enforced by convention; violations are logged here):
    /// - GetString takes the BARE key (e.g. "Capture_MicrophoneUnavailable").
    /// - x:Uid looks up "<Key>.<Property>" (e.g. "Capture_MicrophoneUnavailable.Text"),
    ///   so property-suffixed keys must never be passed to GetString — MRT Core
    ///   fails such lookups and this method would fall back to the raw key text.
    /// </summary>
    public static string GetString(string key)
    {
        try
        {
            _resourceLoader ??= new ResourceLoader();
            string value = _resourceLoader.GetString(key);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            Log.Error(
                $"Localized string missing for key '{key}'. " +
                "Ensure the key exists in Strings/zh-CN and Strings/en-US " +
                "and that no x:Uid property suffix ('.Text', '.Content') is appended.");
            return key;
        }
        catch (Exception ex)
        {
            Log.Error($"Resource lookup failed for '{key}': {ex.Message}");
            return key;
        }
    }
}
