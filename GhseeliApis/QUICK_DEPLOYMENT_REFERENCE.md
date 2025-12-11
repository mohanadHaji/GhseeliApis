# ?? Quick Deployment Reference Card

## MonsterASP.NET Production Database
```
Server:   db34836.public.databaseasp.net
Database: db34836
User ID:  db34836
Password: kG=5C7b+aS#9
```

## ?? Publish Command (Visual Studio)
1. Right-click `GhseeliApis` ? **Publish**
2. Select **Folder**
3. Configuration: **Release**
4. Target: `bin\Release\net8.0\publish\`
5. Click **Publish**

## ?? Publish Command (CLI)
```powershell
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"
dotnet publish -c Release -o bin\Release\net8.0\publish
```

## ??? Apply Migrations to Production
```powershell
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"
dotnet ef database update --connection "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
```

## ?? Environment Variables to Set

Copy these to MonsterASP.NET Control Panel:

```ini
# Database
ConnectionStrings__DefaultConnection=Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;

# Environment
ASPNETCORE_ENVIRONMENT=Production

# JWT (CHANGE THESE!)
JwtSettings__SecretKey=YOUR_PRODUCTION_SECRET_KEY_MINIMUM_32_CHARS
JwtSettings__Issuer=GhseeliApis
JwtSettings__Audience=GhseeliApis
JwtSettings__ExpirationMinutes=60

# Google OAuth (CONFIGURE THESE!)
Authentication__Google__ClientId=YOUR_GOOGLE_CLIENT_ID
Authentication__Google__ClientSecret=YOUR_GOOGLE_CLIENT_SECRET

# Facebook OAuth (CONFIGURE THESE!)
Authentication__Facebook__AppId=YOUR_FACEBOOK_APP_ID
Authentication__Facebook__AppSecret=YOUR_FACEBOOK_APP_SECRET

# Stripe (CHANGE TO LIVE KEYS!)
Stripe__SecretKey=sk_live_YOUR_LIVE_SECRET_KEY
Stripe__PublishableKey=pk_live_YOUR_LIVE_PUBLISHABLE_KEY
Stripe__WebhookSecret=whsec_YOUR_WEBHOOK_SECRET
```

## ?? OAuth Redirect URIs to Add

### Google Cloud Console
`https://yourdomain.com/api/auth/google-callback`
`https://yourdomain.com/signin-google`

### Facebook Developer Console
`https://yourdomain.com/api/auth/facebook-callback`
`https://yourdomain.com/signin-facebook`

### Stripe Webhooks
`https://yourdomain.com/api/stripe/webhook`

## ? Quick Test Endpoints

```bash
# Health Check
GET https://yourdomain.com/api/health

# Database Health
GET https://yourdomain.com/api/health/db

# Register
POST https://yourdomain.com/api/auth/register
{
  "email": "test@example.com",
  "password": "Test@1234",
  "fullName": "Test User",
  "phone": "+1234567890"
}

# Login
POST https://yourdomain.com/api/auth/login
{
  "email": "test@example.com",
  "password": "Test@1234"
}

# Google OAuth
GET https://yourdomain.com/api/auth/external-login?provider=Google

# Swagger (if enabled)
GET https://yourdomain.com/swagger
```

## ?? FTP Upload Checklist
- [ ] Upload all files from `bin\Release\net8.0\publish\`
- [ ] Verify `web.config` is present
- [ ] Ensure `appsettings.json` is uploaded
- [ ] Ensure `appsettings.Production.json` is uploaded
- [ ] **DO NOT** upload `appsettings.Development.json`

## ?? Security Checklist
- [ ] Changed JWT SecretKey from default
- [ ] Using LIVE Stripe keys (not test)
- [ ] Added HTTPS redirect URIs for OAuth
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Removed/secured Swagger endpoint
- [ ] Enabled HTTPS metadata validation

## ?? Common Issues

| Issue | Solution |
|-------|----------|
| Database connection fails | Check connection string and firewall |
| 401 Unauthorized | Verify JWT secret key is set |
| OAuth fails | Check redirect URIs and credentials |
| Stripe webhook fails | Verify webhook secret and endpoint URL |
| Migrations not applied | Run `dotnet ef database update` manually |

## ?? Emergency Contacts
- **MonsterASP.NET Support:** https://help.monsterasp.net/
- **Deployment Guide:** `MONSTERASP_DEPLOYMENT_GUIDE.md`
- **Migration Summary:** `MYSQL_TO_MSSQL_MIGRATION_SUMMARY.md`

---

## ?? Deployment Steps (Summary)

1. ? **Migrate database** - Run migrations on production DB
2. ? **Publish app** - Build in Release mode
3. ? **Upload files** - FTP to MonsterASP.NET
4. ? **Configure env vars** - Set all environment variables
5. ? **Update OAuth** - Add production redirect URIs
6. ? **Test endpoints** - Verify all functionality
7. ? **Monitor logs** - Check for errors

---

**Status:** ? READY FOR DEPLOYMENT
**Build:** ? Successful
**Migrations:** ? Created (pending application to production)

**Next:** Follow `MONSTERASP_DEPLOYMENT_GUIDE.md` for detailed instructions
