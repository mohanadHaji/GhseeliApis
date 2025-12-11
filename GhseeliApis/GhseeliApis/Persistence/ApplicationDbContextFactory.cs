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
        // Build configuration from appsettings.json and user secrets
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<ApplicationDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Try to get connection string from various sources
        // Priority: RemoteTest (user secrets) > Production (env vars) > DefaultConnection (local)
        var connectionString = configuration.GetConnectionString("RemoteTest")
            ?? configuration.GetConnectionString("Production")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No connection string found for migrations. Please configure a connection string.");

        optionsBuilder.UseSqlServer(
            connectionString,
            options => options.EnableRetryOnFailure(
                maxRetryCount: 0  // Disable retry for design-time
            )
        );

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
