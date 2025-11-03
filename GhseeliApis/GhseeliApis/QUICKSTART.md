# Quick Start Guide - Google Cloud SQL Setup

## Project Structure

```
GhseeliApis/
??? Extensions/
?   ??? GoogleSqlSetupExtension.cs    # Google Cloud SQL configuration extension
??? Data/
?   ??? ApplicationDbContext.cs        # EF Core DbContext
??? Models/
?   ??? User.cs                        # Sample entity model
??? Program.cs                         # Main application entry point
??? appsettings.json                   # Configuration file
??? appsettings.Development.json       # Development configuration
??? GOOGLE_SQL_SETUP.md               # Detailed setup guide
```

## What Has Been Set Up

? **Google Cloud SQL Extension** - Located in `Extensions/GoogleSqlSetupExtension.cs`
- Configures MySQL connection for Google Cloud SQL
- Supports both Unix socket (Cloud Run/GKE) and TCP connections
- Includes retry logic and connection pooling
- SSL/TLS enabled by default

? **Database Context** - `Data/ApplicationDbContext.cs`
- Entity Framework Core DbContext
- Configured with User entity as an example
- Ready for migrations

? **Sample Model** - `Models/User.cs`
- Demonstrates entity creation
- Includes common fields (Id, Name, Email, timestamps)

? **API Endpoints** - `Program.cs`
- `/api/hello` - Test endpoint
- `/api/health/db` - Database health check
- `/api/users` - Full CRUD operations for User entity

? **Configuration Files**
- `appsettings.json` - Production settings template
- `appsettings.Development.json` - Development settings

## Quick Setup Steps

### 1. Update Configuration

Edit `appsettings.Development.json`:

```json
{
  "CloudSql": {
    "Server": "localhost",
    "Port": "3306",
    "Database": "ghseeli_db",
    "UserId": "root",
    "Password": "your_password"
  }
}
```

### 2. Create Database (Using EF Core Migrations)

```bash
# Install EF Core CLI tools (if not already installed)
dotnet tool install --global dotnet-ef

# Create initial migration
dotnet ef migrations add InitialCreate --project GhseeliApis

# Update database
dotnet ef database update --project GhseeliApis
```

### 3. Run the Application

```bash
dotnet run --project GhseeliApis/GhseeliApis.csproj
```

### 4. Test the API

Open your browser and navigate to:
- `https://localhost:5001` - Swagger UI
- `https://localhost:5001/api/health/db` - Check database connection
- `https://localhost:5001/api/hello` - Test endpoint

## Available Endpoints

### Health Check
- **GET** `/api/health/db` - Check database connection status

### Users (CRUD)
- **GET** `/api/users` - Get all users
- **GET** `/api/users/{id}` - Get user by ID
- **POST** `/api/users` - Create new user
- **PUT** `/api/users/{id}` - Update user
- **DELETE** `/api/users/{id}` - Delete user

## Example API Calls

### Create User
```bash
curl -X POST https://localhost:5001/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "name": "John Doe",
    "email": "john@example.com",
    "isActive": true
  }'
```

### Get All Users
```bash
curl https://localhost:5001/api/users
```

## For Google Cloud Deployment

### 1. Create Cloud SQL Instance

```bash
gcloud sql instances create ghseeli-instance \
    --database-version=MYSQL_8_0 \
    --tier=db-f1-micro \
    --region=us-central1
```

### 2. Update appsettings.json for Cloud

```json
{
  "CloudSql": {
    "Database": "ghseeli_db",
    "UserId": "ghseeli_user",
    "Password": "secure_password",
    "InstanceConnectionName": "project-id:region:instance-name"
  }
}
```

### 3. Deploy to Cloud Run

```bash
# Build container
gcloud builds submit --tag gcr.io/PROJECT_ID/ghseeli-api

# Deploy
gcloud run deploy ghseeli-api \
    --image gcr.io/PROJECT_ID/ghseeli-api \
    --add-cloudsql-instances PROJECT_ID:REGION:INSTANCE_NAME \
    --set-env-vars CloudSql__InstanceConnectionName=PROJECT_ID:REGION:INSTANCE_NAME
```

## Adding New Entities

### 1. Create Model Class

Create a new file in `Models/` folder:

```csharp
namespace GhseeliApis.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}
```

### 2. Add DbSet to ApplicationDbContext

```csharp
public DbSet<Product> Products { get; set; }
```

### 3. Configure Entity (Optional)

```csharp
modelBuilder.Entity<Product>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
    entity.Property(e => e.Price).HasPrecision(18, 2);
});
```

### 4. Create Migration

```bash
dotnet ef migrations add AddProduct --project GhseeliApis
dotnet ef database update --project GhseeliApis
```

## Troubleshooting

### Database Connection Failed
1. Check MySQL is running: `mysql -u root -p`
2. Verify credentials in appsettings.json
3. Check database exists: `SHOW DATABASES;`

### Migration Errors
1. Remove existing migrations: `rm -rf Migrations/`
2. Recreate: `dotnet ef migrations add InitialCreate`
3. Update: `dotnet ef database update`

### Build Errors
1. Clean: `dotnet clean`
2. Restore: `dotnet restore`
3. Rebuild: `dotnet build`

## Next Steps

1. ? Configure your database connection
2. ? Run migrations to create database schema
3. ? Test the API using Swagger UI
4. ? Add your own entity models
5. ? Deploy to Google Cloud

For detailed documentation, see [GOOGLE_SQL_SETUP.md](./GOOGLE_SQL_SETUP.md)
