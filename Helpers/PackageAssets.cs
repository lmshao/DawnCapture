using System;

namespace DawnCapture.Helpers;

/// <summary>
/// Canonical paths for files under <c>Assets/</c>.
/// Runtime icons use <see cref="AppIcon"/> and <see cref="AboutLogo"/>.
/// MSIX / Store tiles reference the same paths via <c>Package.appxmanifest</c>.
/// </summary>
public static class PackageAssets
{
    public const string Root = "Assets";

    /// <summary>Hand-maintained vector masters under <c>Assets/brand/</c> (source only; not shipped).</summary>
    public const string LogoMasterSvg = "Assets/brand/dawncapture-logo-master.svg";
    public const string LogoSymbolSvg = "Assets/brand/dawncapture-logo-symbol.svg";
    public const string LogoMarkSvg = "Assets/brand/dawncapture-logo-mark.svg";

    // Runtime (unpackaged + MSIX)
    public const string AppIcon = "Assets/AppIcon.ico";
    /// <summary>256×256 PNG for in-app About header (downscaled in UI, never upscaled).</summary>
    public const string AboutLogo = "Assets/AboutLogo.png";

    // MSIX visual elements (Package.appxmanifest)
    public const string StoreLogo = "Assets/StoreLogo.png";
    public const string Square44x44Logo = "Assets/Square44x44Logo.png";
    public const string Square150x150Logo = "Assets/Square150x150Logo.png";
    public const string Wide310x150Logo = "Assets/Wide310x150Logo.png";
    public const string SplashScreen = "Assets/SplashScreen.png";
    public const string LockScreenLogo = "Assets/LockScreenLogo.png";

    // Scale-200 source files on disk (manifest uses logical names above)
    public const string Square44x44LogoScale200 = "Assets/Square44x44Logo.scale-200.png";
    public const string Square44x44LogoTarget24Unplated = "Assets/Square44x44Logo.targetsize-24_altform-unplated.png";
    public const string Square150x150LogoScale200 = "Assets/Square150x150Logo.scale-200.png";
    public const string Wide310x150LogoScale200 = "Assets/Wide310x150Logo.scale-200.png";
    public const string SplashScreenScale200 = "Assets/SplashScreen.scale-200.png";
    public const string LockScreenLogoScale200 = "Assets/LockScreenLogo.scale-200.png";

    public static string AboutLogoSource => $"ms-appx:///{AboutLogo}";
}
