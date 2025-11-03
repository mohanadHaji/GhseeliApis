# ?? Configuration & Secrets Management Guide

## Summary

Removed all hardcoded connection strings and passwords from code. Configuration now properly uses appsettings.json with support for environment variables and User Secrets.

## ? What Was Fixed

### ? Before (Hardcoded)
```csharp
// ApplicationDbContextFactory.cs
var connectionString = "Server=localhost;Database=ghseeli_db;User=root;Password=temp;";  // ? BAD
```

### ? After (Configuration-based)
```csharp
// ApplicationDbContextFactory.cs
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()  // ? Supports env vars
    .Build();

var server = configuration["CloudSql:Server"];
var password = configuration["CloudSql:Password"];  // ? From config
```

## ?? Security Best Practices

### 1. **Never Hardcode Secrets**

? **DON'T:**
```csharp
var password = "myPassword123";  // NEVER DO THIS
var apiKey = "sk-abc123xyz";     // NEVER DO THIS
```

? **DO:**
```csharp
var password = configuration["CloudSql:Password"];
var apiKey = configuration["ApiKeys:OpenAI"];
```

### 2. **Never Commit Secrets to Git**

Your `.gitignore` already protects:
```gitignore
appsettings.*.json  # ? Development settings ignored
*.user              # ? User-specific files ignored
secrets.json        # ? User secrets ignored
```

But ensure:
- `appsettings.json` has **empty** passwords (committed)
- `appsettings.Development.json` is in `.gitignore` (NOT committed)
- Real passwords only in environment variables or User Secrets

### 3. **Use Different Methods per Environment**

| Environment | Method | Why |
|-------------|--------|-----|
| **Local Development** | User Secrets | Isolated per developer, not in git |
| **CI/CD** | Environment Variables | Injected by pipeline |
| **Production** | Azure Key Vault / AWS Secrets Manager | Centralized, audited, rotated |

## ?? Configuration Methods

### Method 1: User Secrets (Local Development)

**Best for:** Local development, keeps secrets out of git

```bash
# Initialize user secrets for the project
dotnet user-secrets init --project GhseeliApis

# Set individual secrets
dotnet user-secrets set "CloudSql:Password" "your_local_password" --project GhseeliApis
dotnet user-secrets set "CloudSql:Server" "localhost" --project GhseeliApis

# List all secrets
dotnet user-secrets list --project GhseeliApis

# Remove a secret
dotnet user-secrets remove "CloudSql:Password" --project GhseeliApis

# Clear all secrets
dotnet user-secrets clear --project GhseeliApis
```

**Where secrets are stored:**
- Windows: `%APPDATA%\Microsoft\UserSecrets\<user_secrets_id>\secrets.json`
- macOS/Linux: `~/.microsoft/usersecrets/<user_secrets_id>/secrets.json`

**How it works:**
```json
// Your project file will have:
<PropertyGroup>
  <UserSecretsId>a-unique-guid-here</UserSecricsId>
</PropertyGroup>

// Secrets stored in secrets.json (NOT in your project):
{
  "CloudSql": {
    "Password": "your_actual_password"
  }
}
```

### Method 2: Environment Variables

**Best for:** CI/CD, Docker, Production

```bash
# Windows (PowerShell)
$env:CloudSql__Password = "your_password"
$env:CloudSql__Server = "localhost"

# Windows (CMD)
set CloudSql__Password=your_password
set CloudSql__Server=localhost

# Linux/macOS
export CloudSql__Password="your_password"
export CloudSql__Server="localhost"

# Docker
docker run -e CloudSql__Password=your_password myapp

# Kubernetes
kubectl create secret generic cloudsql-secret \
  --from-literal=password=your_password
```

**Note:** Use double underscores `__` for nested configuration:
- `CloudSql:Password` ? `CloudSql__Password`
- `Logging:LogLevel:Default` ? `Logging__LogLevel__Default`

### Method 3: Azure Key Vault (Production)

**Best for:** Production in Azure

```csharp
// Program.cs
builder.Configuration.AddAzureKeyVault(
    new Uri("https://your-vault.vault.azure.net/"),
    new DefaultAzureCredential());
```

### Method 4: AWS Secrets Manager (Production)

**Best for:** Production in AWS

```csharp
// Add NuGet: Amazon.Extensions.Configuration.SystemsManager
builder.Configuration.AddSystemsManager("/myapp");
```

### Method 5: Google Secret Manager (Production)

**Best for:** Production in Google Cloud

```csharp
// Add NuGet: Google.Cloud.SecretManager.V1
// Access secrets via API
```

## ?? Configuration Priority Order

ASP.NET Core loads configuration in this order (later overrides earlier):

1. `appsettings.json` (committed, no secrets)
2. `appsettings.{Environment}.json` (environment-specific)
3. **User Secrets** (development only)
4. **Environment Variables** (all environments)
5. **Command-line arguments** (highest priority)

Example:
```json
// appsettings.json (committed)
{
  "CloudSql": {
    "Password": ""  // Empty placeholder
  }
}

// User Secrets (local only, NOT committed)
{
  "CloudSql": {
    "Password": "local_dev_password"  // ? Overrides appsettings.json
  }
}

// Environment Variable (production)
CloudSql__Password=production_password  // ? Overrides everything
```

## ?? Project Structure for Secrets

```
GhseeliApis/
??? appsettings.json                 ? Committed (NO secrets)
??? appsettings.Development.json     ? NOT committed (optional)
??? appsettings.Production.json      ? NOT committed
??? secrets.json                     ? NOT in project (User Secrets location)
??? .gitignore                       ? Protects sensitive files
```

## ?? Updated Files

### 1. ApplicationDbContextFactory.cs ?
**Changed:** Now reads from configuration instead of hardcoded string

**Before:**
```csharp
var connectionString = "Server=localhost;Database=ghseeli_db;User=root;Password=temp;";
```

**After:**
```csharp
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var password = configuration["CloudSql:Password"];
```

### 2. appsettings.json ?
**Changed:** Removed placeholder passwords, added security comments

```json
{
  "CloudSql": {
    "Password": "",  // ? Empty - set via User Secrets or Environment Variables
  },
  "// NOTE": "Never commit real passwords. Use User Secrets or environment variables."
}
```

### 3. appsettings.Development.json ?
**Changed:** Removed placeholder passwords

```json
{
  "CloudSql": {
    "Password": ""  // ? Empty - set via User Secrets
  }
}
```

## ?? Quick Setup Guide

### For Local Development

```bash
# 1. Initialize User Secrets
dotnet user-secrets init --project GhseeliApis

# 2. Set your local MySQL password
dotnet user-secrets set "CloudSql:Password" "your_local_mysql_password" --project GhseeliApis

# 3. Run the application
dotnet run --project GhseeliApis

# Your password is now stored safely outside the project!
```

### For CI/CD (GitHub Actions example)

```yaml
# .github/workflows/deploy.yml
env:
  CloudSql__Password: ${{ secrets.MYSQL_PASSWORD }}
  CloudSql__Server: ${{ secrets.MYSQL_SERVER }}

steps:
  - name: Run migrations
    run: dotnet ef database update
```

### For Production (Azure App Service)

```bash
# Azure CLI
az webapp config appsettings set \
  --name your-app \
  --resource-group your-rg \
  --settings CloudSql__Password="production_password"
```

### For Production (Google Cloud Run)

```bash
# Google Cloud CLI
gcloud run services update your-service \
  --set-env-vars CloudSql__Password="production_password"
```

## ??? Security Checklist

### Before Committing
- [ ] No passwords in code files
- [ ] No API keys in code files
- [ ] `appsettings.json` has empty passwords
- [ ] `appsettings.Development.json` is in `.gitignore`
- [ ] User Secrets configured for local development
- [ ] `.gitignore` includes `secrets.json`, `*.user`, `appsettings.*.json`

### For Each Environment
- [ ] **Development:** User Secrets configured
- [ ] **Staging:** Environment variables set
- [ ] **Production:** Key Vault/Secrets Manager configured
- [ ] **CI/CD:** GitHub Secrets / Azure DevOps Variables set

### Regular Maintenance
- [ ] Rotate passwords every 90 days
- [ ] Use different passwords per environment
- [ ] Never share passwords via email/chat
- [ ] Use password manager for team access
- [ ] Audit who has access to secrets

## ?? Additional Resources

### Microsoft Docs
- [Safe storage of app secrets in development](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [Configuration in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [Azure Key Vault configuration provider](https://docs.microsoft.com/en-us/aspnet/core/security/key-vault-configuration)

### Best Practices
- [OWASP Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)
- [The Twelve-Factor App - Config](https://12factor.net/config)

## ?? Summary

? **Hardcoded Secrets Removed** - No passwords in code  
? **Configuration-Based** - Reads from appsettings.json  
? **Environment Variables Support** - Production-ready  
? **User Secrets Support** - Developer-friendly  
? **Secure Defaults** - Empty passwords in committed files  
? **Documentation Added** - Clear setup instructions  
? **Build Successful** - No breaking changes  

**Your application now follows security best practices for configuration management!** ??
