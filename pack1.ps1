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
    $runningInstances = @(Get-Process -Name 'TrayChrome' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $destinationExe })
    foreach ($instance in $runningInstances) {
        Write-Host "Stopping TrayChrome instance $($instance.Id) before deployment..." -ForegroundColor Yellow
        Stop-Process -Id $instance.Id -Force -ErrorAction Stop
        if (-not $instance.WaitForExit(10000)) {
            throw "TrayChrome instance $($instance.Id) did not exit; deployment skipped."
        }
    }
    Copy-Item -LiteralPath $sourcePath -Destination $destinationExe -Force
    Write-Host "Copied executable to $destinationExe" -ForegroundColor Green
    Write-Host 'Existing settings, App groups and icons were not changed.' -ForegroundColor Gray
} catch {
    Write-Host "Packaging/deployment failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Check permissions and ensure the destination executable is not locked, then retry.' -ForegroundColor Yellow
    exit 1
}
