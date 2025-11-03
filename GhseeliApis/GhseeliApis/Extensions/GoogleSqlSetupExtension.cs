using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using GhseeliApis.Data;

namespace GhseeliApis.Extensions;

/// <summary>
/// Extension methods for setting up Google Cloud SQL with MySQL
/// </summary>
public static class GoogleSqlSetupExtension
{
    /// <summary>
    /// Adds Google Cloud SQL MySQL database context to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddGoogleCloudSql(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = GetCloudSqlConnectionString(configuration);
        
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseMySql(
                connectionString,
                ServerVersion.AutoDetect(connectionString),
                mySqlOptions =>
                {
                    // Enable retry on failure for resilience
                    mySqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    
                    // Command timeout
                    mySqlOptions.CommandTimeout(60);
                });
            
            // Enable sensitive data logging in development
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        return services;
    }

    /// <summary>
    /// Builds the connection string for Google Cloud SQL
    /// Supports both Unix socket (recommended for Cloud Run/GKE) and TCP connections
    /// </summary>
    private static string GetCloudSqlConnectionString(IConfiguration configuration)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            // Database credentials from configuration
            Server = configuration["CloudSql:Server"] ?? "localhost",
            Database = configuration["CloudSql:Database"] ?? "ghseeli_db",
            UserID = configuration["CloudSql:UserId"] ?? "root",
            Password = configuration["CloudSql:Password"] ?? "",
            Port = uint.Parse(configuration["CloudSql:Port"] ?? "3306"),
            
            // Connection pooling settings
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 100,
            ConnectionTimeout = 30,
            
            // SSL/TLS settings for secure connections
            SslMode = MySqlSslMode.Required,
            
            // Character set
            CharacterSet = "utf8mb4"
        };

        // Check if using Unix socket for Cloud SQL (Cloud Run, GKE, etc.)
        var instanceConnectionName = configuration["CloudSql:InstanceConnectionName"];
        if (!string.IsNullOrEmpty(instanceConnectionName))
        {
            // Format: /cloudsql/PROJECT:REGION:INSTANCE
            builder.Server = $"/cloudsql/{instanceConnectionName}";
            builder.ConnectionProtocol = MySqlConnectionProtocol.UnixSocket;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Alternative method to add Google Cloud SQL with a custom connection string
    /// </summary>
    public static IServiceCollection AddGoogleCloudSql(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseMySql(
                connectionString,
                ServerVersion.AutoDetect(connectionString),
                mySqlOptions =>
                {
                    mySqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                });
        });

        return services;
    }

    /// <summary>
    /// Ensures the database is created and applies pending migrations
    /// Use with caution in production - consider using migration scripts instead
    /// </summary>
    public static async Task<IApplicationBuilder> EnsureDatabaseCreatedAsync(
        this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        try
        {
            // Ensure database is created
            await dbContext.Database.EnsureCreatedAsync();
            
            // Apply any pending migrations
            if ((await dbContext.Database.GetPendingMigrationsAsync()).Any())
            {
                await dbContext.Database.MigrateAsync();
            }
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
            logger.LogError(ex, "An error occurred while creating/migrating the database.");
            throw;
        }

        return app;
    }
}
