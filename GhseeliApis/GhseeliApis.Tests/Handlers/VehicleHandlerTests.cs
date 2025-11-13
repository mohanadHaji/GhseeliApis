using FluentAssertions;
using GhseeliApis.Handlers;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Repositories.Interfaces;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for VehicleHandler using Moq
/// </summary>
public class VehicleHandlerTests
{
    private readonly Mock<IVehicleRepository> _mockRepository;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly VehicleHandler _handler;
    private readonly Guid _testUserId;

    public VehicleHandlerTests()
    {
        _mockRepository = new Mock<IVehicleRepository>();
        _mockLogger = new Mock<IAppLogger>();
        _handler = new VehicleHandler(_mockRepository.Object, _mockLogger.Object);
        _testUserId = Guid.NewGuid();
    }

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoVehiclesExist()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Vehicle>());

        // Act
        var vehicles = await _handler.GetAllAsync();

        // Assert
        vehicles.Should().BeEmpty();
        _mockRepository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllVehicles_WhenVehiclesExist()
    {
        // Arrange
        var vehicleList = new List<Vehicle>
        {
            new Vehicle { Id = Guid.NewGuid(), Make = "Toyota", Model = "Camry" },
            new Vehicle { Id = Guid.NewGuid(), Make = "Honda", Model = "Accord" }
        };
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(vehicleList);

        // Act
        var vehicles = await _handler.GetAllAsync();

        // Assert
        vehicles.Should().HaveCount(2);
        vehicles.Should().Contain(v => v.Make == "Toyota");
        vehicles.Should().Contain(v => v.Make == "Honda");
    }

    #endregion

    #region GetByUserIdAsync Tests

    [Fact]
    public async Task GetByUserIdAsync_ReturnsEmptyList_WhenUserHasNoVehicles()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(new List<Vehicle>());

        // Act
        var vehicles = await _handler.GetByUserIdAsync(_testUserId);

        // Assert
        vehicles.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOnlyUserVehicles()
    {
        // Arrange
        var user1Id = Guid.NewGuid();
        var vehicleList = new List<Vehicle>
        {
            new Vehicle { Id = Guid.NewGuid(), UserId = user1Id, Make = "Toyota" },
            new Vehicle { Id = Guid.NewGuid(), UserId = user1Id, Make = "Honda" }
        };
        _mockRepository.Setup(r => r.GetByUserIdAsync(user1Id))
            .ReturnsAsync(vehicleList);

        // Act
        var vehicles = await _handler.GetByUserIdAsync(user1Id);

        // Assert
        vehicles.Should().HaveCount(2);
        vehicles.Should().AllSatisfy(v => v.UserId.Should().Be(user1Id));
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenVehicleDoesNotExist()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync((Vehicle?)null);

        // Act
        var vehicle = await _handler.GetByIdAsync(vehicleId);

        // Assert
        vehicle.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsVehicle_WhenVehicleExists()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var vehicle = new Vehicle
        {
            Id = vehicleId,
            Make = "Toyota",
            Model = "Camry",
            Year = "2023"
        };
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(vehicle);

        // Act
        var result = await _handler.GetByIdAsync(vehicleId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(vehicleId);
        result.Make.Should().Be("Toyota");
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_CreatesVehicle_WithValidData()
    {
        // Arrange
        var vehicle = new Vehicle
        {
            Make = "Toyota",
            Model = "Camry",
            Year = "2023",
            LicensePlate = "ABC123"
        };
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<Vehicle>()))
            .ReturnsAsync((Vehicle v) =>
            {
                v.Id = Guid.NewGuid();
                return v;
            });

        // Act
        var created = await _handler.CreateAsync(vehicle, _testUserId);

        // Assert
        created.Should().NotBeNull();
        created.Id.Should().NotBe(Guid.Empty);
        created.UserId.Should().Be(_testUserId);
        created.Make.Should().Be("Toyota");
        _mockRepository.Verify(r => r.AddAsync(It.Is<Vehicle>(v => v.UserId == _testUserId)), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_SetsUserId_OverridingProvidedValue()
    {
        // Arrange
        var vehicle = new Vehicle { Make = "Toyota", Model = "Camry", Year = "2023" };
        var expectedUserId = Guid.NewGuid();
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<Vehicle>()))
            .ReturnsAsync((Vehicle v) => v);

        // Act
        var created = await _handler.CreateAsync(vehicle, expectedUserId);

        // Assert
        created.UserId.Should().Be(expectedUserId);
    }

    [Fact]
    public async Task CreateAsync_GeneratesNewId()
    {
        // Arrange
        var vehicle = new Vehicle { Make = "Toyota", Model = "Camry", Year = "2023" };
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<Vehicle>()))
            .ReturnsAsync((Vehicle v) =>
            {
                v.Id = Guid.NewGuid();
                return v;
            });

        // Act
        var created = await _handler.CreateAsync(vehicle, _testUserId);

        // Assert
        created.Id.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenVehicleDoesNotExist()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var vehicle = new Vehicle { Make = "Toyota" };
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync((Vehicle?)null);

        // Act
        var result = await _handler.UpdateAsync(vehicleId, vehicle, _testUserId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenUserDoesNotOwnVehicle()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var existingVehicle = new Vehicle { Id = vehicleId, UserId = ownerId };
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(existingVehicle);

        // Act
        var result = await _handler.UpdateAsync(vehicleId, new Vehicle(), Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesVehicle_WhenUserOwnsIt()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var existingVehicle = new Vehicle
        {
            Id = vehicleId,
            UserId = _testUserId,
            Make = "Toyota",
            Model = "Camry"
        };
        var updateData = new Vehicle
        {
            Make = "Honda",
            Model = "Accord",
            Year = "2024",
            Color = "Black"
        };

        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(existingVehicle);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<Vehicle>()))
            .ReturnsAsync((Vehicle v) => v);

        // Act
        var result = await _handler.UpdateAsync(vehicleId, updateData, _testUserId);

        // Assert
        result.Should().NotBeNull();
        result!.Make.Should().Be("Honda");
        result.Model.Should().Be("Accord");
        result.Year.Should().Be("2024");
        result.Color.Should().Be("Black");
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<Vehicle>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChangesToDatabase()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var existingVehicle = new Vehicle
        {
            Id = vehicleId,
            UserId = _testUserId,
            Make = "Toyota"
        };
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(existingVehicle);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<Vehicle>()))
            .ReturnsAsync((Vehicle v) => v);

        // Act
        await _handler.UpdateAsync(vehicleId, new Vehicle { Make = "Honda" }, _testUserId);

        // Assert
        _mockRepository.Verify(r => r.UpdateAsync(It.Is<Vehicle>(v => v.Make == "Honda")), Times.Once);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenVehicleDoesNotExist()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync((Vehicle?)null);

        // Act
        var result = await _handler.DeleteAsync(vehicleId, _testUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenUserDoesNotOwnVehicle()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var vehicle = new Vehicle { Id = vehicleId, UserId = Guid.NewGuid() };
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(vehicle);

        // Act
        var result = await _handler.DeleteAsync(vehicleId, _testUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrue_WhenVehicleDeleted()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var vehicle = new Vehicle { Id = vehicleId, UserId = _testUserId };
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(vehicle);
        _mockRepository.Setup(r => r.HasActiveBookingsAsync(vehicleId))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.DeleteAsync(It.IsAny<Vehicle>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.DeleteAsync(vehicleId, _testUserId);

        // Assert
        result.Should().BeTrue();
        _mockRepository.Verify(r => r.DeleteAsync(It.IsAny<Vehicle>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_RemovesVehicleFromDatabase()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var vehicle = new Vehicle { Id = vehicleId, UserId = _testUserId };
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(vehicle);
        _mockRepository.Setup(r => r.HasActiveBookingsAsync(vehicleId))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.DeleteAsync(It.IsAny<Vehicle>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.DeleteAsync(vehicleId, _testUserId);

        // Assert
        _mockRepository.Verify(r => r.DeleteAsync(vehicle), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsException_WhenVehicleHasActiveBookings()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var vehicle = new Vehicle { Id = vehicleId, UserId = _testUserId };
        _mockRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(vehicle);
        _mockRepository.Setup(r => r.HasActiveBookingsAsync(vehicleId))
            .ReturnsAsync(true);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.DeleteAsync(vehicleId, _testUserId));
        
        _mockRepository.Verify(r => r.DeleteAsync(It.IsAny<Vehicle>()), Times.Never);
    }

    #endregion

    #region CanDeleteAsync Tests

    [Fact]
    public async Task CanDeleteAsync_ReturnsTrue_WhenNoActiveBookings()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockRepository.Setup(r => r.HasActiveBookingsAsync(vehicleId))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.CanDeleteAsync(vehicleId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanDeleteAsync_ReturnsFalse_WhenActiveBookingsExist()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockRepository.Setup(r => r.HasActiveBookingsAsync(vehicleId))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.CanDeleteAsync(vehicleId);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
