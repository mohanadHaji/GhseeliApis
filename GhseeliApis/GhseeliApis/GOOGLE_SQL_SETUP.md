# Google Cloud SQL Setup Guide

## Overview
This project is configured to use Google Cloud SQL with MySQL through the `GoogleSqlSetupExtension`.

## Configuration

### appsettings.json
Configure your database connection in `appsettings.json`:

```json
{
  "CloudSql": {
    "Server": "localhost",
    "Port": "3306",
    "Database": "ghseeli_db",
    "UserId": "root",
    "Password": "your_password_here",
    "InstanceConnectionName": ""
  }
}
```

### For Google Cloud SQL Instance

When deploying to Google Cloud (Cloud Run, GKE, Compute Engine), set the `InstanceConnectionName`:

```json
{
  "CloudSql": {
    "Server": "",
    "Port": "3306",
    "Database": "ghseeli_db",
    "UserId": "your_user",
    "Password": "your_password",
    "InstanceConnectionName": "project-id:region:instance-name"
  }
}
```

### Environment Variables (Recommended for Production)

Instead of storing credentials in appsettings.json, use environment variables:

```bash
CloudSql__Server=localhost
CloudSql__Port=3306
CloudSql__Database=ghseeli_db
CloudSql__UserId=root
CloudSql__Password=your_password
CloudSql__InstanceConnectionName=project-id:region:instance-name
```

## Usage

### In Program.cs

The extension is already configured in `Program.cs`:

```csharp
using GhseeliApis.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Google Cloud SQL
builder.Services.AddGoogleCloudSql(builder.Configuration);

var app = builder.Build();
```

### Alternative: Using Connection String Directly

```csharp
builder.Services.AddGoogleCloudSql("Server=localhost;Database=ghseeli_db;User=root;Password=password;");
```

### Database Migration (Optional)

To automatically create database and apply migrations on startup:

```csharp
var app = builder.Build();

// Ensure database is created (use carefully in production)
await app.EnsureDatabaseCreatedAsync();

app.Run();
```

## Testing Database Connection

The API includes a health check endpoint:

```
GET /api/health/db
```

This will return:
```json
{
  "status": "Healthy",
  "database": "Google Cloud SQL",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

## Adding Entity Models

1. Create your entity classes in a `Models` folder
2. Add DbSet properties to `ApplicationDbContext.cs`:

```csharp
public class ApplicationDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Product> Products { get; set; }
    
    // ... existing code
}
```

3. Configure entities in `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>(entity =>
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
    });
}
```

## Entity Framework Core Migrations

### Install EF Core Tools

```bash
dotnet tool install --global dotnet-ef
```

### Create Initial Migration

```bash
dotnet ef migrations add InitialCreate --project GhseeliApis
```

### Update Database

```bash
dotnet ef database update --project GhseeliApis
```

## Google Cloud SQL Setup Steps

### 1. Create Cloud SQL Instance

```bash
gcloud sql instances create ghseeli-instance \
    --database-version=MYSQL_8_0 \
    --tier=db-f1-micro \
    --region=us-central1
```

### 2. Create Database

```bash
gcloud sql databases create ghseeli_db --instance=ghseeli-instance
```

### 3. Create User

```bash
gcloud sql users create ghseeli_user \
    --instance=ghseeli-instance \
    --password=YOUR_PASSWORD
```

### 4. Get Instance Connection Name

```bash
gcloud sql instances describe ghseeli-instance --format="value(connectionName)"
```

## Connection Methods

### 1. Unix Socket (Recommended for Cloud Run/GKE)
Set `InstanceConnectionName` in configuration. The application will automatically use Unix socket.

### 2. Cloud SQL Proxy (Local Development)

```bash
./cloud_sql_proxy -instances=PROJECT:REGION:INSTANCE=tcp:3306
```

Then connect to `localhost:3306`

### 3. Public IP (Not Recommended)
Enable public IP on your Cloud SQL instance and whitelist your IP addresses.

## Security Best Practices

1. **Never commit credentials** - Use environment variables or Secret Manager
2. **Use Cloud SQL Proxy** for local development
3. **Enable SSL/TLS** - The extension requires SSL by default
4. **Use IAM authentication** when possible
5. **Rotate passwords regularly**
6. **Use connection pooling** - Already configured in the extension

## Troubleshooting

### Connection Timeout
- Increase `ConnectionTimeout` in `GoogleSqlSetupExtension.cs`
- Check network connectivity to Cloud SQL instance
- Verify firewall rules

### SSL Certificate Errors
- Ensure `SslMode = MySqlSslMode.Required` is set
- Download and configure Cloud SQL server certificate if needed

### Authentication Failed
- Verify username and password
- Check user permissions on the database
- Ensure user is created for the correct host

## NuGet Packages Installed

- `Pomelo.EntityFrameworkCore.MySql` - EF Core provider for MySQL
- `MySql.Data` - MySQL connector
- `Google.Cloud.Diagnostics.AspNetCore` - Google Cloud diagnostics

## Additional Resources

- [Google Cloud SQL Documentation](https://cloud.google.com/sql/docs)
- [Pomelo EF Core Documentation](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql)
- [Entity Framework Core Documentation](https://docs.microsoft.com/en-us/ef/core/)
