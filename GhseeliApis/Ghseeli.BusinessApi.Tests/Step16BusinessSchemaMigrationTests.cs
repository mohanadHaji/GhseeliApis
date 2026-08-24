using FluentAssertions;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ghseeli.BusinessApi.Tests;

/// <summary>
/// Requires one clean Business initial migration that is repeatable on an isolated empty database.
/// </summary>
public class Step16BusinessSchemaMigrationTests
{
    private static readonly string[] ExpectedTables =
    [
        "AddonChoices",
        "AddonGroups",
        "AppointmentReservations",
        "AspNetRoleClaims",
        "AspNetRoles",
        "AspNetUserClaims",
        "AspNetUserLogins",
        "AspNetUserRoles",
        "AspNetUsers",
        "AspNetUserTokens",
        "BookingStatusOutboxMessages",
        "BookingStatusRequeueHistory",
        "Branches",
        "BranchAvailabilityOverrides",
        "BranchAvailabilitySettings",
        "BranchRecurringSchedules",
        "BranchServiceAreas",
        "BusinessUserAssignments",
        "Companies",
        "InternalServiceIdempotencyRecords",
        "InternalServiceNonces",
        "ServiceCategories",
        "ServiceOfferings",
        "WorkOrderItems",
        "WorkOrders",
        "WorkOrderSelections"
    ];

    [Fact]
    public void MigrationAssembly_ContainsExactlyOneCleanBusinessInitialMigration()
    {
        using var context = CreateContext("Step16BusinessMigrationMetadata");

        context.Database.GetMigrations().Should().ContainSingle()
            .Which.Should().EndWith("_InitialBusinessDatabase");
    }

    [Fact]
    public async Task CleanInitialMigration_AppliesFromEmptyLocalDb_HasExactInventory_AndRepeatsAsNoOp()
    {
        var databaseName = $"GhseeliBusinessStep16_{Guid.NewGuid():N}";
        await using var context = CreateContext(databaseName);

        try
        {
            await context.Database.EnsureDeletedAsync();
            (await context.Database.GetPendingMigrationsAsync()).Should().ContainSingle();

            await context.Database.MigrateAsync();

            (await context.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
            context.Database.HasPendingModelChanges().Should().BeFalse();

            (await QueryNamesAsync(
                context,
                """
                SELECT CONCAT(SCHEMA_NAME([schema_id]), '|', [name])
                FROM sys.tables
                ORDER BY SCHEMA_NAME([schema_id]), [name]
                """)).Should().BeEquivalentTo(
                    ExpectedTables
                        .Append("__EFMigrationsHistory")
                        .Select(table => $"dbo|{table}"));

            var expectedIndexes = ExpectedModelIndexes(context);
            (await QueryNamesAsync(
                context,
                """
                SELECT CONCAT(t.[name], '|', i.[name], '|', IIF(i.[is_unique] = 1, 'True', 'False'))
                FROM sys.indexes i
                INNER JOIN sys.tables t ON t.[object_id] = i.[object_id]
                WHERE i.[is_primary_key] = 0
                  AND i.[is_hypothetical] = 0
                  AND t.[name] <> '__EFMigrationsHistory'
                ORDER BY t.[name], i.[name]
                """)).Should().BeEquivalentTo(expectedIndexes);

            var expectedConstraints = ExpectedModelConstraints(context);
            (await QueryNamesAsync(
                context,
                """
                SELECT CONCAT(t.[name], '|', kc.[name])
                FROM sys.key_constraints kc
                INNER JOIN sys.tables t ON t.[object_id] = kc.[parent_object_id]
                WHERE t.[name] <> '__EFMigrationsHistory'
                UNION ALL
                SELECT CONCAT(t.[name], '|', fk.[name])
                FROM sys.foreign_keys fk
                INNER JOIN sys.tables t ON t.[object_id] = fk.[parent_object_id]
                UNION ALL
                SELECT CONCAT(t.[name], '|', cc.[name])
                FROM sys.check_constraints cc
                INNER JOIN sys.tables t ON t.[object_id] = cc.[parent_object_id]
                """)).Should().BeEquivalentTo(expectedConstraints);

            var before = await QueryMigrationHistoryAsync(context);
            await context.Database.MigrateAsync();
            var after = await QueryMigrationHistoryAsync(context);

            after.Should().Equal(before);
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CleanInitialMigration_WithNonDboPrincipalDefault_StillCreatesOnlyDboTables()
    {
        const string nonOwnedSchema = "step16_non_dbo";
        var databaseName = $"GhseeliBusinessStep16Principal_{Guid.NewGuid():N}";
        await using var context = CreateContext(databaseName);
        var impersonating = false;

        try
        {
            await context.GetService<IRelationalDatabaseCreator>().CreateAsync();
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE SCHEMA [step16_non_dbo] AUTHORIZATION [dbo];");
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE USER [Step16BusinessMigrationPrincipal] WITHOUT LOGIN
                    WITH DEFAULT_SCHEMA = [step16_non_dbo];
                """);
            await context.Database.ExecuteSqlRawAsync(
                "ALTER ROLE [db_owner] ADD MEMBER [Step16BusinessMigrationPrincipal];");
            await context.Database.ExecuteSqlRawAsync(
                "EXECUTE AS USER = 'Step16BusinessMigrationPrincipal';");
            impersonating = true;

            (await QueryNamesAsync(context, "SELECT SCHEMA_NAME()"))
                .Should().Equal(nonOwnedSchema);

            await context.Database.MigrateAsync();

            (await QueryNamesAsync(
                context,
                """
                SELECT DISTINCT SCHEMA_NAME([schema_id])
                FROM sys.tables
                ORDER BY SCHEMA_NAME([schema_id])
                """)).Should().Equal(BusinessSchemaOptions.OwnedDefaultSchema);
            (await QueryNamesAsync(
                context,
                """
                SELECT CONCAT(SCHEMA_NAME([schema_id]), '|', [name])
                FROM sys.tables
                WHERE [name] = '__EFMigrationsHistory'
                """)).Should().Equal("dbo|__EFMigrationsHistory");
        }
        finally
        {
            if (impersonating)
            {
                await context.Database.ExecuteSqlRawAsync("REVERT;");
            }
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static BusinessDbContext CreateContext(string databaseName)
    {
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlServer(
                connectionString,
                sqlServer => sqlServer.MigrationsHistoryTable(
                    BusinessSchemaOptions.MigrationsHistoryTable,
                    BusinessSchemaOptions.OwnedDefaultSchema))
            .Options;
        return new BusinessDbContext(options);
    }

    private static async Task<IReadOnlyList<string>> QueryNamesAsync(
        BusinessDbContext context,
        string sql)
    {
        var values = new List<string>();
        var connection = (SqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static Task<IReadOnlyList<string>> QueryMigrationHistoryAsync(BusinessDbContext context) =>
        QueryNamesAsync(
            context,
            "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId]");

    private static IEnumerable<string> ExpectedModelIndexes(BusinessDbContext context) =>
        context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetIndexes())
            .Select(index =>
                $"{index.DeclaringEntityType.GetTableName()}|{index.GetDatabaseName()}|{index.IsUnique}");

    private static IEnumerable<string> ExpectedModelConstraints(BusinessDbContext context)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var primaryKeys = model.GetEntityTypes()
            .Select(entity => entity.FindPrimaryKey())
            .Where(key => key is not null)
            .Select(key =>
                $"{key!.DeclaringEntityType.GetTableName()}|{key.GetName()}");
        var alternateKeys = model.GetEntityTypes()
            .SelectMany(entity => entity.GetKeys().Where(key => !key.IsPrimaryKey()))
            .Select(key =>
                $"{key.DeclaringEntityType.GetTableName()}|{key.GetName()}");
        var foreignKeys = model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Select(foreignKey =>
                $"{foreignKey.DeclaringEntityType.GetTableName()}|{foreignKey.GetConstraintName()}");
        var checks = model.GetEntityTypes()
            .SelectMany(entity => entity.GetCheckConstraints())
            .Select(check =>
                $"{check.EntityType.GetTableName()}|{check.Name}");

        return primaryKeys.Concat(alternateKeys).Concat(foreignKeys).Concat(checks);
    }
}
