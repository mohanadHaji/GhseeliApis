using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Service;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for ServicesController
/// </summary>
public class ServicesControllerTests
{
    private readonly Mock<IServiceHandler> _mockServiceHandler;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly ServicesController _controller;
    private readonly Guid _testUserId;

    public ServicesControllerTests()
    {
        _mockServiceHandler = new Mock<IServiceHandler>();
        _mockLogger = new Mock<IAppLogger>();
        _controller = new ServicesController(_mockServiceHandler.Object, _mockLogger.Object);
        _testUserId = Guid.NewGuid();

        SetupAuthenticatedUser(_testUserId);
    }

    private void SetupAuthenticatedUser(Guid userId, string role = "User")
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };
    }

    private static Service CreateTestService()
    {
        return new Service
        {
            Id = Guid.NewGuid(),
            Name = "Basic Wash",
            Description = "Standard car wash service"
        };
    }

    #region GetAll Tests

    [Fact]
    public async Task GetAll_ReturnsOk_WithAllServices()
    {
        // Arrange
        var services = new List<Service>
        {
            CreateTestService(),
            CreateTestService()
        };

        _mockServiceHandler.Setup(h => h.GetAllAsync())
            .ReturnsAsync(services);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<ServiceResponse>>().Subject;
        response.Should().HaveCount(2);

        _mockServiceHandler.Verify(h => h.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAll_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockServiceHandler.Setup(h => h.GetAllAsync())
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetAll();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_ReturnsOk_WithService_WhenServiceExists()
    {
        // Arrange
        var service = CreateTestService();
        _mockServiceHandler.Setup(h => h.GetByIdAsync(service.Id))
            .ReturnsAsync(service);

        // Act
        var result = await _controller.GetById(service.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ServiceResponse>().Subject;
        response.Id.Should().Be(service.Id);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenServiceDoesNotExist()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        _mockServiceHandler.Setup(h => h.GetByIdAsync(serviceId))
            .ReturnsAsync((Service?)null);

        // Act
        var result = await _controller.GetById(serviceId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    #endregion

    #region GetWithOptions Tests

    [Fact]
    public async Task GetWithOptions_ReturnsOk_WithServiceAndOptions()
    {
        // Arrange
        var service = CreateTestService();
        service.Options = new List<ServiceOption>
        {
            new ServiceOption { Id = Guid.NewGuid(), Name = "Standard Package" }
        };

        _mockServiceHandler.Setup(h => h.GetByIdWithOptionsAsync(service.Id))
            .ReturnsAsync(service);

        // Act
        var result = await _controller.GetWithOptions(service.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ServiceResponse>().Subject;
        response.OptionCount.Should().Be(1);
    }

    [Fact]
    public async Task GetWithOptions_ReturnsNotFound_WhenServiceDoesNotExist()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        _mockServiceHandler.Setup(h => h.GetByIdWithOptionsAsync(serviceId))
            .ReturnsAsync((Service?)null);

        // Act
        var result = await _controller.GetWithOptions(serviceId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    #endregion

    #region Create Tests (Company or Admin)

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenServiceIsCreatedSuccessfully_AsAdmin()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var request = new CreateServiceRequest
        {
            Name = "Premium Wash",
            Description = "Premium car wash"
        };

        var createdService = new Service
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description
        };

        _mockServiceHandler.Setup(h => h.CreateAsync(It.IsAny<Service>()))
            .ReturnsAsync(createdService);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(ServicesController.GetById));
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenServiceIsCreatedSuccessfully_AsCompany()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company");
        var request = new CreateServiceRequest
        {
            Name = "Premium Wash",
            Description = "Premium car wash"
        };

        var createdService = new Service
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description
        };

        _mockServiceHandler.Setup(h => h.CreateAsync(It.IsAny<Service>()))
            .ReturnsAsync(createdService);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(ServicesController.GetById));
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var request = new CreateServiceRequest
        {
            Name = "Duplicate Service",
            Description = "Test"
        };

        _mockServiceHandler.Setup(h => h.CreateAsync(It.IsAny<Service>()))
            .ThrowsAsync(new InvalidOperationException("Service already exists"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }

    #endregion

    #region Update Tests (Company or Admin)

    [Fact]
    public async Task Update_ReturnsOk_WhenServiceIsUpdatedSuccessfully_AsAdmin()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            Name = "Updated Service",
            Description = "Updated description"
        };

        var updatedService = new Service
        {
            Id = serviceId,
            Name = request.Name,
            Description = request.Description
        };

        _mockServiceHandler.Setup(h => h.UpdateAsync(serviceId, It.IsAny<Service>()))
            .ReturnsAsync(updatedService);

        // Act
        var result = await _controller.Update(serviceId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ServiceResponse>().Subject;
        response.Name.Should().Be(request.Name);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenServiceDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            Name = "Updated Service"
        };

        _mockServiceHandler.Setup(h => h.UpdateAsync(serviceId, It.IsAny<Service>()))
            .ReturnsAsync((Service?)null);

        // Act
        var result = await _controller.Update(serviceId, request);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    #endregion

    #region Delete Tests (Admin Only)

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenServiceIsDeletedSuccessfully()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceId = Guid.NewGuid();
        _mockServiceHandler.Setup(h => h.DeleteAsync(serviceId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Delete(serviceId);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenServiceDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceId = Guid.NewGuid();
        _mockServiceHandler.Setup(h => h.DeleteAsync(serviceId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Delete(serviceId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    #endregion
}
