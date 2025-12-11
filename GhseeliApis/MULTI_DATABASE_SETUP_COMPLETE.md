# ? Multi-Database Configuration Complete!

## ?? What Was Set Up

Your application now supports **three separate databases** with easy switching:

### 1. **Local Development Database** (Default)
- **Server:** `(localdb)\mssqllocaldb`
- **Database:** `GhseeliDb_Dev`
- **Use for:** Daily development, fast iteration
- **Benefits:** Fast, offline, safe to experiment

### 2. **Remote Test Database** (MonsterASP.NET)
- **Server:** `db34836.public.databaseasp.net`
- **Database:** `db34836`
- **Use for:** Pre-deployment testing
- **Benefits:** Test against production environment

### 3. **Production Database** (MonsterASP.NET)
- **Server:** `db34836.public.databaseasp.net`
- **Database:** `db34836` (or separate)
- **Use for:** Live deployed application
- **Configuration:** Environment variables

---

## ?? Quick Start - Switch Databases

### **Check Current Database:**
```powershell
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis"
.\switch-database.ps1 -Database status
```

### **Switch to Local Database:**
```powershell
.\switch-database.ps1 -Database local
dotnet ef database update
dotnet run
```

### **Switch to Remote Database:**
```powershell
.\switch-database.ps1 -Database remote
dotnet ef database update
dotnet run
```

---

## ?? Current Status

```
? Currently Using: REMOTE test database (MonsterASP.NET)
   Server: db34836.public.databaseasp.net
   Database: db34836

??  Configuration Priority:
   1. RemoteTest (User Secrets) ? Currently Active
   2. Production (Environment Variables)
   3. DefaultConnection (appsettings.Development.json)
```

---

## ?? Recommended Workflow

### **For Development:**
```powershell
# 1. Switch to local database (fast, safe)
.\switch-database.ps1 -Database local

# 2. Develop and test locally
dotnet run

# 3. Create migrations for your changes
dotnet ef migrations add YourFeatureName

# 4. Apply migrations locally
dotnet ef database update

# 5. Commit migrations to git
git add Migrations/
git commit -m "Add migration: YourFeatureName"
```

### **For Testing:**
```powershell
# 1. Switch to remote database
.\switch-database.ps1 -Database remote

# 2. Apply migrations to remote
dotnet ef database update

# 3. Test against production-like environment
dotnet run

# 4. Verify everything works
curl https://localhost:5001/api/health/db
```

### **For Deployment:**
```powershell
# 1. Publish application
dotnet publish -c Release -o bin\Release\net8.0\publish

# 2. Upload to MonsterASP.NET via FTP

# 3. Set environment variables in control panel
# 4. Migrations apply automatically on startup
```

---

## ?? Migration Management - IMPORTANT!

### **? YES - Always Commit Migrations to Git!**

**What to commit:**
```bash
git add Migrations/
git add GhseeliApis/Persistence/ApplicationDbContextModelSnapshot.cs
git add GhseeliApis/appsettings.json
git add GhseeliApis/appsettings.Development.json
git add GhseeliApis/appsettings.Production.json
git commit -m "Add migration: [description]"
git push origin master
```

**Why commit migrations?**
- ? Team members can apply same schema
- ? Production deployment needs them
- ? Documents database changes
- ? Version control for database schema

**What NOT to commit:**
- ? User secrets (automatically ignored by git)
- ? Actual connection strings with passwords
- ? `.env` files with secrets

---

## ?? Files Changed

### **Modified:**
1. `GhseeliApis/appsettings.Development.json`
   - Added named connection strings (LocalDev, RemoteTest, Production)
   - DefaultConnection points to local SQL Server

2. `GhseeliApis/appsettings.Production.json`
   - Updated to use environment variables

3. `GhseeliApis/Extensions/SqlServerSetupExtension.cs`
   - Updated priority order: RemoteTest ? Production ? DefaultConnection

4. `GhseeliApis/Persistence/ApplicationDbContextFactory.cs`
   - Updated to support all three connection strings

### **Created:**
1. `DATABASE_MANAGEMENT_GUIDE.md` - Comprehensive guide
2. `switch-database.ps1` - Database switching script
3. `MULTI_DATABASE_SETUP_COMPLETE.md` - This file

### **User Secrets Updated:**
- Renamed from `ConnectionStrings:MonsterAspNet` to `ConnectionStrings:RemoteTest`
- Currently set to MonsterASP.NET database

---

## ?? Testing Different Databases

### **Test Local Database:**
```powershell
# Switch to local
.\switch-database.ps1 -Database local

# Apply migrations
dotnet ef database update

# Run application
dotnet run

# Test endpoint
curl https://localhost:5001/api/health/db
# Should show: "Server: (localdb)\mssqllocaldb"
```

### **Test Remote Database:**
```powershell
# Switch to remote
.\switch-database.ps1 -Database remote

# Apply migrations (already done)
dotnet ef database update

# Run application
dotnet run

# Test endpoint
curl https://localhost:5001/api/health/db
# Should show: "Server: db34836.public.databaseasp.net"
```

---

## ?? Common Questions

### **Q: Which database should I use for daily development?**
**A:** Use **LOCAL** database for fast development:
```powershell
.\switch-database.ps1 -Database local
```

### **Q: When should I use the remote database?**
**A:** Use **REMOTE** database when:
- Testing before deployment
- Verifying database connectivity
- Testing with production-like data
- Final validation before going live

### **Q: Will migrations be committed to git?**
**A:** **YES!** Always commit migrations. They are code, not data.

### **Q: Will connection strings be committed to git?**
**A:** **NO!** Connection strings are in:
- User secrets (git-ignored)
- Environment variables (not in code)
- appsettings files show PLACEHOLDERS only

### **Q: How do I know which database I'm using?**
**A:** Run:
```powershell
.\switch-database.ps1 -Database status
```

### **Q: Can I have a separate production database?**
**A:** Yes! In MonsterASP.NET, you can:
1. Create a second database (e.g., `db34836_prod`)
2. Set `ConnectionStrings__Production` in environment variables
3. Keep `db34836` for testing, `db34836_prod` for production

---

## ?? Database Comparison Table

| Feature | Local Dev | Remote Test | Production |
|---------|-----------|-------------|------------|
| **Location** | Your machine | MonsterASP.NET | MonsterASP.NET |
| **Connection** | localhost | Internet | Internet |
| **Speed** | ? Very Fast | ?? Network | ?? Network |
| **Offline Work** | ? Yes | ? No | ? No |
| **Safe Experiments** | ? Yes | ?? Careful | ? No |
| **Reset Data** | ? Easy | ?? Manual | ? Never |
| **Apply Migrations** | ? Freely | ?? Test first | ?? Carefully |
| **Configuration** | appsettings | User Secrets | Env Vars |

---

## ?? Next Steps

### **1. Start with Local Database (Recommended)**
```powershell
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis"

# Switch to local
.\switch-database.ps1 -Database local

# Apply migrations to local database
cd GhseeliApis
dotnet ef database update

# Run application
dotnet run
```

### **2. Test Your Endpoints Locally**
Open browser to `https://localhost:5001` and test:
- Health endpoint
- Register user
- Login
- Create bookings
- etc.

### **3. When Ready, Test Against Remote**
```powershell
# Switch to remote
.\switch-database.ps1 -Database remote

# Migrations already applied, just run
dotnet run

# Test endpoints again
```

### **4. Commit Your Work**
```powershell
# Check status
git status

# Add migrations (if any new ones)
git add Migrations/

# Commit
git commit -m "Add feature: [description]"

# Push
git push origin master
```

---

## ?? Documentation References

- **DATABASE_MANAGEMENT_GUIDE.md** - Complete guide for all scenarios
- **QUICK_DEPLOYMENT_REFERENCE.md** - Deployment commands
- **MONSTERASP_DEPLOYMENT_GUIDE.md** - Full deployment guide
- **MIGRATIONS_APPLIED_SUCCESS.md** - Remote database setup

---

## ? Summary

Your application now has:
- ? **Three-database support** (Local, Remote Test, Production)
- ? **Easy switching** with `switch-database.ps1` script
- ? **Git-friendly** configuration (no secrets committed)
- ? **Clear priority order** (RemoteTest ? Production ? DefaultConnection)
- ? **Comprehensive documentation** for all scenarios

**Current State:**
- ?? Using: **REMOTE** test database (MonsterASP.NET)
- ?? Migrations: Applied to remote database
- ? Build: Successful
- ?? Documentation: Complete

---

## ?? Quick Commands Reference

```powershell
# Navigate to solution
cd "C:\Users\v-mhaj\OneDrive - Microsoft\Desktop\GhseeliApis\GhseeliApis"

# Check which database you're using
.\switch-database.ps1 -Database status

# Switch to local database
.\switch-database.ps1 -Database local

# Switch to remote database
.\switch-database.ps1 -Database remote

# Apply migrations
cd GhseeliApis
dotnet ef database update

# Run application
dotnet run

# Run tests
dotnet test

# Commit migrations
git add Migrations/
git commit -m "Add migration: [name]"
git push
```

---

**?? You're all set! Your database configuration is now flexible, secure, and production-ready!**

**Recommended:** Start with local database for fast development, then test on remote before deploying.

```powershell
# Start developing with local database now:
.\switch-database.ps1 -Database local
cd GhseeliApis
dotnet ef database update
dotnet run
```
