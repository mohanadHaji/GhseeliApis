using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Verifies internal car-wash vertical markers remain outside current HTTP contracts.
/// </summary>
public sealed class BusinessVerticalSnapshotSchemaTests
{
    [Fact]
    public void CustomerModel_DefaultsCatalogAndBookingSnapshotsToCarWash()
    {
        using var context = CreateContext();

        var providerVertical = context.Model.FindEntityType(typeof(CatalogProviderReadModel))!
            .FindProperty(nameof(CatalogProviderReadModel.BusinessVerticalCode))!;
        providerVertical.GetDefaultValue()
            .Should().Be(BusinessVerticalSnapshotDefaults.CarWashCode);
        providerVertical.GetMaxLength().Should().Be(64);
        providerVertical.IsNullable.Should().BeFalse();
        var bookingVertical = context.Model.FindEntityType(typeof(CustomerBooking))!
            .FindProperty(nameof(CustomerBooking.BusinessVerticalCode))!;
        bookingVertical.GetDefaultValue()
            .Should().Be(BusinessVerticalSnapshotDefaults.CarWashCode);
        bookingVertical.GetMaxLength().Should().Be(64);
        bookingVertical.IsNullable.Should().BeFalse();

        new CatalogProviderReadModel().BusinessVerticalCode
            .Should().Be(BusinessVerticalSnapshotDefaults.CarWashCode);
        new CustomerBooking().BusinessVerticalCode
            .Should().Be(BusinessVerticalSnapshotDefaults.CarWashCode);
    }

    [Fact]
    public async Task SaveChanges_ChangingPersistedBookingVerticalSnapshot_RejectsMutation()
    {
        await using var context = CreateContext();
        var booking = new CustomerBooking();
        context.CustomerBookings.Add(booking);
        await context.SaveChangesAsync();

        booking.BusinessVerticalCode = "mechanics";

        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*snapshot*");
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }
}
