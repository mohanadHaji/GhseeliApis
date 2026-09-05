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

    [Fact]
    public async Task SaveChanges_NewBookingWithNonCarWashVerticalSnapshot_RejectsWithoutPersistence()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using (var context = CreateContext(databaseName))
        {
            context.CustomerBookings.Add(new CustomerBooking
            {
                BusinessVerticalCode = "mechanics"
            });

            var action = () => context.SaveChangesAsync();

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*business vertical*snapshot*car wash*");
        }

        await using var verificationContext = CreateContext(databaseName);
        verificationContext.CustomerBookings.Should().BeEmpty();
    }

    private static ApplicationDbContext CreateContext()
        => CreateContext(Guid.NewGuid().ToString("N"));

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }
}
