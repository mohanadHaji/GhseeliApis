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
        context.BusinessVerticals.Add(new BusinessVertical
        {
            Id = BusinessVerticalDefaults.CarWashId,
            Code = BusinessVerticalDefaults.CarWashCode,
            NameAr = "غسيل السيارات",
            IsActive = true,
            RegistrationEnabled = true
        });
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
    public async Task GetPublicationByIdAsync_WhenCompanyHasNoActiveCarWashAssignment_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new BusinessDbContext(options);
        var futureVertical = new BusinessVertical
        {
            Id = Guid.NewGuid(),
            Code = "mechanics",
            NameAr = "ميكانيكا",
            IsActive = true
        };
        var company = new Company { NameAr = "شركة" };
        company.BusinessVerticals.Add(new CompanyBusinessVertical
        {
            BusinessVertical = futureVertical,
            BusinessVerticalId = futureVertical.Id,
            IsPrimary = true,
            IsActive = true
        });
        context.Add(company);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await new CompanyRepository(context)
            .GetPublicationByIdAsync(company.Id);

        result.Should().BeNull();
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

    [Fact]
    public async Task CreateForOwnerAsync_WhenCarWashRegistrationIsDisabled_RejectsCompany()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new BusinessDbContext(options);
        context.BusinessVerticals.Add(new BusinessVertical
        {
            Id = BusinessVerticalDefaults.CarWashId,
            Code = BusinessVerticalDefaults.CarWashCode,
            NameAr = "غسيل السيارات",
            IsActive = true,
            RegistrationEnabled = false
        });
        await context.SaveChangesAsync();
        var company = new Company { NameAr = "شركة" };
        company.BusinessVerticals.Add(new CompanyBusinessVertical
        {
            BusinessVerticalId = BusinessVerticalDefaults.CarWashId,
            IsPrimary = true,
            IsActive = true
        });

        var action = () => new CompanyRepository(context).CreateForOwnerAsync(
            company,
            new BusinessUserAssignment
            {
                UserId = Guid.NewGuid(),
                CompanyId = company.Id,
                Role = BusinessMembershipRole.Owner
            });

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*registration*disabled*");
        context.Companies.Should().BeEmpty();
    }
}
