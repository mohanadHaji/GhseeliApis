using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Validators.Internal;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies availability discovery capacity behavior against SQL Server.
/// </summary>
public sealed class AvailabilityDiscoveryRelationalTests
{
    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-CAPACITY-017")]
    [Trait("ScenarioId", "FAN-AVAILABILITY-ADVISORY-018")]
    public async Task DiscoverAsync_TranslatesCapacityQueryAndDoesNotCreateOrConsumeReservations()
    {
        await using var database = await SqlServerAvailabilityDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            IsActive = true
        };
        var branch = CreateBranch(company, capacity: 4);
        context.AddRange(company, branch);
        var slotStart = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);
        foreach (var status in new[]
                 {
                     BookingStatuses.Pending,
                     BookingStatuses.Confirmed,
                     BookingStatuses.InProgress,
                     BookingStatuses.Completed,
                     BookingStatuses.Cancelled,
                     BookingStatuses.NoShow
                 })
        {
            context.AppointmentReservations.Add(CreateReservation(
                branch.Id,
                status,
                slotStart,
                slotStart.AddMinutes(30),
                now));
        }
        context.AppointmentReservations.AddRange(
            CreateReservation(
                branch.Id,
                BookingStatuses.Confirmed,
                slotStart.AddMinutes(-30),
                slotStart,
                now),
            CreateReservation(
                branch.Id,
                BookingStatuses.Confirmed,
                slotStart.AddMinutes(30),
                slotStart.AddMinutes(60),
                now));
        await context.SaveChangesAsync();
        var beforeIds = await context.AppointmentReservations
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync();

        var service = new AvailabilityDiscoveryService(
            context,
            new ServiceAreaCalculator(),
            new FakeClock(now),
            new AvailabilityDiscoveryRequestValidator());
        var response = await service.DiscoverAsync(
            new AvailabilityDiscoveryRequest
            {
                Date = new DateOnly(2026, 9, 28),
                PreferredLocalTime = new TimeOnly(9),
                Candidates =
                [
                    new AvailabilityDiscoveryCompanyCandidate
                    {
                        CompanyId = company.Id,
                        BranchIds = [branch.Id]
                    }
                ]
            },
            default);

        var result = response.Results.Should().ContainSingle().Subject;
        result.ConfiguredCapacity.Should().Be(4);
        result.RemainingCapacity.Should().Be(1);
        context.ServiceOfferings.Should().BeEmpty();
        (await context.AppointmentReservations
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync())
            .Should().Equal(beforeIds);
    }

    private static Branch CreateBranch(Company company, int capacity)
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Company = company,
            CompanyId = company.Id,
            NameAr = "فرع",
            AddressAr = "عنوان",
            IsActive = true
        };
        branch.AvailabilitySettings = new BranchAvailabilitySettings
        {
            BranchId = branch.Id,
            Branch = branch,
            TimeZoneId = "UTC",
            BookingHorizonDays = 30,
            MinimumLeadMinutes = 0,
            IsActive = true
        };
        branch.RecurringSchedules.Add(new BranchRecurringSchedule
        {
            BranchId = branch.Id,
            Branch = branch,
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = TimeSpan.FromHours(9),
            EndLocalTime = TimeSpan.FromHours(10),
            SlotDurationMinutes = 30,
            Capacity = capacity,
            IsActive = true
        });
        return branch;
    }

    private static AppointmentReservation CreateReservation(
        Guid branchId,
        string status,
        DateTime start,
        DateTime end,
        DateTime now) => new()
    {
        BranchId = branchId,
        CustomerBookingReference = Guid.NewGuid(),
        OrderGuid = Guid.NewGuid(),
        RequestHash = Guid.NewGuid().ToString("N"),
        Currency = "ILS",
        RequestedSlotStartUtc = start,
        RequestedSlotEndUtc = end,
        Status = status,
        StatusChangedAtUtc = now,
        CreatedAtUtc = now
    };

    private sealed class FakeClock(DateTime utcNow) : ISystemClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class SqlServerAvailabilityDatabase : IAsyncDisposable
    {
        private readonly string _databaseName =
            $"Ghseeli_AvailabilityDiscovery_{Guid.NewGuid():N}";
        private const string MasterConnection =
            "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

        public static async Task<SqlServerAvailabilityDatabase> CreateAsync()
        {
            var database = new SqlServerAvailabilityDatabase();
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public BusinessDbContext CreateContext()
        {
            var connection = new SqlConnectionStringBuilder(MasterConnection)
            {
                InitialCatalog = _databaseName,
                MultipleActiveResultSets = true
            }.ConnectionString;
            var options = new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlServer(connection)
                .Options;
            return new BusinessDbContext(options);
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(MasterConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
