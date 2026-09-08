# DawnCapture one-stop distribution builder
#
# Builds installable packages for every distribution channel:
#   ZIP  - portable folder: no certificate, no install, unzip and run
#   MSIX - signed sideload bundle (msix + cert + runtime + install helper)
#
# Artifacts (Name-Version-Arch-Channel):
#   bin\DawnCapture-<version>-<arch>-portable.zip
#   bin\DawnCapture-<version>-<arch>-sideload.zip
#
# Usage:
#   distribute.ps1           # shows this help
#   distribute.ps1 zip       # portable zip
#   distribute.ps1 msix      # msix bundle
#   distribute.ps1 all       # both
#   distribute.ps1 zip arm64 # arch / version options
#
# Build-only: the script cleans the Release build cache first, then packages
# and validates artifacts; it never installs or launches the app. Version
# and publisher come from Package.appxmanifest.
#
# Note: keep this file ASCII-only. PowerShell 5.1 reads BOM-less files as
# ANSI, so Chinese literals would break. The publisher is read from
# Package.appxmanifest instead.
param(
    [Parameter(Position = 0)]
    [ValidateSet("zip", "msix", "all", "")]
    [string]$Method = "",
    [string]$Configuration = "Release",
    [string]$Architecture = "x64",
    [string]$Version = "",
    [string]$Publisher = "",
    [int]$CertYears = 5
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not $Method)
{
    Write-Host "DawnCapture distribution builder" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor White
    Write-Host "  distribute.ps1             shows this help"
    Write-Host "  distribute.ps1 zip         portable zip (unzip and run)"
    Write-Host "  distribute.ps1 msix        sideload bundle (msix + cert + runtime)"
    Write-Host "  distribute.ps1 all         both packages"
    Write-Host ""
    Write-Host "Options:" -ForegroundColor White
    Write-Host "  -Architecture x64|arm64|x86   default x64"
    Write-Host "  -Version 1.0.0.0              default from Package.appxmanifest"
    Write-Host "  -Publisher `"CN=...`"           default from Package.appxmanifest"
    Write-Host "  -Configuration Release         default Release"
    Write-Host ""
    Write-Host "Artifacts (Name-Version-Arch-Channel):" -ForegroundColor White
    Write-Host "  bin\DawnCapture-<version>-<arch>-portable.zip"
    Write-Host "  bin\DawnCapture-<version>-<arch>-sideload.zip"
    exit 0
}

$project = Join-Path $root "DawnCapture.csproj"
$framework = "net8.0-windows10.0.19041.0"
$runtimeId = "win-$($Architecture.ToLower())"
$publishDir = Join-Path $root "bin\$Configuration\$framework\$runtimeId\publish"

# ---------------------------------------------------------------------------
# Identity from the manifest: version and publisher are the single source of
# truth, so artifacts always carry the real version.
# ---------------------------------------------------------------------------
[xml]$manifest = Get-Content "Package.appxmanifest" -Raw -Encoding UTF8
if (-not $Version)
{
    $Version = $manifest.Package.Identity.Version
}

if (-not $Publisher)
{
    $Publisher = $manifest.Package.Identity.Publisher
}

# Industry-style artifact names: Name-Version-Arch-Channel.
$zipPath = Join-Path $root "bin\DawnCapture-${Version}-${Architecture}-portable.zip"
$sideloadZip = Join-Path $root "bin\DawnCapture-${Version}-${Architecture}-sideload.zip"
$msixDir = Join-Path $root "AppPackages\DawnCapture_${Version}_${Architecture}_Release"
$finalMsix = Join-Path $msixDir "DawnCapture-${Version}-${Architecture}.msix"

Write-Host "Version: $Version | Publisher: $Publisher" -ForegroundColor Gray

# Close any running instance so files are not locked.
Get-Process DawnCapture -ErrorAction SilentlyContinue | Stop-Process -Force

# ---------------------------------------------------------------------------
# Clean the build cache first so every package is built from scratch.
# ---------------------------------------------------------------------------
Write-Host "==> Cleaning $Configuration build cache" -ForegroundColor Cyan
foreach ($dir in @(
    (Join-Path $root "bin\$Configuration"),
    (Join-Path $root "obj\$Configuration"),
    (Join-Path $root "bin\$Architecture\$Configuration"),
    (Join-Path $root "obj\$Architecture\$Configuration")
))
{
    if (Test-Path $dir)
    {
        Remove-Item $dir -Recurse -Force
    }
}

# ---------------------------------------------------------------------------
# ZIP: unpackaged self-contained publish, packaged as a portable folder zip.
# ---------------------------------------------------------------------------
function Build-ZipPackage
{
    Write-Host ""
    Write-Host "==> [ZIP] Publishing unpackaged app ($Configuration/$Architecture)" -ForegroundColor Cyan
    if (Test-Path $publishDir)
    {
        Remove-Item $publishDir -Recurse -Force
    }

    dotnet publish $project `
        -c $Configuration `
        -p:Platform=$Architecture `
        -p:PublishTrimmed=false `
        -nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Publish failed with exit code $LASTEXITCODE"
    }

    # DawnCapture.pri is required by WinUI 3 at startup; without it the app
    # crashes with 0xC000027B in Microsoft.UI.Xaml.dll.
    $pri = Join-Path $publishDir "DawnCapture.pri"
    if (-not (Test-Path $pri))
    {
        throw "Missing $pri - the app would crash on launch."
    }

    # Guard against a trimmed publish (TypeLoadException at startup).
    $runtimeDll = Join-Path $publishDir "System.Runtime.dll"
    if ((Get-Item $runtimeDll).Length -lt 30000)
    {
        throw "System.Runtime.dll looks trimmed; publish must use PublishTrimmed=false."
    }

    if (Test-Path $zipPath)
    {
        Remove-Item $zipPath -Force
    }

    Write-Host "==> [ZIP] Packaging zip" -ForegroundColor Cyan
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

    $sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "OK: $zipPath ($sizeMb MB)" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# MSIX: framework-dependent signed package for sideloading.
# ---------------------------------------------------------------------------
function Build-MsixPackage
{
    Write-Host ""
    Write-Host "==> [MSIX] Resolving self-signed certificate" -ForegroundColor Cyan
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $Publisher -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert)
    {
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $Publisher `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -NotAfter (Get-Date).AddYears($CertYears)
    }

    Write-Host "Certificate: $($cert.Thumbprint)" -ForegroundColor Gray

    # Two properties are essential here:
    # - WindowsAppSDKSelfContained=false: self-contained + MSIX crashes at
    #   startup (XAML stowed exception 0xC000027B).
    # - PublishTrimmed=false: trimming breaks WindowsAppRuntime.Projection
    #   assembly load (.NET FileNotFoundException).
    # The Windows App SDK nests the output one level deeper, so use a
    # relative AppxPackageDir and flatten afterwards (absolute paths are
    # silently ignored by the packaging targets).
    if (Test-Path $msixDir)
    {
        Remove-Item $msixDir -Recurse -Force
    }

    if (Test-Path $publishDir)
    {
        Remove-Item $publishDir -Recurse -Force
    }

    $relativePackageDir = "AppPackages\DawnCapture_${Version}_${Architecture}_Release\"

    Write-Host "==> [MSIX] Building signed package ($Configuration/$Architecture)" -ForegroundColor Cyan
    dotnet publish $project `
        -c $Configuration `
        -p:Platform=$Architecture `
        -p:EnableMsixTooling=true `
        -p:WindowsPackageType=MSIX `
        -p:WindowsAppSDKSelfContained=false `
        -p:PublishTrimmed=false `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageSigningEnabled=true `
        -p:PackageCertificateThumbprint=$($cert.Thumbprint) `
        -p:AppxBundle=Never `
        "-p:AppxPackageDir=$relativePackageDir" `
        -nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "MSIX build failed with exit code $LASTEXITCODE"
    }

    # Flatten the nested output into the clean package folder.
    $nestedDir = Get-ChildItem $msixDir -Directory | Select-Object -First 1
    if (-not $nestedDir)
    {
        throw "No nested package folder found under $msixDir"
    }

    $msix = Get-ChildItem $nestedDir.FullName -Filter "*.msix" |
        Where-Object { $_.FullName -notmatch "[\\/]Dependencies[\\/]" } |
        Select-Object -First 1
    if (-not $msix)
    {
        throw "No .msix found under $($nestedDir.FullName)"
    }

    Move-Item $msix.FullName -Destination $finalMsix

    $cer = Get-ChildItem $nestedDir.FullName -Filter "*.cer" | Select-Object -First 1
    if ($cer)
    {
        Move-Item $cer.FullName -Destination (Join-Path $msixDir "DawnCapture-${Version}-${Architecture}.cer") -Force
    }

    Get-ChildItem $nestedDir.FullName -File |
        ForEach-Object { Move-Item $_.FullName -Destination $msixDir -Force }
    Get-ChildItem $nestedDir.FullName -Directory |
        ForEach-Object {
            $target = Join-Path $msixDir $_.Name
            if (Test-Path $target)
            {
                Remove-Item $target -Recurse -Force
            }

            Move-Item $_.FullName -Destination $msixDir
        }

    Remove-Item $nestedDir.FullName -Recurse -Force -ErrorAction SilentlyContinue

    # Verify signature and runtime dependencies.
    $sig = Get-AuthenticodeSignature $finalMsix
    if (-not $sig.SignerCertificate)
    {
        throw "MSIX is not signed."
    }

    if ($sig.SignerCertificate.Subject -ne $Publisher)
    {
        throw "Signature subject mismatch: expected $Publisher, got $($sig.SignerCertificate.Subject)."
    }

    $depDir = Join-Path $msixDir "Dependencies"
    if (-not (Test-Path $depDir) -or (Get-ChildItem $depDir -Recurse -Filter "*.msix" | Measure-Object).Count -eq 0)
    {
        throw "Runtime dependencies are missing from the package folder."
    }

    $sizeMb = [Math]::Round((Get-Item $finalMsix).Length / 1MB, 1)
    Write-Host "OK: $finalMsix ($sizeMb MB)" -ForegroundColor Green

    # Package the whole sideload bundle (msix + cert + runtime dependencies
    # + install helpers) into a single zip for easy distribution.
    if (Test-Path $sideloadZip)
    {
        Remove-Item $sideloadZip -Force
    }

    Write-Host "==> [MSIX] Packaging sideload bundle" -ForegroundColor Cyan
    Compress-Archive -Path "$msixDir\*" -DestinationPath $sideloadZip -CompressionLevel Optimal

    $bundleMb = [Math]::Round((Get-Item $sideloadZip).Length / 1MB, 1)
    Write-Host "OK: $sideloadZip ($bundleMb MB)" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Main dispatch. ZIP first so the unpackaged publish is never polluted by
# the MSIX-configured build output.
# ---------------------------------------------------------------------------
if ($Method -eq "zip" -or $Method -eq "all")
{
    Build-ZipPackage
}

if ($Method -eq "msix" -or $Method -eq "all")
{
    Build-MsixPackage
}

# ---------------------------------------------------------------------------
# Summary.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Distribution artifacts ===" -ForegroundColor Cyan
if ($Method -eq "zip" -or $Method -eq "all")
{
    Write-Host "ZIP : $zipPath" -ForegroundColor White
    Write-Host "       Portable. Unzip anywhere and run DawnCapture.exe." -ForegroundColor Gray
    Write-Host "       No certificate, no install, no admin required." -ForegroundColor Gray
}

if ($Method -eq "msix" -or $Method -eq "all")
{
    Write-Host "MSIX: $sideloadZip" -ForegroundColor White
    Write-Host "       Sideload bundle (msix + cert + runtime + install helper)." -ForegroundColor Gray
    Write-Host "       Unzip on the target PC and run Add-AppDevPackage.ps1." -ForegroundColor Gray
}
