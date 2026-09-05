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
        
        var connectionString = configuration.GetConnectionString("CustomerConnection")
            ?? throw new InvalidOperationException(
                "Customer database connection is not configured. Set ConnectionStrings__CustomerConnection.");

        optionsBuilder.UseSqlServer(
            connectionString,
            options =>
            {
                options.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CustomerSchemaOptions.OwnedDefaultSchema);
                options.EnableRetryOnFailure(
                    maxRetryCount: 0);
            }
        );

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
