# Publish only the framework-dependent edition and deploy its single-file executable.
param(
    [string]$DotnetPath
)

$ErrorActionPreference = 'Stop'
$destinationPath = 'C:\AAA\Traychrome'

try {
    $packArguments = @{
        Edition = 'framework-dependent'
        NoPrompt = $true
    }
    if ($DotnetPath) { $packArguments.DotnetPath = $DotnetPath }
    & (Join-Path $PSScriptRoot 'pack.ps1') @packArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packaging failed (exit code $LASTEXITCODE); deployment skipped."
    }

    $sourcePath = Join-Path $PSScriptRoot 'publish\framework-dependent\TrayChrome.exe'
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Packaged executable not found: $sourcePath"
    }
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    $destinationExe = Join-Path $destinationPath 'TrayChrome.exe'
    Copy-Item -LiteralPath $sourcePath -Destination $destinationExe -Force
    Write-Host "Copied executable to $destinationExe" -ForegroundColor Green
    Write-Host 'Existing settings, App groups and icons were not changed.' -ForegroundColor Gray
} catch {
    Write-Host "Packaging/deployment failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'If the destination executable is in use, close its instances and retry. This script does not stop them.' -ForegroundColor Yellow
    exit 1
}
