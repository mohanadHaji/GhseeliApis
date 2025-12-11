# ?? Troubleshooting HTTP 500.30 - Application Failed to Start

## What This Error Means
HTTP 500.30 indicates the ASP.NET Core application **failed to start entirely**. This happens BEFORE any of your code runs, usually during configuration/dependency injection setup.

---

## Most Common Causes (In Order of Likelihood)

### 1. ? Missing Required Configuration (MOST LIKELY)
**Symptom**: Application crashes immediately on startup
**Cause**: Required configuration values are missing

Your `Program.cs` requires these configurations that will throw exceptions if missing:

```csharp
// Line 64 - JWT Secret REQUIRED
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");

// Line 88 - Google OAuth REQUIRED
options.ClientId = googleAuth["ClientId"] ?? throw new InvalidOperationException("Google ClientId is not configured");
options.ClientSecret = googleAuth["ClientSecret"] ?? throw new InvalidOperationException("Google ClientSecret is not configured");

// Line 96 - Facebook OAuth REQUIRED
options.AppId = facebookAuth["AppId"] ?? throw new InvalidOperationException("Facebook AppId is not configured");
options.AppSecret = facebookAuth["AppSecret"] ?? throw new InvalidOperationException("Facebook AppSecret is not configured");
```

**SOLUTION**: Set these environment variables in MonsterASP.NET control panel.

---

### 2. ? Database Connection String Missing
**Symptom**: Application crashes during DbContext initialization
**Cause**: Connection string not configured

**SOLUTION**: Set `ConnectionStrings__DefaultConnection` in environment variables.

---

### 3. ? web.config Issues
**Symptom**: IIS can't start the application
**Cause**: Incorrect or missing web.config

---

## ?? IMMEDIATE FIX - Make Configuration Optional for Startup

Let's modify `Program.cs` to allow the app to START even if OAuth providers aren't configured yet. This will let you see better error messages.

### Step 1: Update Program.cs to Skip OAuth if Not Configured

Replace the OAuth configuration sections with this safer version:

```csharp
// Configure JWT Authentication (REQUIRED)
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];

if (string.IsNullOrEmpty(secretKey))
{
    throw new InvalidOperationException("JWT SecretKey is not configured. Set JwtSettings__SecretKey environment variable.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = builder.Environment.IsProduction();
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

// Configure Google OAuth (OPTIONAL - only if configured)
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.SaveTokens = true;
            options.CallbackPath = "/api/auth/google-callback";
        });
    Console.WriteLine("? Google OAuth configured");
}
else
{
    Console.WriteLine("??  Google OAuth not configured - Google login will not be available");
}

// Configure Facebook OAuth (OPTIONAL - only if configured)
var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"];
var facebookAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];

if (!string.IsNullOrEmpty(facebookAppId) && !string.IsNullOrEmpty(facebookAppSecret))
{
    builder.Services.AddAuthentication()
        .AddFacebook(options =>
        {
            options.AppId = facebookAppId;
            options.AppSecret = facebookAppSecret;
            options.SaveTokens = true;
            options.CallbackPath = "/api/auth/facebook-callback";
            options.Fields.Add("name");
            options.Fields.Add("email");
            options.Fields.Add("picture");
        });
    Console.WriteLine("? Facebook OAuth configured");
}
else
{
    Console.WriteLine("??  Facebook OAuth not configured - Facebook login will not be available");
}
```

---

## ?? Minimum Environment Variables Required to START

Set these in MonsterASP.NET control panel:

### CRITICAL (App won't start without these):
```bash
# Database Connection
ConnectionStrings__DefaultConnection=Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;

# JWT Secret (REQUIRED)
JwtSettings__SecretKey=YOUR_64_CHARACTER_SECRET_HERE

# JWT Configuration
JwtSettings__Issuer=GhseeliApis
JwtSettings__Audience=GhseeliApis
JwtSettings__ExpirationMinutes=60

# Environment
ASPNETCORE_ENVIRONMENT=Production
```

### OPTIONAL (Can be added later):
```bash
# Google OAuth (optional)
Authentication__Google__ClientId=YOUR_CLIENT_ID
Authentication__Google__ClientSecret=YOUR_CLIENT_SECRET

# Facebook OAuth (optional)
Authentication__Facebook__AppId=YOUR_APP_ID
Authentication__Facebook__AppSecret=YOUR_APP_SECRET

# Stripe (optional - for payments)
Stripe__SecretKey=sk_test_YOUR_KEY
Stripe__PublishableKey=pk_test_YOUR_KEY
Stripe__WebhookSecret=whsec_YOUR_SECRET
```

---

## ?? How to Get Better Error Messages

### Enable Detailed Errors in web.config

Update `web.config` in your published files:

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
          <!-- Enable detailed errors -->
          <environmentVariable name="ASPNETCORE_DETAILEDERRORS" value="true" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

### Check Application Logs

1. **In MonsterASP.NET control panel**: Look for application logs
2. **Check the `logs` folder**: `logs\stdout-*.log` files
3. **Windows Event Viewer**: Application logs

---

## ?? Quick Fix Steps (Do These Now)

### Step 1: Generate JWT Secret
```powershell
# Generate 64-character secret
-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 64 | % {[char]$_})
```

### Step 2: Set Minimum Environment Variables in MonsterASP.NET

Go to your web app configuration and add:

1. **ConnectionStrings__DefaultConnection** = `Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;`

2. **JwtSettings__SecretKey** = `<your-generated-64-char-secret>`

3. **JwtSettings__Issuer** = `GhseeliApis`

4. **JwtSettings__Audience** = `GhseeliApis`

5. **ASPNETCORE_ENVIRONMENT** = `Production`

### Step 3: Update Program.cs (Optional OAuth)

Use the code I provided above to make OAuth optional.

### Step 4: Enable Detailed Errors in web.config

Add the `ASPNETCORE_DETAILEDERRORS` environment variable.

### Step 5: Republish

```powershell
cd GhseeliApis
dotnet publish --configuration Release --output "bin\Release\net8.0\publish"
```

---

## ?? Alternative: Test Configuration Locally First

Before deploying, test with production-like settings locally:

```powershell
cd GhseeliApis

# Set environment variables
$env:ConnectionStrings__DefaultConnection="Server=db34836.public.databaseasp.net;Database=db34836;User Id=db34836;Password=kG=5C7b+aS#9;Encrypt=True;TrustServerCertificate=True;"
$env:JwtSettings__SecretKey="YOUR_GENERATED_SECRET"
$env:JwtSettings__Issuer="GhseeliApis"
$env:JwtSettings__Audience="GhseeliApis"
$env:ASPNETCORE_ENVIRONMENT="Production"

# Run the app
dotnet run --configuration Release

# Test health endpoint
curl http://localhost:5000/api/health
```

If it fails locally, you'll see the exact error message!

---

## ?? Diagnostic Checklist

- [ ] JWT SecretKey set in environment variables
- [ ] Database connection string set in environment variables
- [ ] JwtSettings__Issuer and JwtSettings__Audience set
- [ ] ASPNETCORE_ENVIRONMENT set to "Production"
- [ ] web.config exists in published folder
- [ ] GhseeliApis.dll exists in published folder
- [ ] All dependency DLLs published
- [ ] OAuth providers made optional (or credentials provided)
- [ ] Detailed errors enabled in web.config
- [ ] Application logs checked

---

## ?? Most Likely Issue

Based on your error, the issue is almost certainly:

**Missing JWT SecretKey** - Your `Program.cs` line 64 throws an exception if this isn't set.

**Quick fix**: Set `JwtSettings__SecretKey` environment variable in MonsterASP.NET control panel.

---

Would you like me to:
1. Create the updated Program.cs with optional OAuth?
2. Generate a JWT secret for you?
3. Create a script to test locally with production settings?
