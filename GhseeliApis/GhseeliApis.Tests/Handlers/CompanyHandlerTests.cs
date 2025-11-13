using FluentAssertions;
using GhseeliApis.Handlers;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for CompanyHandler using Moq
/// </summary>
public class CompanyHandlerTests
{
    private readonly Mock<ICompanyRepository> _mockRepository;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly CompanyHandler _handler;

    public CompanyHandlerTests()
    {
        _mockRepository = new Mock<ICompanyRepository>();
        _mockLogger = new Mock<IAppLogger>();
        _handler = new CompanyHandler(_mockRepository.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllCompanies()
    {
        // Arrange
        var companies = new List<Company>
        {
            new Company { Id = Guid.NewGuid(), Name = "ABC Wash" },
            new Company { Id = Guid.NewGuid(), Name = "XYZ Wash" }
        };
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(companies);

        // Act
        var result = await _handler.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCompany_WhenExists()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var company = new Company { Id = companyId, Name = "ABC Wash" };
        _mockRepository.Setup(r => r.GetByIdAsync(companyId))
            .ReturnsAsync(company);

        // Act
        var result = await _handler.GetByIdAsync(companyId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("ABC Wash");
    }

    [Fact]
    public async Task CreateAsync_CreatesCompany_WithValidData()
    {
        // Arrange
        var company = new Company { Name = "ABC Wash", Phone = "555-1234" };
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<Company>()))
            .ReturnsAsync((Company c) =>
            {
                c.Id = Guid.NewGuid();
                return c;
            });

        // Act
        var created = await _handler.CreateAsync(company);

        // Assert
        created.Should().NotBeNull();
        created.Id.Should().NotBe(Guid.Empty);
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<Company>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenValidationFails()
    {
        // Arrange
        var company = new Company { Name = "" }; // Invalid

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(company));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesCompany_WhenExists()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var existingCompany = new Company { Id = companyId, Name = "Original" };
        var updateData = new Company { Name = "Updated Name" };
        
        _mockRepository.Setup(r => r.GetByIdAsync(companyId))
            .ReturnsAsync(existingCompany);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<Company>()))
            .ReturnsAsync((Company c) => c);

        // Act
        var updated = await _handler.UpdateAsync(companyId, updateData);

        // Assert
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenCompanyDoesNotExist()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(companyId))
            .ReturnsAsync((Company?)null);

        // Act
        var result = await _handler.UpdateAsync(companyId, new Company { Name = "Test" });

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_DeletesCompany_WhenExists()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var company = new Company { Id = companyId, Name = "ABC Wash" };
        _mockRepository.Setup(r => r.GetByIdAsync(companyId))
            .ReturnsAsync(company);
        _mockRepository.Setup(r => r.DeleteAsync(It.IsAny<Company>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.DeleteAsync(companyId);

        // Assert
        result.Should().BeTrue();
        _mockRepository.Verify(r => r.DeleteAsync(company), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenCompanyDoesNotExist()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(companyId))
            .ReturnsAsync((Company?)null);

        // Act
        var result = await _handler.DeleteAsync(companyId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetByServiceAreaAsync_ReturnsMatchingCompanies()
    {
        // Arrange
        var companies = new List<Company>
        {
            new Company { Id = Guid.NewGuid(), Name = "ABC Wash", ServiceAreaDescription = "Downtown" }
        };
        _mockRepository.Setup(r => r.GetByServiceAreaAsync("Downtown"))
            .ReturnsAsync(companies);

        // Act
        var result = await _handler.GetByServiceAreaAsync("Downtown");

        // Assert
        result.Should().HaveCount(1);
        result.First().Name.Should().Be("ABC Wash");
    }
}
