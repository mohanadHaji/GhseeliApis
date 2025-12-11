# ?? Missing Configuration for MonsterASP.NET Deployment

## Critical Items That Need Configuration

### ? 1. JWT Secret Key (CRITICAL - SECURITY RISK)
**Current Status**: Default placeholder in `appsettings.json`

**File**: `appsettings.json`
```json
"JwtSettings": {
  "SecretKey": "YOUR_SECRET_KEY_HERE_MINIMUM_32_CHARACTERS_LONG_FOR_SECURITY"  // ? NOT SET
}
```

**Required Action**:
```powershell
# Generate a secure secret key (at least 32 characters)
$secretKey = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 64 | % {[char]$_})
Write-Host "Generated Secret Key: $secretKey"

# Add to user secrets for local testing
dotnet user-secrets set "JwtSettings:SecretKey" "$secretKey" --project GhseeliApis

# For production, set as environment variable in MonsterASP.NET control panel:
# JwtSettings__SecretKey = <your-generated-key>
```

---

### ? 2. Google OAuth Credentials (REQUIRED for Google Login)
**Current Status**: Placeholder values

**File**: `appsettings.json`
```json
"Authentication": {
  "Google": {
    "ClientId": "YOUR_GOOGLE_CLIENT_ID_HERE",      // ? NOT SET
    "ClientSecret": "YOUR_GOOGLE_CLIENT_SECRET_HERE" // ? NOT SET
  }
}
```

**Required Steps**:

1. **Get Credentials**:
   - Go to: https://console.cloud.google.com/apis/credentials
   - Create OAuth 2.0 Client ID (or use existing)
   - Copy Client ID and Client Secret

2. **Configure Redirect URIs** (MUST DO BEFORE DEPLOYMENT):
   ```
   Authorized JavaScript origins:
   - https://yourdomain.com
   - http://localhost:5000 (for local testing)
   
   Authorized redirect URIs:
   - https://yourdomain.com/api/auth/google-callback
   - https://yourdomain.com/signin-google
   - http://localhost:5000/api/auth/google-callback (for local testing)
   ```

3. **Set in User Secrets** (local):
   ```powershell
   dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_CLIENT_ID" --project GhseeliApis
   dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_CLIENT_SECRET" --project GhseeliApis
   ```

4. **Set in MonsterASP.NET** (production):
   ```
   Environment Variables:
   Authentication__Google__ClientId = <your-client-id>
   Authentication__Google__ClientSecret = <your-client-secret>
   ```

---

### ? 3. Facebook OAuth Credentials (REQUIRED for Facebook Login)
**Current Status**: Placeholder values

**File**: `appsettings.json`
```json
"Authentication": {
  "Facebook": {
    "AppId": "YOUR_FACEBOOK_APP_ID_HERE",      // ? NOT SET
    "AppSecret": "YOUR_FACEBOOK_APP_SECRET_HERE" // ? NOT SET
  }
}
```

**Required Steps**:

1. **Get Credentials**:
   - Go to: https://developers.facebook.com/apps
   - Select your app ? Settings ? Basic
   - Copy App ID and App Secret

2. **Configure Redirect URIs** (MUST DO BEFORE DEPLOYMENT):
   ```
   Valid OAuth Redirect URIs:
   - https://yourdomain.com/api/auth/facebook-callback
   - https://yourdomain.com/signin-facebook
   - http://localhost:5000/api/auth/facebook-callback (for local testing)
   ```

3. **Set in User Secrets** (local):
   ```powershell
   dotnet user-secrets set "Authentication:Facebook:AppId" "YOUR_APP_ID" --project GhseeliApis
   dotnet user-secrets set "Authentication:Facebook:AppSecret" "YOUR_APP_SECRET" --project GhseeliApis
   ```

4. **Set in MonsterASP.NET** (production):
   ```
   Environment Variables:
   Authentication__Facebook__AppId = <your-app-id>
   Authentication__Facebook__AppSecret = <your-app-secret>
   ```

---

### ? 4. Stripe API Keys (REQUIRED for Payments)
**Current Status**: Test placeholders

**File**: `appsettings.json`
```json
"Stripe": {
  "PublishableKey": "pk_test_YOUR_PUBLISHABLE_KEY_HERE",  // ? TEST KEY
  "SecretKey": "sk_test_YOUR_SECRET_KEY_HERE",           // ? TEST KEY
  "WebhookSecret": "whsec_YOUR_WEBHOOK_SECRET_HERE"      // ? NOT SET
}
```

**Required Steps**:

1. **Get Live API Keys**:
   - Go to: https://dashboard.stripe.com/apikeys
   - Switch to **Live mode** (toggle in top-left)
   - Copy:
     - **Publishable key** (starts with `pk_live_`)
     - **Secret key** (starts with `sk_live_`)

2. **Configure Webhook** (CRITICAL for payment status updates):
   - Go to: https://dashboard.stripe.com/webhooks
   - Click "Add endpoint"
   - Endpoint URL: `https://yourdomain.com/api/stripe/webhook`
   - Select events:
     - `payment_intent.succeeded`
     - `payment_intent.payment_failed`
     - `payment_intent.canceled`
     - `charge.refunded`
   - Copy the **Signing secret** (starts with `whsec_`)

3. **Set in User Secrets** (local - use TEST keys):
   ```powershell
   dotnet user-secrets set "Stripe:SecretKey" "sk_test_YOUR_KEY" --project GhseeliApis
   dotnet user-secrets set "Stripe:PublishableKey" "pk_test_YOUR_KEY" --project GhseeliApis
   dotnet user-secrets set "Stripe:WebhookSecret" "whsec_YOUR_LOCAL_WEBHOOK_SECRET" --project GhseeliApis
   ```

4. **Set in MonsterASP.NET** (production - use LIVE keys):
   ```
   Environment Variables:
   Stripe__SecretKey = sk_live_YOUR_LIVE_SECRET_KEY
   Stripe__PublishableKey = pk_live_YOUR_LIVE_PUBLISHABLE_KEY
   Stripe__WebhookSecret = whsec_YOUR_WEBHOOK_SECRET
   ```

?? **WARNING**: Never commit live Stripe keys to git!

---

### ?? 5. Production Connection String
**Current Status**: Set in user secrets (not in repository)

**User Secrets** (currently configured):
```
ConnectionStrings:RemoteTest = Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;
```

**Required for Production**:
Set in MonsterASP.NET environment variables:
```
ConnectionStrings__DefaultConnection = Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;
```

? **Status**: Database credentials available, just needs to be set in hosting environment

---

### ?? 6. Domain/URL Configuration
**Current Issues**:
- OAuth redirect URIs use placeholders: `https://yourdomain.com`
- Stripe webhook uses placeholder: `https://yourdomain.com`

**Required Action**:
1. Obtain your actual MonsterASP.NET domain (e.g., `myapp.monsterasp.net`)
2. Update all OAuth redirect URIs to use real domain
3. Update Stripe webhook URL to use real domain
4. Consider custom domain if needed

---

## ?? Quick Setup Script for Local Development

Run this script to configure all secrets locally:

```powershell
# Navigate to project directory
cd "C:\personalProject\GhseeliApis\GhseeliApis\GhseeliApis"

# JWT Secret (generate random 64-char string)
$jwtSecret = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 64 | % {[char]$_})
dotnet user-secrets set "JwtSettings:SecretKey" "$jwtSecret"

# Google OAuth (replace with your actual credentials)
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_GOOGLE_CLIENT_ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_GOOGLE_CLIENT_SECRET"

# Facebook OAuth (replace with your actual credentials)
dotnet user-secrets set "Authentication:Facebook:AppId" "YOUR_FACEBOOK_APP_ID"
dotnet user-secrets set "Authentication:Facebook:AppSecret" "YOUR_FACEBOOK_APP_SECRET"

# Stripe TEST keys (replace with your test keys)
dotnet user-secrets set "Stripe:SecretKey" "sk_test_YOUR_TEST_SECRET_KEY"
dotnet user-secrets set "Stripe:PublishableKey" "pk_test_YOUR_TEST_PUBLISHABLE_KEY"
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_YOUR_TEST_WEBHOOK_SECRET"

# Database already configured
# ConnectionStrings:RemoteTest is already set

# List all secrets to verify
dotnet user-secrets list
```

---

## ?? MonsterASP.NET Environment Variables Checklist

Copy this to your MonsterASP.NET control panel (Environment Variables section):

```bash
# CRITICAL - Security
JwtSettings__SecretKey=<64-character-random-string>

# Database (already have credentials)
ConnectionStrings__DefaultConnection=Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;

# Google OAuth
Authentication__Google__ClientId=<your-google-client-id>
Authentication__Google__ClientSecret=<your-google-client-secret>

# Facebook OAuth
Authentication__Facebook__AppId=<your-facebook-app-id>
Authentication__Facebook__AppSecret=<your-facebook-app-secret>

# Stripe LIVE keys (use live keys in production!)
Stripe__SecretKey=sk_live_<your-live-secret-key>
Stripe__PublishableKey=pk_live_<your-live-publishable-key>
Stripe__WebhookSecret=whsec_<your-webhook-secret>

# JWT Configuration
JwtSettings__Issuer=GhseeliApis
JwtSettings__Audience=GhseeliApis
JwtSettings__ExpirationMinutes=60

# Environment
ASPNETCORE_ENVIRONMENT=Production
```

---

## ?? Security Best Practices

1. **Never commit secrets to git**
   - ? Already using user secrets for local development
   - ? `appsettings.Production.json` uses environment variable placeholders

2. **Use different keys for different environments**
   - Local: Test Stripe keys, development OAuth apps
   - Production: Live Stripe keys, production OAuth apps

3. **Rotate JWT secret periodically**
   - Generate new secret every 90 days
   - Update in production and invalidate old tokens

4. **Use strong passwords**
   - Current database password: `kG=5C7b+aS#9` ? (strong)
   - JWT secret should be 64+ characters ?? (not set)

---

## ?? Testing Configuration Before Deployment

### Test Locally with User Secrets
```powershell
cd "C:\personalProject\GhseeliApis\GhseeliApis\GhseeliApis"

# Verify all secrets are set
dotnet user-secrets list

# Run the application
dotnet run

# Test endpoints
curl http://localhost:5000/api/health
```

### Test OAuth Providers
1. Navigate to: `http://localhost:5000/api/auth/external-login?provider=Google`
2. Should redirect to Google login
3. After login, should redirect back with token

### Test Stripe Integration
1. Create a test payment
2. Check Stripe dashboard for webhook events
3. Verify payment status updates in database

---

## ?? Configuration Status Summary

| Configuration Item | Status | Priority |
|--------------------|--------|----------|
| Database Connection | ? **Configured** | CRITICAL |
| JWT Secret Key | ? **NOT SET** | CRITICAL |
| Google OAuth | ? **NOT SET** | HIGH |
| Facebook OAuth | ? **NOT SET** | HIGH |
| Stripe API Keys | ?? **TEST KEYS** | HIGH |
| Stripe Webhook | ? **NOT SET** | HIGH |
| Domain Configuration | ?? **PLACEHOLDER** | MEDIUM |
| Environment Variables | ?? **NOT SET IN HOSTING** | CRITICAL |

---

## ?? Next Immediate Actions

### For Local Development:
1. ? Database already working (RemoteTest configured)
2. ? Set JWT secret in user secrets
3. ? Set Google OAuth credentials
4. ? Set Facebook OAuth credentials
5. ? Set Stripe test keys

### For Production Deployment:
1. ? Database credentials ready
2. ? Generate production JWT secret
3. ? Configure OAuth redirect URIs with production domain
4. ? Create Stripe webhook with production domain
5. ? Set all environment variables in MonsterASP.NET control panel
6. ? Switch Stripe from test to live keys
7. ? Test deployment thoroughly

---

## ?? Where to Get Help

- **JWT Issues**: https://jwt.io/introduction
- **Google OAuth**: https://console.cloud.google.com/apis/credentials
- **Facebook OAuth**: https://developers.facebook.com/docs/facebook-login
- **Stripe Setup**: https://stripe.com/docs/webhooks
- **MonsterASP.NET Support**: https://help.monsterasp.net/

---

## ?? Secret Management Best Practices

### ? What's Already Done Right:
- User secrets for local development (git-ignored)
- Environment variable placeholders in production config
- Secure database password stored safely

### ? What Needs to Be Done:
- Generate secure JWT secret (64+ characters)
- Obtain OAuth credentials from providers
- Set up production Stripe account
- Configure all environment variables in hosting

---

**? Estimated Time to Complete All Configuration**: 2-3 hours

**?? Priority Order**:
1. JWT Secret (5 min)
2. Database environment variable (5 min)
3. Google OAuth (30 min)
4. Facebook OAuth (30 min)
5. Stripe setup (60 min)
6. Domain configuration (30 min)
7. Testing (30 min)
