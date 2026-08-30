# DawnCapture one-click rebuild, register and run (Debug/x64)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# Close any existing instance so we get a fresh launch
Get-Process DawnCapture -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "==> Building DawnCapture (Debug/x64)" -ForegroundColor Cyan
dotnet build DawnCapture.csproj -c Debug -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$appxManifest = Join-Path $root "bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\AppX\AppxManifest.xml"
if (-not (Test-Path $appxManifest)) {
    Write-Error "AppxManifest.xml not found: $appxManifest"
    exit 1
}

Write-Host "==> Registering app package" -ForegroundColor Cyan
Add-AppxPackage -Register $appxManifest -ForceUpdateFromAnyVersion

[xml]$manifest = Get-Content (Join-Path $root "Package.appxmanifest")
$packageName = $manifest.Package.Identity.Name
$package = Get-AppxPackage -Name $packageName
if (-not $package) {
    Write-Error "Package not found after registration: $packageName"
    exit 1
}

Write-Host "==> Launching $($package.PackageFamilyName)" -ForegroundColor Cyan
explorer.exe "shell:AppsFolder\$($package.PackageFamilyName)!App"
Write-Host "Done."
