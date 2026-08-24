using System.Reflection;
using FluentAssertions;
using GhseeliApis.Constants;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GhseeliApis.Tests;

/// <summary>
/// Frozen Step 16 Customer ownership checks for the compiled EF model.
/// </summary>
public sealed class Step16CustomerSchemaModelTests
{
    private static readonly string[] ExpectedTables =
    [
        "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins",
        "AspNetUserRoles", "AspNetUsers", "AspNetUserTokens",
        "BookingConfirmationAttempts", "CatalogAddonChoices", "CatalogAddonGroups",
        "CatalogBranches", "CatalogCategories", "CatalogOfferings", "CatalogProviders",
        "CheckoutDraftItems", "CheckoutDraftPricingItemSnapshots",
        "CheckoutDraftPricingSelectionSnapshots", "CheckoutDraftPricingSnapshots",
        "CheckoutDraftSelections", "CheckoutDrafts", "CustomerBookingItems",
        "CustomerBookingSelections", "CustomerBookings", "CustomerConfigurations",
        "CustomerDevices", "CustomerInternalIdempotencyRecords",
        "CustomerInternalServiceNonces", "CustomerPaymentIdempotencyRecords",
        "CustomerPayments", "ProcessedBookingStatusMessages", "StripeWebhookEvents",
        "UserAddresses", "Vehicles"
    ];

    private static readonly string[] RemovedTables =
    [
        "Companies", "CompanyAvailabilities", "Services", "ServiceOptions", "Bookings",
        "Payments", "Wallets", "WalletTransactions", "Notifications"
    ];

    private static readonly string[] RemovedClrTypes =
    [
        "Company", "CompanyAvailability", "Service", "ServiceOption", "Booking",
        "Payment", "Wallet", "WalletTransaction", "Notification"
    ];

    [Fact]
    public void Customer_model_contains_exact_owned_table_allowlist()
    {
        using var context = CreateContext();

        context.Model.GetDefaultSchema().Should().Be(CustomerSchemaOptions.OwnedDefaultSchema);

        var tables = context.Model.GetEntityTypes()
            .Select(entity => new
            {
                Table = entity.GetTableName(),
                Schema = entity.GetSchema()
            })
            .Where(mapping => mapping.Table is not null)
            .Distinct()
            .ToArray();

        tables.Should().OnlyContain(mapping =>
            mapping.Schema == CustomerSchemaOptions.OwnedDefaultSchema);

        var tableNames = tables
            .Select(mapping => mapping.Table!)
            .Order(StringComparer.Ordinal);

        tableNames.Should().Equal(ExpectedTables.Order(StringComparer.Ordinal));
        tableNames.Should().NotContain(RemovedTables);
    }

    [Fact]
    public void Customer_context_exposes_only_owned_domain_dbsets()
    {
        var dbSets = typeof(ApplicationDbContext).GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        var expected = ExpectedTables
            .Except(
            [
                "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins",
                "AspNetUserRoles", "AspNetUsers", "AspNetUserTokens"
            ])
            .Order(StringComparer.Ordinal);

        dbSets.Should().Equal(expected);
        dbSets.Should().NotContain(RemovedTables);
    }

    [Fact]
    public void Customer_model_has_no_removed_entity_or_navigation()
    {
        using var context = CreateContext();

        context.Model.GetEntityTypes()
            .Select(entity => entity.ClrType.Name)
            .Should().NotIntersectWith(RemovedClrTypes);

        context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetNavigations())
            .Select(navigation => navigation.TargetEntityType.ClrType.Name)
            .Should().NotIntersectWith(RemovedClrTypes);
    }

    [Fact]
    public void Customer_foreign_keys_stay_inside_allowlist_and_use_frozen_delete_behaviors()
    {
        using var context = CreateContext();
        var foreignKeys = context.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()).ToArray();

        foreignKeys.Should().OnlyContain(foreignKey =>
            ExpectedTables.Contains(foreignKey.DeclaringEntityType.GetTableName(), StringComparer.Ordinal) &&
            ExpectedTables.Contains(foreignKey.PrincipalEntityType.GetTableName(), StringComparer.Ordinal));

        AssertForeignKey(foreignKeys, "UserAddresses", "AspNetUsers", DeleteBehavior.Cascade);
        AssertForeignKey(foreignKeys, "Vehicles", "AspNetUsers", DeleteBehavior.Cascade);
        AssertForeignKey(foreignKeys, "CatalogBranches", "CatalogProviders", DeleteBehavior.Cascade);
        AssertForeignKey(foreignKeys, "CatalogCategories", "CatalogProviders", DeleteBehavior.Cascade);
        AssertForeignKey(foreignKeys, "CatalogOfferings", "CatalogCategories", DeleteBehavior.Cascade);
        AssertForeignKey(foreignKeys, "CatalogOfferings", "CatalogBranches", DeleteBehavior.NoAction);
        AssertForeignKey(foreignKeys, "CustomerBookings", "AspNetUsers", DeleteBehavior.Restrict);
        AssertForeignKey(foreignKeys, "CustomerPayments", "CustomerBookings", DeleteBehavior.Restrict);
        AssertForeignKey(foreignKeys, "StripeWebhookEvents", "CustomerPayments", DeleteBehavior.Restrict);

        foreignKeys.Where(foreignKey =>
                foreignKey.DeclaringEntityType.GetTableName() is
                    "CheckoutDraftItems" or "CheckoutDraftSelections" or
                    "CheckoutDraftPricingSnapshots" or "CheckoutDraftPricingItemSnapshots" or
                    "CheckoutDraftPricingSelectionSnapshots" or "CustomerBookingItems" or
                    "CustomerBookingSelections" or "CustomerPaymentIdempotencyRecords" or
                    "ProcessedBookingStatusMessages")
            .Should().OnlyContain(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
    }

    [Fact]
    public void Customer_money_coordinates_rates_and_rows_have_frozen_store_facets()
    {
        using var context = CreateContext();

        foreach (var entity in context.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties().Where(property =>
                         property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?)))
            {
                var isCoordinate = property.Name is "Latitude" or "Longitude";
                var isRate = property.Name is "ServiceFeePercentageRate" or "TaxRatePercent";
                property.GetPrecision().Should().Be(isCoordinate || isRate ? 9 : 18,
                    $"{entity.GetTableName()}.{property.Name} has a frozen precision");
                property.GetScale().Should().Be(isCoordinate ? 6 : isRate ? 4 : 2,
                    $"{entity.GetTableName()}.{property.Name} has a frozen scale");
            }
        }

        foreach (var entity in context.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties().Where(property => property.Name == "RowVersion"))
            {
                property.IsConcurrencyToken.Should().BeTrue();
                property.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
            }
        }
    }

    [Fact]
    public void Customer_model_has_required_unique_and_filtered_indexes()
    {
        using var context = CreateContext();

        AssertIndex(context.Model, "CustomerDevices", true, null, "InstallationId");
        AssertIndex(context.Model, "CustomerDevices", true, null, "TokenHash");
        AssertIndex(context.Model, "CustomerConfigurations", true, "[IsActive] = 1", "IsActive");
        AssertIndex(context.Model, "CheckoutDrafts", true, null, "OrderGuid");
        AssertIndex(context.Model, "CustomerBookings", true, null, "PublicReference");
        AssertIndex(context.Model, "CustomerBookings", true, null, "OrderGuid");
        AssertIndex(context.Model, "CustomerPayments", true, "[PaymentIntentId] IS NOT NULL", "PaymentIntentId");
        AssertIndex(context.Model, "CustomerPayments", true, null,
            "UserId", "OwnerDeviceId", "IdempotencyKey");
        AssertIndex(context.Model, "CustomerInternalIdempotencyRecords", true,
            "[OwnerToken] IS NOT NULL", "OwnerToken");
    }

    [Fact]
    public void Customer_roles_are_exactly_user_and_admin()
    {
        typeof(AppRoles).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Should().BeEquivalentTo(["User", "Admin"]);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Ghseeli_Step16_Customer_Model;Trusted_Connection=True")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void AssertForeignKey(
        IEnumerable<IForeignKey> foreignKeys,
        string dependentTable,
        string principalTable,
        DeleteBehavior deleteBehavior)
    {
        foreignKeys.Should().ContainSingle(foreignKey =>
            foreignKey.DeclaringEntityType.GetTableName() == dependentTable &&
            foreignKey.PrincipalEntityType.GetTableName() == principalTable &&
            foreignKey.DeleteBehavior == deleteBehavior);
    }

    private static void AssertIndex(
        IModel model,
        string table,
        bool unique,
        string? filter,
        params string[] properties)
    {
        var index = model.GetEntityTypes()
            .Single(entity => entity.GetTableName() == table)
            .GetIndexes()
            .Single(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual(properties));

        index.IsUnique.Should().Be(unique);
        index.GetFilter().Should().Be(filter);
    }
}
