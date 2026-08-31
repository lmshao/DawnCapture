# DawnCapture one-click clean, rebuild, and launch (Debug/x64)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$project = Join-Path $root "DawnCapture.csproj"
$configuration = "Debug"
$platform = "x64"
$framework = "net8.0-windows10.0.19041.0"
$runtimeIdentifier = "win-x64"
$executable = Join-Path $root "bin\$platform\$configuration\$framework\$runtimeIdentifier\DawnCapture.exe"

# Close any existing instance so files are not locked during clean.
Get-Process DawnCapture -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "==> Cleaning DawnCapture ($configuration/$platform)" -ForegroundColor Cyan
dotnet clean $project -c $configuration -p:Platform=$platform -p:RuntimeIdentifier=$runtimeIdentifier
if ($LASTEXITCODE -ne 0) {
    Write-Error "Clean failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "==> Building DawnCapture ($configuration/$platform)" -ForegroundColor Cyan
dotnet build $project `
    -c $configuration `
    -p:Platform=$platform `
    -p:RuntimeIdentifier=$runtimeIdentifier
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

if (-not (Test-Path $executable)) {
    Write-Error "Application executable not found after build: $executable"
    exit 1
}

Write-Host "==> Launching DawnCapture" -ForegroundColor Cyan
$process = Start-Process -FilePath $executable -WorkingDirectory (Split-Path $executable) -PassThru
if ($process.WaitForExit(10000)) {
    Write-Error "DawnCapture exited during startup with code $($process.ExitCode)"
    exit 1
}

Write-Host "Done. DawnCapture is running (PID $($process.Id))." -ForegroundColor Green
