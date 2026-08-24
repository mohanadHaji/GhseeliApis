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
        var connectionString = configuration.GetConnectionString("CustomerConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Customer database connection is not configured. Set ConnectionStrings__CustomerConnection.");
        }

        // Add DbContext with SQL Server
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlServerOptions =>
            {
                sqlServerOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CustomerSchemaOptions.OwnedDefaultSchema);

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

            #if DEBUG
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
