using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Tests.Repositories;

/// <summary>
/// Verifies company persistence and ownership queries.
/// </summary>
public class CompanyRepositoryTests
{
    [Fact]
    public async Task GetPublicationByIdAsync_LoadsBranchAvailabilityGraph()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new BusinessDbContext(options);
        var company = new Company { NameAr = "شركة" };
        var branch = new Branch
        {
            CompanyId = company.Id,
            Company = company,
            NameAr = "فرع",
            AddressAr = "عنوان"
        };
        var settings = new BranchAvailabilitySettings
        {
            BranchId = branch.Id,
            Branch = branch,
            TimeZoneId = "UTC",
            MinimumLeadMinutes = 30,
            BookingHorizonDays = 14
        };
        var schedule = new BranchRecurringSchedule
        {
            BranchId = branch.Id,
            Branch = branch,
            DayOfWeek = DayOfWeek.Friday,
            StartLocalTime = TimeSpan.FromHours(9),
            EndLocalTime = TimeSpan.FromHours(17),
            SlotDurationMinutes = 30,
            Capacity = 2
        };
        var availabilityOverride = new BranchAvailabilityOverride
        {
            BranchId = branch.Id,
            Branch = branch,
            OverrideDate = new DateOnly(2026, 8, 28),
            IsClosed = true
        };
        company.Branches.Add(branch);

        context.AddRange(company, branch, settings, schedule, availabilityOverride);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new CompanyRepository(context);

        var result = await repository.GetPublicationByIdAsync(company.Id);

        var publishedBranch = result!.Branches.Should().ContainSingle().Subject;
        publishedBranch.AvailabilitySettings.Should().NotBeNull();
        publishedBranch.RecurringSchedules.Should().ContainSingle();
        publishedBranch.AvailabilityOverrides.Should().ContainSingle();
    }

    [Fact]
    public async Task GetForUserAsync_WhenUserHasCompany_ReturnsCompanyWithBranches()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new BusinessDbContext(options);
        var user = new BusinessUser
        {
            Id = Guid.NewGuid(),
            UserName = "owner@test.com",
            Email = "owner@test.com",
            FullName = "Test Owner"
        };
        var company = new Company
        {
            NameAr = "شركة",
            NameHe = "חברה"
        };
        var branch = new Branch
        {
            CompanyId = company.Id,
            Company = company,
            NameAr = "فرع",
            NameHe = "סניף",
            AddressAr = "عنوان",
            AddressHe = "כתובת"
        };
        company.Branches.Add(branch);
        var assignment = new BusinessUserAssignment
        {
            UserId = user.Id,
            User = user,
            CompanyId = company.Id,
            Company = company,
            Role = BusinessMembershipRole.Owner
        };

        context.AddRange(user, company, branch, assignment);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new CompanyRepository(context);

        var result = await repository.GetForUserAsync(user.Id);

        result.Should().NotBeNull();
        result!.Branches.Should().ContainSingle(item => item.Id == branch.Id);
    }
}
