using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
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
        "BusinessVerticals",
        "BusinessUserAssignments",
        "CompanyBusinessVerticals",
        "Companies",
        "InternalServiceIdempotencyRecords",
        "InternalServiceNonces",
        "ServiceCategories",
        "ServiceOfferings",
        "WorkOrderItems",
        "WorkOrders",
        "WorkOrderSelections",
        "VehicleWorkOrderDetails"
    ];

    [Fact]
    public void MigrationAssembly_ContainsCleanInitialAndVerticalReadinessMigration()
    {
        using var context = CreateContext("Step16BusinessMigrationMetadata");

        context.Database.GetMigrations().Should().HaveCount(2);
        context.Database.GetMigrations().First().Should().EndWith("_InitialBusinessDatabase");
        context.Database.GetMigrations().Last().Should().EndWith("_AddBusinessVerticalReadiness");
    }

    [Fact]
    public async Task CleanInitialMigration_AppliesFromEmptyLocalDb_HasExactInventory_AndRepeatsAsNoOp()
    {
        var databaseName = $"GhseeliBusinessStep16_{Guid.NewGuid():N}";
        await using var context = CreateContext(databaseName);

        try
        {
            await context.Database.EnsureDeletedAsync();
            (await context.Database.GetPendingMigrationsAsync()).Should().HaveCount(2);

            await context.Database.MigrateAsync();

            (await context.Database.GetAppliedMigrationsAsync()).Should().HaveCount(2);
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

    [Fact]
    public async Task VerticalReadinessMigration_BackfillsExistingCompaniesCategoriesAndVehicleOrders()
    {
        var databaseName = $"GhseeliBusinessVerticalUpgrade_{Guid.NewGuid():N}";
        await using var context = CreateContext(databaseName);
        var companyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        try
        {
            await context.Database.EnsureDeletedAsync();
            var initialMigration = context.Database.GetMigrations().First();
            await context.GetService<IMigrator>().MigrateAsync(initialMigration);

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                    INSERT INTO [dbo].[Companies]
                        ([Id], [NameAr], [IsActive], [CatalogVersion], [CreatedAt])
                    VALUES
                        ({companyId}, N'Existing car wash', 1, 1, SYSUTCDATETIME());

                    INSERT INTO [dbo].[ServiceCategories]
                        ([Id], [CompanyId], [NameAr], [DisplayOrder], [IsActive], [CreatedAt])
                    VALUES
                        ({categoryId}, {companyId}, N'Existing category', 0, 1, SYSUTCDATETIME());

                    INSERT INTO [dbo].[AppointmentReservations]
                        ([Id], [PublicId], [CustomerBookingReference], [OrderGuid], [RequestHash],
                         [BranchId], [CatalogVersion], [Currency], [ItemSubtotal],
                         [TotalDurationMinutes], [RequestedSlotStartUtc], [RequestedSlotEndUtc],
                         [Status], [StatusSequence], [StatusChangedAtUtc], [CreatedAtUtc])
                    VALUES
                        ({reservationId}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                         {new string('a', 64)}, {Guid.NewGuid()}, 1, 'ILS', 50, 30,
                         SYSUTCDATETIME(), DATEADD(minute, 30, SYSUTCDATETIME()),
                         'Pending', 0, SYSUTCDATETIME(), SYSUTCDATETIME());

                    INSERT INTO [dbo].[WorkOrders]
                        ([Id], [PublicId], [AppointmentReservationId], [Status], [CustomerName],
                         [VehicleType], [LicensePlate], [VehicleMake], [VehicleModel], [VehicleColor],
                         [AddressLine], [Latitude], [Longitude], [CreatedAtUtc])
                    VALUES
                        ({workOrderId}, {Guid.NewGuid()}, {reservationId}, 'Pending', N'Existing customer',
                         'SUV', '12-345-67', 'Toyota', 'RAV4', 'Blue',
                         N'Existing address', 32.085300, 34.781800, SYSUTCDATETIME());
                    """);

            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();

            (await context.BusinessVerticals.SingleAsync()).Code
                .Should().Be(BusinessVerticalDefaults.CarWashCode);
            (await context.CompanyBusinessVerticals.SingleAsync()).Should().Match<CompanyBusinessVertical>(
                assignment =>
                    assignment.CompanyId == companyId &&
                    assignment.BusinessVerticalId == BusinessVerticalDefaults.CarWashId &&
                    assignment.IsPrimary &&
                    assignment.IsActive);
            (await context.ServiceCategories.SingleAsync()).BusinessVerticalId
                .Should().Be(BusinessVerticalDefaults.CarWashId);
            (await context.AppointmentReservations.SingleAsync()).BusinessVerticalCode
                .Should().Be(BusinessVerticalDefaults.CarWashCode);

            var workOrder = await context.WorkOrders.SingleAsync();
            workOrder.BusinessVerticalCode.Should().Be(BusinessVerticalDefaults.CarWashCode);
            workOrder.VehicleType.Should().Be("SUV");
            workOrder.LicensePlate.Should().Be("12-345-67");
            workOrder.VehicleMake.Should().Be("Toyota");
            workOrder.VehicleModel.Should().Be("RAV4");
            workOrder.VehicleColor.Should().Be("Blue");
        }
        finally
        {
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
