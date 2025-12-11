# MonsterASP.NET Deployment Guide

## ? Migration Completed - MySQL to SQL Server

Your GhseeliApis project has been successfully migrated from MySQL (Google Cloud SQL) to SQL Server for deployment on MonsterASP.NET.

---

## ?? What Was Changed

### 1. **Database Provider Migration**
- ? Removed: `Pomelo.EntityFrameworkCore.MySql` (v9.0.0)
- ? Removed: `MySql.Data` (v9.5.0)
- ? Added: `Microsoft.EntityFrameworkCore.SqlServer` (v8.0.11)
- ? Updated: `Microsoft.EntityFrameworkCore.Design` (v8.0.11)

### 2. **New Files Created**
- `GhseeliApis/Extensions/SqlServerSetupExtension.cs` - SQL Server configuration
- `GhseeliApis/appsettings.Production.json` - Production configuration
- `GhseeliApis/Migrations/InitialCreate.cs` - New SQL Server migrations

### 3. **Files Modified**
- `Program.cs` - Changed from `AddGoogleCloudSql()` to `AddSqlServer()`
- `ApplicationDbContext.cs` - Updated default value from `CURRENT_TIMESTAMP` to `GETUTCDATE()`
- `ApplicationDbContextFactory.cs` - Updated for SQL Server design-time support
- `HealthController.cs` - Updated database name display
- `appsettings.json` - Added SQL Server connection strings
- `appsettings.Development.json` - Added local SQL Server configuration

### 4. **Files Removed**
- ? `GhseeliApis/Extensions/GoogleSqlSetupExtension.cs` (no longer needed)
- ? All old MySQL migrations (recreated for SQL Server)

### 5. **Secure Credentials Storage**
? MonsterASP.NET production connection string saved in **User Secrets** (git-ignored)
```
ConnectionStrings:MonsterAspNet = "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
```

---

## ?? Step-by-Step Deployment to MonsterASP.NET

### **Phase 1: Pre-Deployment Preparation** ??

#### Step 1.1: Update Production Configuration
1. Open `appsettings.Production.json`
2. Verify the connection string (already configured):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=REPLACE_WITH_ACTUAL_PASSWORD;Encrypt=True;TrustServerCertificate=True;"
  }
}
```

#### Step 1.2: Configure OAuth Redirect URIs
Before deploying, update your OAuth provider settings:

**Google Cloud Console:**
1. Go to https://console.cloud.google.com/apis/credentials
2. Select your OAuth 2.0 Client ID
3. Add to **Authorized redirect URIs**:
   - `https://yourdomain.com/api/auth/google-callback`
   - `https://yourdomain.com/signin-google`

**Facebook Developer Console:**
1. Go to https://developers.facebook.com/apps
2. Select your app ? Settings ? Basic
3. Add to **Valid OAuth Redirect URIs**:
   - `https://yourdomain.com/api/auth/facebook-callback`
   - `https://yourdomain.com/signin-facebook`

#### Step 1.3: Update Stripe Webhooks
1. Go to https://dashboard.stripe.com/webhooks
2. Add new webhook endpoint:
   - URL: `https://yourdomain.com/api/stripe/webhook`
   - Events to send: `payment_intent.succeeded`, `payment_intent.payment_failed`, etc.
3. Copy the **Webhook Signing Secret** for later

---

### **Phase 2: Database Setup** ???

#### Step 2.1: Access MonsterASP.NET Control Panel
1. Login to your MonsterASP.NET account
2. Navigate to your hosting control panel
3. Your database is already created:
   - **Server:** `db34836.public.databaseasp.net`
   - **Database:** `db34836`
   - **Username:** `db34836`
   - **Password:** `kG=5C7b+aS#9`

#### Step 2.2: Apply Database Migrations
Option A: **Local Migration (Recommended)**
```powershell
# Set production connection string temporarily
$env:ConnectionStrings__DefaultConnection="Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"

# Apply migrations
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"
dotnet ef database update --connection "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
```

Option B: **After Deployment**
- The migrations will run automatically on first startup (if enabled)
- Or manually run through the deployed app

---

### **Phase 3: Build and Publish** ??

#### Step 3.1: Publish the Application
1. Open Visual Studio
2. Right-click `GhseeliApis` project ? **Publish**
3. Choose **Folder** as publish target
4. Configure publish profile:
   - **Configuration:** `Release`
   - **Target Framework:** `net8.0`
   - **Deployment Mode:** `Framework-dependent`
   - **Target Runtime:** `Portable`
   - **Target Location:** `bin\Release\net8.0\publish\`
5. Click **Publish**

#### Step 3.2: Verify Published Files
After publishing, verify these files exist in `bin\Release\net8.0\publish\`:
```
? GhseeliApis.dll
? appsettings.json
? appsettings.Production.json
? web.config
? wwwroot/ (if any)
? All dependency DLLs
```

**?? IMPORTANT:** Do NOT include `appsettings.Development.json` in deployment

---

### **Phase 4: Deploy to MonsterASP.NET** ??

#### Step 4.1: Connect via FTP
1. Get FTP credentials from MonsterASP.NET control panel
2. Use FileZilla or any FTP client:
   - **Host:** `ftp.monsterasp.net` (or your specific FTP host)
   - **Username:** Your MonsterASP.NET username
   - **Password:** Your FTP password
   - **Port:** 21

#### Step 4.2: Upload Files
1. Navigate to your application directory (usually `wwwroot` or specified folder)
2. Upload ALL files from `bin\Release\net8.0\publish\`
3. Ensure folder structure is preserved

#### Step 4.3: Configure web.config
Ensure `web.config` contains:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" 
                  arguments=".\GhseeliApis.dll" 
                  stdoutLogEnabled="true" 
                  stdoutLogFile=".\logs\stdout" 
                  hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

---

### **Phase 5: Environment Configuration** ??

#### Step 5.1: Set Environment Variables (MonsterASP.NET Control Panel)
Configure these in your hosting control panel:

**Database:**
```
ConnectionStrings__DefaultConnection = Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;
```

**JWT Settings:**
```
JwtSettings__SecretKey = [YOUR_PRODUCTION_SECRET_KEY_32_CHARS_MINIMUM]
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

**Stripe:**
```
Stripe__SecretKey = sk_live_YOUR_LIVE_SECRET_KEY
Stripe__PublishableKey = pk_live_YOUR_LIVE_PUBLISHABLE_KEY
Stripe__WebhookSecret = whsec_YOUR_WEBHOOK_SECRET
```

**Environment:**
```
ASPNETCORE_ENVIRONMENT = Production
```

#### Step 5.2: Update Production Settings in Code (Optional)
If MonsterASP.NET doesn't support environment variables well, update `appsettings.Production.json` directly with actual values (except passwords).

---

### **Phase 6: Post-Deployment Verification** ?

#### Step 6.1: Health Checks
1. Navigate to: `https://yourdomain.com/api/health`
   - Should return: `{ "status": "Healthy" }`
2. Check database: `https://yourdomain.com/api/health/db`
   - Should return: `{ "status": "Healthy", "database": "SQL Server" }`

#### Step 6.2: Test Authentication Endpoints
```bash
# Test registration
POST https://yourdomain.com/api/auth/register
{
  "email": "test@example.com",
  "password": "Test@1234",
  "fullName": "Test User",
  "phone": "+1234567890"
}

# Test login
POST https://yourdomain.com/api/auth/login
{
  "email": "test@example.com",
  "password": "Test@1234"
}
```

#### Step 6.3: Test OAuth Providers
1. Google Login: `https://yourdomain.com/api/auth/external-login?provider=Google`
2. Facebook Login: `https://yourdomain.com/api/auth/external-login?provider=Facebook`

#### Step 6.4: Verify Swagger (if enabled in production)
Navigate to: `https://yourdomain.com/swagger`

#### Step 6.5: Test Stripe Payment
Create a test payment and verify webhook is received

---

## ?? Security Checklist

Before going live:

- [ ] Change all default passwords and secrets
- [ ] Use **LIVE** Stripe keys (not test keys)
- [ ] Enable HTTPS (should be default on MonsterASP.NET)
- [ ] Update `RequireHttpsMetadata = true` in `Program.cs` JWT configuration
- [ ] Remove Swagger in production (or secure it)
- [ ] Verify all OAuth redirect URIs use HTTPS
- [ ] Enable detailed error logging in production
- [ ] Set up monitoring and alerting
- [ ] Test role-based authorization
- [ ] Verify CORS settings if API is consumed by frontend

---

## ??? Troubleshooting

### Issue: "Cannot connect to database"
**Solution:**
- Verify connection string in environment variables
- Check firewall rules (MonsterASP.NET should allow connections)
- Ensure migrations are applied

### Issue: "401 Unauthorized" on all endpoints
**Solution:**
- Check JWT secret key is set correctly
- Verify token expiration settings
- Check `ASPNETCORE_ENVIRONMENT` is set to `Production`

### Issue: OAuth login fails
**Solution:**
- Verify redirect URIs in Google/Facebook console
- Check OAuth credentials in environment variables
- Ensure HTTPS is enabled

### Issue: Stripe webhooks not working
**Solution:**
- Verify webhook URL is correct
- Check webhook secret is set
- Review Stripe dashboard event logs
- Ensure endpoint is publicly accessible

### Issue: "Migrations not applied"
**Solution:**
```powershell
# Connect and apply manually
dotnet ef database update --connection "YOUR_PRODUCTION_CONNECTION_STRING"
```

---

## ?? Local Development with MonsterASP.NET Database

To test locally against the production database:

```powershell
# Use user secrets (already configured)
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"
dotnet user-secrets list

# Run the app
dotnet run

# Or override connection string
$env:ConnectionStrings__DefaultConnection="Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
dotnet run
```

---

## ?? Rollback Plan

If deployment fails:

1. **Database Rollback:**
```powershell
dotnet ef database update PreviousMigrationName --connection "YOUR_CONNECTION_STRING"
```

2. **File Rollback:**
- Keep a backup of previous deployment files
- Re-upload via FTP if needed

3. **Configuration Rollback:**
- Revert environment variables in MonsterASP.NET control panel

---

## ?? Monitoring and Maintenance

### View Application Logs
MonsterASP.NET should provide:
- IIS logs
- Application stdout logs (in `logs\stdout` directory)

### Database Maintenance
- Regular backups (check MonsterASP.NET backup schedule)
- Monitor database size and performance
- Review query performance logs

### Performance Monitoring
- Monitor API response times
- Check memory usage
- Review error rates

---

## ?? Next Steps After Deployment

1. **Test all features thoroughly**
2. **Monitor logs for first 24 hours**
3. **Set up automated backups**
4. **Configure custom domain (if not already)**
5. **Set up SSL certificate (should be automatic on MonsterASP.NET)**
6. **Implement CI/CD pipeline** (optional)
7. **Set up application insights** (optional)

---

## ?? Support Resources

- **MonsterASP.NET Support:** https://help.monsterasp.net/
- **ASP.NET Core Docs:** https://docs.microsoft.com/aspnet/core
- **Entity Framework Core:** https://docs.microsoft.com/ef/core/
- **Stripe Documentation:** https://stripe.com/docs

---

## ? Deployment Checklist

Use this checklist when deploying:

- [ ] Updated OAuth redirect URIs (Google, Facebook)
- [ ] Updated Stripe webhook URL
- [ ] Applied database migrations
- [ ] Published application to folder
- [ ] Uploaded files via FTP
- [ ] Configured environment variables
- [ ] Verified web.config
- [ ] Tested health endpoints
- [ ] Tested authentication (register, login, OAuth)
- [ ] Tested payment flow
- [ ] Verified role-based access
- [ ] Checked application logs
- [ ] Monitored for errors
- [ ] Backed up database
- [ ] Documented deployment

---

## ?? Your Application is Ready for MonsterASP.NET!

All necessary changes have been made. Follow the steps above to deploy your GhseeliApis project to MonsterASP.NET.

**Production Database Details (Stored in User Secrets):**
- Server: `db34836.public.databaseasp.net`
- Database: `db34836`
- Username: `db34836`
- Password: `kG=5C7b+aS#9`

Good luck with your deployment! ??
