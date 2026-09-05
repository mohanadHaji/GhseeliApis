using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GhseeliApis.Tests;

/// <summary>
/// Frozen Step 16 clean Customer migration and SQL catalog checks.
/// </summary>
public sealed class Step16CustomerSchemaMigrationTests
{
    private const string DatabaseName = "Ghseeli_Step16_CustomerSchema_Red";
    private const string MasterConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly string[] ExpectedTables =
    [
        "__EFMigrationsHistory", "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims",
        "AspNetUserLogins", "AspNetUserRoles", "AspNetUsers", "AspNetUserTokens",
        "BookingConfirmationAttempts", "CatalogAddonChoices", "CatalogAddonGroups",
        "CatalogBranches", "CatalogCategories", "CatalogOfferings", "CatalogProviders",
        "CheckoutDraftItems", "CheckoutDraftPricingItemSnapshots",
        "CheckoutDraftPricingSelectionSnapshots", "CheckoutDraftPricingSnapshots",
        "CheckoutDraftSelections", "CheckoutDrafts", "CustomerBookingItems",
        "CustomerBookingSelections", "CustomerBookings", "CustomerConfigurations",
        "CustomerDevices", "CustomerInternalIdempotencyRecords",
        "CustomerInternalServiceNonces", "CustomerPaymentIdempotencyRecords",
        "CustomerPayments", "ProcessedBookingStatusMessages", "PaymentWebhookEvents",
        "UserAddresses", "Vehicles"
    ];

    private static readonly string[] RemovedNames =
    [
        "Companies", "CompanyAvailabilities", "Services", "ServiceOptions", "Bookings",
        "Payments", "Wallets", "WalletTransactions", "Notifications"
    ];

    [Fact]
    public void Customer_migration_assembly_retains_clean_initial_and_current_model()
    {
        using var context = CreateContext();
        var migrations = context.GetService<IMigrationsAssembly>().Migrations;
        var migrationNames = migrations.Values.Select(migration => migration.Name);

        migrationNames.Should().StartWith("InitialCustomerDatabase");
        migrationNames.Should().Contain("AddCustomerDeviceActiveState");
        migrations.Keys.Should().BeInAscendingOrder();
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public void Customer_clean_migration_sql_and_snapshot_contain_only_owned_names()
    {
        using var context = CreateContext();
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: null,
            toMigration: null,
            MigrationsSqlGenerationOptions.Idempotent);

        foreach (var expectedTable in ExpectedTables)
        {
            script.Should().Contain(
                $"[{CustomerSchemaOptions.OwnedDefaultSchema}].[{expectedTable}]");
        }

        foreach (var removedName in RemovedNames)
        {
            script.Should().NotContain($"[{removedName}]");
            script.Should().NotContain($".Models.{removedName.TrimEnd('s')}");
        }

        script.ToUpperInvariant().Should().NotContain("POMELO");
        script.ToUpperInvariant().Should().NotContain("MYSQL");
        script.Should().Contain(
            "[InitializationState] IN (''NotStarted'',''Initialized'',''Ambiguous'',''Legacy'')");
        script.Should().Contain(
            "([Provider] = ''Lahza'' AND [Currency] IN (''ILS'',''JOD'',''USD'')) OR " +
            "([Provider] = ''Stripe'' AND [Currency] IN (''ILS'',''USD'',''EUR''))");
        script.Should().Contain(
            "PRIMARY KEY ([Provider], [EventId])");
    }

    [Fact]
    public async Task Customer_initial_migration_applies_from_empty_localdb_and_repeat_is_no_op()
    {
        await DropDatabaseAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();

            var firstHistory = await ReadStringsAsync(
                context, "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId]");
            var firstTables = await ReadStringsAsync(
                context,
                """
                SELECT s.[name] + '.' + t.[name]
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.[schema_id] = t.[schema_id]
                ORDER BY s.[name], t.[name]
                """);

            firstHistory.Should().HaveCount(4);
            firstTables.Order(StringComparer.Ordinal)
                .Should().Equal(ExpectedTables
                    .Select(table => $"{CustomerSchemaOptions.OwnedDefaultSchema}.{table}")
                    .Order(StringComparer.Ordinal));
            firstTables.Should().NotIntersectWith(RemovedNames.Select(table =>
                $"{CustomerSchemaOptions.OwnedDefaultSchema}.{table}"));
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

            var firstCatalog = await ReadCatalogSignatureAsync(context);
            await context.Database.MigrateAsync();
            var secondHistory = await ReadStringsAsync(
                context, "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId]");
            var secondCatalog = await ReadCatalogSignatureAsync(context);

            secondHistory.Should().Equal(firstHistory);
            secondCatalog.Should().Equal(firstCatalog);
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task Customer_vertical_snapshot_migration_backfills_populated_database()
    {
        await DropDatabaseAsync();
        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20260824230024_AddCustomerDeviceActiveState");
            var providerId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var bookingId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[AspNetUsers]
                    ([Id], [FullName], [Email], [IsActive], [EmailConfirmed], [PhoneNumberConfirmed],
                     [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [CreatedAt])
                VALUES
                    ({userId}, {"Customer"}, {"customer@example.test"}, {true}, {false}, {false},
                     {false}, {false}, {0}, {now.UtcDateTime});

                INSERT INTO [dbo].[CatalogProviders]
                    ([Id], [SourceCompanyId], [IsEnabled], [DisplayOrder], [NameAr],
                     [CatalogVersion])
                VALUES
                    ({providerId}, {Guid.NewGuid()}, {true}, {0}, {"مغسلة"}, {7L});

                INSERT INTO [dbo].[CustomerBookings]
                    ([Id], [PublicReference], [OrderGuid], [UserId], [OwnerDeviceId],
                     [BusinessReservationId], [BusinessWorkOrderId], [BusinessSourceId],
                     [BranchSourceId], [CatalogVersion], [ConfirmedDraftVersion], [Status],
                     [BusinessStatusSequence], [StatusChangedAtUtc], [RequestedSlotStartUtc],
                     [RequestedSlotEndUtc], [ProviderNameAr], [BranchNameAr], [VehicleType],
                     [AddressLine], [Latitude], [Longitude], [Currency], [BaseSubtotal],
                     [AddonSubtotal], [ItemSubtotal], [ServiceFee], [ServiceFeeMode],
                     [ServiceFeeFlatAmount], [ServiceFeePercentageRate], [TaxableSubtotal],
                     [TaxRatePercent], [TaxAppliesToServiceFee], [Tax], [GrandTotal],
                     [IsPaid], [PaymentState], [TotalDurationMinutes], [QuotedAtUtc],
                     [CreatedAtUtc])
                VALUES
                    ({bookingId}, {Guid.NewGuid()}, {Guid.NewGuid()}, {userId}, {Guid.NewGuid()},
                     {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                     {7L}, {1}, {"Pending"}, {0L}, {now}, {now.AddHours(1)},
                     {now.AddHours(2)}, {"مغسلة"}, {"فرع"}, {"SUV"}, {"Street"},
                     {32.1m}, {34.8m}, {"ILS"}, {100m}, {10m}, {110m}, {5m},
                     {"Flat"}, {5m}, {0m}, {115m}, {0m}, {false}, {0m}, {115m},
                     {false}, {"Unpaid"}, {45}, {now}, {now});
                """);

            await context.Database.MigrateAsync();

            var values = await ReadStringsAsync(
                context,
                """
                SELECT 'B:' + LOWER(CONVERT(varchar(36), [Id])) + ':' + [BusinessVerticalCode]
                FROM [dbo].[CustomerBookings]
                UNION ALL
                SELECT 'P:' + LOWER(CONVERT(varchar(36), [Id])) + ':' + [BusinessVerticalCode]
                FROM [dbo].[CatalogProviders]
                ORDER BY 1
                """);
            values.Should().Equal(
                $"B:{bookingId:D}:{BusinessVerticalSnapshotDefaults.CarWashCode}",
                $"P:{providerId:D}:{BusinessVerticalSnapshotDefaults.CarWashCode}");
            await context.Database.MigrateAsync();
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task Customer_initial_migration_uses_dbo_for_non_dbo_default_principal()
    {
        const string principalName = "Step16CustomerNonDboPrincipal";
        const string principalSchema = "customer_probe";

        await DropDatabaseAsync();
        try
        {
            await CreateDatabaseAsync();
            await using var connection = new SqlConnection(
                new SqlConnectionStringBuilder(MasterConnection)
                {
                    InitialCatalog = DatabaseName
                }.ConnectionString);
            await connection.OpenAsync();

            foreach (var sql in new[]
            {
                $"CREATE SCHEMA [{principalSchema}]",
                $"""
                 CREATE USER [{principalName}] WITHOUT LOGIN
                     WITH DEFAULT_SCHEMA = [{principalSchema}]
                 """,
                $"ALTER ROLE [db_owner] ADD MEMBER [{principalName}]",
                $"EXECUTE AS USER = '{principalName}'"
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connection, sqlServerOptions =>
                    sqlServerOptions.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        CustomerSchemaOptions.OwnedDefaultSchema))
                .Options;
            await using (var context = new ApplicationDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    REVERT;
                    SELECT s.[name] + '.' + t.[name]
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON s.[schema_id] = t.[schema_id]
                    ORDER BY s.[name], t.[name];
                    """;
                var tables = new List<string>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }

                tables.Order(StringComparer.Ordinal).Should().Equal(ExpectedTables
                    .Select(table => $"{CustomerSchemaOptions.OwnedDefaultSchema}.{table}")
                    .Order(StringComparer.Ordinal));
                tables.Should().NotContain(table =>
                    table.StartsWith($"{principalSchema}.", StringComparison.Ordinal));
            }
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task Customer_sql_catalog_indexes_foreign_keys_checks_and_precision_match_exact_ef_allowlist()
    {
        await DropDatabaseAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            var model = context.GetService<IDesignTimeModel>().Model;

            var expectedIndexes = model.GetEntityTypes()
                .SelectMany(entity => entity.GetIndexes())
                .Select(index => index.GetDatabaseName())
                .Where(name => name is not null)
                .Order(StringComparer.Ordinal);
            var expectedForeignKeys = model.GetEntityTypes()
                .SelectMany(entity => entity.GetForeignKeys())
                .Select(foreignKey => foreignKey.GetConstraintName())
                .Where(name => name is not null)
                .Order(StringComparer.Ordinal);
            var expectedChecks = model.GetEntityTypes()
                .SelectMany(entity => entity.GetCheckConstraints())
                .Select(check => check.Name)
                .Order(StringComparer.Ordinal);

            (await ReadStringsAsync(context,
                """
                SELECT i.[name]
                FROM sys.indexes i
                INNER JOIN sys.tables t ON t.[object_id] = i.[object_id]
                WHERE i.[name] IS NOT NULL
                  AND i.[is_primary_key] = 0
                  AND t.[is_ms_shipped] = 0
                ORDER BY i.[name]
                """))
                .Order(StringComparer.Ordinal).Should().Equal(expectedIndexes);
            (await ReadStringsAsync(context,
                "SELECT [name] FROM sys.foreign_keys ORDER BY [name]"))
                .Order(StringComparer.Ordinal).Should().Equal(expectedForeignKeys);
            (await ReadStringsAsync(context,
                "SELECT [name] FROM sys.check_constraints ORDER BY [name]"))
                .Order(StringComparer.Ordinal).Should().Equal(expectedChecks);

            var expectedDecimals = model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties()
                    .Where(property => property.GetPrecision() is not null)
                    .Select(property =>
                        $"{entity.GetTableName()}.{property.GetColumnName()}:{property.GetPrecision()},{property.GetScale()}"))
                .Order(StringComparer.Ordinal);
            var actualDecimals = await ReadStringsAsync(context, """
                SELECT t.[name] + '.' + c.[name] + ':' +
                       CONVERT(varchar(3), c.[precision]) + ',' + CONVERT(varchar(3), c.[scale])
                FROM sys.columns c
                INNER JOIN sys.tables t ON t.[object_id] = c.[object_id]
                WHERE c.[system_type_id] IN (106, 108)
                ORDER BY t.[name], c.[name]
                """);
            actualDecimals.Order(StringComparer.Ordinal).Should().Equal(expectedDecimals);
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    private static ApplicationDbContext CreateContext()
    {
        var connection = new SqlConnectionStringBuilder(MasterConnection)
        {
            InitialCatalog = DatabaseName
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection, sqlServerOptions =>
                sqlServerOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CustomerSchemaOptions.OwnedDefaultSchema))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task CreateDatabaseAsync()
    {
        await using var connection = new SqlConnection(MasterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{DatabaseName}];";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync()
    {
        await using var connection = new SqlConnection(MasterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ReadStringsAsync(
        ApplicationDbContext context,
        string sql)
    {
        var values = new List<string>();
        var connection = context.Database.GetDbConnection();
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

        return values.ToArray();
    }

    private static Task<string[]> ReadCatalogSignatureAsync(ApplicationDbContext context) =>
        ReadStringsAsync(context, """
            SELECT 'T:' + [name] FROM sys.tables
            UNION ALL
            SELECT 'I:' + [name] FROM sys.indexes WHERE [name] IS NOT NULL
            UNION ALL
            SELECT 'F:' + [name] FROM sys.foreign_keys
            UNION ALL
            SELECT 'C:' + [name] FROM sys.check_constraints
            ORDER BY 1
            """);
}
