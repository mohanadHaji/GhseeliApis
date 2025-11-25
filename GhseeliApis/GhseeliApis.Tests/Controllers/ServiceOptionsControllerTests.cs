using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.ServiceOption;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for ServiceOptionsController
/// </summary>
public class ServiceOptionsControllerTests
{
    private readonly Mock<IServiceOptionHandler> _mockServiceOptionHandler;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly ServiceOptionsController _controller;
    private readonly Guid _testUserId;

    public ServiceOptionsControllerTests()
    {
        _mockServiceOptionHandler = new Mock<IServiceOptionHandler>();
        _mockLogger = new Mock<IAppLogger>();
        _controller = new ServiceOptionsController(_mockServiceOptionHandler.Object, _mockLogger.Object);
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

    private static ServiceOption CreateTestServiceOption()
    {
        return new ServiceOption
        {
            Id = Guid.NewGuid(),
            ServiceId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Name = "Standard Package",
            Description = "Basic wash package",
            DurationMinutes = 30,
            Price = 50.00m
        };
    }

    #region GetAll Tests

    [Fact]
    public async Task GetAll_ReturnsOk_WithAllServiceOptions()
    {
        // Arrange
        var serviceOptions = new List<ServiceOption>
        {
            CreateTestServiceOption(),
            CreateTestServiceOption()
        };

        _mockServiceOptionHandler.Setup(h => h.GetAllAsync())
            .ReturnsAsync(serviceOptions);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<ServiceOptionResponse>>().Subject;
        response.Should().HaveCount(2);

        _mockServiceOptionHandler.Verify(h => h.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAll_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockServiceOptionHandler.Setup(h => h.GetAllAsync())
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
    public async Task GetById_ReturnsOk_WithServiceOption_WhenServiceOptionExists()
    {
        // Arrange
        var serviceOption = CreateTestServiceOption();
        _mockServiceOptionHandler.Setup(h => h.GetByIdAsync(serviceOption.Id))
            .ReturnsAsync(serviceOption);

        // Act
        var result = await _controller.GetById(serviceOption.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ServiceOptionResponse>().Subject;
        response.Id.Should().Be(serviceOption.Id);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenServiceOptionDoesNotExist()
    {
        // Arrange
        var serviceOptionId = Guid.NewGuid();
        _mockServiceOptionHandler.Setup(h => h.GetByIdAsync(serviceOptionId))
            .ReturnsAsync((ServiceOption?)null);

        // Act
        var result = await _controller.GetById(serviceOptionId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    #endregion

    #region GetByServiceId Tests

    [Fact]
    public async Task GetByServiceId_ReturnsOk_WithServiceOptions()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        var serviceOptions = new List<ServiceOption>
        {
            CreateTestServiceOption()
        };
        serviceOptions[0].ServiceId = serviceId;

        _mockServiceOptionHandler.Setup(h => h.GetByServiceIdAsync(serviceId))
            .ReturnsAsync(serviceOptions);

        // Act
        var result = await _controller.GetByServiceId(serviceId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<ServiceOptionResponse>>().Subject;
        response.Should().HaveCount(1);

        _mockServiceOptionHandler.Verify(h => h.GetByServiceIdAsync(serviceId), Times.Once);
    }

    [Fact]
    public async Task GetByServiceId_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        _mockServiceOptionHandler.Setup(h => h.GetByServiceIdAsync(serviceId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetByServiceId(serviceId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetByCompanyId Tests

    [Fact]
    public async Task GetByCompanyId_ReturnsOk_WithServiceOptions()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var serviceOptions = new List<ServiceOption>
        {
            CreateTestServiceOption()
        };
        serviceOptions[0].CompanyId = companyId;

        _mockServiceOptionHandler.Setup(h => h.GetByCompanyIdAsync(companyId))
            .ReturnsAsync(serviceOptions);

        // Act
        var result = await _controller.GetByCompanyId(companyId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<ServiceOptionResponse>>().Subject;
        response.Should().HaveCount(1);

        _mockServiceOptionHandler.Verify(h => h.GetByCompanyIdAsync(companyId), Times.Once);
    }

    [Fact]
    public async Task GetByCompanyId_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        _mockServiceOptionHandler.Setup(h => h.GetByCompanyIdAsync(companyId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetByCompanyId(companyId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Create Tests (Company or Admin)

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenServiceOptionIsCreatedSuccessfully_AsAdmin()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var request = new CreateServiceOptionRequest
        {
            ServiceId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Name = "Premium Package",
            Description = "Full service package",
            DurationMinutes = 60,
            Price = 100.00m
        };

        var createdServiceOption = new ServiceOption
        {
            Id = Guid.NewGuid(),
            ServiceId = request.ServiceId,
            CompanyId = request.CompanyId,
            Name = request.Name,
            Description = request.Description,
            DurationMinutes = request.DurationMinutes,
            Price = request.Price
        };

        _mockServiceOptionHandler.Setup(h => h.CreateAsync(It.IsAny<ServiceOption>()))
            .ReturnsAsync(createdServiceOption);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(ServiceOptionsController.GetById));
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenServiceOptionIsCreatedSuccessfully_AsCompany()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company");
        var request = new CreateServiceOptionRequest
        {
            ServiceId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Name = "Premium Package",
            DurationMinutes = 60,
            Price = 100.00m
        };

        var createdServiceOption = new ServiceOption
        {
            Id = Guid.NewGuid(),
            ServiceId = request.ServiceId,
            CompanyId = request.CompanyId,
            Name = request.Name,
            DurationMinutes = request.DurationMinutes,
            Price = request.Price
        };

        _mockServiceOptionHandler.Setup(h => h.CreateAsync(It.IsAny<ServiceOption>()))
            .ReturnsAsync(createdServiceOption);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(ServiceOptionsController.GetById));
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var request = new CreateServiceOptionRequest
        {
            ServiceId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Name = "Invalid Option",
            DurationMinutes = 30,
            Price = 50.00m
        };

        _mockServiceOptionHandler.Setup(h => h.CreateAsync(It.IsAny<ServiceOption>()))
            .ThrowsAsync(new InvalidOperationException("Service option already exists"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }

    #endregion

    #region Update Tests (Company or Admin)

    [Fact]
    public async Task Update_ReturnsOk_WhenServiceOptionIsUpdatedSuccessfully_AsAdmin()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceOptionId = Guid.NewGuid();
        var request = new UpdateServiceOptionRequest
        {
            Name = "Updated Package",
            Description = "Updated description",
            DurationMinutes = 45,
            Price = 75.00m
        };

        var updatedServiceOption = new ServiceOption
        {
            Id = serviceOptionId,
            Name = request.Name,
            Description = request.Description,
            DurationMinutes = request.DurationMinutes,
            Price = request.Price
        };

        _mockServiceOptionHandler.Setup(h => h.UpdateAsync(serviceOptionId, It.IsAny<ServiceOption>()))
            .ReturnsAsync(updatedServiceOption);

        // Act
        var result = await _controller.Update(serviceOptionId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ServiceOptionResponse>().Subject;
        response.Name.Should().Be(request.Name);
        response.Price.Should().Be(request.Price);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenServiceOptionDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceOptionId = Guid.NewGuid();
        var request = new UpdateServiceOptionRequest
        {
            Name = "Updated Package",
            DurationMinutes = 45,
            Price = 75.00m
        };

        _mockServiceOptionHandler.Setup(h => h.UpdateAsync(serviceOptionId, It.IsAny<ServiceOption>()))
            .ReturnsAsync((ServiceOption?)null);

        // Act
        var result = await _controller.Update(serviceOptionId, request);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Update_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company");
        var serviceOptionId = Guid.NewGuid();
        var request = new UpdateServiceOptionRequest
        {
            Name = "Updated Package",
            DurationMinutes = 45,
            Price = 75.00m
        };

        _mockServiceOptionHandler.Setup(h => h.UpdateAsync(serviceOptionId, It.IsAny<ServiceOption>()))
            .ThrowsAsync(new InvalidOperationException("Cannot update service option"));

        // Act
        var result = await _controller.Update(serviceOptionId, request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }

    #endregion

    #region Delete Tests (Admin Only)

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenServiceOptionIsDeletedSuccessfully()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceOptionId = Guid.NewGuid();
        _mockServiceOptionHandler.Setup(h => h.DeleteAsync(serviceOptionId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Delete(serviceOptionId);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        _mockServiceOptionHandler.Verify(h => h.DeleteAsync(serviceOptionId), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenServiceOptionDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceOptionId = Guid.NewGuid();
        _mockServiceOptionHandler.Setup(h => h.DeleteAsync(serviceOptionId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Delete(serviceOptionId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Delete_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var serviceOptionId = Guid.NewGuid();
        _mockServiceOptionHandler.Setup(h => h.DeleteAsync(serviceOptionId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Delete(serviceOptionId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion
}
