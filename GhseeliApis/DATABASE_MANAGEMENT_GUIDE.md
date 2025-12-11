# ??? Multi-Database Management Guide

## ?? Database Strategy Overview

Your application now supports **three separate databases**:

| Database | Purpose | Connection String Location | When to Use |
|----------|---------|---------------------------|-------------|
| **Local Dev** | Daily development | `appsettings.Development.json` | Default for local development |
| **Remote Test** | Pre-deployment testing | User Secrets | Testing against MonsterASP.NET |
| **Production** | Live application | Environment Variables | Deployed application |

---

## ?? How It Works

### **Priority Order:**
1. **RemoteTest** (User Secrets) - Highest priority
2. **Production** (Environment Variables) - Production only
3. **DefaultConnection** (appsettings.Development.json) - Local dev default

### **Current Configuration:**

```
???????????????????????????????????????????
?  Which Database Am I Using?             ?
???????????????????????????????????????????
?                                          ?
?  1. Check User Secrets                  ?
?     ?? RemoteTest configured?           ?
?     ?? ? Use Remote Test DB             ?
?                                          ?
?  2. Check Environment Variables          ?
?     ?? Production configured?            ?
?     ?? Use Production DB                 ?
?                                          ?
?  3. Check appsettings.Development.json   ?
?     ?? DefaultConnection configured?     ?
?     ?? ? Use Local Dev DB (Default)     ?
?                                          ?
???????????????????????????????????????????
```

---

## ?? Usage Scenarios

### **Scenario 1: Local Development (Default)**

**Purpose:** Daily development work with fast local database

**Setup:**
```powershell
# Remove RemoteTest from user secrets to use local
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"
dotnet user-secrets remove "ConnectionStrings:RemoteTest"

# Run application
dotnet run
```

**Database Used:** `(localdb)\mssqllocaldb` - `GhseeliDb_Dev`

**Apply Migrations:**
```powershell
# Migrations will use local database automatically
dotnet ef migrations add YourMigrationName
dotnet ef database update
```

**Benefits:**
- ? Fast and responsive
- ? No internet required
- ? Safe to experiment
- ? Can reset/recreate easily

---

### **Scenario 2: Remote Testing (MonsterASP.NET)**

**Purpose:** Test against production database before deployment

**Setup:**
```powershell
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"

# Add RemoteTest to user secrets
dotnet user-secrets set "ConnectionStrings:RemoteTest" "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"

# Run application
dotnet run
```

**Database Used:** `db34836.public.databaseasp.net` - `db34836`

**Apply Migrations:**
```powershell
# Migrations will use remote database automatically
dotnet ef database update
```

**Benefits:**
- ? Test against real production environment
- ? Verify database connectivity
- ? Test with production-like data
- ? Validate before deployment

**?? Warning:** This uses the real production database!

---

### **Scenario 3: Production Deployment**

**Purpose:** Live application on MonsterASP.NET

**Setup:** (In MonsterASP.NET Control Panel)
```
Environment Variable:
ConnectionStrings__Production = Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;

ASPNETCORE_ENVIRONMENT = Production
```

**Database Used:** `db34836.public.databaseasp.net` - `db34836`

**Apply Migrations:** Automatically on deployment or manually:
```powershell
dotnet ef database update --connection "Server=db34836.public.databaseasp.net;..."
```

---

## ?? Switching Between Databases

### **Switch to Local Dev Database:**
```powershell
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"

# Remove remote test connection
dotnet user-secrets remove "ConnectionStrings:RemoteTest"

# Verify
dotnet user-secrets list

# Run (will use local database)
dotnet run
```

### **Switch to Remote Test Database:**
```powershell
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"

# Add remote test connection
dotnet user-secrets set "ConnectionStrings:RemoteTest" "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"

# Run (will use remote database)
dotnet run
```

### **Quick Switch Script:**

Save as `switch-database.ps1`:
```powershell
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("local", "remote")]
    [string]$Database
)

$projectPath = "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"
Set-Location $projectPath

if ($Database -eq "local") {
    Write-Host "Switching to LOCAL development database..." -ForegroundColor Cyan
    dotnet user-secrets remove "ConnectionStrings:RemoteTest"
    Write-Host "? Now using: (localdb)\mssqllocaldb - GhseeliDb_Dev" -ForegroundColor Green
}
elseif ($Database -eq "remote") {
    Write-Host "Switching to REMOTE test database..." -ForegroundColor Cyan
    dotnet user-secrets set "ConnectionStrings:RemoteTest" "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
    Write-Host "? Now using: db34836.public.databaseasp.net - db34836" -ForegroundColor Green
}

Write-Host ""
Write-Host "Current secrets:" -ForegroundColor Yellow
dotnet user-secrets list
```

**Usage:**
```powershell
# Switch to local
.\switch-database.ps1 -Database local

# Switch to remote
.\switch-database.ps1 -Database remote
```

---

## ?? Migration Management

### **Should I Commit Migrations?**

**? YES - Always commit migrations to git!**

**Why?**
- Migrations are part of your code
- Team members need them
- Production deployment needs them
- They document database schema changes

**What to Commit:**
```
? Migrations/*.cs files
? ApplicationDbContextModelSnapshot.cs
? appsettings.json (without secrets)
? appsettings.Development.json (with local DB connection)
? appsettings.Production.json (without actual connection string)
```

**What NOT to Commit:**
```
? User secrets (automatically ignored)
? Actual production connection strings
? Passwords or sensitive data
```

---

## ?? Git Configuration

### **.gitignore** (Already configured)
```gitignore
# User secrets
**/secrets.json

# Environment variables
.env
.env.local
.env.production

# App settings with secrets (keep templates)
appsettings.*.json
!appsettings.json
!appsettings.Development.json
!appsettings.Production.json
```

### **Committing Strategy:**

```powershell
# Check what will be committed
git status

# Add migration files
git add Migrations/
git add GhseeliApis/Persistence/ApplicationDbContextModelSnapshot.cs

# Add config files (placeholders only)
git add GhseeliApis/appsettings.json
git add GhseeliApis/appsettings.Development.json
git add GhseeliApis/appsettings.Production.json

# Commit
git commit -m "Add database migrations for [feature name]"

# Push
git push origin master
```

---

## ?? Testing Different Databases

### **Test Local Database:**
```powershell
# Switch to local
dotnet user-secrets remove "ConnectionStrings:RemoteTest"

# Apply migrations
dotnet ef database update

# Run tests
dotnet test

# Run application
dotnet run
```

### **Test Remote Database:**
```powershell
# Switch to remote
dotnet user-secrets set "ConnectionStrings:RemoteTest" "Server=db34836.public.databaseasp.net;..."

# Apply migrations
dotnet ef database update

# Run application
dotnet run

# Test endpoints
curl https://localhost:5001/api/health/db
```

---

## ?? Verify Which Database You're Using

### **Check Current Configuration:**
```powershell
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"

# View user secrets
dotnet user-secrets list

# If RemoteTest is set ? Using REMOTE database
# If RemoteTest is NOT set ? Using LOCAL database
```

### **Add Logging to Program.cs (Optional):**

Add this after `var app = builder.Build();`:

```csharp
// Log which database we're using (Development only)
if (app.Environment.IsDevelopment())
{
    var connectionString = builder.Configuration.GetConnectionString("RemoteTest")
        ?? builder.Configuration.GetConnectionString("DefaultConnection");
    
    var dbSource = builder.Configuration.GetConnectionString("RemoteTest") != null 
        ? "REMOTE TEST" 
        : "LOCAL DEV";
    
    Console.WriteLine($"=================================");
    Console.WriteLine($"Using database: {dbSource}");
    Console.WriteLine($"Connection: {connectionString?.Substring(0, Math.Min(50, connectionString.Length))}...");
    Console.WriteLine($"=================================");
}
```

---

## ?? Database Comparison

| Feature | Local Dev | Remote Test | Production |
|---------|-----------|-------------|------------|
| **Speed** | ? Very Fast | ?? Slower (network) | ?? Slower (network) |
| **Availability** | ? Always | ?? Requires internet | ?? Requires internet |
| **Data Persistence** | ?? Local only | ? Shared | ? Live data |
| **Safe to Experiment** | ? Yes | ?? Careful | ? No |
| **Reset Database** | ? Easy | ?? Careful | ? Never |
| **Migrations** | ? Apply freely | ?? Test before prod | ? After testing |

---

## ?? Best Practices

### **1. Development Workflow:**
```
1. Develop with LOCAL database (fast, safe)
2. Test with LOCAL database
3. Create migration for changes
4. Commit migration to git
5. Switch to REMOTE test database
6. Apply migrations to remote
7. Test against remote
8. Deploy to production
9. Apply migrations in production
```

### **2. Migration Workflow:**
```powershell
# Always start with local
dotnet user-secrets remove "ConnectionStrings:RemoteTest"

# Create migration
dotnet ef migrations add YourMigrationName

# Test locally
dotnet ef database update
dotnet run

# Commit migration
git add Migrations/
git commit -m "Add migration: YourMigrationName"

# Test on remote
dotnet user-secrets set "ConnectionStrings:RemoteTest" "..."
dotnet ef database update
dotnet run

# If successful, deploy to production
```

### **3. Team Collaboration:**
```powershell
# Pull latest changes
git pull origin master

# Apply any new migrations to your local DB
dotnet ef database update

# Continue development
```

---

## ?? Troubleshooting

### **Issue: Not sure which database I'm using**
```powershell
# Check user secrets
dotnet user-secrets list

# If you see RemoteTest ? Remote database
# If no RemoteTest ? Local database
```

### **Issue: Want to reset local database**
```powershell
# Switch to local
dotnet user-secrets remove "ConnectionStrings:RemoteTest"

# Drop and recreate
dotnet ef database drop
dotnet ef database update
```

### **Issue: Migrations out of sync**
```powershell
# Check migration status
dotnet ef migrations list

# If behind, apply missing migrations
dotnet ef database update

# If ahead, may need to create new migration
```

---

## ?? Quick Reference Commands

```powershell
# Navigate to project
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis\GhseeliApis"

# USE LOCAL DATABASE
dotnet user-secrets remove "ConnectionStrings:RemoteTest"

# USE REMOTE DATABASE
dotnet user-secrets set "ConnectionStrings:RemoteTest" "Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"

# CHECK CURRENT CONFIG
dotnet user-secrets list

# CREATE MIGRATION
dotnet ef migrations add MigrationName

# APPLY MIGRATIONS
dotnet ef database update

# LIST MIGRATIONS
dotnet ef migrations list

# RUN APPLICATION
dotnet run

# RUN TESTS
dotnet test
```

---

## ? Summary

Your application now supports:
- ? **Local development** with fast local SQL Server
- ? **Remote testing** with MonsterASP.NET database
- ? **Production deployment** with environment variables
- ? **Easy switching** between databases
- ? **Git-friendly** configuration (no secrets committed)

**Default behavior:** Uses local database unless RemoteTest is configured in user secrets

**Recommended workflow:**
1. Develop locally (fast)
2. Test remotely (safe)
3. Deploy to production (confident)

---

**Need help switching databases? Use the commands in the Quick Reference section above!** ??
