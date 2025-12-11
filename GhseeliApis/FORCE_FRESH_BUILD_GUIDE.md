# Force Fresh Build - Deployment Checklist

## Problem
Visual Studio or `dotnet publish` might use cached builds, causing old code to be deployed even after making changes.

## Solution: Three-Step Fresh Build Process

### Step 1: Force Clean Build (Run This EVERY Time Before Deploy)

```powershell
# Navigate to solution root
cd C:\personalProject\GhseeliApis\GhseeliApis

# Option A: Use the enhanced script (RECOMMENDED)
.\publish-production.ps1

# Option B: Manual clean build
cd GhseeliApis
Remove-Item bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet clean --configuration Release
dotnet build --configuration Release --no-incremental --force
cd ..
```

### Step 2: Verify Fresh Build

```powershell
# Run verification script
.\verify-fresh-build.ps1
```

**What to Look For:**
- ? **"FRESH BUILD"** message (DLL less than 5 minutes old)
- ? **"unchanged since build"** for Program.cs and SqlServerSetupExtension.cs
- ? **"modified AFTER build"** = YOU MUST REBUILD!

### Step 3: Check Enhanced Logging is Present

The fresh build should include these console log lines in `SqlServerSetupExtension.cs`:

```csharp
Console.WriteLine($"?? Using database connection string from: {GetConnectionStringSource(configuration)}");
```

**To Verify Your Code Has This:**
1. Open `GhseeliApis\Extensions\SqlServerSetupExtension.cs`
2. Search for "?? Using database connection string"
3. If NOT found ? Your deployed code is OLD!

---

## Updated Publish Profile

Your `site46287-WebDeploy.pubxml` has been updated with:

```xml
<!-- Force Release configuration -->
<Configuration>Release</Configuration>

<!-- Clean before build to ensure fresh compilation -->
<DeleteExistingFiles>true</DeleteExistingFiles>

<!-- Exclude development files -->
<ExcludeFilesFromDeployment>appsettings.Development.json</ExcludeFilesFromDeployment>

<!-- Database Configuration -->
<ItemGroup>
  <MSDeployParameterValue Include="DefaultConnection-Web.config Connection String">
    <ParameterValue>Server=db34836.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;</ParameterValue>
    <UpdateDestWebConfig>true</UpdateDestWebConfig>
  </MSDeployParameterValue>
</ItemGroup>
```

**Key Changes:**
- ? Database connection string configured in publish profile
- ? Forces Release configuration
- ? Deletes existing files before deployment
- ? Changed `Encrypt=False` ? `Encrypt=True` (MonsterASP.NET's default was insecure)
- ? Added `TrustServerCertificate=True` (required for their SSL setup)

---

## In Visual Studio: Publish with Fresh Build

### Method 1: Right-Click Publish (GUI)

1. **Right-click** `GhseeliApis` project ? **Clean**
2. **Build** ? **Rebuild Solution** (Ctrl+Shift+B)
3. **Right-click** `GhseeliApis` project ? **Publish**
4. Select `site46287-WebDeploy` profile
5. Click **Publish**

? The updated profile will automatically use your database connection string!

### Method 2: Publish Profile with CLI

```powershell
cd GhseeliApis
dotnet clean --configuration Release
dotnet publish /p:PublishProfile=site46287-WebDeploy
```

---

## Verification Commands

### Check DLL Build Timestamp
```powershell
Get-Item "GhseeliApis\bin\Release\net8.0\GhseeliApis.dll" | Select-Object LastWriteTime
```

### Check If Code Files Are Newer Than Build
```powershell
$dll = Get-Item "GhseeliApis\bin\Release\net8.0\GhseeliApis.dll"
$program = Get-Item "GhseeliApis\Program.cs"
$extension = Get-Item "GhseeliApis\Extensions\SqlServerSetupExtension.cs"

Write-Host "DLL Built:        $($dll.LastWriteTime)"
Write-Host "Program.cs:       $($program.LastWriteTime)"
Write-Host "SqlExtension.cs:  $($extension.LastWriteTime)"

if ($program.LastWriteTime -gt $dll.LastWriteTime -or $extension.LastWriteTime -gt $dll.LastWriteTime) {
    Write-Host "? CODE IS NEWER THAN BUILD - REBUILD REQUIRED!" -ForegroundColor Red
} else {
    Write-Host "? Build is up to date" -ForegroundColor Green
}
```

---

## Common Signs Your Deployed Code Is OLD

### At Runtime (Check MonsterASP.NET Logs)

? **OLD CODE:**
```
Application '/LM/W3SVC/1714/ROOT' ... exception code = '0xe0434352'
System.ArgumentException: Format of the initialization string...
```
- No console output showing "?? Using database connection string"
- No OAuth configuration messages
- Generic framework exceptions

? **FRESH CODE:**
```
??  Google OAuth not configured - Google login will not be available
??  Facebook OAuth not configured - Facebook login will not be available
?? Using database connection string from: DefaultConnection (appsettings)
```
- Clear console output with emojis
- Helpful error messages
- Shows which connection string source is used

---

## Troubleshooting

### Problem: "Build seems cached"

**Solution:**
1. Close Visual Studio completely
2. Delete `bin` and `obj` folders manually
3. Restart Visual Studio
4. Clean ? Rebuild ? Publish

### Problem: "Still getting old errors after deploy"

**Causes:**
1. MonsterASP.NET is caching old files
2. IIS application pool hasn't restarted
3. Published wrong files

**Solution:**
1. Check `DeleteExistingFiles` is `true` in publish profile (we set this)
2. Enable `EnableMsDeployAppOffline` (we set this - takes app offline during deploy)
3. After upload, wait 30 seconds for IIS to restart
4. Check MonsterASP.NET control panel ? Restart application pool

### Problem: "How do I know if my specific code change is deployed?"

**Add a Temporary Console Log:**

```csharp
// In Program.cs after line 31 (after builder.Services.AddSqlServer)
Console.WriteLine("?? BUILD TIMESTAMP: 2024-12-11 10:00 AM - LATEST VERSION");
```

1. Save Program.cs
2. Run `.\publish-production.ps1`
3. Verify DLL timestamp is fresh (under 2 minutes)
4. Deploy to MonsterASP.NET
5. Check logs for your timestamp message

---

## Best Practice: Pre-Deployment Checklist

Before EVERY deployment:

- [ ] Run `.\verify-fresh-build.ps1` - must show "FRESH BUILD"
- [ ] Check Git status: `git status` (no uncommitted changes)
- [ ] Run tests: `dotnet test`
- [ ] Build Release: `.\publish-production.ps1`
- [ ] Verify key files in `bin\Release\net8.0\publish\`
- [ ] Publish via Visual Studio or CLI
- [ ] Wait 30 seconds after deployment
- [ ] Test: `http://gasli.runasp.net/api/health`
- [ ] Check logs in MonsterASP.NET control panel

---

## Quick Reference

| Command | Purpose |
|---------|---------|
| `.\verify-fresh-build.ps1` | Check if build is fresh |
| `.\publish-production.ps1` | Clean + Build + Publish (local) |
| `dotnet clean --configuration Release` | Clean Release build |
| `Remove-Item bin,obj -Recurse -Force` | Nuclear clean (deletes all build artifacts) |
| `dotnet build --configuration Release --no-incremental` | Force full rebuild without cache |
| `dotnet publish /p:PublishProfile=site46287-WebDeploy` | Publish using Visual Studio profile |

---

## Expected Console Output After Fresh Deploy

When your **FRESH CODE** runs on MonsterASP.NET, you should see:

```
??  Google OAuth not configured - Google login will not be available
??  Facebook OAuth not configured - Facebook login will not be available
?? Using database connection string from: DefaultConnection (appsettings)
Role 'User' created successfully
Role 'Company' created successfully
Role 'Admin' created successfully
```

If you see the **old error** instead:
```
System.ArgumentException: Format of the initialization string does not conform to specification starting at index 0
```

Then your deployed code is **STILL OLD** - repeat fresh build process!
