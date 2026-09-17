// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DawnCapture.Helpers;
using DawnCapture.Services;
using DawnCapture.Views;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace DawnCapture.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private const string AuthorEmailAddress = "lmshao@163.com";

    public AboutViewModel()
    {
        VersionLabel = FormatVersionLabel(Assembly.GetExecutingAssembly().GetName().Version);
    }

    public string Tagline => LocalizationService.GetString("About_Title");

    public string Description => LocalizationService.GetString("About_Description");

    public string AuthorPrefix => LocalizationService.GetString("About_AuthorPrefix");

    public string AuthorEmail => AuthorEmailAddress;

    public Uri AuthorMailUri { get; } = new($"mailto:{AuthorEmailAddress}");

    public string TrustLocalText => LocalizationService.GetString("About_Trust_Local");

    public string TrustNoAccountText => LocalizationService.GetString("About_Trust_NoAccount");

    public string TrustWindowsText => LocalizationService.GetString("About_Trust_Windows11");

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    private static string FormatVersionLabel(Version? version)
    {
        if (version is null)
        {
            return LocalizationService.GetString("About_ProductName");
        }

        return string.Format(
            LocalizationService.GetString("About_Version"),
            version.ToString(3));
    }

    [RelayCommand]
    private Task ShowThirdPartyNoticesAsync() =>
        ShowLegalDocumentAsync("THIRD-PARTY-NOTICES.md", "About_ThirdPartyNotices_Title", "About_ThirdPartyNoticesMissing");

    [RelayCommand]
    private Task ShowPrivacyPolicyAsync() =>
        ShowLegalDocumentAsync("PRIVACY-POLICY.md", "About_PrivacyPolicy_Title", "About_PrivacyPolicyMissing");

    private static async Task ShowLegalDocumentAsync(string fileName, string titleKey, string missingKey)
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(filePath))
        {
            await DialogHelper.ShowErrorAsync(LocalizationService.GetString(missingKey));
            return;
        }

        try
        {
            await LegalDocumentDialog.ShowAsync(
                LocalizationService.GetString(titleKey),
                await File.ReadAllTextAsync(filePath));
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read '{fileName}'", ex);
            await DialogHelper.ShowErrorAsync(LocalizationService.GetString("About_LegalReadFailed"));
        }
    }
}
