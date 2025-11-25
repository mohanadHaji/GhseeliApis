using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Vehicle;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for VehiclesController
/// </summary>
public class VehiclesControllerTests
{
    private readonly Mock<IVehicleHandler> _mockVehicleHandler;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly VehiclesController _controller;
    private readonly Guid _testUserId;

    public VehiclesControllerTests()
    {
        _mockVehicleHandler = new Mock<IVehicleHandler>();
        _mockLogger = new Mock<IAppLogger>();
        _controller = new VehiclesController(_mockVehicleHandler.Object, _mockLogger.Object);
        _testUserId = Guid.NewGuid();

        // Setup controller context with authenticated user
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

    private static Vehicle CreateTestVehicle(Guid? userId = null)
    {
        return new Vehicle
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            Make = "Toyota",
            Model = "Camry",
            Year = "2020",
            LicensePlate = "ABC123",
            Color = "Blue"
        };
    }

    #region GetMyVehicles Tests

    [Fact]
    public async Task GetMyVehicles_ReturnsOk_WithEmptyList_WhenNoVehiclesExist()
    {
        // Arrange
        _mockVehicleHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(new List<Vehicle>());

        // Act
        var result = await _controller.GetMyVehicles();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var vehicles = okResult.Value.Should().BeAssignableTo<IEnumerable<VehicleResponse>>().Subject;
        vehicles.Should().BeEmpty();

        _mockVehicleHandler.Verify(h => h.GetByUserIdAsync(_testUserId), Times.Once);
        _mockLogger.Verify(l => l.LogInfo(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetMyVehicles_ReturnsOk_WithVehicles_WhenVehiclesExist()
    {
        // Arrange
        var vehicles = new List<Vehicle>
        {
            CreateTestVehicle(_testUserId),
            CreateTestVehicle(_testUserId)
        };
        _mockVehicleHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(vehicles);

        // Act
        var result = await _controller.GetMyVehicles();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<VehicleResponse>>().Subject.ToList();
        response.Should().HaveCount(2);
        response.Should().AllSatisfy(v => v.UserId.Should().Be(_testUserId));

        _mockVehicleHandler.Verify(h => h.GetByUserIdAsync(_testUserId), Times.Once);
    }

    [Fact]
    public async Task GetMyVehicles_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockVehicleHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetMyVehicles();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
        statusCodeResult.Value.Should().Be("An error occurred while retrieving vehicles");

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_ReturnsOk_WithVehicle_WhenVehicleExists()
    {
        // Arrange
        var vehicle = CreateTestVehicle(_testUserId);
        _mockVehicleHandler.Setup(h => h.GetByIdAsync(vehicle.Id))
            .ReturnsAsync(vehicle);

        // Act
        var result = await _controller.GetById(vehicle.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<VehicleResponse>().Subject;
        response.Id.Should().Be(vehicle.Id);
        response.Make.Should().Be(vehicle.Make);
        response.Model.Should().Be(vehicle.Model);
        response.Year.Should().Be(vehicle.Year);

        _mockVehicleHandler.Verify(h => h.GetByIdAsync(vehicle.Id), Times.Once);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenVehicleDoesNotExist()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockVehicleHandler.Setup(h => h.GetByIdAsync(vehicleId))
            .ReturnsAsync((Vehicle?)null);

        // Act
        var result = await _controller.GetById(vehicleId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.Value.Should().NotBeNull();

        _mockVehicleHandler.Verify(h => h.GetByIdAsync(vehicleId), Times.Once);
        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetById_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockVehicleHandler.Setup(h => h.GetByIdAsync(vehicleId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetById(vehicleId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region Create Tests

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenVehicleIsValid()
    {
        // Arrange
        var request = new CreateVehicleRequest
        {
            Make = "Honda",
            Model = "Accord",
            Year = "2021",
            LicensePlate = "XYZ789",
            Color = "Red"
        };

        var createdVehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Make = request.Make,
            Model = request.Model,
            Year = request.Year,
            LicensePlate = request.LicensePlate,
            Color = request.Color
        };

        _mockVehicleHandler.Setup(h => h.CreateAsync(It.IsAny<Vehicle>(), _testUserId))
            .ReturnsAsync(createdVehicle);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(VehiclesController.GetById));
        
        var response = createdResult.Value.Should().BeOfType<VehicleResponse>().Subject;
        response.Id.Should().Be(createdVehicle.Id);
        response.Make.Should().Be(request.Make);
        response.Model.Should().Be(request.Model);

        _mockVehicleHandler.Verify(h => h.CreateAsync(It.IsAny<Vehicle>(), _testUserId), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenValidationFails()
    {
        // Arrange
        var request = new CreateVehicleRequest
        {
            Make = new string('X', 200), // Invalid - exceeds max length of 150
            Model = "Test",
            Year = "2021",
            LicensePlate = "ABC123"
        };

        // Act
        var result = await _controller.Create(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        _mockVehicleHandler.Verify(h => h.CreateAsync(It.IsAny<Vehicle>(), It.IsAny<Guid>()), Times.Never);
        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var request = new CreateVehicleRequest
        {
            Make = "Toyota",
            Model = "Camry",
            Year = "2020",
            LicensePlate = "ABC123",
            Color = "Blue"
        };

        _mockVehicleHandler.Setup(h => h.CreateAsync(It.IsAny<Vehicle>(), _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
        statusCodeResult.Value.Should().Be("An error occurred while creating the vehicle");

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task Update_ReturnsOk_WhenVehicleIsUpdatedSuccessfully()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var request = new UpdateVehicleRequest
        {
            Make = "Honda",
            Model = "Civic",
            Year = "2022",
            LicensePlate = "UPD123",
            Color = "Green"
        };

        var updatedVehicle = new Vehicle
        {
            Id = vehicleId,
            UserId = _testUserId,
            Make = request.Make,
            Model = request.Model,
            Year = request.Year,
            LicensePlate = request.LicensePlate,
            Color = request.Color
        };

        _mockVehicleHandler.Setup(h => h.UpdateAsync(vehicleId, It.IsAny<Vehicle>(), _testUserId))
            .ReturnsAsync(updatedVehicle);

        // Act
        var result = await _controller.Update(vehicleId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<VehicleResponse>().Subject;
        response.Id.Should().Be(vehicleId);
        response.Make.Should().Be(request.Make);

        _mockVehicleHandler.Verify(h => h.UpdateAsync(vehicleId, It.IsAny<Vehicle>(), _testUserId), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenVehicleDoesNotExist()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var request = new UpdateVehicleRequest
        {
            Make = "Honda",
            Model = "Civic",
            Year = "2022",
            LicensePlate = "UPD123",
            Color = "Green"
        };

        _mockVehicleHandler.Setup(h => h.UpdateAsync(vehicleId, It.IsAny<Vehicle>(), _testUserId))
            .ReturnsAsync((Vehicle?)null);

        // Act
        var result = await _controller.Update(vehicleId, request);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsBadRequest_WhenValidationFails()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var request = new UpdateVehicleRequest
        {
            Make = new string('X', 200), // Invalid - exceeds max length
            Model = "Test",
            Year = "2021",
            LicensePlate = "ABC123"
        };

        // Act
        var result = await _controller.Update(vehicleId, request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        _mockVehicleHandler.Verify(h => h.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Vehicle>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Update_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var request = new UpdateVehicleRequest
        {
            Make = "Toyota",
            Model = "Camry",
            Year = "2020",
            LicensePlate = "ABC123",
            Color = "Blue"
        };

        _mockVehicleHandler.Setup(h => h.UpdateAsync(vehicleId, It.IsAny<Vehicle>(), _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Update(vehicleId, request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenVehicleIsDeletedSuccessfully()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockVehicleHandler.Setup(h => h.DeleteAsync(vehicleId, _testUserId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Delete(vehicleId);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        _mockVehicleHandler.Verify(h => h.DeleteAsync(vehicleId, _testUserId), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenVehicleDoesNotExist()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockVehicleHandler.Setup(h => h.DeleteAsync(vehicleId, _testUserId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Delete(vehicleId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.Value.Should().NotBeNull();

        _mockVehicleHandler.Verify(h => h.DeleteAsync(vehicleId, _testUserId), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockVehicleHandler.Setup(h => h.DeleteAsync(vehicleId, _testUserId))
            .ThrowsAsync(new InvalidOperationException("Vehicle has active bookings"));

        // Act
        var result = await _controller.Delete(vehicleId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        _mockVehicleHandler.Setup(h => h.DeleteAsync(vehicleId, _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Delete(vehicleId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion
}
