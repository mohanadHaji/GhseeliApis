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
    private readonly string _databaseName =
        $"Ghseeli_Step16_CustomerSchema_{Guid.NewGuid():N}";
    private const string MasterConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly string[] ExpectedTables =
    [
        "__EFMigrationsHistory", "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims",
        "AspNetUserLogins", "AspNetUserRoles", "AspNetUsers", "AspNetUserTokens",
        "Banners", "BookingConfirmationAttempts", "BusinessFavourites", "BusinessReviews",
        "CatalogAddonChoices", "CatalogAddonGroups",
        "CatalogBranches", "CatalogCategories", "CatalogOfferings", "CatalogProviders",
        "CheckoutDraftItems", "CheckoutDraftPricingItemSnapshots",
        "CheckoutDraftPricingSelectionSnapshots", "CheckoutDraftPricingSnapshots",
        "CheckoutDraftSelections", "CheckoutDrafts", "CustomerBookingItems",
        "CustomerBookingSelections", "CustomerBookings", "CustomerConfigurations",
        "CustomerDevices", "CustomerInternalIdempotencyRecords",
        "CustomerInternalServiceNonces", "CustomerOtpChallenges",
        "CustomerPaymentIdempotencyRecords", "CustomerPayments",
        "CustomerRefreshTokens", "ProcessedBookingStatusMessages", "PaymentWebhookEvents",
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
        migrationNames.Should().Contain("AddCustomerDeviceOwnership");
        migrationNames.Should().Contain("AddCustomerDemoDataPartition");
        migrationNames.Should().Contain("AddCustomerOfferingPresentationMetadata");
        migrationNames.Should().Contain("AddCustomerVehicleContractsAndImages");
        migrationNames.Should().Contain("AddCustomerCategoryPresentationMetadata");
        migrationNames.Should().Contain("MakeCustomerCatalogSourceIdentityPartitionSafe");
        migrationNames.Should().Contain("AddCustomerCompletedBookingBusinessReviews");
        migrationNames.Should().Contain("AddCustomerBusinessFavourites");
        migrationNames.Should().Contain("AddCustomerBanners");
        migrationNames.Should().Contain("AddCustomerBusinessVerticalProjection");
        migrations.Keys.Should().BeInAscendingOrder();
        context.Database.HasPendingModelChanges().Should().BeFalse();

        var script = context.GetService<IMigrator>().GenerateScript();
        script.Should().Contain("[CatalogCategories]");
        script.Should().Contain("[ColorHex]");
        script.Should().Contain("[ImageUrl]");
        script.Should().Contain("nvarchar(7)");
        script.Should().Contain("nvarchar(500)");
        script.Should().Contain("[BusinessReviews]");
        script.Should().Contain("CK_BusinessReviews_Rating");
        script.Should().Contain("IX_BusinessReviews_BusinessSourceId_IsDemo_CreatedAtUtc_Id");
        script.Should().Contain("[BusinessFavourites]");
        script.Should().Contain("IX_BusinessFavourites_UserId_BusinessSourceId_IsDemo");
        script.Should().Contain("[Banners]");
        script.Should().Contain("CK_Banners_DisplayOrder");
        script.Should().Contain("IX_Banners_IsDemo_IsActive_DisplayOrder_Id");
        script.Should().Contain("[BusinessVerticalId]");
        script.Should().Contain("[BusinessVerticalNameAr]");
        script.Should().Contain(
            "IX_CatalogProviders_BusinessVerticalId_IsEnabled_BusinessVerticalDisplayOrder");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-REVIEW-MIGRATION-017")]
    public async Task BusinessReviewMigration_UpAndDown_are_executable_and_preserve_unrelated_rows()
    {
        await DropDatabaseAsync();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var prior = context.Database.GetMigrations().Single(value =>
                value.EndsWith("_MakeCustomerCatalogSourceIdentityPartitionSafe"));
            var migration = context.Database.GetMigrations().Single(value =>
                value.EndsWith("_AddCustomerCompletedBookingBusinessReviews"));
            await migrator.MigrateAsync(prior);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[AspNetUsers]
                    ([Id],[FullName],[Email],[IsActive],[EmailConfirmed],[PhoneNumberConfirmed],
                     [TwoFactorEnabled],[LockoutEnabled],[AccessFailedCount],[CreatedAt],[IsDemo])
                VALUES ({userId},{"Review migration user"},{"review-migration@example.test"},{true},
                    {false},{false},{false},{false},{0},{now.UtcDateTime},{false});
                INSERT INTO [dbo].[CustomerDevices]
                    ([Id],[InstallationId],[Platform],[TokenHash],[CreatedAt],[UpdatedAt],
                     [ExpiresAt],[IsActive],[UserId],[IsDemo])
                VALUES ({deviceId},{Guid.NewGuid()},{"Android"},{Enumerable.Repeat((byte)31, 32).ToArray()},
                    {now},{now},{now.AddDays(30)},{true},{userId},{false});
                INSERT INTO [dbo].[CustomerBookings]
                    ([Id],[PublicReference],[OrderGuid],[UserId],[OwnerDeviceId],
                     [BusinessReservationId],[BusinessWorkOrderId],[BusinessSourceId],
                     [BranchSourceId],[CatalogVersion],[ConfirmedDraftVersion],[Status],
                     [BusinessStatusSequence],[StatusChangedAtUtc],[RequestedSlotStartUtc],
                     [RequestedSlotEndUtc],[ProviderNameAr],[BranchNameAr],[VehicleType],
                     [AddressLine],[Latitude],[Longitude],[Currency],[BaseSubtotal],
                     [AddonSubtotal],[ItemSubtotal],[ServiceFee],[ServiceFeeMode],
                     [ServiceFeeFlatAmount],[ServiceFeePercentageRate],[TaxableSubtotal],
                     [TaxRatePercent],[TaxAppliesToServiceFee],[Tax],[GrandTotal],
                     [IsPaid],[PaymentState],[TotalDurationMinutes],[QuotedAtUtc],
                     [CreatedAtUtc],[BusinessVerticalCode],[IsDemo])
                VALUES ({bookingId},{Guid.NewGuid()},{Guid.NewGuid()},{userId},{deviceId},
                    {Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},
                    {1L},{1},{"Completed"},{0L},{now},{now.AddHours(1)},{now.AddHours(2)},
                    {"Provider"},{"Branch"},{"Sedan"},{"Street"},{32.1m},{34.8m},
                    {"ILS"},{100m},{0m},{100m},{0m},{"None"},{0m},{0m},{100m},
                    {0m},{false},{0m},{100m},{false},{"Unpaid"},{30},{now},{now},
                    {BusinessVerticalSnapshotDefaults.CarWashCode},{false});
                """);

            await migrator.MigrateAsync(migration);
            (await ReadStringsAsync(context, """
                SELECT c.[name] + '|' + TYPE_NAME(c.[user_type_id]) + '|' +
                       CONVERT(varchar(5),c.[max_length]) + '|' +
                       CONVERT(varchar(1),c.[is_nullable])
                FROM sys.columns c
                WHERE c.[object_id] = OBJECT_ID('[dbo].[BusinessReviews]')
                ORDER BY c.[column_id]
                """)).Should().Contain(
                    "Comment|nvarchar|2000|1",
                    "RowVersion|timestamp|8|0",
                    "CustomerBookingId|uniqueidentifier|16|0",
                    "UserId|uniqueidentifier|16|0",
                    "IsDemo|bit|1|0");
            (await ReadStringsAsync(context, """
                SELECT i.[name] + '|' + CONVERT(varchar(1),i.[is_unique])
                FROM sys.indexes i
                WHERE i.[object_id] = OBJECT_ID('[dbo].[BusinessReviews]')
                  AND i.[name] IS NOT NULL
                ORDER BY i.[name]
                """)).Should().Contain(
                    "IX_BusinessReviews_CustomerBookingId|1",
                    "IX_BusinessReviews_BusinessSourceId_IsDemo_CreatedAtUtc_Id|0");
            (await ReadStringsAsync(context, """
                SELECT [name] FROM sys.foreign_keys
                WHERE parent_object_id = OBJECT_ID('[dbo].[BusinessReviews]')
                ORDER BY [name]
                """)).Should().Equal(
                    "FK_BusinessReviews_AspNetUsers_UserId",
                    "FK_BusinessReviews_CustomerBookings_CustomerBookingId");
            (await ReadStringsAsync(context, """
                SELECT [name] FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID('[dbo].[BusinessReviews]')
                """)).Should().Equal("CK_BusinessReviews_Rating");
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[BusinessReviews]
                    ([Id],[CustomerBookingId],[UserId],[BusinessSourceId],[Rating],
                     [Comment],[CreatedAtUtc],[UpdatedAtUtc],[IsDemo])
                SELECT {reviewId},{bookingId},{userId},[BusinessSourceId],{5},
                       NULL,{now},{now},{false}
                FROM [dbo].[CustomerBookings] WHERE [Id] = {bookingId};
                """);
            (await ReadStringsAsync(context, $"""
                SELECT CONCAT(DATALENGTH([RowVersion]),'|',[Rating],'|',
                    CASE WHEN [Comment] IS NULL THEN 'null' ELSE 'value' END)
                FROM [dbo].[BusinessReviews] WHERE [Id] = '{reviewId:D}'
                """)).Should().Equal("8|5|null");
            (await ReadStringsAsync(context, $"""
                SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] = '{migration}'
                """)).Should().Equal(migration);

            await migrator.MigrateAsync(prior);
            (await ReadStringsAsync(context, """
                SELECT [name] FROM sys.tables WHERE [name] = 'BusinessReviews'
                """)).Should().BeEmpty();
            (await ReadStringsAsync(context,
                "SELECT CONVERT(varchar(10),COUNT(*)) FROM [dbo].[CustomerBookings]"))
                .Should().Equal("1");
            (await ReadStringsAsync(context, $"""
                SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] = '{migration}'
                """)).Should().BeEmpty();
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task BusinessFavouriteMigration_UpAndDown_enforces_fk_indexes_and_preserves_users()
    {
        await DropDatabaseAsync();
        var userId = Guid.NewGuid();
        var favouriteId = Guid.NewGuid();
        var businessSourceId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 24, 16, 0, 0, TimeSpan.Zero);
        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var prior = context.Database.GetMigrations().Single(value =>
                value.EndsWith("_AddCustomerCompletedBookingBusinessReviews"));
            var migration = context.Database.GetMigrations().Single(value =>
                value.EndsWith("_AddCustomerBusinessFavourites"));
            await migrator.MigrateAsync(prior);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[AspNetUsers]
                    ([Id],[FullName],[Email],[IsActive],[EmailConfirmed],[PhoneNumberConfirmed],
                     [TwoFactorEnabled],[LockoutEnabled],[AccessFailedCount],[CreatedAt],[IsDemo])
                VALUES ({userId},{"Favourite migration user"},
                    {"favourite-migration@example.test"},{true},{false},{false},{false},
                    {false},{0},{now.UtcDateTime},{false});
                """);

            await migrator.MigrateAsync(migration);
            (await ReadStringsAsync(context, """
                SELECT c.[name] + '|' + TYPE_NAME(c.[user_type_id]) + '|' +
                       CONVERT(varchar(5),c.[max_length]) + '|' +
                       CONVERT(varchar(1),c.[is_nullable])
                FROM sys.columns c
                WHERE c.[object_id] = OBJECT_ID('[dbo].[BusinessFavourites]')
                ORDER BY c.[column_id]
                """)).Should().Contain(
                    "Id|uniqueidentifier|16|0",
                    "UserId|uniqueidentifier|16|0",
                    "BusinessSourceId|uniqueidentifier|16|0",
                    "CreatedAtUtc|datetimeoffset|10|0",
                    "IsDemo|bit|1|0");
            (await ReadStringsAsync(context, """
                SELECT i.[name] + '|' + CONVERT(varchar(1),i.[is_unique])
                FROM sys.indexes i
                WHERE i.[object_id] = OBJECT_ID('[dbo].[BusinessFavourites]')
                  AND i.[name] IS NOT NULL
                ORDER BY i.[name]
                """)).Should().Contain(
                    "IX_BusinessFavourites_BusinessSourceId_IsDemo|0",
                    "IX_BusinessFavourites_UserId_BusinessSourceId_IsDemo|1");
            (await ReadStringsAsync(context, """
                SELECT [name] FROM sys.foreign_keys
                WHERE parent_object_id = OBJECT_ID('[dbo].[BusinessFavourites]')
                """)).Should().Equal(
                    "FK_BusinessFavourites_AspNetUsers_UserId");

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[BusinessFavourites]
                    ([Id],[UserId],[BusinessSourceId],[CreatedAtUtc],[IsDemo])
                VALUES ({favouriteId},{userId},{businessSourceId},{now},{false});
                """);
            var duplicate = async () => await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[BusinessFavourites]
                    ([Id],[UserId],[BusinessSourceId],[CreatedAtUtc],[IsDemo])
                VALUES ({Guid.NewGuid()},{userId},{businessSourceId},{now},{false});
                """);
            (await duplicate.Should().ThrowAsync<SqlException>())
                .Which.Number.Should().BeOneOf(2601, 2627);
            var badForeignKey = async () => await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[BusinessFavourites]
                    ([Id],[UserId],[BusinessSourceId],[CreatedAtUtc],[IsDemo])
                VALUES ({Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},{now},{false});
                """);
            (await badForeignKey.Should().ThrowAsync<SqlException>())
                .Which.Number.Should().Be(547);

            await migrator.MigrateAsync(prior);
            (await ReadStringsAsync(context, """
                SELECT [name] FROM sys.tables WHERE [name] = 'BusinessFavourites'
                """)).Should().BeEmpty();
            (await ReadStringsAsync(context, $"""
                SELECT CONVERT(varchar(10),COUNT(*)) FROM [dbo].[AspNetUsers]
                WHERE [Id] = '{userId:D}'
                """)).Should().Equal("1");
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-MIGRATION-013")]
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
        script.Should().Contain("Suv5Seater");
        script.Should().Contain("Sedan");
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

            firstHistory.Should().HaveCount(15);
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
    public async Task OfferingPresentationMetadataMigration_PreservesRows_AddsNullableBoundedColumns_AndDowngrades()
    {
        await DropDatabaseAsync();
        var providerId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();

        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var priorMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddCustomerDemoDataPartition"));
            await migrator.MigrateAsync(priorMigration);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                        INSERT INTO [dbo].[CatalogProviders]
                            ([Id], [SourceCompanyId], [IsEnabled], [DisplayOrder], [NameAr],
                             [CatalogVersion], [BusinessVerticalCode], [IsDemo])
                        VALUES
                            ({providerId}, {Guid.NewGuid()}, 1, 0, N'Existing provider', 1,
                             {BusinessVerticalSnapshotDefaults.CarWashCode}, 0);

                        INSERT INTO [dbo].[CatalogCategories]
                            ([Id], [SourceCategoryId], [ProviderId], [NameAr], [DisplayOrder])
                        VALUES
                            ({categoryId}, {Guid.NewGuid()}, {providerId}, N'Existing category', 0);

                        INSERT INTO [dbo].[CatalogOfferings]
                            ([Id], [SourceOfferingId], [CategoryId], [NameAr], [BasePrice],
                             [DurationMinutes], [DisplayOrder])
                        VALUES
                            ({offeringId}, {Guid.NewGuid()}, {categoryId}, N'Existing offering',
                             50, 30, 0);
                    """);

            await context.Database.MigrateAsync();

            (await ReadStringsAsync(
                context,
                $"""
                    SELECT CONCAT(
                        CASE WHEN [QualifierAr] IS NULL THEN 'null' ELSE 'value' END, '|',
                        CASE WHEN [QualifierHe] IS NULL THEN 'null' ELSE 'value' END, '|',
                        CASE WHEN [BadgeCode] IS NULL THEN 'null' ELSE 'value' END)
                    FROM [dbo].[CatalogOfferings]
                    WHERE [Id] = '{offeringId:D}'
                    UNION ALL
                    SELECT CONCAT(c.[name], '|', TYPE_NAME(c.[user_type_id]), '|',
                        c.[max_length], '|', c.[is_nullable])
                    FROM sys.columns c
                    WHERE c.[object_id] = OBJECT_ID('[dbo].[CatalogOfferings]')
                      AND c.[name] IN ('QualifierAr', 'QualifierHe', 'BadgeCode')
                    ORDER BY 1
                """)).Should().BeEquivalentTo(
                    "null|null|null",
                    "BadgeCode|nvarchar|100|1",
                    "QualifierAr|nvarchar|400|1",
                    "QualifierHe|nvarchar|400|1");

            await migrator.MigrateAsync(priorMigration);

            (await ReadStringsAsync(
                context,
                $"""
                    SELECT [NameAr]
                    FROM [dbo].[CatalogOfferings]
                    WHERE [Id] = '{offeringId:D}'
                    UNION ALL
                    SELECT c.[name]
                    FROM sys.columns c
                    WHERE c.[object_id] = OBJECT_ID('[dbo].[CatalogOfferings]')
                      AND c.[name] IN ('QualifierAr', 'QualifierHe', 'BadgeCode')
                """)).Should().Equal("Existing offering");
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    [Trait("ScenarioId", "FAN-CATEGORY-MIGRATION-010")]
    public async Task CategoryPresentationMigration_PreservesPopulatedRows_AddsNullColumns_AndDowngrades()
    {
        await DropDatabaseAsync();
        var providerId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var priorMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddCustomerVehicleContractsAndImages"));
            await migrator.MigrateAsync(priorMigration);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                    INSERT INTO [dbo].[CatalogProviders]
                        ([Id], [SourceCompanyId], [IsEnabled], [DisplayOrder], [NameAr],
                         [CatalogVersion], [BusinessVerticalCode], [IsDemo])
                    VALUES
                        ({providerId}, {Guid.NewGuid()}, 1, 0, N'Existing provider', 1,
                         {BusinessVerticalSnapshotDefaults.CarWashCode}, 0);

                    INSERT INTO [dbo].[CatalogCategories]
                        ([Id], [SourceCategoryId], [ProviderId], [NameAr], [DescriptionAr], [DisplayOrder])
                    VALUES
                        ({categoryId}, {Guid.NewGuid()}, {providerId}, N'Existing category',
                         N'Existing description', 3);
                """);

            await migrator.MigrateAsync(
                context.Database.GetMigrations()
                    .Single(migration => migration.EndsWith("_AddCustomerCategoryPresentationMetadata")));

            (await ReadStringsAsync(
                context,
                $"""
                    SELECT CONCAT([NameAr], '|', [DescriptionAr], '|',
                        CASE WHEN [ImageUrl] IS NULL THEN 'null' ELSE 'value' END, '|',
                        CASE WHEN [ColorHex] IS NULL THEN 'null' ELSE 'value' END)
                    FROM [dbo].[CatalogCategories]
                    WHERE [Id] = '{categoryId:D}'
                    UNION ALL
                    SELECT CONCAT(c.[name], '|', TYPE_NAME(c.[user_type_id]), '|',
                        c.[max_length], '|', c.[is_nullable])
                    FROM sys.columns c
                    WHERE c.[object_id] = OBJECT_ID('[dbo].[CatalogCategories]')
                      AND c.[name] IN ('ImageUrl', 'ColorHex')
                    ORDER BY 1
                """)).Should().BeEquivalentTo(
                    "Existing category|Existing description|null|null",
                    "ColorHex|nvarchar|14|1",
                    "ImageUrl|nvarchar|1000|1");

            await migrator.MigrateAsync(priorMigration);

            (await ReadStringsAsync(
                context,
                $"""
                    SELECT CONCAT([NameAr], '|', [DescriptionAr])
                    FROM [dbo].[CatalogCategories]
                    WHERE [Id] = '{categoryId:D}'
                    UNION ALL
                    SELECT c.[name]
                    FROM sys.columns c
                    WHERE c.[object_id] = OBJECT_ID('[dbo].[CatalogCategories]')
                      AND c.[name] IN ('ImageUrl', 'ColorHex')
                """)).Should().Equal("Existing category|Existing description");
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    [Trait("ScenarioId", "FAN-CUSTOMER-SOURCE-IDENTITY-MIGRATION-014")]
    public async Task CustomerCatalogSourceIdentityMigration_UpgradesPopulatedLegacyRowsWithoutMutation()
    {
        await DropDatabaseAsync();
        var providerId = Guid.NewGuid();
        var sourceCompanyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var sourceCategoryId = Guid.NewGuid();

        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var priorMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddCustomerCategoryPresentationMetadata"));
            var sourceIdentityMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_MakeCustomerCatalogSourceIdentityPartitionSafe"));
            await migrator.MigrateAsync(priorMigration);
            await InsertCatalogProviderAndCategoryAsync(
                context,
                providerId,
                sourceCompanyId,
                false,
                categoryId,
                sourceCategoryId);

            await migrator.MigrateAsync(sourceIdentityMigration);

            (await ReadStringsAsync(
                context,
                $"""
                SELECT CONCAT(
                    LOWER(CONVERT(varchar(36), p.[Id])), '|',
                    LOWER(CONVERT(varchar(36), p.[SourceCompanyId])), '|',
                    p.[IsDemo], '|',
                    LOWER(CONVERT(varchar(36), c.[Id])), '|',
                    LOWER(CONVERT(varchar(36), c.[SourceCategoryId])), '|',
                    LOWER(CONVERT(varchar(36), c.[ProviderId])))
                FROM [dbo].[CatalogProviders] p
                INNER JOIN [dbo].[CatalogCategories] c ON c.[ProviderId] = p.[Id]
                WHERE p.[Id] = '{providerId:D}'
                UNION ALL
                SELECT i.[name]
                FROM sys.indexes i
                WHERE i.[name] IN (
                    'IX_CatalogProviders_SourceCompanyId_IsDemo',
                    'IX_CatalogCategories_ProviderId_SourceCategoryId')
                ORDER BY 1
                """)).Should().BeEquivalentTo(
                    $"{providerId:D}|{sourceCompanyId:D}|0|{categoryId:D}|{sourceCategoryId:D}|{providerId:D}",
                    "IX_CatalogProviders_SourceCompanyId_IsDemo",
                    "IX_CatalogCategories_ProviderId_SourceCategoryId");
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    [Trait("ScenarioId", "FAN-CUSTOMER-SOURCE-IDENTITY-MIGRATION-015")]
    public async Task CustomerCatalogSourceIdentityMigration_AllowsSameSourceIdsAcrossPartitionsAfterUp()
    {
        await DropDatabaseAsync();
        var sourceCompanyId = Guid.NewGuid();
        var sourceCategoryId = Guid.NewGuid();

        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var priorMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddCustomerCategoryPresentationMetadata"));
            var sourceIdentityMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_MakeCustomerCatalogSourceIdentityPartitionSafe"));
            await migrator.MigrateAsync(priorMigration);
            await InsertCatalogProviderAndCategoryAsync(
                context,
                Guid.NewGuid(),
                sourceCompanyId,
                false,
                Guid.NewGuid(),
                sourceCategoryId);
            await migrator.MigrateAsync(sourceIdentityMigration);

            await InsertCatalogProviderAndCategoryAsync(
                context,
                Guid.NewGuid(),
                sourceCompanyId,
                true,
                Guid.NewGuid(),
                sourceCategoryId);

            (await ReadStringsAsync(
                context,
                $"""
                SELECT CONCAT(
                    'providers:', COUNT(*), ':partitions:', COUNT(DISTINCT [IsDemo]))
                FROM [dbo].[CatalogProviders]
                WHERE [SourceCompanyId] = '{sourceCompanyId:D}'
                UNION ALL
                SELECT CONCAT('categories:', COUNT(*))
                FROM [dbo].[CatalogCategories]
                WHERE [SourceCategoryId] = '{sourceCategoryId:D}'
                ORDER BY 1
                """)).Should().Equal(
                    "categories:2",
                    "providers:2:partitions:2");
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Theory]
    [InlineData("SourceCompanyId")]
    [InlineData("SourceCategoryId")]
    [Trait("ScenarioId", "FAN-CUSTOMER-SOURCE-IDENTITY-MIGRATION-016")]
    public async Task CustomerCatalogSourceIdentityMigration_DownRejectsCrossPartitionDuplicatesWithoutMutation(
        string duplicateColumn)
    {
        await DropDatabaseAsync();
        var firstProviderId = Guid.NewGuid();
        var secondProviderId = Guid.NewGuid();
        var firstCategoryId = Guid.NewGuid();
        var secondCategoryId = Guid.NewGuid();
        var firstSourceCompanyId = Guid.NewGuid();
        var firstSourceCategoryId = Guid.NewGuid();

        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var priorMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddCustomerCategoryPresentationMetadata"));
            var sourceIdentityMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_MakeCustomerCatalogSourceIdentityPartitionSafe"));
            await migrator.MigrateAsync(priorMigration);
            await InsertCatalogProviderAndCategoryAsync(
                context,
                firstProviderId,
                firstSourceCompanyId,
                false,
                firstCategoryId,
                firstSourceCategoryId);
            await migrator.MigrateAsync(sourceIdentityMigration);
            await InsertCatalogProviderAndCategoryAsync(
                context,
                secondProviderId,
                duplicateColumn == "SourceCompanyId" ? firstSourceCompanyId : Guid.NewGuid(),
                true,
                secondCategoryId,
                duplicateColumn == "SourceCategoryId" ? firstSourceCategoryId : Guid.NewGuid());

            Func<Task> downgrade = () => migrator.MigrateAsync(priorMigration);

            var exception = await downgrade.Should().ThrowAsync<SqlException>();
            exception.Which.Number.Should().Be(51001);
            exception.Which.Message.Should().Contain(
                "Cannot downgrade customer catalog source identity: duplicate SourceCompanyId or SourceCategoryId values exist; global unique indexes cannot be restored.");
            (await ReadStringsAsync(
                context,
                $"""
                SELECT CONCAT('providers:', COUNT(*))
                FROM [dbo].[CatalogProviders]
                WHERE [Id] IN ('{firstProviderId:D}', '{secondProviderId:D}')
                UNION ALL
                SELECT CONCAT('categories:', COUNT(*))
                FROM [dbo].[CatalogCategories]
                WHERE [Id] IN ('{firstCategoryId:D}', '{secondCategoryId:D}')
                UNION ALL
                SELECT 'index:' + i.[name]
                FROM sys.indexes i
                WHERE i.[name] IN (
                    'IX_CatalogProviders_SourceCompanyId',
                    'IX_CatalogCategories_SourceCategoryId',
                    'IX_CatalogProviders_SourceCompanyId_IsDemo',
                    'IX_CatalogCategories_ProviderId_SourceCategoryId')
                UNION ALL
                SELECT 'migration:' + [MigrationId]
                FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] = '{sourceIdentityMigration}'
                ORDER BY 1
                """)).Should().Equal(
                    "categories:2",
                    "index:IX_CatalogCategories_ProviderId_SourceCategoryId",
                    "index:IX_CatalogProviders_SourceCompanyId_IsDemo",
                    $"migration:{sourceIdentityMigration}",
                    "providers:2");
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    [Trait("ScenarioId", "FAN-CUSTOMER-SOURCE-IDENTITY-MIGRATION-017")]
    public async Task CustomerCatalogSourceIdentityMigration_DownSucceedsWithoutDuplicatesAndPreservesRows()
    {
        await DropDatabaseAsync();
        var providerId = Guid.NewGuid();
        var sourceCompanyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var sourceCategoryId = Guid.NewGuid();

        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var priorMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddCustomerCategoryPresentationMetadata"));
            var sourceIdentityMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_MakeCustomerCatalogSourceIdentityPartitionSafe"));
            await migrator.MigrateAsync(priorMigration);
            await InsertCatalogProviderAndCategoryAsync(
                context,
                providerId,
                sourceCompanyId,
                false,
                categoryId,
                sourceCategoryId);
            await migrator.MigrateAsync(sourceIdentityMigration);

            await migrator.MigrateAsync(priorMigration);

            (await ReadStringsAsync(
                context,
                $"""
                SELECT CONCAT(
                    LOWER(CONVERT(varchar(36), p.[Id])), '|',
                    LOWER(CONVERT(varchar(36), p.[SourceCompanyId])), '|',
                    LOWER(CONVERT(varchar(36), c.[Id])), '|',
                    LOWER(CONVERT(varchar(36), c.[SourceCategoryId])))
                FROM [dbo].[CatalogProviders] p
                INNER JOIN [dbo].[CatalogCategories] c ON c.[ProviderId] = p.[Id]
                WHERE p.[Id] = '{providerId:D}'
                UNION ALL
                SELECT i.[name]
                FROM sys.indexes i
                WHERE i.[name] IN (
                    'IX_CatalogProviders_SourceCompanyId',
                    'IX_CatalogCategories_SourceCategoryId',
                    'IX_CatalogProviders_SourceCompanyId_IsDemo',
                    'IX_CatalogCategories_ProviderId_SourceCategoryId')
                ORDER BY 1
                """)).Should().BeEquivalentTo(
                    $"{providerId:D}|{sourceCompanyId:D}|{categoryId:D}|{sourceCategoryId:D}",
                    "IX_CatalogProviders_SourceCompanyId",
                    "IX_CatalogCategories_SourceCategoryId");
            (await ReadStringsAsync(
                context,
                $"""
                SELECT [MigrationId]
                FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] = '{sourceIdentityMigration}'
                """)).Should().BeEmpty();
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    [Trait("ScenarioId", "STEP24-DEVICE-MIGRATION-011")]
    public async Task Customer_device_ownership_migration_preserves_existing_devices_as_unowned()
    {
        await DropDatabaseAsync();
        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20260914202323_AddCustomerOtpRefreshAndFcm");
            var deviceId = Guid.NewGuid();
            var installationId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
            var tokenHash = Enumerable.Repeat((byte)24, 32).ToArray();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[CustomerDevices]
                    ([Id], [InstallationId], [Platform], [AppVersion], [FcmToken],
                     [TokenHash], [CreatedAt], [UpdatedAt], [LastSeenAt], [ExpiresAt],
                     [IsActive])
                VALUES
                    ({deviceId}, {installationId}, {"Android"}, {"1.0.0"}, {null},
                     {tokenHash}, {now}, {now}, {null}, {now.AddDays(30)}, {true});
                """);

            await context.Database.MigrateAsync();

            var values = await ReadStringsAsync(
                context,
                $"""
                SELECT
                    CASE WHEN [UserId] IS NULL THEN 'unowned' ELSE 'owned' END
                FROM [dbo].[CustomerDevices]
                WHERE [Id] = '{deviceId:D}'
                UNION ALL
                SELECT [name]
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID('[dbo].[CustomerDevices]')
                  AND [name] = 'IX_CustomerDevices_UserId'
                UNION ALL
                SELECT [name]
                FROM sys.foreign_keys
                WHERE [parent_object_id] = OBJECT_ID('[dbo].[CustomerDevices]')
                  AND [name] = 'FK_CustomerDevices_AspNetUsers_UserId'
                """);
            values.Should().BeEquivalentTo(
                "unowned",
                "IX_CustomerDevices_UserId",
                "FK_CustomerDevices_AspNetUsers_UserId");
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
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
                UNION ALL
                SELECT 'V:' + LOWER(CONVERT(varchar(36), [Id])) + ':' + [VehicleType]
                FROM [dbo].[CustomerBookings]
                ORDER BY 1
                """);
            values.Should().Equal(
                $"B:{bookingId:D}:{BusinessVerticalSnapshotDefaults.CarWashCode}",
                $"P:{providerId:D}:{BusinessVerticalSnapshotDefaults.CarWashCode}",
                $"V:{bookingId:D}:Suv5Seater");
            await context.Database.MigrateAsync();
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
        finally
        {
            await DropDatabaseAsync();
        }
    }

    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-MIGRATION-013")]
    public async Task CustomerVehicleMigration_NormalizesLegacyValues_AddsColumns_AndDowngradesInIsolation()
    {
        await DropDatabaseAsync();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        var types = new[] { "Car", "SUV", "Hovercraft", "Suv7Seater" };
        var expected = new[] { "Sedan", "Suv5Seater", "Sedan", "Suv7Seater" };

        try
        {
            await using var context = CreateContext();
            var migrator = context.GetService<IMigrator>();
            var priorMigration = context.Database.GetMigrations()
                .Single(migration => migration.EndsWith("_AddCustomerOfferingPresentationMetadata"));
            await migrator.MigrateAsync(priorMigration);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[AspNetUsers]
                    ([Id], [FullName], [Email], [IsActive], [EmailConfirmed], [PhoneNumberConfirmed],
                     [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [CreatedAt], [IsDemo])
                VALUES
                    ({userId}, {"Vehicle migration user"}, {"vehicle-migration@example.test"}, {true},
                     {false}, {false}, {false}, {false}, {0}, {now.UtcDateTime}, {false});

                INSERT INTO [dbo].[CustomerDevices]
                    ([Id], [InstallationId], [Platform], [TokenHash], [CreatedAt], [UpdatedAt],
                     [ExpiresAt], [IsActive], [UserId], [IsDemo])
                VALUES
                    ({deviceId}, {Guid.NewGuid()}, {"Android"}, {Enumerable.Repeat((byte)42, 32).ToArray()},
                     {now}, {now}, {now.AddDays(30)}, {true}, {userId}, {false});

                INSERT INTO [dbo].[Vehicles]
                    ([Id], [UserId], [Make], [Model], [Year], [LicensePlate], [Color])
                VALUES
                    ({vehicleId}, {userId}, {"Toyota"}, {"Corolla"}, {"2025"}, {"TEST-001"}, {"White"});
                """);

            for (var index = 0; index < types.Length; index++)
            {
                var draftId = Guid.NewGuid();
                var bookingId = Guid.NewGuid();
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [dbo].[CheckoutDrafts]
                        ([Id], [OrderGuid], [OwnerDeviceId], [BusinessSourceId], [BranchSourceId],
                         [CatalogVersion], [PublicVersion], [RequestedSlotStartUtc], [VehicleType],
                         [AddressLine], [Latitude], [Longitude], [RequiresReprice], [CreatedAt],
                         [UpdatedAt], [ExpiresAt], [IsDemo])
                    VALUES
                        ({draftId}, {Guid.NewGuid()}, {deviceId}, {Guid.NewGuid()}, {Guid.NewGuid()},
                         {1L}, {1}, {now.AddHours(1)}, {types[index]}, {"Street"}, {32.1m}, {34.8m},
                         {true}, {now}, {now}, {now.AddHours(2)}, {false});

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
                         [CreatedAtUtc], [BusinessVerticalCode], [IsDemo])
                    VALUES
                        ({bookingId}, {Guid.NewGuid()}, {Guid.NewGuid()}, {userId}, {deviceId},
                         {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                         {1L}, {1}, {"Pending"}, {0L}, {now}, {now.AddHours(1)}, {now.AddHours(2)},
                         {"Provider"}, {"Branch"}, {types[index]}, {"Street"}, {32.1m}, {34.8m},
                         {"ILS"}, {100m}, {0m}, {100m}, {0m}, {"Flat"}, {0m}, {0m}, {100m},
                         {0m}, {false}, {0m}, {100m}, {false}, {"Unpaid"}, {30}, {now}, {now},
                         {BusinessVerticalSnapshotDefaults.CarWashCode}, {false});
                    """);
            }

            await context.Database.MigrateAsync();

            (await ReadStringsAsync(
                context,
                "SELECT [VehicleType] FROM [dbo].[CheckoutDrafts] ORDER BY [CreatedAt], [Id]"))
                .Order(StringComparer.Ordinal)
                .Should().Equal(expected.Order(StringComparer.Ordinal));
            (await ReadStringsAsync(
                context,
                "SELECT [VehicleType] FROM [dbo].[CustomerBookings] ORDER BY [CreatedAtUtc], [Id]"))
                .Order(StringComparer.Ordinal)
                .Should().Equal(expected.Order(StringComparer.Ordinal));
            (await ReadStringsAsync(
                context,
                $"""
                SELECT CONCAT([VehicleType], '|',
                    CASE WHEN [ImageUrl] IS NULL THEN 'null' ELSE 'value' END)
                FROM [dbo].[Vehicles]
                WHERE [Id] = '{vehicleId:D}'
                """)).Should().Equal("Sedan|null");
            var columns = await ReadStringsAsync(
                context,
                """
                SELECT CONCAT(OBJECT_NAME(c.[object_id]), '.', c.[name], '|',
                    TYPE_NAME(c.[user_type_id]), '|',
                    c.[max_length], '|', c.[is_nullable], '|',
                    COALESCE(dc.[definition], 'none'))
                FROM sys.columns c
                LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
                WHERE c.[object_id] IN (
                    OBJECT_ID('[dbo].[Vehicles]'),
                    OBJECT_ID('[dbo].[CheckoutDrafts]'),
                    OBJECT_ID('[dbo].[CustomerBookings]'))
                  AND c.[name] IN ('VehicleType', 'ImageUrl', 'VehicleImageUrl')
                ORDER BY OBJECT_NAME(c.[object_id]), c.[name]
                """);
            columns.Should().Contain(
                value => value.StartsWith("Vehicles.VehicleType|nvarchar|100|0|", StringComparison.Ordinal) &&
                         value.Contains("Sedan", StringComparison.Ordinal));
            columns.Should().Contain("Vehicles.ImageUrl|nvarchar|1000|1|none");
            columns.Should().Contain("CheckoutDrafts.VehicleImageUrl|nvarchar|1000|1|none");
            columns.Should().Contain("CustomerBookings.VehicleImageUrl|nvarchar|1000|1|none");

            await migrator.MigrateAsync(priorMigration);

            (await ReadStringsAsync(
                context,
                """
                SELECT c.[name]
                FROM sys.columns c
                WHERE c.[name] IN ('ImageUrl', 'VehicleImageUrl')
                  AND c.[object_id] IN (
                      OBJECT_ID('[dbo].[Vehicles]'),
                      OBJECT_ID('[dbo].[CheckoutDrafts]'),
                      OBJECT_ID('[dbo].[CustomerBookings]'))
                """)).Should().BeEmpty();
            (await ReadStringsAsync(
                context,
                "SELECT CONVERT(varchar(10), COUNT(*)) FROM [dbo].[CustomerBookings]"))
                .Should().Equal("4");
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
                    InitialCatalog = _databaseName
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

    [Fact]
    [Trait("ScenarioId", "FAN-BANNER-MIGRATION-012")]
    public async Task BannerMigration_UpAndDown_has_required_contract_and_preserves_existing_rows()
    {
        var databaseName = $"Ghseeli_BannerMigration_{Guid.NewGuid():N}";
        await DropDatabaseAsync(databaseName);
        var userId = Guid.NewGuid();
        var bannerId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 24, 20, 0, 0, TimeSpan.Zero);
        try
        {
            await using var context = CreateContext(databaseName);
            var migrator = context.GetService<IMigrator>();
            var prior = context.Database.GetMigrations().Single(value =>
                value.EndsWith("_AddCustomerBusinessFavourites"));
            var migration = context.Database.GetMigrations().Single(value =>
                value.EndsWith("_AddCustomerBanners"));
            await migrator.MigrateAsync(prior);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[AspNetUsers]
                    ([Id],[FullName],[Email],[IsActive],[EmailConfirmed],[PhoneNumberConfirmed],
                     [TwoFactorEnabled],[LockoutEnabled],[AccessFailedCount],[CreatedAt],[IsDemo])
                VALUES ({userId},{"Banner migration user"},{"banner-migration@example.test"},
                    {true},{false},{false},{false},{false},{0},{now.UtcDateTime},{false});
                """);

            await migrator.MigrateAsync(migration);
            (await ReadStringsAsync(context, """
                SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] LIKE '%AddCustomerBanners'
                """)).Should().Equal(migration);
            (await ReadStringsAsync(context, """
                SELECT c.[name] + '|' + TYPE_NAME(c.[user_type_id]) + '|' +
                       CONVERT(varchar(5),c.[max_length]) + '|' +
                       CONVERT(varchar(1),c.[is_nullable])
                FROM sys.columns c
                WHERE c.[object_id] = OBJECT_ID('[dbo].[Banners]')
                ORDER BY c.[column_id]
                """)).Should().Contain(
                    "ImageUrl|nvarchar|1000|0",
                    "DisplayOrder|int|4|0",
                    "IsActive|bit|1|0",
                    "IsDemo|bit|1|0",
                    "RowVersion|timestamp|8|0");
            (await ReadStringsAsync(context, """
                SELECT i.[name] + '|' +
                       STRING_AGG(c.[name], ',') WITHIN GROUP (ORDER BY ic.[key_ordinal])
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic
                    ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                INNER JOIN sys.columns c
                    ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                WHERE i.[object_id] = OBJECT_ID('[dbo].[Banners]')
                  AND i.[name] = 'IX_Banners_IsDemo_IsActive_DisplayOrder_Id'
                GROUP BY i.[name]
                """)).Should().Equal(
                    "IX_Banners_IsDemo_IsActive_DisplayOrder_Id|IsDemo,IsActive,DisplayOrder,Id");
            (await ReadStringsAsync(context, """
                SELECT [name] + '|' + [definition] FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID('[dbo].[Banners]')
                """)).Should().ContainSingle(value =>
                    value.StartsWith("CK_Banners_DisplayOrder|", StringComparison.Ordinal) &&
                    value.Contains("[DisplayOrder]>=(0)", StringComparison.Ordinal) &&
                    value.Contains("[DisplayOrder]<=(10000)", StringComparison.Ordinal));
            (await ReadStringsAsync(context, """
                SELECT [name] FROM sys.foreign_keys
                WHERE parent_object_id = OBJECT_ID('[dbo].[Banners]')
                """)).Should().BeEmpty();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[Banners]
                    ([Id],[ImageUrl],[DisplayOrder],[IsActive],[CreatedAtUtc],[UpdatedAtUtc],[IsDemo])
                VALUES ({bannerId},{"https://cdn.example.test/banners/migration.png"},
                    {10},{true},{now},{now},{false});
                """);
            (await ReadStringsAsync(context, $"""
                SELECT CONCAT(DATALENGTH([RowVersion]),'|',[DisplayOrder],'|',[IsActive])
                FROM [dbo].[Banners] WHERE [Id] = '{bannerId:D}'
                """)).Should().Equal("8|10|1");

            await migrator.MigrateAsync(prior);
            (await ReadStringsAsync(context,
                "SELECT [name] FROM sys.tables WHERE [name] = 'Banners'"))
                .Should().BeEmpty();
            (await ReadStringsAsync(context, """
                SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] LIKE '%AddCustomerBanners'
                """)).Should().BeEmpty();
            (await ReadStringsAsync(context, """
                SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] LIKE '%AddCustomerBusinessFavourites'
                """)).Should().Equal(prior);
            (await ReadStringsAsync(context, $"""
                SELECT CONVERT(varchar(10),COUNT(*)) FROM [dbo].[AspNetUsers]
                WHERE [Id] = '{userId:D}'
                """)).Should().Equal("1");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(databaseName);
        }
    }

    private ApplicationDbContext CreateContext(string? databaseName = null)
    {
        var connection = new SqlConnectionStringBuilder(MasterConnection)
        {
            InitialCatalog = databaseName ?? _databaseName
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection, sqlServerOptions =>
                sqlServerOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CustomerSchemaOptions.OwnedDefaultSchema))
            .Options;
        return new ApplicationDbContext(options);
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new SqlConnection(MasterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{_databaseName}];";
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropDatabaseAsync()
        => await DropDatabaseAsync(_databaseName);

    private static async Task DropDatabaseAsync(string databaseName)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(MasterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
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

    private static Task InsertCatalogProviderAndCategoryAsync(
        ApplicationDbContext context,
        Guid providerId,
        Guid sourceCompanyId,
        bool isDemo,
        Guid categoryId,
        Guid sourceCategoryId) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [dbo].[CatalogProviders]
                ([Id], [SourceCompanyId], [IsEnabled], [DisplayOrder], [NameAr],
                 [CatalogVersion], [BusinessVerticalCode], [IsDemo])
            VALUES
                ({providerId}, {sourceCompanyId}, 1, 0, N'Existing provider', 1,
                 {BusinessVerticalSnapshotDefaults.CarWashCode}, {isDemo});

            INSERT INTO [dbo].[CatalogCategories]
                ([Id], [SourceCategoryId], [ProviderId], [NameAr], [DescriptionAr],
                 [DisplayOrder], [ImageUrl], [ColorHex])
            VALUES
                ({categoryId}, {sourceCategoryId}, {providerId}, N'Existing category',
                 N'Existing description', 0, NULL, NULL);
            """);

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
