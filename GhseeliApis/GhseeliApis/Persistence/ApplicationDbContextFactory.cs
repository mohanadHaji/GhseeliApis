using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace GhseeliApis.Persistence;

/// <summary>
/// Design-time factory for ApplicationDbContext to support EF Core migrations
/// This is ONLY used during design-time for migrations - it won't affect runtime behavior
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Build configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Read connection details from configuration
        var server = configuration["CloudSql:Server"];
        var port = configuration["CloudSql:Port"];
        var database = configuration["CloudSql:Database"];
        var userId = configuration["CloudSql:UserId"];
        var password = configuration["CloudSql:Password"];

        // Build connection string from configuration
        var connectionString = $"Server={server};Port={port};Database={database};User={userId};Password={password};";
        
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 30));
        optionsBuilder.UseMySql(
            connectionString, 
            serverVersion,
            options => options.EnableRetryOnFailure(
                maxRetryCount: 0  // Disable retry for design-time
            )
        );

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
