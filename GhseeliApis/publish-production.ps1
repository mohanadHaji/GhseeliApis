# Publish GhseeliApis for Production Deployment
# Run this script from the solution root directory

Write-Host "Starting production publish..." -ForegroundColor Cyan

# Navigate to project directory
Set-Location -Path "GhseeliApis"

# Clean ALL build artifacts (including bin and obj folders)
Write-Host "`nCleaning ALL build artifacts..." -ForegroundColor Yellow
if (Test-Path "bin") {
    Remove-Item -Path "bin" -Recurse -Force
    Write-Host "  ? Deleted bin folder" -ForegroundColor Green
}
if (Test-Path "obj") {
    Remove-Item -Path "obj" -Recurse -Force
    Write-Host "  ? Deleted obj folder" -ForegroundColor Green
}

# Clean using dotnet command
dotnet clean --configuration Release --verbosity minimal

# Restore dependencies
Write-Host "`nRestoring dependencies..." -ForegroundColor Yellow
dotnet restore --verbosity minimal

# Publish the application (FORCE FRESH BUILD)
Write-Host "`nPublishing application (forcing fresh build)..." -ForegroundColor Yellow
Write-Host "  Configuration: Release" -ForegroundColor Gray
Write-Host "  Target: .NET 8.0" -ForegroundColor Gray
Write-Host "  Build timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray

dotnet publish `
    --configuration Release `
    --output "bin\Release\net8.0\publish" `
    --runtime win-x64 `
    --self-contained false `
    --no-restore `
    /p:PublishTrimmed=false `
    /p:PublishSingleFile=false `
    /p:DebugType=None `
    /p:DebugSymbols=false

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n? Publish completed successfully!" -ForegroundColor Green
    Write-Host "`nPublished files location:" -ForegroundColor Cyan
    Write-Host "$(Get-Location)\bin\Release\net8.0\publish" -ForegroundColor White
    
    Write-Host "`nVerifying published files..." -ForegroundColor Yellow
    $publishPath = "bin\Release\net8.0\publish"
    
    if (Test-Path $publishPath) {
        $files = Get-ChildItem -Path $publishPath -Recurse | Measure-Object
        Write-Host "? Found $($files.Count) files in publish directory" -ForegroundColor Green
        
        Write-Host "`nKey files to verify:" -ForegroundColor Cyan
        
        $keyFiles = @(
            "GhseeliApis.dll",
            "GhseeliApis.deps.json",
            "GhseeliApis.runtimeconfig.json",
            "appsettings.json",
            "appsettings.Production.json",
            "web.config"
        )
        
        foreach ($file in $keyFiles) {
            $filePath = Join-Path $publishPath $file
            if (Test-Path $filePath) {
                $fileInfo = Get-Item $filePath
                $buildTime = $fileInfo.LastWriteTime
                Write-Host "  ? $file (Built: $($buildTime.ToString('yyyy-MM-dd HH:mm:ss')))" -ForegroundColor Green
            } else {
                Write-Host "  ??  $file (missing)" -ForegroundColor Yellow
            }
        }
        
        # Verify fresh build by checking DLL timestamp
        $mainDll = Join-Path $publishPath "GhseeliApis.dll"
        if (Test-Path $mainDll) {
            $dllBuildTime = (Get-Item $mainDll).LastWriteTime
            $timeSinceBuild = (Get-Date) - $dllBuildTime
            
            if ($timeSinceBuild.TotalMinutes -lt 2) {
                Write-Host "`n? VERIFIED: Fresh build (DLL compiled $([math]::Round($timeSinceBuild.TotalSeconds)) seconds ago)" -ForegroundColor Green
            } else {
                Write-Host "`n??  WARNING: DLL is $([math]::Round($timeSinceBuild.TotalMinutes)) minutes old" -ForegroundColor Yellow
                Write-Host "   This might indicate build cache was used. Consider running script again." -ForegroundColor Yellow
            }
        }
        
        # Check if appsettings.Development.json exists (should NOT be published)
        $devSettings = Join-Path $publishPath "appsettings.Development.json"
        if (Test-Path $devSettings) {
            Write-Host "`n??  WARNING: appsettings.Development.json found in publish directory!" -ForegroundColor Red
            Write-Host "   This should NOT be deployed to production." -ForegroundColor Red
            Write-Host "   Consider removing it before uploading." -ForegroundColor Yellow
        } else {
            Write-Host "`n? Development settings excluded (correct)" -ForegroundColor Green
        }
    }
    
    Write-Host "`n?? Next Steps:" -ForegroundColor Cyan
    Write-Host "1. Review the published files in: $publishPath" -ForegroundColor White
    Write-Host "2. Ensure appsettings.Production.json contains correct placeholders" -ForegroundColor White
    Write-Host "3. Continue with MonsterASP.NET deployment guide Step 4 (FTP upload)" -ForegroundColor White
    
} else {
    Write-Host "`n? Publish failed with exit code: $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Check the error messages above for details." -ForegroundColor Yellow
}

# Return to solution directory
Set-Location -Path ".."

Write-Host "`nPress any key to continue..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
