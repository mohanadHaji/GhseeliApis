using FluentAssertions;
using GhseeliApis.Handlers;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for ServiceHandler using Moq
/// </summary>
public class ServiceHandlerTests
{
    private readonly Mock<IServiceRepository> _mockRepository;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly ServiceHandler _handler;

    public ServiceHandlerTests()
    {
        _mockRepository = new Mock<IServiceRepository>();
        _mockLogger = new Mock<IAppLogger>();
        _handler = new ServiceHandler(_mockRepository.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllServices()
    {
        // Arrange
        var services = new List<Service>
        {
            new Service { Id = Guid.NewGuid(), Name = "Basic Wash" },
            new Service { Id = Guid.NewGuid(), Name = "Premium Wash" }
        };
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(services);

        // Act
        var result = await _handler.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsService_WhenExists()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        var service = new Service { Id = serviceId, Name = "Basic Wash" };
        _mockRepository.Setup(r => r.GetByIdAsync(serviceId))
            .ReturnsAsync(service);

        // Act
        var result = await _handler.GetByIdAsync(serviceId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Basic Wash");
    }

    [Fact]
    public async Task GetByIdWithOptionsAsync_ReturnsServiceWithOptions()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        var service = new Service
        {
            Id = serviceId,
            Name = "Basic Wash",
            Options = new List<ServiceOption>
            {
                new ServiceOption { Id = Guid.NewGuid(), Name = "Standard Package" }
            }
        };
        _mockRepository.Setup(r => r.GetByIdWithOptionsAsync(serviceId))
            .ReturnsAsync(service);

        // Act
        var result = await _handler.GetByIdWithOptionsAsync(serviceId);

        // Assert
        result.Should().NotBeNull();
        result!.Options.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateAsync_CreatesService_WithValidData()
    {
        // Arrange
        var service = new Service { Name = "Basic Wash", Description = "Standard wash" };
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<Service>()))
            .ReturnsAsync((Service s) =>
            {
                s.Id = Guid.NewGuid();
                return s;
            });

        // Act
        var created = await _handler.CreateAsync(service);

        // Assert
        created.Should().NotBeNull();
        created.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenValidationFails()
    {
        // Arrange
        var service = new Service { Name = "" }; // Invalid

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(service));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesService_WhenExists()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        var existingService = new Service { Id = serviceId, Name = "Original" };
        var updateData = new Service { Name = "Updated" };
        
        _mockRepository.Setup(r => r.GetByIdAsync(serviceId))
            .ReturnsAsync(existingService);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<Service>()))
            .ReturnsAsync((Service s) => s);

        // Act
        var updated = await _handler.UpdateAsync(serviceId, updateData);

        // Assert
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteAsync_DeletesService()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        var service = new Service { Id = serviceId, Name = "Basic Wash" };
        _mockRepository.Setup(r => r.GetByIdAsync(serviceId))
            .ReturnsAsync(service);
        _mockRepository.Setup(r => r.DeleteAsync(It.IsAny<Service>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.DeleteAsync(serviceId);

        // Assert
        result.Should().BeTrue();
    }
}
