# One-click packaging script - Package application as single file
# Framework-dependent (requires .NET runtime on target machine)

param(
    [string]$DotnetPath,
    [switch]$NoPrompt
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

    # Publish as single file, framework-dependent (requires .NET runtime on target)
    Write-Host "Publishing application..." -ForegroundColor Cyan
    & $dotnet publish $projectPath -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishPath
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nPackaging successful!" -ForegroundColor Green
        Write-Host "Output directory: $publishPath" -ForegroundColor White
        Write-Host "Executable file: TrayChrome.exe" -ForegroundColor White
        Write-Host "`nImportant notes:" -ForegroundColor Yellow
        Write-Host "   • Target machine needs .NET 9.0 Desktop Runtime installed" -ForegroundColor Gray
        Write-Host "   • Smaller file size but requires runtime dependency" -ForegroundColor Gray
        Write-Host "   • Download: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Gray
        
        # Display file information
        $exePath = Join-Path $publishPath 'TrayChrome.exe'
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
            throw "Publish returned success but TrayChrome.exe was not created."
        }
        if (Test-Path $exePath) {
            $fileInfo = Get-Item $exePath
            $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
            Write-Host "`nFile size: $fileSizeMB MB" -ForegroundColor Cyan
        }
        
        # Ask if user wants to open publish directory
        if (-not $NoPrompt) {
            $openFolder = Read-Host "`nOpen publish directory? (y/n)"
            if ($openFolder -eq 'y' -or $openFolder -eq 'Y') {
                Start-Process "explorer.exe" -ArgumentList "`"$publishPath`""
            }
        }
    } else {
        Write-Host "`nPackaging failed! Please check error messages." -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "`nError occurred during packaging: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "`nPackaging completed!" -ForegroundColor Green