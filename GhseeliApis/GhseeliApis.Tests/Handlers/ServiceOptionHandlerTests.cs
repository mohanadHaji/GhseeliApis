using FluentAssertions;
using GhseeliApis.Handlers;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for ServiceOptionHandler using Moq
/// </summary>
public class ServiceOptionHandlerTests
{
    private readonly Mock<IServiceOptionRepository> _mockRepository;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly ServiceOptionHandler _handler;
    private readonly Guid _testServiceId;
    private readonly Guid _testCompanyId;

    public ServiceOptionHandlerTests()
    {
        _mockRepository = new Mock<IServiceOptionRepository>();
        _mockLogger = new Mock<IAppLogger>();
        _handler = new ServiceOptionHandler(_mockRepository.Object, _mockLogger.Object);
        
        _testServiceId = Guid.NewGuid();
        _testCompanyId = Guid.NewGuid();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllServiceOptions()
    {
        // Arrange
        var options = new List<ServiceOption>
        {
            new ServiceOption { Id = Guid.NewGuid(), Name = "Standard" },
            new ServiceOption { Id = Guid.NewGuid(), Name = "Premium" }
        };
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(options);

        // Act
        var result = await _handler.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsServiceOption_WhenExists()
    {
        // Arrange
        var optionId = Guid.NewGuid();
        var option = new ServiceOption { Id = optionId, Name = "Standard Package" };
        _mockRepository.Setup(r => r.GetByIdAsync(optionId))
            .ReturnsAsync(option);

        // Act
        var result = await _handler.GetByIdAsync(optionId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Standard Package");
    }

    [Fact]
    public async Task GetByServiceIdAsync_ReturnsOptionsForService()
    {
        // Arrange
        var options = new List<ServiceOption>
        {
            new ServiceOption { Id = Guid.NewGuid(), ServiceId = _testServiceId, Name = "Option 1" },
            new ServiceOption { Id = Guid.NewGuid(), ServiceId = _testServiceId, Name = "Option 2" }
        };
        _mockRepository.Setup(r => r.GetByServiceIdAsync(_testServiceId))
            .ReturnsAsync(options);

        // Act
        var result = await _handler.GetByServiceIdAsync(_testServiceId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(o => o.ServiceId.Should().Be(_testServiceId));
    }

    [Fact]
    public async Task GetByCompanyIdAsync_ReturnsOptionsForCompany()
    {
        // Arrange
        var options = new List<ServiceOption>
        {
            new ServiceOption { Id = Guid.NewGuid(), CompanyId = _testCompanyId, Name = "Option 1" }
        };
        _mockRepository.Setup(r => r.GetByCompanyIdAsync(_testCompanyId))
            .ReturnsAsync(options);

        // Act
        var result = await _handler.GetByCompanyIdAsync(_testCompanyId);

        // Assert
        result.Should().HaveCount(1);
        result.First().CompanyId.Should().Be(_testCompanyId);
    }

    [Fact]
    public async Task CreateAsync_CreatesServiceOption_WithValidData()
    {
        // Arrange
        var option = new ServiceOption
        {
            ServiceId = _testServiceId,
            CompanyId = _testCompanyId,
            Name = "Standard",
            Price = 25.00m,
            DurationMinutes = 30
        };
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<ServiceOption>()))
            .ReturnsAsync((ServiceOption o) =>
            {
                o.Id = Guid.NewGuid();
                return o;
            });

        // Act
        var created = await _handler.CreateAsync(option);

        // Assert
        created.Should().NotBeNull();
        created.Id.Should().NotBe(Guid.Empty);
        created.Price.Should().Be(25.00m);
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenPriceIsNegative()
    {
        // Arrange
        var option = new ServiceOption
        {
            ServiceId = _testServiceId,
            Name = "Test",
            Price = -10m,  // Invalid
            DurationMinutes = 30
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(option));
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenDurationIsZero()
    {
        // Arrange
        var option = new ServiceOption
        {
            ServiceId = _testServiceId,
            Name = "Test",
            Price = 25.00m,
            DurationMinutes = 0  // Invalid
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(option));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesServiceOption_WhenExists()
    {
        // Arrange
        var optionId = Guid.NewGuid();
        var existingOption = new ServiceOption
        {
            Id = optionId,
            Name = "Original",
            Price = 25.00m,
            DurationMinutes = 30
        };
        var updateData = new ServiceOption
        {
            Name = "Updated",
            Price = 35.00m,
            DurationMinutes = 45
        };
        
        _mockRepository.Setup(r => r.GetByIdAsync(optionId))
            .ReturnsAsync(existingOption);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<ServiceOption>()))
            .ReturnsAsync((ServiceOption o) => o);

        // Act
        var updated = await _handler.UpdateAsync(optionId, updateData);

        // Assert
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated");
        updated.Price.Should().Be(35.00m);
        updated.DurationMinutes.Should().Be(45);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenServiceOptionDoesNotExist()
    {
        // Arrange
        var optionId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(optionId))
            .ReturnsAsync((ServiceOption?)null);

        // Act
        var result = await _handler.UpdateAsync(optionId, new ServiceOption { Name = "Test", Price = 25, DurationMinutes = 30 });

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_DeletesServiceOption()
    {
        // Arrange
        var optionId = Guid.NewGuid();
        var option = new ServiceOption { Id = optionId, Name = "Test" };
        _mockRepository.Setup(r => r.GetByIdAsync(optionId))
            .ReturnsAsync(option);
        _mockRepository.Setup(r => r.DeleteAsync(It.IsAny<ServiceOption>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.DeleteAsync(optionId);

        // Assert
        result.Should().BeTrue();
        _mockRepository.Verify(r => r.DeleteAsync(option), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenServiceOptionDoesNotExist()
    {
        // Arrange
        var optionId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(optionId))
            .ReturnsAsync((ServiceOption?)null);

        // Act
        var result = await _handler.DeleteAsync(optionId);

        // Assert
        result.Should().BeFalse();
    }
}
