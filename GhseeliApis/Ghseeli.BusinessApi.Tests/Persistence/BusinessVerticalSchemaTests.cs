using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Tests.Persistence;

/// <summary>
/// Verifies the internal vertical-ready schema while the public product remains car-wash only.
/// </summary>
public sealed class BusinessVerticalSchemaTests
{
    [Fact]
    public void Model_DefinesCarWashVerticalRelationshipsAndVehicleExtension()
    {
        using var context = CreateInMemoryContext();

        var vertical = context.Model.FindEntityType(typeof(BusinessVertical));
        vertical.Should().NotBeNull();
        vertical!.FindProperty(nameof(BusinessVertical.Code))!.GetMaxLength().Should().Be(64);
        vertical.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(BusinessVertical.Code) }));

        var companyVertical = context.Model.FindEntityType(typeof(CompanyBusinessVertical));
        companyVertical.Should().NotBeNull();
        companyVertical!.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .Should().Equal(
                nameof(CompanyBusinessVertical.CompanyId),
                nameof(CompanyBusinessVertical.BusinessVerticalId));

        var category = context.Model.FindEntityType(typeof(ServiceCategory));
        category!.FindProperty(nameof(ServiceCategory.BusinessVerticalId))
            .Should().NotBeNull();
        category.GetForeignKeys().Should().Contain(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(CompanyBusinessVertical) &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(
            new[]
            {
                nameof(ServiceCategory.CompanyId),
                nameof(ServiceCategory.BusinessVerticalId)
            }));

        context.Model.FindEntityType(typeof(AppointmentReservation))!
            .FindProperty(nameof(AppointmentReservation.BusinessVerticalCode))!
            .GetDefaultValue()
            .Should().Be(BusinessVerticalDefaults.CarWashCode);
        context.Model.FindEntityType(typeof(WorkOrder))!
            .FindProperty(nameof(WorkOrder.BusinessVerticalCode))!
            .GetDefaultValue()
            .Should().Be(BusinessVerticalDefaults.CarWashCode);

        var workOrder = context.Model.FindEntityType(typeof(WorkOrder))!;
        workOrder.FindProperty(nameof(WorkOrder.VehicleType)).Should().BeNull();
        context.Model.FindEntityType(typeof(VehicleWorkOrderDetails))!
            .FindPrimaryKey()!.Properties.Should().ContainSingle()
            .Which.Name.Should().Be(nameof(VehicleWorkOrderDetails.WorkOrderId));
    }

    [Fact]
    public void NewCarWashAggregate_UsesInternalDefaultsWithoutChangingCallers()
    {
        var company = new Company();
        var category = new ServiceCategory { CompanyId = company.Id };
        var reservation = new AppointmentReservation();
        var workOrder = new WorkOrder
        {
            VehicleType = "SUV",
            LicensePlate = "12-345-67",
            VehicleMake = "Toyota"
        };

        company.BusinessVerticals.Add(new CompanyBusinessVertical
        {
            BusinessVerticalId = BusinessVerticalDefaults.CarWashId
        });
        company.BusinessVerticals.Should().ContainSingle(assignment =>
            assignment.BusinessVerticalId == BusinessVerticalDefaults.CarWashId);
        category.BusinessVerticalId.Should().Be(BusinessVerticalDefaults.CarWashId);
        reservation.BusinessVerticalId.Should().Be(BusinessVerticalDefaults.CarWashId);
        reservation.BusinessVerticalCode.Should().Be(BusinessVerticalDefaults.CarWashCode);
        workOrder.BusinessVerticalId.Should().Be(BusinessVerticalDefaults.CarWashId);
        workOrder.BusinessVerticalCode.Should().Be(BusinessVerticalDefaults.CarWashCode);
        workOrder.VehicleDetails.VehicleType.Should().Be("SUV");
        workOrder.VehicleType.Should().Be("SUV");
        workOrder.LicensePlate.Should().Be("12-345-67");
        workOrder.VehicleMake.Should().Be("Toyota");
    }

    [Fact]
    public async Task SaveChanges_NewCompanyWithoutAssignments_AddsOnePrimaryCarWashAssignment()
    {
        await using var context = CreateInMemoryContext();
        var company = new Company { NameAr = "شركة" };
        context.Companies.Add(company);

        await context.SaveChangesAsync();

        company.BusinessVerticals.Should().ContainSingle(assignment =>
            assignment.BusinessVerticalId == BusinessVerticalDefaults.CarWashId &&
            assignment.IsPrimary &&
            assignment.IsActive);
    }

    [Fact]
    public async Task SaveChanges_NewCompanyWithNoActivePrimaryAssignment_RejectsInvalidAggregate()
    {
        await using var context = CreateInMemoryContext();
        var company = new Company { NameAr = "شركة" };
        company.BusinessVerticals.Add(new CompanyBusinessVertical
        {
            BusinessVerticalId = Guid.NewGuid(),
            IsPrimary = false,
            IsActive = true
        });
        context.Companies.Add(company);

        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active primary business vertical*");
    }

    [Fact]
    public async Task SaveChanges_ChangingPersistedReservationOrWorkOrderVerticalSnapshot_RejectsMutation()
    {
        await using var context = CreateInMemoryContext();
        var reservation = new AppointmentReservation
        {
            WorkOrder = new WorkOrder { VehicleType = "SUV" }
        };
        context.AppointmentReservations.Add(reservation);
        await context.SaveChangesAsync();

        reservation.BusinessVerticalCode = "mechanics";
        reservation.WorkOrder.BusinessVerticalId = Guid.NewGuid();

        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*snapshot*");
    }

    private static BusinessDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new BusinessDbContext(options);
    }
}
