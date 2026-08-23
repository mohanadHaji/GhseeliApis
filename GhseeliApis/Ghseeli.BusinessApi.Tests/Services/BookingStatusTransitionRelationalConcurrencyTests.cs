using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.IntegrationContracts.Bookings;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies SQL row-version protection for concurrent work-order transition writes.
/// </summary>
public sealed class BookingStatusTransitionRelationalConcurrencyTests
{
    [Fact]
    public async Task ConcurrentTransitionWrites_SecondSaveThrowsDbUpdateConcurrencyException()
    {
        await using var database = await SqlDatabase.CreateAsync();
        var workOrderId = await database.SeedAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstWorkOrder = await first.WorkOrders
            .Include(value => value.AppointmentReservation)
            .SingleAsync(value => value.PublicId == workOrderId);
        var secondWorkOrder = await second.WorkOrders
            .Include(value => value.AppointmentReservation)
            .SingleAsync(value => value.PublicId == workOrderId);
        firstWorkOrder.Status = BookingStatuses.Confirmed;
        firstWorkOrder.AppointmentReservation.Status = BookingStatuses.Confirmed;
        firstWorkOrder.AppointmentReservation.StatusSequence = 1;
        secondWorkOrder.Status = BookingStatuses.Cancelled;
        secondWorkOrder.AppointmentReservation.Status = BookingStatuses.Cancelled;
        secondWorkOrder.AppointmentReservation.StatusSequence = 1;

        await first.SaveChangesAsync();
        var action = () => second.SaveChangesAsync();

        await action.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private sealed class SqlDatabase : IAsyncDisposable
    {
        private readonly string _name = $"GhseeliTransitionConcurrency_{Guid.NewGuid():N}";

        public static async Task<SqlDatabase> CreateAsync()
        {
            var database = new SqlDatabase();
            await using var context = database.CreateContext();
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public BusinessDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlServer(ConnectionString)
                .Options);

        public async Task<Guid> SeedAsync()
        {
            await using var context = CreateContext();
            var workOrderId = Guid.NewGuid();
            context.AppointmentReservations.Add(new AppointmentReservation
            {
                CustomerBookingReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                RequestHash = new string('a', 64),
                BranchId = Guid.NewGuid(),
                Currency = "ILS",
                RequestedSlotStartUtc = DateTime.UtcNow,
                RequestedSlotEndUtc = DateTime.UtcNow.AddHours(1),
                Status = BookingStatuses.Pending,
                StatusSequence = 0,
                StatusChangedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                WorkOrder = new WorkOrder
                {
                    PublicId = workOrderId,
                    Status = BookingStatuses.Pending,
                    CustomerName = "Concurrency customer",
                    VehicleType = "Sedan",
                    AddressLine = "Concurrency address",
                    CreatedAtUtc = DateTime.UtcNow
                }
            });
            await context.SaveChangesAsync();
            return workOrderId;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var context = CreateContext();
                await context.Database.EnsureDeletedAsync();
            }
            catch (SqlException)
            {
            }
        }

        private string ConnectionString =>
            $"Server=(localdb)\\MSSQLLocalDB;Database={_name};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }
}
