using GhseeliApis.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GhseeliApis.Tests.Support;

internal sealed class SqlServerCatalogDatabase : IAsyncDisposable
{
    private readonly string _databaseName = $"GhseeliCatalogTests_{Guid.NewGuid():N}";

    public string ConnectionString =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public static async Task<SqlServerCatalogDatabase> CreateAsync()
    {
        var database = new SqlServerCatalogDatabase();
        await database.ResetAsync();
        return database;
    }

    public ApplicationDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString, sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
                sqlServerOptions.CommandTimeout(60);
                sqlServerOptions.UseCompatibilityLevel(120);
            });

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    public async Task ExecuteAsync(Action<ApplicationDbContext> action)
    {
        await using var context = CreateContext();
        action(context);
        await context.SaveChangesAsync();
    }

    public async Task ExecuteAsync(Func<ApplicationDbContext, Task> action)
    {
        await using var context = CreateContext();
        await action(context);
    }

    public async ValueTask DisposeAsync()
    {
        await DeleteAsync();
    }

    private async Task ResetAsync()
    {
        await DeleteAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    private async Task DeleteAsync()
    {
        SqlConnection.ClearAllPools();

        var builder = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "master"
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        var commandText = $"""
IF DB_ID(N'{_databaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{_databaseName}];
END
""";

        await using var command = new SqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }
}
