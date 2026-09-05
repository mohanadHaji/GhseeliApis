using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Xml.Linq;

namespace Ghseeli.BusinessApi.Tests;

/// <summary>
/// Defines the exact Step 16 ownership boundary and relational model for the Business database.
/// </summary>
public class Step16BusinessSchemaModelTests
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
        "VehicleWorkOrderDetails",
        "WorkOrderItems",
        "WorkOrders",
        "WorkOrderSelections"
    ];

    [Fact]
    public void Model_HasExactBusinessOwnedEntityAndTableAllowlist()
    {
        using var context = CreateContext();

        var tables = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        tables.Should().Equal(ExpectedTables.Order(StringComparer.Ordinal));

        context.Model.GetEntityTypes()
            .Select(entity => entity.ClrType)
            .Should()
            .BeEquivalentTo(
            [
                typeof(AddonChoice),
                typeof(AddonGroup),
                typeof(AppointmentReservation),
                typeof(BookingStatusOutboxMessage),
                typeof(BookingStatusRequeueHistory),
                typeof(Branch),
                typeof(BranchAvailabilityOverride),
                typeof(BranchAvailabilitySettings),
                typeof(BranchRecurringSchedule),
                typeof(BranchServiceArea),
                typeof(BusinessUser),
                typeof(BusinessUserAssignment),
                typeof(BusinessVertical),
                typeof(Company),
                typeof(CompanyBusinessVertical),
                typeof(IdentityRole<Guid>),
                typeof(IdentityRoleClaim<Guid>),
                typeof(IdentityUserClaim<Guid>),
                typeof(IdentityUserLogin<Guid>),
                typeof(IdentityUserRole<Guid>),
                typeof(IdentityUserToken<Guid>),
                typeof(InternalServiceIdempotencyRecord),
                typeof(InternalServiceNonce),
                typeof(ServiceCategory),
                typeof(ServiceOffering),
                typeof(VehicleWorkOrderDetails),
                typeof(WorkOrder),
                typeof(WorkOrderItem),
                typeof(WorkOrderSelection)
            ]);
    }

    [Fact]
    public void Model_MapsEveryBusinessOwnedTableToDbo()
    {
        using var context = CreateContext();

        context.Model.GetEntityTypes()
            .Select(entity => $"{entity.GetSchema()}|{entity.GetTableName()}")
            .Distinct(StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(ExpectedTables.Select(table => $"dbo|{table}"));
    }

    [Fact]
    public void Model_AndDbSets_ExcludeEveryCustomerDatabaseConcern()
    {
        using var context = CreateContext();
        var forbiddenFragments = new[]
        {
            "CustomerDevice",
            "CustomerProfile",
            "Device",
            "Profile",
            "UserAddress",
            "Address",
            "Draft",
            "CustomerBooking",
            "Payment",
            "Stripe",
            "Wallet",
            "Notification"
        };

        var ownedNames = context.Model.GetEntityTypes()
            .SelectMany(entity => new[]
            {
                entity.ClrType.Name,
                entity.GetTableName() ?? string.Empty
            })
            .Concat(typeof(BusinessDbContext).GetProperties()
                .Where(property => property.PropertyType.IsGenericType &&
                    property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Select(property => property.Name))
            .ToList();

        foreach (var forbidden in forbiddenFragments)
        {
            ownedNames.Should().NotContain(
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"the Business database must not own the Customer {forbidden} concern");
        }

        ownedNames.Should().NotContain(name =>
            string.Equals(name, "Vehicle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Vehicles", StringComparison.OrdinalIgnoreCase),
            "vehicle snapshots are allowed, but the Business database must not own customer vehicles");
    }

    [Fact]
    public void RuntimeAssemblies_HaveNoForbiddenApiImplementationReferences()
    {
        var businessReferences = typeof(BusinessDbContext).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();
        var contractReferences = typeof(Ghseeli.IntegrationContracts.ContractAssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        businessReferences.Should().NotContain("Ghseeli.CustomerApi");
        contractReferences.Should().NotContain(name =>
            name == "Ghseeli.CustomerApi" ||
            name == "Ghseeli.BusinessApi" ||
            name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectFiles_HaveOnlyNeutralSharedProjectReferences()
    {
        var solutionDirectory = FindSolutionDirectory();

        ProjectReferences(Path.Combine(
                solutionDirectory,
                "Ghseeli.BusinessApi",
                "Ghseeli.BusinessApi.csproj"))
            .Should()
            .BeEquivalentTo(
            [
                "Ghseeli.Common\\Ghseeli.Common.csproj",
                "Ghseeli.IntegrationContracts\\Ghseeli.IntegrationContracts.csproj"
            ]);

        ProjectReferences(Path.Combine(
                solutionDirectory,
                "Ghseeli.IntegrationContracts",
                "Ghseeli.IntegrationContracts.csproj"))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Model_HasExactIndexes()
    {
        using var context = CreateContext();

        IndexSignatures(context.Model).Should().BeEquivalentTo(
        [
            "AddonChoice|AddonGroupId,DisplayOrder|False",
            "AddonChoice|AddonGroupId,IsActive|False",
            "AddonGroup|ServiceOfferingId,DisplayOrder|False",
            "AddonGroup|ServiceOfferingId,IsActive|False",
            "AppointmentReservation|BusinessVerticalId|False",
            "AppointmentReservation|BranchId,RequestedSlotStartUtc,RequestedSlotEndUtc,Status|False",
            "AppointmentReservation|CustomerBookingReference|True",
            "AppointmentReservation|OrderGuid|True",
            "AppointmentReservation|PublicId|True",
            "BookingStatusOutboxMessage|AppointmentReservationId,Sequence|True",
            "BookingStatusOutboxMessage|DeliveryState,NextAttemptAtUtc,LeaseExpiresAtUtc|False",
            "BookingStatusRequeueHistory|BookingStatusOutboxMessageId,Generation|True",
            "BookingStatusRequeueHistory|BookingStatusOutboxMessageId,RequestId|True",
            "Branch|CompanyId,IsActive|False",
            "BranchAvailabilityOverride|BranchId,OverrideDate|True",
            "BranchAvailabilitySettings|BranchId|True",
            "BranchRecurringSchedule|BranchId,DayOfWeek,IsActive|False",
            "BranchServiceArea|BranchId|True",
            "BusinessUserAssignment|BranchId|False",
            "BusinessUserAssignment|CompanyId|False",
            "BusinessUserAssignment|UserId,CompanyId,BranchId|True",
            "BusinessUser|NormalizedEmail|False",
            "BusinessUser|NormalizedUserName|True",
            "BusinessVertical|Code|True",
            "CompanyBusinessVertical|BusinessVerticalId,IsActive|False",
            "CompanyBusinessVertical|CompanyId|True",
            "InternalServiceIdempotencyRecord|ExpiresAtUtc|False",
            "InternalServiceIdempotencyRecord|ServiceId,Operation,IdempotencyKey|True",
            "InternalServiceNonce|ExpiresAtUtc|False",
            "InternalServiceNonce|ServiceId,Nonce|True",
            "ServiceCategory|CompanyId,DisplayOrder|False",
            "ServiceCategory|CompanyId,IsActive|False",
            "ServiceCategory|BusinessVerticalId,IsActive|False",
            "ServiceCategory|CompanyId,BusinessVerticalId|False",
            "ServiceOffering|BranchId,IsActive|False",
            "ServiceOffering|CategoryId,DisplayOrder|False",
            "ServiceOffering|CategoryId,IsActive|False",
            "WorkOrder|AppointmentReservationId|True",
            "WorkOrder|BusinessVerticalId|False",
            "WorkOrder|PublicId|True",
            "WorkOrderItem|WorkOrderId,DisplayOrder|False",
            "WorkOrderItem|WorkOrderId,OfferingId|True",
            "WorkOrderSelection|WorkOrderItemId,AddonChoiceId|True",
            "WorkOrderSelection|WorkOrderItemId,DisplayOrder|False",
            "IdentityRole`1|NormalizedName|True",
            "IdentityRoleClaim`1|RoleId|False",
            "IdentityUserClaim`1|UserId|False",
            "IdentityUserLogin`1|UserId|False",
            "IdentityUserRole`1|RoleId|False"
        ]);
    }

    [Fact]
    public void Model_HasExactForeignKeysAndDeleteBehavior()
    {
        using var context = CreateContext();

        ForeignKeySignatures(context.Model).Should().BeEquivalentTo(
        [
            "AddonChoice(AddonGroupId)->AddonGroup(Id)|Cascade",
            "AddonGroup(ServiceOfferingId)->ServiceOffering(Id)|Cascade",
            "AppointmentReservation(BusinessVerticalId)->BusinessVertical(Id)|Restrict",
            "BookingStatusOutboxMessage(AppointmentReservationId)->AppointmentReservation(Id)|Cascade",
            "BookingStatusRequeueHistory(BookingStatusOutboxMessageId)->BookingStatusOutboxMessage(Id)|Cascade",
            "Branch(CompanyId)->Company(Id)|Cascade",
            "BranchAvailabilityOverride(BranchId)->Branch(Id)|Cascade",
            "BranchAvailabilitySettings(BranchId)->Branch(Id)|Cascade",
            "BranchRecurringSchedule(BranchId)->Branch(Id)|Cascade",
            "BranchServiceArea(BranchId)->Branch(Id)|Cascade",
            "BusinessUserAssignment(BranchId)->Branch(Id)|NoAction",
            "BusinessUserAssignment(CompanyId)->Company(Id)|Cascade",
            "BusinessUserAssignment(UserId)->BusinessUser(Id)|Restrict",
            "CompanyBusinessVertical(BusinessVerticalId)->BusinessVertical(Id)|Restrict",
            "CompanyBusinessVertical(CompanyId)->Company(Id)|Cascade",
            "IdentityRoleClaim`1(RoleId)->IdentityRole`1(Id)|Cascade",
            "IdentityUserClaim`1(UserId)->BusinessUser(Id)|Cascade",
            "IdentityUserLogin`1(UserId)->BusinessUser(Id)|Cascade",
            "IdentityUserRole`1(RoleId)->IdentityRole`1(Id)|Cascade",
            "IdentityUserRole`1(UserId)->BusinessUser(Id)|Cascade",
            "IdentityUserToken`1(UserId)->BusinessUser(Id)|Cascade",
            "ServiceCategory(CompanyId)->Company(Id)|Cascade",
            "ServiceCategory(BusinessVerticalId)->BusinessVertical(Id)|Restrict",
            "ServiceCategory(CompanyId,BusinessVerticalId)->CompanyBusinessVertical(CompanyId,BusinessVerticalId)|Restrict",
            "ServiceOffering(BranchId)->Branch(Id)|Restrict",
            "ServiceOffering(CategoryId)->ServiceCategory(Id)|Cascade",
            "WorkOrder(AppointmentReservationId)->AppointmentReservation(Id)|Cascade",
            "WorkOrder(BusinessVerticalId)->BusinessVertical(Id)|Restrict",
            "VehicleWorkOrderDetails(WorkOrderId)->WorkOrder(Id)|Cascade",
            "WorkOrderItem(WorkOrderId)->WorkOrder(Id)|Cascade",
            "WorkOrderSelection(WorkOrderItemId)->WorkOrderItem(Id)|Cascade"
        ]);
    }

    [Fact]
    public void Model_HasExactMoneyCoordinateAndConcurrencyConfiguration()
    {
        using var context = CreateContext();

        PrecisionSignatures(context.Model).Should().BeEquivalentTo(
        [
            "AddonChoice.PriceAdjustment|18,2",
            "AppointmentReservation.ItemSubtotal|18,2",
            "ServiceOffering.BasePrice|18,2",
            "WorkOrder.Latitude|9,6",
            "WorkOrder.Longitude|9,6",
            "WorkOrderItem.AddonSubtotal|18,2",
            "WorkOrderItem.BaseSubtotal|18,2",
            "WorkOrderItem.ItemSubtotal|18,2",
            "WorkOrderSelection.TotalPriceAdjustment|18,2",
            "WorkOrderSelection.UnitPriceAdjustment|18,2"
        ]);

        context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.IsConcurrencyToken)
            .Select(property => $"{property.DeclaringType.ClrType.Name}.{property.Name}|{property.ValueGenerated}")
            .Should()
            .BeEquivalentTo(
            [
                "AddonChoice.RowVersion|OnAddOrUpdate",
                "AddonGroup.RowVersion|OnAddOrUpdate",
                "AppointmentReservation.RowVersion|OnAddOrUpdate",
                "BookingStatusOutboxMessage.RowVersion|OnAddOrUpdate",
                "Branch.RowVersion|OnAddOrUpdate",
                "BranchAvailabilityOverride.RowVersion|OnAddOrUpdate",
                "BranchAvailabilitySettings.RowVersion|OnAddOrUpdate",
                "BranchRecurringSchedule.RowVersion|OnAddOrUpdate",
                "BranchServiceArea.RowVersion|OnAddOrUpdate",
                "BusinessUser.ConcurrencyStamp|Never",
                "BusinessVertical.RowVersion|OnAddOrUpdate",
                "Company.RowVersion|OnAddOrUpdate",
                "IdentityRole`1.ConcurrencyStamp|Never",
                "ServiceCategory.RowVersion|OnAddOrUpdate",
                "ServiceOffering.RowVersion|OnAddOrUpdate",
                "WorkOrder.RowVersion|OnAddOrUpdate"
            ]);
    }

    private static BusinessDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Step16BusinessModel;Trusted_Connection=True")
            .Options;
        return new BusinessDbContext(options);
    }

    private static IEnumerable<string> IndexSignatures(IModel model) =>
        model.GetEntityTypes()
            .SelectMany(entity => entity.GetIndexes().Select(index =>
                $"{entity.ClrType.Name}|{string.Join(",", index.Properties.Select(property => property.Name))}|{index.IsUnique}"));

    private static IEnumerable<string> ForeignKeySignatures(IModel model) =>
        model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys().Select(foreignKey =>
                $"{entity.ClrType.Name}({string.Join(",", foreignKey.Properties.Select(property => property.Name))})" +
                $"->{foreignKey.PrincipalEntityType.ClrType.Name}({string.Join(",", foreignKey.PrincipalKey.Properties.Select(property => property.Name))})" +
                $"|{foreignKey.DeleteBehavior}"));

    private static IEnumerable<string> PrecisionSignatures(IModel model) =>
        model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Where(property => property.GetPrecision().HasValue || property.GetScale().HasValue)
                .Select(property =>
                    $"{entity.ClrType.Name}.{property.Name}|{property.GetPrecision()},{property.GetScale()}"));

    private static string FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GhseeliApis.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the GhseeliApis solution directory.");
    }

    private static IEnumerable<string> ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Select(value => Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(projectPath)!, value!)))
            .Select(path => Path.GetRelativePath(
                Path.GetDirectoryName(Path.GetDirectoryName(projectPath))!,
                path));
}
