using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace GhseeliApis.Extensions;

/// <summary>
/// Extension methods for configuring Google Cloud SQL with MySQL
/// </summary>
public static class GoogleSqlSetupExtension
{
    /// <summary>
    /// Adds Google Cloud SQL with MySQL to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddGoogleCloudSql(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Get connection configuration from appsettings
        var server = configuration["CloudSql:Server"];
        var port = configuration["CloudSql:Port"];
        var database = configuration["CloudSql:Database"];
        var userId = configuration["CloudSql:UserId"];
        var password = configuration["CloudSql:Password"];
        var instanceConnectionName = configuration["CloudSql:InstanceConnectionName"];

        // Build connection string
        var connectionString = BuildConnectionString(
            server, port, database, userId, password, instanceConnectionName);

        // Add DbContext with MySQL
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var serverVersion = new MySqlServerVersion(new Version(8, 0, 30));
            
            options.UseMySql(connectionString, serverVersion, mysqlOptions =>
            {
                // Enable retry logic for transient failures
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);

                // Command timeout (optional)
                mysqlOptions.CommandTimeout(60);
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
    /// Adds Google Cloud SQL with a custom connection string
    /// </summary>
    public static IServiceCollection AddGoogleCloudSql(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var serverVersion = new MySqlServerVersion(new Version(8, 0, 30));
            options.UseMySql(connectionString, serverVersion);
        });

        return services;
    }

    /// <summary>
    /// Builds the MySQL connection string based on configuration
    /// </summary>
    private static string BuildConnectionString(
        string? server,
        string? port,
        string? database,
        string? userId,
        string? password,
        string? instanceConnectionName)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Database = database,
            UserID = userId,
            Password = password,
            SslMode = MySqlSslMode.Required,
            ConnectionTimeout = 30,
            DefaultCommandTimeout = 30,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 100,
            AllowUserVariables = true
        };

        // Check if we're using Unix socket (Cloud Run/GKE) or TCP
        if (!string.IsNullOrEmpty(instanceConnectionName))
        {
            // Using Cloud SQL Unix socket
            builder.Server = $"/cloudsql/{instanceConnectionName}";
            builder.SslMode = MySqlSslMode.Disabled; // Unix socket doesn't need SSL
        }
        else
        {
            // Using TCP connection (local development or public IP)
            builder.Server = server;
            builder.Port = uint.TryParse(port, out var portNumber) ? portNumber : 3306;
        }

        return builder.ConnectionString;
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
