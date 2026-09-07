using System;
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
        string? languageOverride = language switch
        {
            SimplifiedChinese => SimplifiedChinese,
            English => English,
            _ => null
        };

        if (languageOverride is not null)
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageOverride;
        }
        else if (!string.IsNullOrEmpty(ApplicationLanguages.PrimaryLanguageOverride))
        {
            ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
        }

        _resourceLoader = null;
    }

    public static string GetString(string key)
    {
        try
        {
            _resourceLoader ??= new ResourceLoader();
            string value = _resourceLoader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception ex)
        {
            Log.Info($"Resource lookup failed for '{key}': {ex.Message}");
            return key;
        }
    }
}
