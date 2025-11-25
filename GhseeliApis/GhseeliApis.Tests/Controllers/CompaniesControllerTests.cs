using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Company;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for CompaniesController
/// </summary>
public class CompaniesControllerTests
{
    private readonly Mock<ICompanyHandler> _mockCompanyHandler;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly CompaniesController _controller;
    private readonly Guid _testUserId;

    public CompaniesControllerTests()
    {
        _mockCompanyHandler = new Mock<ICompanyHandler>();
        _mockLogger = new Mock<IAppLogger>();
        _controller = new CompaniesController(_mockCompanyHandler.Object, _mockLogger.Object);
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

    private static Company CreateTestCompany()
    {
        return new Company
        {
            Id = Guid.NewGuid(),
            Name = "Test Wash Co",
            Phone = "555-0100",
            Description = "Premium car washing service",
            ServiceAreaDescription = "Downtown Area"
        };
    }

    #region GetAll Tests

    [Fact]
    public async Task GetAll_ReturnsOk_WithAllCompanies()
    {
        // Arrange
        var companies = new List<Company>
        {
            CreateTestCompany(),
            CreateTestCompany()
        };

        _mockCompanyHandler.Setup(h => h.GetAllAsync())
            .ReturnsAsync(companies);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<CompanyListResponse>>().Subject;
        response.Should().HaveCount(2);

        _mockCompanyHandler.Verify(h => h.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoCompaniesExist()
    {
        // Arrange
        _mockCompanyHandler.Setup(h => h.GetAllAsync())
            .ReturnsAsync(new List<Company>());

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<CompanyListResponse>>().Subject;
        response.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockCompanyHandler.Setup(h => h.GetAllAsync())
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetAll();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_ReturnsOk_WithCompany_WhenCompanyExists()
    {
        // Arrange
        var company = CreateTestCompany();
        _mockCompanyHandler.Setup(h => h.GetByIdAsync(company.Id))
            .ReturnsAsync(company);

        // Act
        var result = await _controller.GetById(company.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CompanyResponse>().Subject;
        response.Id.Should().Be(company.Id);
        response.Name.Should().Be(company.Name);

        _mockCompanyHandler.Verify(h => h.GetByIdAsync(company.Id), Times.Once);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenCompanyDoesNotExist()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        _mockCompanyHandler.Setup(h => h.GetByIdAsync(companyId))
            .ReturnsAsync((Company?)null);

        // Act
        var result = await _controller.GetById(companyId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetById_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        _mockCompanyHandler.Setup(h => h.GetByIdAsync(companyId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetById(companyId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetByArea Tests

    [Fact]
    public async Task GetByArea_ReturnsOk_WithCompaniesInArea()
    {
        // Arrange
        var area = "Downtown";
        var companies = new List<Company>
        {
            CreateTestCompany()
        };
        companies[0].ServiceAreaDescription = area;

        _mockCompanyHandler.Setup(h => h.GetByServiceAreaAsync(area))
            .ReturnsAsync(companies);

        // Act
        var result = await _controller.GetByArea(area);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<CompanyListResponse>>().Subject;
        response.Should().HaveCount(1);

        _mockCompanyHandler.Verify(h => h.GetByServiceAreaAsync(area), Times.Once);
    }

    [Fact]
    public async Task GetByArea_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var area = "Downtown";
        _mockCompanyHandler.Setup(h => h.GetByServiceAreaAsync(area))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetByArea(area);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Create Tests (Admin Only)

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenCompanyIsCreatedSuccessfully()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var request = new CreateCompanyRequest
        {
            Name = "New Company",
            Phone = "555-0200",
            Description = "New service provider",
            ServiceAreaDescription = "North Side"
        };

        var createdCompany = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Phone = request.Phone,
            Description = request.Description,
            ServiceAreaDescription = request.ServiceAreaDescription
        };

        _mockCompanyHandler.Setup(h => h.CreateAsync(It.IsAny<Company>()))
            .ReturnsAsync(createdCompany);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(CompaniesController.GetById));
        
        var response = createdResult.Value.Should().BeOfType<CompanyResponse>().Subject;
        response.Name.Should().Be(request.Name);

        _mockCompanyHandler.Verify(h => h.CreateAsync(It.IsAny<Company>()), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var request = new CreateCompanyRequest
        {
            Name = "Duplicate Company",
            Phone = "555-0200"
        };

        _mockCompanyHandler.Setup(h => h.CreateAsync(It.IsAny<Company>()))
            .ThrowsAsync(new InvalidOperationException("Company already exists"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var request = new CreateCompanyRequest
        {
            Name = "New Company",
            Phone = "555-0200"
        };

        _mockCompanyHandler.Setup(h => h.CreateAsync(It.IsAny<Company>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Update Tests (Company or Admin)

    [Fact]
    public async Task Update_ReturnsOk_WhenCompanyIsUpdatedSuccessfully_AsAdmin()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var companyId = Guid.NewGuid();
        var request = new UpdateCompanyRequest
        {
            Name = "Updated Company",
            Phone = "555-0300",
            Description = "Updated description",
            ServiceAreaDescription = "Expanded Area"
        };

        var updatedCompany = new Company
        {
            Id = companyId,
            Name = request.Name,
            Phone = request.Phone,
            Description = request.Description,
            ServiceAreaDescription = request.ServiceAreaDescription
        };

        _mockCompanyHandler.Setup(h => h.UpdateAsync(companyId, It.IsAny<Company>()))
            .ReturnsAsync(updatedCompany);

        // Act
        var result = await _controller.Update(companyId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CompanyResponse>().Subject;
        response.Name.Should().Be(request.Name);

        _mockCompanyHandler.Verify(h => h.UpdateAsync(companyId, It.IsAny<Company>()), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsOk_WhenCompanyIsUpdatedSuccessfully_AsCompany()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company");
        var companyId = Guid.NewGuid();
        var request = new UpdateCompanyRequest
        {
            Name = "Updated Company",
            Phone = "555-0300"
        };

        var updatedCompany = new Company
        {
            Id = companyId,
            Name = request.Name,
            Phone = request.Phone
        };

        _mockCompanyHandler.Setup(h => h.UpdateAsync(companyId, It.IsAny<Company>()))
            .ReturnsAsync(updatedCompany);

        // Act
        var result = await _controller.Update(companyId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CompanyResponse>().Subject;
        response.Name.Should().Be(request.Name);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenCompanyDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var companyId = Guid.NewGuid();
        var request = new UpdateCompanyRequest
        {
            Name = "Updated Company",
            Phone = "555-0300"
        };

        _mockCompanyHandler.Setup(h => h.UpdateAsync(companyId, It.IsAny<Company>()))
            .ReturnsAsync((Company?)null);

        // Act
        var result = await _controller.Update(companyId, request);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Update_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var companyId = Guid.NewGuid();
        var request = new UpdateCompanyRequest
        {
            Name = "Updated Company",
            Phone = "555-0300"
        };

        _mockCompanyHandler.Setup(h => h.UpdateAsync(companyId, It.IsAny<Company>()))
            .ThrowsAsync(new InvalidOperationException("Cannot update company"));

        // Act
        var result = await _controller.Update(companyId, request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }

    #endregion

    #region Delete Tests (Admin Only)

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenCompanyIsDeletedSuccessfully()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var companyId = Guid.NewGuid();
        _mockCompanyHandler.Setup(h => h.DeleteAsync(companyId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Delete(companyId);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        _mockCompanyHandler.Verify(h => h.DeleteAsync(companyId), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenCompanyDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var companyId = Guid.NewGuid();
        _mockCompanyHandler.Setup(h => h.DeleteAsync(companyId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Delete(companyId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Delete_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var companyId = Guid.NewGuid();
        _mockCompanyHandler.Setup(h => h.DeleteAsync(companyId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Delete(companyId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion
}
