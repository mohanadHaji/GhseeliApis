using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Address;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for AddressesController
/// </summary>
public class AddressesControllerTests
{
    private readonly Mock<IUserAddressHandler> _mockAddressHandler;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly AddressesController _controller;
    private readonly Guid _testUserId;

    public AddressesControllerTests()
    {
        _mockAddressHandler = new Mock<IUserAddressHandler>();
        _mockLogger = new Mock<IAppLogger>();
        _controller = new AddressesController(_mockAddressHandler.Object, _mockLogger.Object);
        _testUserId = Guid.NewGuid();

        SetupAuthenticatedUser(_testUserId);
    }

    private void SetupAuthenticatedUser(Guid userId)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Name, "Test User")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };
    }

    private static UserAddress CreateTestAddress(Guid? userId = null)
    {
        return new UserAddress
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            AddressLine = "123 Main St",
            City = "Test City",
            Area = "Test Area",
            Latitude = 40.7128,
            Longitude = -74.0060,
            IsPrimary = false
        };
    }

    #region GetMyAddresses Tests

    [Fact]
    public async Task GetMyAddresses_ReturnsOk_WithEmptyList_WhenNoAddressesExist()
    {
        // Arrange
        _mockAddressHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(new List<UserAddress>());

        // Act
        var result = await _controller.GetMyAddresses();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var addresses = okResult.Value.Should().BeAssignableTo<IEnumerable<AddressResponse>>().Subject;
        addresses.Should().BeEmpty();

        _mockAddressHandler.Verify(h => h.GetByUserIdAsync(_testUserId), Times.Once);
    }

    [Fact]
    public async Task GetMyAddresses_ReturnsOk_WithAddresses_WhenAddressesExist()
    {
        // Arrange
        var addresses = new List<UserAddress>
        {
            CreateTestAddress(_testUserId),
            CreateTestAddress(_testUserId)
        };
        addresses[0].IsPrimary = true;

        _mockAddressHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(addresses);

        // Act
        var result = await _controller.GetMyAddresses();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<AddressResponse>>().Subject.ToList();
        response.Should().HaveCount(2);
        response.Should().Contain(a => a.IsPrimary == true);
        response.Should().AllSatisfy(a => a.UserId.Should().Be(_testUserId));

        _mockAddressHandler.Verify(h => h.GetByUserIdAsync(_testUserId), Times.Once);
    }

    [Fact]
    public async Task GetMyAddresses_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockAddressHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetMyAddresses();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_ReturnsOk_WithAddress_WhenAddressExists()
    {
        // Arrange
        var address = CreateTestAddress(_testUserId);
        _mockAddressHandler.Setup(h => h.GetByIdAsync(address.Id))
            .ReturnsAsync(address);

        // Act
        var result = await _controller.GetById(address.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AddressResponse>().Subject;
        response.Id.Should().Be(address.Id);
        response.AddressLine.Should().Be(address.AddressLine);
        response.City.Should().Be(address.City);

        _mockAddressHandler.Verify(h => h.GetByIdAsync(address.Id), Times.Once);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenAddressDoesNotExist()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.GetByIdAsync(addressId))
            .ReturnsAsync((UserAddress?)null);

        // Act
        var result = await _controller.GetById(addressId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();

        _mockAddressHandler.Verify(h => h.GetByIdAsync(addressId), Times.Once);
    }

    [Fact]
    public async Task GetById_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.GetByIdAsync(addressId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetById(addressId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region Create Tests

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenAddressIsValid()
    {
        // Arrange
        var request = new CreateAddressRequest
        {
            AddressLine = "456 Oak Ave",
            City = "New City",
            Area = "Downtown",
            Latitude = 34.0522,
            Longitude = -118.2437,
            IsPrimary = false
        };

        var createdAddress = new UserAddress
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            AddressLine = request.AddressLine,
            City = request.City,
            Area = request.Area,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsPrimary = request.IsPrimary
        };

        _mockAddressHandler.Setup(h => h.CreateAsync(It.IsAny<UserAddress>(), _testUserId))
            .ReturnsAsync(createdAddress);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(AddressesController.GetById));
        
        var response = createdResult.Value.Should().BeOfType<AddressResponse>().Subject;
        response.Id.Should().Be(createdAddress.Id);
        response.AddressLine.Should().Be(request.AddressLine);
        response.City.Should().Be(request.City);

        _mockAddressHandler.Verify(h => h.CreateAsync(It.IsAny<UserAddress>(), _testUserId), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenValidationFails()
    {
        // Arrange
        var request = new CreateAddressRequest
        {
            AddressLine = "", // Invalid - required
            City = "Test City",
            Area = "Test Area"
        };

        // Act
        var result = await _controller.Create(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        _mockAddressHandler.Verify(h => h.CreateAsync(It.IsAny<UserAddress>(), It.IsAny<Guid>()), Times.Never);
        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var request = new CreateAddressRequest
        {
            AddressLine = "123 Main St",
            City = "Test City",
            Area = "Test Area"
        };

        _mockAddressHandler.Setup(h => h.CreateAsync(It.IsAny<UserAddress>(), _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task Update_ReturnsOk_WhenAddressIsUpdatedSuccessfully()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var request = new UpdateAddressRequest
        {
            AddressLine = "789 Updated St",
            City = "Updated City",
            Area = "Updated Area",
            Latitude = 40.7589,
            Longitude = -73.9851,
            IsPrimary = true
        };

        var updatedAddress = new UserAddress
        {
            Id = addressId,
            UserId = _testUserId,
            AddressLine = request.AddressLine,
            City = request.City,
            Area = request.Area,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsPrimary = request.IsPrimary
        };

        _mockAddressHandler.Setup(h => h.UpdateAsync(addressId, It.IsAny<UserAddress>(), _testUserId))
            .ReturnsAsync(updatedAddress);

        // Act
        var result = await _controller.Update(addressId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AddressResponse>().Subject;
        response.Id.Should().Be(addressId);
        response.AddressLine.Should().Be(request.AddressLine);
        response.IsPrimary.Should().BeTrue();

        _mockAddressHandler.Verify(h => h.UpdateAsync(addressId, It.IsAny<UserAddress>(), _testUserId), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenAddressDoesNotExist()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var request = new UpdateAddressRequest
        {
            AddressLine = "123 Main St",
            City = "Test City"
        };

        _mockAddressHandler.Setup(h => h.UpdateAsync(addressId, It.IsAny<UserAddress>(), _testUserId))
            .ReturnsAsync((UserAddress?)null);

        // Act
        var result = await _controller.Update(addressId, request);

        // Assert
        result.Should().BeOfType<NotFoundResult>();

        _mockAddressHandler.Verify(h => h.UpdateAsync(addressId, It.IsAny<UserAddress>(), _testUserId), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsBadRequest_WhenValidationFails()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var request = new UpdateAddressRequest
        {
            AddressLine = "", // Invalid
            City = "Test City"
        };

        // Act
        var result = await _controller.Update(addressId, request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        _mockAddressHandler.Verify(h => h.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UserAddress>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Update_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        var request = new UpdateAddressRequest
        {
            AddressLine = "123 Main St",
            City = "Test City"
        };

        _mockAddressHandler.Setup(h => h.UpdateAsync(addressId, It.IsAny<UserAddress>(), _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Update(addressId, request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenAddressIsDeletedSuccessfully()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.DeleteAsync(addressId, _testUserId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Delete(addressId);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        _mockAddressHandler.Verify(h => h.DeleteAsync(addressId, _testUserId), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenAddressDoesNotExist()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.DeleteAsync(addressId, _testUserId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Delete(addressId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();

        _mockAddressHandler.Verify(h => h.DeleteAsync(addressId, _testUserId), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.DeleteAsync(addressId, _testUserId))
            .ThrowsAsync(new InvalidOperationException("Cannot delete primary address"));

        // Act
        var result = await _controller.Delete(addressId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.DeleteAsync(addressId, _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Delete(addressId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region SetAsPrimary Tests

    [Fact]
    public async Task SetAsPrimary_ReturnsOk_WhenAddressIsSetAsPrimary()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.SetAsPrimaryAsync(addressId, _testUserId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.SetAsPrimary(addressId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();

        _mockAddressHandler.Verify(h => h.SetAsPrimaryAsync(addressId, _testUserId), Times.Once);
    }

    [Fact]
    public async Task SetAsPrimary_ReturnsNotFound_WhenAddressDoesNotExist()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.SetAsPrimaryAsync(addressId, _testUserId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.SetAsPrimary(addressId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SetAsPrimary_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var addressId = Guid.NewGuid();
        _mockAddressHandler.Setup(h => h.SetAsPrimaryAsync(addressId, _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.SetAsPrimary(addressId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion
}
