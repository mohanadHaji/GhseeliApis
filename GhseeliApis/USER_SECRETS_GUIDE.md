# User Secrets Configuration Guide

**IMPORTANT**: Never commit real passwords, secret keys, or API credentials to version control. Use User Secrets for development and environment variables for production.

## User Secrets Commands

### Initialize User Secrets (Already Done)
```bash
dotnet user-secrets init --project GhseeliApis/GhseeliApis.csproj
```

### JWT Settings
```bash
dotnet user-secrets set "JwtSettings:SecretKey" "your_secret_key_minimum_32_characters" --project GhseeliApis/GhseeliApis.csproj
```

### OAuth Credentials - Google
```bash
dotnet user-secrets set "Authentication:Google:ClientId" "your_google_client_id" --project GhseeliApis/GhseeliApis.csproj
dotnet user-secrets set "Authentication:Google:ClientSecret" "your_google_client_secret" --project GhseeliApis/GhseeliApis.csproj
```

### OAuth Credentials - Facebook
```bash
dotnet user-secrets set "Authentication:Facebook:AppId" "your_facebook_app_id" --project GhseeliApis/GhseeliApis.csproj
dotnet user-secrets set "Authentication:Facebook:AppSecret" "your_facebook_app_secret" --project GhseeliApis/GhseeliApis.csproj
```

### Stripe API Keys
```bash
dotnet user-secrets set "Stripe:PublishableKey" "pk_test_your_key" --project GhseeliApis/GhseeliApis.csproj
dotnet user-secrets set "Stripe:SecretKey" "sk_test_your_key" --project GhseeliApis/GhseeliApis.csproj
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_your_secret" --project GhseeliApis/GhseeliApis.csproj
```

### Database Password
```bash
dotnet user-secrets set "CloudSql:Password" "your_database_password" --project GhseeliApis/GhseeliApis.csproj
```

### List All Secrets
```bash
dotnet user-secrets list --project GhseeliApis/GhseeliApis.csproj
```

### Remove a Secret
```bash
dotnet user-secrets remove "KeyName" --project GhseeliApis/GhseeliApis.csproj
```

### Clear All Secrets
```bash
dotnet user-secrets clear --project GhseeliApis/GhseeliApis.csproj
```

## Environment Variables (Production)

### Using Environment Variables
```bash
export CloudSql__Password=your_password
export JwtSettings__SecretKey=your_secret_key
export Stripe__SecretKey=sk_live_your_key
```

### Azure App Service
```bash
az webapp config appsettings set --name YourAppName --resource-group YourResourceGroup \
  --settings JwtSettings__SecretKey="your_key" \
             Stripe__SecretKey="sk_live_..." \
             CloudSql__Password="your_password"
```

### Docker
```bash
docker run -e "JwtSettings__SecretKey=your_key" \
           -e "Stripe__SecretKey=sk_live_..." \
           your-image
```

### Kubernetes
```bash
kubectl create secret generic app-secrets \
  --from-literal=jwt-secret=your_key \
  --from-literal=stripe-secret=sk_live_... \
  --from-literal=db-password=your_password
```

## Security Best Practices

1. ? **Never commit secrets to Git**
2. ? **Use User Secrets in Development** - Already configured with UserSecretsId
3. ? **Use Environment Variables in Production**
4. ? **Rotate keys regularly**
5. ? **Use different keys for test and production**
6. ? **Store webhook secrets securely**
7. ? **Use Azure Key Vault or similar for production**

## Current Configuration Status

### User Secrets (Development)
- ? UserSecretsId: `beb103e8-8fc9-41e4-b8e2-44f1698990dc`
- ? Stripe keys configured (placeholder values)
- ?? OAuth credentials: Set when you get real values from Google/Facebook
- ?? JWT secret: Set your production secret key
- ?? Database password: Set your database password

### Configuration Files
- ? `appsettings.json` - Contains placeholder values (safe to commit)
- ? `appsettings.Development.json` - Can override development settings
- ? `User Secrets` - Contains real sensitive values (not in Git)

## Getting Real API Keys

### Stripe
1. Go to [Stripe Dashboard](https://dashboard.stripe.com)
2. Navigate to Developers ? API keys
3. Copy test keys for development (pk_test_*, sk_test_*)
4. Copy live keys for production (pk_live_*, sk_live_*)

### Google OAuth
1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Create project ? Enable Google+ API
3. Create OAuth 2.0 credentials
4. Copy Client ID and Client Secret

### Facebook OAuth
1. Go to [Facebook Developers](https://developers.facebook.com)
2. Create app ? Add Facebook Login product
3. Copy App ID and App Secret

## Troubleshooting

### "UserSecretsId not found" Error
```bash
# Initialize user secrets
dotnet user-secrets init --project GhseeliApis/GhseeliApis.csproj
```

### Secrets Not Loading
```bash
# Verify secrets are set
dotnet user-secrets list --project GhseeliApis/GhseeliApis.csproj

# Check UserSecretsId in .csproj file
cat GhseeliApis/GhseeliApis.csproj | grep UserSecretsId
```

### Invalid JSON in appsettings.json
- JSON does not support comments
- All keys must be valid JSON strings
- Use this guide instead of inline comments
