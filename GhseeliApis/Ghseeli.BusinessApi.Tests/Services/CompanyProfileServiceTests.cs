using FluentAssertions;
using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Defines company ownership enforcement for business users.
/// </summary>
public class CompanyProfileServiceTests
{
    private readonly Mock<ICompanyRepository> _repository = new();
    private readonly CompanyProfileService _service;

    public CompanyProfileServiceTests()
    {
        _service = new CompanyProfileService(_repository.Object);
    }

    [Fact]
    public async Task GetMyCompanyAsync_ReturnsOnlyAssignedCompany()
    {
        var userId = Guid.NewGuid();
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            NameHe = "חברה"
        };
        _repository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(company);

        var result = await _service.GetMyCompanyAsync(userId);

        result.Id.Should().Be(company.Id);
        result.NameAr.Should().Be(company.NameAr);
        result.NameHe.Should().Be(company.NameHe);
    }

    [Fact]
    public async Task UpdateMyCompanyAsync_UpdatesOnlyAssignedCompany()
    {
        var userId = Guid.NewGuid();
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "قديم",
            NameHe = "ישן"
        };
        _repository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(company);
        _repository.Setup(repository => repository.UpdateAsync(company))
            .ReturnsAsync(company);

        var result = await _service.UpdateMyCompanyAsync(userId, new UpdateCompanyProfileRequest
        {
            NameAr = "جديد",
            NameHe = "חדש",
            Phone = "0500000000"
        });

        result.NameAr.Should().Be("جديد");
        result.NameHe.Should().Be("חדש");
        result.Phone.Should().Be("0500000000");
        _repository.Verify(repository => repository.UpdateAsync(company), Times.Once);
    }

    [Fact]
    public async Task GetMyCompanyAsync_WhenUserHasNoAssignment_RejectsAccess()
    {
        var userId = Guid.NewGuid();
        _repository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync((Company?)null);

        var action = () => _service.GetMyCompanyAsync(userId);

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task UpdateBranchAsync_WhenBranchIsNotAssignedToUser_RejectsAccess()
    {
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        _repository.Setup(repository => repository.GetBranchForUserAsync(userId, branchId))
            .ReturnsAsync((Branch?)null);

        var action = () => _service.UpdateBranchAsync(userId, branchId, new UpdateBranchRequest
        {
            NameAr = "فرع",
            NameHe = "סניף",
            AddressAr = "العنوان",
            AddressHe = "כתובת"
        });

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        _repository.Verify(repository => repository.UpdateBranchAsync(It.IsAny<Branch>()), Times.Never);
    }
}
