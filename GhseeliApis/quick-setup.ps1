# Quick Setup Script - Generate Secrets and Test

Write-Host "?? GhseeliApis - Quick Configuration Setup" -ForegroundColor Cyan
Write-Host "=========================================`n" -ForegroundColor Cyan

# Generate JWT Secret
Write-Host "1??  Generating JWT Secret Key..." -ForegroundColor Yellow
$jwtSecret = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 64 | % {[char]$_})
Write-Host "? JWT Secret generated (64 characters)`n" -ForegroundColor Green

# Display secrets
Write-Host "?? COPY THESE TO MONSTERASP.NET ENVIRONMENT VARIABLES:" -ForegroundColor Cyan
Write-Host "====================================================`n" -ForegroundColor Cyan

Write-Host "??  CRITICAL - App won't start without these:`n" -ForegroundColor Yellow

Write-Host "ConnectionStrings__DefaultConnection=" -NoNewline -ForegroundColor White
Write-Host "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;" -ForegroundColor Yellow

Write-Host "`nJwtSettings__SecretKey=" -NoNewline -ForegroundColor White
Write-Host $jwtSecret -ForegroundColor Yellow

Write-Host "`nJwtSettings__Issuer=" -NoNewline -ForegroundColor White
Write-Host "GhseeliApis" -ForegroundColor Yellow

Write-Host "JwtSettings__Audience=" -NoNewline -ForegroundColor White
Write-Host "GhseeliApis" -ForegroundColor Yellow

Write-Host "JwtSettings__ExpirationMinutes=" -NoNewline -ForegroundColor White
Write-Host "60" -ForegroundColor Yellow

Write-Host "`nASPNETCORE_ENVIRONMENT=" -NoNewline -ForegroundColor White
Write-Host "Production" -ForegroundColor Yellow

Write-Host "`n`n?? NOTE: These 6 environment variables are REQUIRED in MonsterASP.NET" -ForegroundColor Cyan
Write-Host "   Without them, you'll get HTTP 500.30 error`n" -ForegroundColor Gray

# Save to file for reference
$envVarsContent = @"
# COPY THESE TO MONSTERASP.NET ENVIRONMENT VARIABLES
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

ConnectionStrings__DefaultConnection=Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;

JwtSettings__SecretKey=$jwtSecret

JwtSettings__Issuer=GhseeliApis

JwtSettings__Audience=GhseeliApis

JwtSettings__ExpirationMinutes=60

ASPNETCORE_ENVIRONMENT=Production

# OPTIONAL (for OAuth features):
# Authentication__Google__ClientId=YOUR_CLIENT_ID
# Authentication__Google__ClientSecret=YOUR_CLIENT_SECRET
# Authentication__Facebook__AppId=YOUR_APP_ID
# Authentication__Facebook__AppSecret=YOUR_APP_SECRET

# OPTIONAL (for Stripe payments):
# Stripe__SecretKey=sk_live_YOUR_KEY
# Stripe__PublishableKey=pk_live_YOUR_KEY
# Stripe__WebhookSecret=whsec_YOUR_SECRET
"@

$envVarsContent | Out-File -FilePath "ENVIRONMENT_VARIABLES.txt" -Encoding UTF8
Write-Host "?? Environment variables saved to: ENVIRONMENT_VARIABLES.txt`n" -ForegroundColor Green

# Ask if user wants to test locally
Write-Host "?? Would you like to test the application locally with these settings? (Y/N)" -ForegroundColor Cyan
$testLocal = Read-Host

if ($testLocal -eq 'Y' -or $testLocal -eq 'y') {
    Write-Host "`n2??  Setting environment variables for local test..." -ForegroundColor Yellow
    
    # Set environment variables
    $env:ConnectionStrings__DefaultConnection = "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
    $env:JwtSettings__SecretKey = $jwtSecret
    $env:JwtSettings__Issuer = "GhseeliApis"
    $env:JwtSettings__Audience = "GhseeliApis"
    $env:JwtSettings__ExpirationMinutes = "60"
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    
    Write-Host "? Environment variables set`n" -ForegroundColor Green
    
    Write-Host "3??  Starting application..." -ForegroundColor Yellow
    Write-Host "   Press Ctrl+C to stop" -ForegroundColor Gray
    Write-Host "   Watch for: '?? Using database connection string from...'" -ForegroundColor Gray
    Write-Host "   Should show: 'DefaultConnection (appsettings)' or 'Production (Environment Variable)'`n" -ForegroundColor Gray
    
    # Navigate to project directory and run
    Push-Location "GhseeliApis"
    
    try {
        dotnet run --configuration Release
    }
    catch {
        Write-Host "`n? Application failed to start" -ForegroundColor Red
        Write-Host "Error: $_" -ForegroundColor Red
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "`n?? Ready to publish!" -ForegroundColor Green
    Write-Host "`nNext steps:" -ForegroundColor Cyan
    Write-Host "1. Go to MonsterASP.NET control panel ? Your App ? Configuration" -ForegroundColor White
    Write-Host "2. Add the 6 environment variables shown above" -ForegroundColor White
    Write-Host "3. Run: .\publish-production.ps1" -ForegroundColor White
    Write-Host "4. Upload the published files via FTP`n" -ForegroundColor White
}

Write-Host "`n?? IMPORTANT:" -ForegroundColor Yellow
Write-Host "   1. JWT secret saved in ENVIRONMENT_VARIABLES.txt - keep it safe!" -ForegroundColor Gray
Write-Host "   2. In MonsterASP.NET, use DOUBLE underscores (__) in variable names" -ForegroundColor Gray
Write-Host "   3. Example: ConnectionStrings__DefaultConnection (not ConnectionStrings:DefaultConnection)`n" -ForegroundColor Gray

Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

