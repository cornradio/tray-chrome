# Package framework-dependent and self-contained single-file editions.

param(
    [string]$DotnetPath,
    [switch]$NoPrompt,
    [ValidateSet('Both', 'framework-dependent', 'self-contained')]
    [string]$Edition = 'Both'
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'TrayChrome.csproj'
$publishPath = Join-Path $PSScriptRoot 'publish'

function Test-CompatibleSdk([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }
    $sdks = & $Path --list-sdks
    if ($LASTEXITCODE -ne 0) {
        return $false
    }
    return @($sdks | Where-Object { $_ -match '^(\d+)\.\d+\.\d+\s' -and [int]$Matches[1] -ge 9 }).Count -gt 0
}

Write-Host "Starting TrayChrome application packaging..." -ForegroundColor Green

try {
    if ($DotnetPath) {
        if (-not (Test-CompatibleSdk $DotnetPath)) {
            throw "No compatible .NET SDK found at '$DotnetPath'. Install the .NET 9 SDK (not just the Desktop Runtime)."
        }
        $dotnet = (Resolve-Path -LiteralPath $DotnetPath).Path
    } else {
        $candidates = @()
        $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
        if ($command) { $candidates += $command.Source }
        if ($env:DOTNET_ROOT) { $candidates += Join-Path $env:DOTNET_ROOT 'dotnet.exe' }
        if ($env:USERPROFILE) { $candidates += Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }
        if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }

        $dotnet = $null
        foreach ($candidate in ($candidates | Select-Object -Unique)) {
            if (Test-CompatibleSdk $candidate) {
                $dotnet = $candidate
                break
            }
        }
        if (-not $dotnet) {
            throw "No .NET 9 or newer SDK found. Install the .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0 (the Desktop Runtime alone cannot build). For a custom installation, run .\pack.ps1 -DotnetPath 'C:\path\to\dotnet.exe'."
        }
    }
    Write-Host "Using .NET SDK host: $dotnet" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

    $editions = @(
        @{ Name = 'framework-dependent'; SelfContained = 'false'; Requirement = 'Requires .NET 9 Desktop Runtime (x64).' },
        @{ Name = 'self-contained'; SelfContained = 'true'; Requirement = 'Includes .NET runtime; no .NET installation required.' }
    )
    if ($Edition -ne 'Both') {
        $editions = @($editions | Where-Object { $_.Name -eq $Edition })
    }
    foreach ($package in $editions) {
        $editionPath = Join-Path $publishPath $package.Name
        New-Item -ItemType Directory -Path $editionPath -Force | Out-Null
        Write-Host "`nPublishing $($package.Name)..." -ForegroundColor Cyan
        $compressionProperty = "-p:EnableCompressionInSingleFile=$($package.SelfContained)"
        & $dotnet publish $projectPath -c Release -r win-x64 --self-contained $package.SelfContained -p:PublishSingleFile=true $compressionProperty -p:IncludeNativeLibrariesForSelfExtract=true -o $editionPath
        if ($LASTEXITCODE -ne 0) {
            throw "Publishing $($package.Name) failed (exit code $LASTEXITCODE)."
        }

        $exePath = Join-Path $editionPath 'TrayChrome.exe'
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
            throw "Publishing $($package.Name) returned success but TrayChrome.exe was not created."
        }
        $fileInfo = Get-Item -LiteralPath $exePath
        $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "Executable: $exePath ($fileSizeMB MB)" -ForegroundColor White
        Write-Host $package.Requirement -ForegroundColor Yellow
    }

    Write-Host "`nSelected editions packaged successfully!" -ForegroundColor Green
    Write-Host "Both editions require WebView2 Runtime. Neither requires the .NET SDK to run." -ForegroundColor Yellow
    Write-Host "Existing files directly under publish are unchanged; use the edition subfolders." -ForegroundColor Gray

    if (-not $NoPrompt) {
        $openFolder = Read-Host "`nOpen publish directory? (y/n)"
        if ($openFolder -eq 'y' -or $openFolder -eq 'Y') {
            Start-Process "explorer.exe" -ArgumentList "`"$publishPath`""
        }
    }
} catch {
    Write-Host "`nError occurred during packaging: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "`nPackaging completed!" -ForegroundColor Green