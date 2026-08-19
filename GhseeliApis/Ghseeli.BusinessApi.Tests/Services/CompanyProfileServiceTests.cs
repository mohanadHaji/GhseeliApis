using FluentAssertions;
using FluentValidation;
using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Validation.Companies;
using Ghseeli.BusinessApi.Validators.Companies;
using Ghseeli.Common.Logging;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Defines company ownership enforcement for business users.
/// </summary>
public class CompanyProfileServiceTests
{
    private readonly Mock<ICompanyRepository> _repository = new();
    private readonly Mock<IAppLogger> _logger = new();
    private readonly CompanyProfileService _service;

    public CompanyProfileServiceTests()
    {
        _service = new CompanyProfileService(
            _repository.Object,
            new CompanyRequestValidator(
                new UpdateCompanyProfileRequestValidator(),
                new CreateBranchRequestValidator(),
                new UpdateBranchRequestValidator()),
            _logger.Object);
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
            Phone = "0500000000"
        });

        result.NameAr.Should().Be("جديد");
        result.NameHe.Should().BeNull();
        result.Phone.Should().Be("0500000000");
        _repository.Verify(repository => repository.UpdateAsync(company), Times.Once);
        _logger.Verify(logger => logger.LogInfo(
            It.Is<string>(message =>
                message.Contains("profile updated", StringComparison.OrdinalIgnoreCase) &&
                message.Contains(company.Id.ToString(), StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }

    [Fact]
    public async Task CreateBranchAsync_WhenHebrewFieldsAreOmitted_CreatesBranch()
    {
        var userId = Guid.NewGuid();
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة"
        };
        Branch? persistedBranch = null;

        _repository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(company);
        _repository.Setup(repository => repository.AddBranchAsync(It.IsAny<Branch>()))
            .Callback<Branch>(branch => persistedBranch = branch)
            .ReturnsAsync((Branch branch) => branch);

        var result = await _service.CreateBranchAsync(userId, new CreateBranchRequest
        {
            NameAr = "فرع رئيسي",
            AddressAr = "الرياض"
        });

        result.NameAr.Should().Be("فرع رئيسي");
        result.NameHe.Should().BeNull();
        result.AddressAr.Should().Be("الرياض");
        result.AddressHe.Should().BeNull();
        persistedBranch.Should().NotBeNull();
        persistedBranch!.NameHe.Should().BeNull();
        persistedBranch.AddressHe.Should().BeNull();
    }

    [Fact]
    public async Task UpdateBranchAsync_WhenHebrewFieldsAreOmitted_UpdatesBranch()
    {
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var branch = new Branch
        {
            Id = branchId,
            CompanyId = Guid.NewGuid(),
            NameAr = "قديم",
            NameHe = "ישן",
            AddressAr = "العنوان القديم",
            AddressHe = "כתובת ישנה",
            IsActive = true
        };

        _repository.Setup(repository => repository.GetBranchForUserAsync(userId, branchId))
            .ReturnsAsync(branch);
        _repository.Setup(repository => repository.UpdateBranchAsync(branch))
            .ReturnsAsync(branch);

        var result = await _service.UpdateBranchAsync(userId, branchId, new UpdateBranchRequest
        {
            NameAr = "فرع محدّث",
            AddressAr = "العنوان الجديد",
            IsActive = false
        });

        result.NameAr.Should().Be("فرع محدّث");
        result.NameHe.Should().BeNull();
        result.AddressAr.Should().Be("العنوان الجديد");
        result.AddressHe.Should().BeNull();
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetMyCompanyAsync_WhenUserHasNoAssignment_RejectsAccess()
    {
        var userId = Guid.NewGuid();
        _repository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync((Company?)null);

        var action = () => _service.GetMyCompanyAsync(userId);

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        _logger.Verify(logger => logger.LogWarning(
            It.Is<string>(message =>
                message.Contains("no active company assignment", StringComparison.OrdinalIgnoreCase) &&
                message.Contains(userId.ToString(), StringComparison.OrdinalIgnoreCase))),
            Times.Once);
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
        _logger.Verify(logger => logger.LogWarning(
            It.Is<string>(message =>
                message.Contains("branch update rejected", StringComparison.OrdinalIgnoreCase) &&
                message.Contains(branchId.ToString(), StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }

    [Fact]
    public async Task UpdateMyCompanyAsync_WhenArabicNameIsMissing_ThrowsValidationException()
    {
        var userId = Guid.NewGuid();

        var action = () => _service.UpdateMyCompanyAsync(userId, new UpdateCompanyProfileRequest
        {
            NameAr = "  "
        });

        await action.Should().ThrowAsync<ValidationException>()
            .Where(exception => exception.Errors.Any(error => error.PropertyName == nameof(UpdateCompanyProfileRequest.NameAr)));
        _repository.Verify(repository => repository.GetForUserAsync(It.IsAny<Guid>()), Times.Never);
    }
}
