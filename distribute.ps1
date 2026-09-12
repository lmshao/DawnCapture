# DawnCapture one-stop distribution builder
#
# Builds installable packages for every distribution channel:
#   ZIP       - portable folder: no certificate, no install, unzip and run
#   MSIX      - signed sideload bundle (msix + cert + runtime + install helper)
#   INSTALLER - unsigned per-user Setup.exe built with Inno Setup 6
#
# Artifacts (Name-Version-Arch-Channel):
#   bin\DawnCapture-<version>-<arch>-portable.zip
#   bin\DawnCapture-<version>-<arch>-sideload.zip
#   bin\DawnCapture-<version>-<arch>-setup.exe
#
# All artifacts are single-architecture: the sideload bundle keeps only the
# runtime dependency of its own -Architecture.
#
# Usage:
#   distribute.ps1           # shows this help
#   distribute.ps1 zip       # portable zip
#   distribute.ps1 msix      # msix bundle
#   distribute.ps1 installer # unsigned Setup.exe
#   distribute.ps1 all       # all three
#   distribute.ps1 zip arm64 # arch / version options
#
# Build-only: the script cleans the Release build cache first, then packages
# and validates artifacts; it never installs or launches the app. Version
# and publisher come from Package.appxmanifest.
#
# Note: keep this file ASCII-only. PowerShell 5.1 reads BOM-less files as
# ANSI, so Chinese literals would break. The publisher is read from
# Package.appxmanifest instead.
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0)]
    [ValidateSet("zip", "msix", "installer", "all", "")]
    [string]$Method = "",
    [string]$Configuration = "Release",
    # Position 1 so that "distribute.ps1 zip arm64" means the architecture,
    # as the usage line has always documented. Everything else is named-only.
    [Parameter(Position = 1)]
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
    Write-Host "  distribute.ps1 installer   Setup.exe (Inno Setup, unsigned)"
    Write-Host "  distribute.ps1 all         every package"
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
    Write-Host "  bin\DawnCapture-<version>-<arch>-setup.exe"
    exit 0
}

$project = Join-Path $root "DawnCapture.csproj"
$framework = "net10.0-windows10.0.19041.0"
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
$setupPath = Join-Path $root "bin\DawnCapture-${Version}-${Architecture}-setup.exe"
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
# Shared payload: the unpackaged self-contained publish. The portable ZIP and
# the Setup.exe wrap exactly this folder, so it is published and guarded once.
# ---------------------------------------------------------------------------
function Invoke-PortablePublish
{
    # The portable zip and the Setup.exe wrap the same payload, so with
    # "-Method all" the second caller reuses what the first one produced.
    if ($script:portablePayloadReady)
    {
        Write-Host "==> Reusing published payload" -ForegroundColor Cyan
        return
    }

    Write-Host ""
    Write-Host "==> Publishing unpackaged app ($Configuration/$Architecture)" -ForegroundColor Cyan
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

    # Guard against the unused Windows App SDK payload (AI/ML/Search/Widgets,
    # ~50 MB) coming back; see the component references in DawnCapture.csproj.
    $unusedPayload = Get-ChildItem $publishDir -File | Where-Object {
        $_.Name -in @("onnxruntime.dll", "DirectML.dll", "Microsoft.ML.OnnxRuntime.dll") -or
        $_.Name -like "Microsoft.Windows.AI.*" -or
        $_.Name -like "Microsoft.Windows.AI.dll" -or
        $_.Name -like "Microsoft.Windows.Search.*" -or
        $_.Name -like "Microsoft.Windows.Widgets.*" -or
        $_.Name -like "Microsoft.Windows.SemanticSearch*"
    }
    if ($unusedPayload)
    {
        $names = ($unusedPayload | Select-Object -ExpandProperty Name) -join ", "
        $mb = [Math]::Round(($unusedPayload | Measure-Object Length -Sum).Sum / 1MB, 1)
        throw "Unused Windows App SDK payload ($mb MB) found in publish output: $names"
    }

    # Debug artifacts are stripped by the StripPublishDebugArtifacts target.
    $debugArtifacts = Get-ChildItem $publishDir -File |
        Where-Object { $_.Extension -eq ".pdb" -or $_.Name -like "Microsoft.DiaSymReader.Native.*" }
    if ($debugArtifacts)
    {
        throw "Debug artifacts found in publish output: $($debugArtifacts.Name -join ', ')"
    }

    $script:portablePayloadReady = $true
}

# ---------------------------------------------------------------------------
# ZIP: the shared payload, packaged as a portable folder zip.
# ---------------------------------------------------------------------------
function Build-ZipPackage
{
    Write-Host ""
    Write-Host "==> [ZIP] Portable zip" -ForegroundColor Cyan
    Invoke-PortablePublish

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
# INSTALLER: a single unsigned Setup.exe built with Inno Setup 6.
#
# Inno Setup emits an ordinary Win32 executable, and Windows does not gate
# PE images on Authenticode - so this channel installs without any
# certificate. That is the whole point: the MSIX channel needs a signing
# certificate trusted by the target machine, this one does not.
#
# There is no prerequisite detection because there is nothing to detect: the
# payload carries .NET 10 and the Windows App SDK, and no native binary imports
# VCRUNTIME140/MSVCP140 (only the OS-provided UCRT, present since Windows 10).
# The script lives in installer\inno\DawnCapture.iss, whose header comments carry
# the full rationale and the SmartScreen caveat.
# ---------------------------------------------------------------------------
function Resolve-Iscc
{
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd)
    {
        return $cmd.Source
    }

    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ))
    {
        if ($candidate -and (Test-Path $candidate))
        {
            return $candidate
        }
    }

    return $null
}

function Build-InstallerPackage
{
    Write-Host ""
    Write-Host "==> [SETUP] Setup.exe" -ForegroundColor Cyan
    Invoke-PortablePublish

    $iscc = Resolve-Iscc
    if (-not $iscc)
    {
        throw "ISCC.exe not found. Install Inno Setup 6 first: winget install --id JRSoftware.InnoSetup -e"
    }

    if (Test-Path $setupPath)
    {
        Remove-Item $setupPath -Force
    }

    # The manifest carries the publisher as an Open Packaging Convention string
    # ("CN=Name"); the installer wants the bare display name.
    $publisherName = $Publisher -replace "^CN=", ""

    # The welcome page quotes the disk space requirement before Inno's own Ready
    # page can show it, so the number has to come from the payload being wrapped
    # rather than from a literal inside the .iss. Ceiling on purpose: the sentence
    # is a promise about room, so rounding down would understate it.
    $payloadMb = [Math]::Ceiling(
        (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
    Write-Host "    payload: $payloadMb MB" -ForegroundColor Gray

    Write-Host "==> [SETUP] Compiling with Inno Setup" -ForegroundColor Cyan
    # Absolute paths: ISCC resolves /D paths against its own working directory,
    # and the solution build rewrites bin\Release.
    & $iscc `
        "/DPayloadDir=$publishDir" `
        "/DAppVersion=$Version" `
        "/DAppPublisher=$publisherName" `
        "/DAppArch=$Architecture" `
        "/DAppSizeMB=$payloadMb" `
        (Join-Path $root "installer\inno\DawnCapture.iss")
    if ($LASTEXITCODE -ne 0)
    {
        throw "ISCC failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path $setupPath))
    {
        throw "Expected artifact was not produced: $setupPath"
    }

    # Report the signature rather than assert on it: this channel is expected
    # to be unsigned, but with no SignTool configured the status is simply
    # informational. If it ever reads Valid, a SignTool was added.
    $signature = Get-AuthenticodeSignature $setupPath
    Write-Host "    signature: $($signature.Status)" -ForegroundColor Gray

    $sizeMb = [Math]::Round((Get-Item $setupPath).Length / 1MB, 1)
    Write-Host "OK: $setupPath ($sizeMb MB)" -ForegroundColor Green
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

    # Keep only this architecture's runtime dependency. The packaging targets
    # emit x64/arm64/x86/win32 (131.7 MB), but an app package only ever resolves
    # the framework for its own architecture, and Add-AppDevPackage.ps1 reads
    # x64/x86/arm/arm64 only - never win32. Removes 87.1 MB from the bundle.
    $depDir = Join-Path $msixDir "Dependencies"
    if (-not (Test-Path $depDir))
    {
        throw "Runtime dependencies are missing from the package folder."
    }

    $keptDepDir = Join-Path $depDir $Architecture
    if (-not (Test-Path $keptDepDir))
    {
        throw "Missing $keptDepDir - the sideload bundle would install no runtime dependency."
    }

    Get-ChildItem $depDir -Directory |
        Where-Object { $_.FullName -ne $keptDepDir } |
        ForEach-Object {
            Write-Host "    dropping unused $($_.Name) dependency" -ForegroundColor Gray
            Remove-Item $_.FullName -Recurse -Force
        }

    if ((Get-ChildItem $depDir -Recurse -Filter "*.msix" | Measure-Object).Count -eq 0)
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
# Main dispatch. The unpackaged channels run before MSIX so their publish is
# never polluted by the MSIX-configured build output, which reuses the same
# publish directory.
# ---------------------------------------------------------------------------
if ($Method -eq "zip" -or $Method -eq "all")
{
    Build-ZipPackage
}

if ($Method -eq "installer" -or $Method -eq "all")
{
    Build-InstallerPackage
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

if ($Method -eq "installer" -or $Method -eq "all")
{
    Write-Host "SETUP: $setupPath" -ForegroundColor White
    Write-Host "       Single unsigned .exe. Per-user install, no admin, no prerequisites." -ForegroundColor Gray
    Write-Host "       Windows may show a SmartScreen prompt until it earns reputation." -ForegroundColor Gray
}
