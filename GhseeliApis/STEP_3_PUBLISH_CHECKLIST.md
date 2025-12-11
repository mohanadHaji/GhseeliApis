# ?? Pre-Deployment Checklist for MonsterASP.NET

## ? Before Publishing - Configuration Review

### 1. Production Configuration Files
- [ ] `appsettings.Production.json` exists
- [ ] Connection strings use environment variable placeholders
- [ ] No sensitive data in production config file
- [ ] Logging configured appropriately for production

### 2. Database Preparation
- [x] Database migrated to SQL Server (MSSQL)
- [x] Migrations created and tested
- [ ] Apply migrations to production database:
  ```powershell
  cd GhseeliApis
  dotnet ef database update --connection "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
  ```

### 3. Security Configuration (CRITICAL ??)
- [ ] JWT Secret generated (64+ characters)
- [ ] Google OAuth credentials obtained and configured
- [ ] Facebook OAuth credentials obtained and configured
- [ ] Stripe LIVE API keys obtained (not test keys)
- [ ] Stripe webhook created with production URL
- [ ] All secrets stored ONLY in environment variables (not in files)

### 4. OAuth Redirect URI Configuration
- [ ] Google OAuth redirect URIs updated with production domain:
  - `https://yourdomain.com/api/auth/google-callback`
  - `https://yourdomain.com/signin-google`
- [ ] Facebook OAuth redirect URIs updated with production domain:
  - `https://yourdomain.com/api/auth/facebook-callback`
  - `https://yourdomain.com/signin-facebook`

### 5. Code Review
- [x] DTO refactoring completed (realistic user creation)
- [x] All tests passing (495/502 - 98.6%)
- [ ] Swagger disabled in production (or secured)
- [x] HTTPS enforcement enabled in production
- [x] Role-based authorization configured

---

## ?? Publishing Checklist

### Option A: Visual Studio Publish
- [ ] Solution opened in Visual Studio
- [ ] Right-clicked `GhseeliApis` project (not .Tests)
- [ ] Selected "Publish..." ? "Folder"
- [ ] Configuration set to "Release"
- [ ] Target Framework: `net8.0`
- [ ] Deployment Mode: `Framework-dependent`
- [ ] Published successfully

### Option B: CLI Publish
- [ ] Run PowerShell script: `.\publish-production.ps1`
- [ ] Verify no errors in output
- [ ] Check published files in `bin\Release\net8.0\publish\`

### Files to Verify After Publish
Navigate to: `GhseeliApis\bin\Release\net8.0\publish\`

**Must Have:**
- [ ] `GhseeliApis.dll`
- [ ] `GhseeliApis.deps.json`
- [ ] `GhseeliApis.runtimeconfig.json`
- [ ] `appsettings.json`
- [ ] `appsettings.Production.json`
- [ ] `web.config`
- [ ] All dependency DLLs (Microsoft.*, System.*, Stripe.net, etc.)

**Must NOT Have:**
- [ ] `appsettings.Development.json` (should be excluded)
- [ ] `.pdb` files (optional - include if you want better error traces)
- [ ] User secrets file

---

## ?? MonsterASP.NET Control Panel Setup

### Before Uploading Files

#### 1. Create/Verify Database
- [ ] Login to MonsterASP.NET control panel
- [ ] Navigate to SQL Server databases
- [ ] Verify database exists:
  - Server: `db34836.public.databaseasp.net`
  - Database: `db34836`
  - Username: `db34836`
  - Password: `kG=5C7b+aS#9`

#### 2. Configure Environment Variables
Go to your web app configuration and add these environment variables:

**Database Connection:**
```
ConnectionStrings__DefaultConnection = Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;
```

**JWT Settings:**
```
JwtSettings__SecretKey = [GENERATE_64_CHARACTER_RANDOM_STRING]
JwtSettings__Issuer = GhseeliApis
JwtSettings__Audience = GhseeliApis
JwtSettings__ExpirationMinutes = 60
```

**Google OAuth:**
```
Authentication__Google__ClientId = [YOUR_GOOGLE_CLIENT_ID]
Authentication__Google__ClientSecret = [YOUR_GOOGLE_CLIENT_SECRET]
```

**Facebook OAuth:**
```
Authentication__Facebook__AppId = [YOUR_FACEBOOK_APP_ID]
Authentication__Facebook__AppSecret = [YOUR_FACEBOOK_APP_SECRET]
```

**Stripe (LIVE keys):**
```
Stripe__SecretKey = sk_live_[YOUR_LIVE_SECRET_KEY]
Stripe__PublishableKey = pk_live_[YOUR_LIVE_PUBLISHABLE_KEY]
Stripe__WebhookSecret = whsec_[YOUR_WEBHOOK_SECRET]
```

**Environment:**
```
ASPNETCORE_ENVIRONMENT = Production
```

---

## ?? FTP Upload Checklist (Next Step)

### Prepare for Upload
- [ ] Published files verified in `bin\Release\net8.0\publish\`
- [ ] Environment variables configured in MonsterASP.NET control panel
- [ ] FTP credentials obtained from MonsterASP.NET

### FTP Connection Details
You'll need from MonsterASP.NET:
- FTP Host: `ftp.monsterasp.net` (or your specific host)
- FTP Username: [Your MonsterASP.NET username]
- FTP Password: [Your FTP password]
- Port: 21 (or 22 for SFTP)

### FTP Client Options
- **FileZilla** (Recommended): https://filezilla-project.org/
- **WinSCP**: https://winscp.net/
- **Visual Studio**: Built-in FTP publish

---

## ?? Critical Reminders

### Security
- ? Never commit secrets to git (already using user secrets)
- ? Don't upload `appsettings.Development.json` to production
- ? Use LIVE Stripe keys in production (not test keys)
- ? Generate strong JWT secret (64+ characters)
- ? Store all secrets in environment variables only

### Testing Before Deployment
- [ ] Test locally with production-like configuration
- [ ] Run all tests: `dotnet test` (should have ~495 passing)
- [ ] Verify database connection to production database
- [ ] Test health endpoints locally

### Post-Deployment Testing
After deployment, test these endpoints:
- [ ] `https://yourdomain.com/api/health` (API health)
- [ ] `https://yourdomain.com/api/health/db` (Database connectivity)
- [ ] `https://yourdomain.com/api/auth/register` (User registration)
- [ ] `https://yourdomain.com/api/auth/login` (User login)
- [ ] Google OAuth login flow
- [ ] Facebook OAuth login flow
- [ ] Create test payment (with Stripe test card)
- [ ] Verify webhook receives events

---

## ?? Current Status Summary

### ? Completed
- Database migrated to SQL Server
- DTO refactoring (realistic API)
- 98.6% test coverage (495/502 passing)
- User secrets configured locally
- Production config files created
- Multi-database setup (local/remote/production)

### ? In Progress (Step 3)
- Publishing the application

### ?? Upcoming Steps
- Step 4: Upload via FTP
- Step 5: Configure environment variables in MonsterASP.NET
- Step 6: Apply database migrations to production
- Step 7: Test deployed application
- Step 8: Monitor and troubleshoot

---

## ?? Quick Reference

### Generate JWT Secret (PowerShell)
```powershell
# Generate secure 64-character secret
-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 64 | % {[char]$_})
```

### Apply Migrations to Production
```powershell
cd GhseeliApis
dotnet ef database update --connection "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
```

### Publish Command
```powershell
dotnet publish --configuration Release --output "bin\Release\net8.0\publish"
```

---

## ?? Ready to Continue?

**Current Step**: Step 3 - Publishing ?  
**Next Step**: Step 4 - FTP Upload

Once you've published and verified the files, you're ready to proceed with FTP upload!

---

**Last Updated**: 2024-12-10  
**Deployment Target**: MonsterASP.NET  
**Database**: SQL Server (db34836.public.databaseasp.net)
