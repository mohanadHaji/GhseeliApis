using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Extensions;

/// <summary>
/// Extension methods for configuring SQL Server
/// </summary>
public static class SqlServerSetupExtension
{
    /// <summary>
    /// Adds SQL Server to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSqlServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Get connection string from configuration based on environment
        // Priority order:
        // 1. RemoteTest (from user secrets for testing)
        // 2. Production (from environment variables in production)
        // 3. DefaultConnection (from appsettings for local dev)
        var connectionString = configuration.GetConnectionString("RemoteTest")    // User secrets
            ?? configuration.GetConnectionString("Production")                     // Production env vars
            ?? configuration.GetConnectionString("DefaultConnection")              // Local dev
            ?? throw new InvalidOperationException("Database connection string not configured");

        // Add DbContext with SQL Server
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlServerOptions =>
            {
                // Enable retry logic for transient failures
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);

                // Command timeout (optional)
                sqlServerOptions.CommandTimeout(60);

                // Use newer compatibility level
                sqlServerOptions.UseCompatibilityLevel(120);
            });

            // Enable sensitive data logging in development
            #if DEBUG
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
            #endif
        });

        return services;
    }

    /// <summary>
    /// Adds SQL Server with a custom connection string
    /// </summary>
    public static IServiceCollection AddSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
            });
        });

        return services;
    }

    /// <summary>
    /// Extension method to ensure database is created and migrations are applied
    /// WARNING: Use this carefully in production
    /// </summary>
    public static async Task<IApplicationBuilder> EnsureDatabaseCreatedAsync(
        this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        try
        {
            // This will create the database if it doesn't exist
            // and apply any pending migrations
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            // Log the error (you should inject ILogger here in production)
            Console.WriteLine($"Error ensuring database created: {ex.Message}");
            throw;
        }

        return app;
    }
}
