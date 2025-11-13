using FluentAssertions;
using GhseeliApis.Handlers;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Repositories.Interfaces;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for UserAddressHandler using Moq
/// </summary>
public class UserAddressHandlerTests
{
    private readonly Mock<IUserAddressRepository> _mockRepository;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly UserAddressHandler _handler;
    private readonly Guid _testUserId;

    public UserAddressHandlerTests()
    {
        _mockRepository = new Mock<IUserAddressRepository>();
        _mockLogger = new Mock<IAppLogger>();
        _handler = new UserAddressHandler(_mockRepository.Object, _mockLogger.Object);
        _testUserId = Guid.NewGuid();
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsUserAddresses()
    {
        // Arrange
        var addresses = new List<UserAddress>
        {
            new UserAddress { Id = Guid.NewGuid(), UserId = _testUserId, AddressLine = "123 Main St" },
            new UserAddress { Id = Guid.NewGuid(), UserId = _testUserId, AddressLine = "456 Oak Ave" }
        };
        _mockRepository.Setup(r => r.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(addresses);

        // Act
        var result = await _handler.GetByUserIdAsync(_testUserId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(a => a.UserId.Should().Be(_testUserId));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsAddress_WhenExists()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var address = new UserAddress
        {
            Id = addressId,
            UserId = _testUserId,
            AddressLine = "123 Main St"
        };
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(address);

        // Act
        var result = await _handler.GetByIdAsync(addressId);

        // Assert
        result.Should().NotBeNull();
        result!.AddressLine.Should().Be("123 Main St");
    }

    [Fact]
    public async Task GetPrimaryAddressAsync_ReturnsPrimaryAddress()
    {
        // Arrange
        var address = new UserAddress
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            AddressLine = "456 Oak Ave",
            IsPrimary = true
        };
        _mockRepository.Setup(r => r.GetPrimaryAddressAsync(_testUserId))
            .ReturnsAsync(address);

        // Act
        var result = await _handler.GetPrimaryAddressAsync(_testUserId);

        // Assert
        result.Should().NotBeNull();
        result!.IsPrimary.Should().BeTrue();
        result.AddressLine.Should().Be("456 Oak Ave");
    }

    [Fact]
    public async Task CreateAsync_CreatesAddress_WithValidData()
    {
        // Arrange
        var address = new UserAddress { AddressLine = "123 Main St", City = "Test City" };
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<UserAddress>()))
            .ReturnsAsync((UserAddress a) =>
            {
                a.Id = Guid.NewGuid();
                return a;
            });

        // Act
        var created = await _handler.CreateAsync(address, _testUserId);

        // Assert
        created.Should().NotBeNull();
        created.Id.Should().NotBe(Guid.Empty);
        created.UserId.Should().Be(_testUserId);
    }

    [Fact]
    public async Task CreateAsync_SetsUserId_OverridingProvidedValue()
    {
        // Arrange
        var address = new UserAddress { AddressLine = "123 Main St" };
        var expectedUserId = Guid.NewGuid();
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<UserAddress>()))
            .ReturnsAsync((UserAddress a) => a);

        // Act
        var created = await _handler.CreateAsync(address, expectedUserId);

        // Assert
        created.UserId.Should().Be(expectedUserId);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenAddressDoesNotExist()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync((UserAddress?)null);

        // Act
        var result = await _handler.UpdateAsync(addressId, new UserAddress(), _testUserId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenUserDoesNotOwnAddress()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var address = new UserAddress { Id = addressId, UserId = Guid.NewGuid() };
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(address);

        // Act
        var result = await _handler.UpdateAsync(addressId, new UserAddress(), _testUserId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesAddress_WhenUserOwnsIt()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var existingAddress = new UserAddress
        {
            Id = addressId,
            UserId = _testUserId,
            AddressLine = "Old Address"
        };
        var updateData = new UserAddress
        {
            AddressLine = "New Address",
            City = "New City"
        };
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(existingAddress);
        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<UserAddress>()))
            .ReturnsAsync((UserAddress a) => a);

        // Act
        var result = await _handler.UpdateAsync(addressId, updateData, _testUserId);

        // Assert
        result.Should().NotBeNull();
        result!.AddressLine.Should().Be("New Address");
        result.City.Should().Be("New City");
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenAddressDoesNotExist()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync((UserAddress?)null);

        // Act
        var result = await _handler.DeleteAsync(addressId, _testUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenUserDoesNotOwnAddress()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var address = new UserAddress { Id = addressId, UserId = Guid.NewGuid() };
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(address);

        // Act
        var result = await _handler.DeleteAsync(addressId, _testUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_DeletesAddress_WhenValid()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var address = new UserAddress { Id = addressId, UserId = _testUserId };
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(address);
        _mockRepository.Setup(r => r.HasActiveBookingsAsync(addressId))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.DeleteAsync(It.IsAny<UserAddress>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.DeleteAsync(addressId, _testUserId);

        // Assert
        result.Should().BeTrue();
        _mockRepository.Verify(r => r.DeleteAsync(address), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsException_WhenAddressHasActiveBookings()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var address = new UserAddress { Id = addressId, UserId = _testUserId };
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(address);
        _mockRepository.Setup(r => r.HasActiveBookingsAsync(addressId))
            .ReturnsAsync(true);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.DeleteAsync(addressId, _testUserId));
    }

    [Fact]
    public async Task SetAsPrimaryAsync_SetsPrimaryAddress()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var address = new UserAddress { Id = addressId, UserId = _testUserId };
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(address);
        _mockRepository.Setup(r => r.SetAsPrimaryAsync(addressId, _testUserId))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.SetAsPrimaryAsync(addressId, _testUserId);

        // Assert
        result.Should().BeTrue();
        _mockRepository.Verify(r => r.SetAsPrimaryAsync(addressId, _testUserId), Times.Once);
    }

    [Fact]
    public async Task SetAsPrimaryAsync_ReturnsFalse_WhenAddressDoesNotExist()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync((UserAddress?)null);

        // Act
        var result = await _handler.SetAsPrimaryAsync(addressId, _testUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetAsPrimaryAsync_ReturnsFalse_WhenUserDoesNotOwnAddress()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var address = new UserAddress { Id = addressId, UserId = Guid.NewGuid() };
        _mockRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(address);

        // Act
        var result = await _handler.SetAsPrimaryAsync(addressId, _testUserId);

        // Assert
        result.Should().BeFalse();
    }
}
