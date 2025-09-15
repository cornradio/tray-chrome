# One-click packaging script - Package application as single file
# Framework-dependent (requires .NET runtime on target machine)

Write-Host "Starting TrayChrome application packaging..." -ForegroundColor Green

# Clean previous publish files
if (Test-Path "./publish") {
    Write-Host "Cleaning old publish files..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force "./publish"
}

# Create publish directory
New-Item -ItemType Directory -Path "./publish" -Force | Out-Null

try {
    # Publish as single file, framework-dependent (requires .NET runtime on target)
    Write-Host "Publishing application..." -ForegroundColor Cyan
    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "./publish"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nPackaging successful!" -ForegroundColor Green
        Write-Host "Output directory: ./publish" -ForegroundColor White
        Write-Host "Executable file: TrayChrome.exe" -ForegroundColor White
        Write-Host "`nImportant notes:" -ForegroundColor Yellow
        Write-Host "   • Target machine needs .NET 6.0 Desktop Runtime installed" -ForegroundColor Gray
        Write-Host "   • Smaller file size but requires runtime dependency" -ForegroundColor Gray
        Write-Host "   • Download: https://dotnet.microsoft.com/download/dotnet/6.0" -ForegroundColor Gray
        
        # Display file information
        $exePath = "./publish/TrayChrome.exe"
        if (Test-Path $exePath) {
            $fileInfo = Get-Item $exePath
            $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
            Write-Host "`nFile size: $fileSizeMB MB" -ForegroundColor Cyan
        }
        
        # Ask if user wants to open publish directory
        $openFolder = Read-Host "`nOpen publish directory? (y/n)"
        if ($openFolder -eq 'y' -or $openFolder -eq 'Y') {
            Start-Process "explorer.exe" -ArgumentList (Resolve-Path "./publish")
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