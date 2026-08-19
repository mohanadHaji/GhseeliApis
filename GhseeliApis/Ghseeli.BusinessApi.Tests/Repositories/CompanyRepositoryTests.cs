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
