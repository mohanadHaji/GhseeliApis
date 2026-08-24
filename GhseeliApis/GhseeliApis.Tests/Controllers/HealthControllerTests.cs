using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.Persistence;
using GhseeliApis.Handlers;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Globalization;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for HealthController
/// </summary>
public class HealthControllerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IAppLogger> _logger = new();
    private readonly IHealthRepository _repository;
    private readonly IHealthHandler _healthHandler;
    private readonly HealthController _controller;

    public HealthControllerTests()
    {
        // Create an in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _context = new ApplicationDbContext(options);
        _repository = new HealthRepository(_context);
        _healthHandler = new HealthHandler(_repository, _logger.Object);
        _controller = new HealthController(_healthHandler, _logger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    #region CheckApiHealth Tests

    [Fact]
    public void CheckApiHealth_ReturnsOk_WithHealthyStatus()
    {
        // Act
        var result = _controller.CheckApiHealth();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        
        var response = okResult.Value;
        response.Should().NotBeNull();
    }

    [Fact]
    public void CheckApiHealth_ReturnsCorrectServiceName()
    {
        // Act
        var result = _controller.CheckApiHealth();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value;
        
        var serviceProperty = response?.GetType().GetProperty("Service");
        serviceProperty.Should().NotBeNull();
        serviceProperty!.GetValue(response).Should().Be("Ghseeli APIs");
    }

    [Fact]
    public void CheckApiHealth_ReturnsHealthyStatus()
    {
        // Act
        var result = _controller.CheckApiHealth();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value;
        
        var statusProperty = response?.GetType().GetProperty("Status");
        statusProperty.Should().NotBeNull();
        statusProperty!.GetValue(response).Should().Be("Healthy");
    }

    [Fact]
    public void CheckApiHealth_ReturnsVersion()
    {
        // Act
        var result = _controller.CheckApiHealth();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value;
        
        var versionProperty = response?.GetType().GetProperty("Version");
        versionProperty.Should().NotBeNull();
        versionProperty!.GetValue(response).Should().Be("v1");
    }

    [Fact]
    public void CheckApiHealth_ReturnsTimestamp()
    {
        // Act
        var beforeCall = DateTime.UtcNow;
        var result = _controller.CheckApiHealth();
        var afterCall = DateTime.UtcNow;

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value;
        
        var timestampProperty = response?.GetType().GetProperty("Timestamp");
        timestampProperty.Should().NotBeNull();
        
        var timestampText = timestampProperty!.GetValue(response).Should().BeOfType<string>().Subject;
        DateTimeOffset.TryParseExact(
            timestampText,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp).Should().BeTrue();
        timestamp.UtcDateTime.Should().BeOnOrAfter(beforeCall).And.BeOnOrBefore(afterCall);
    }

    #endregion

    #region CheckDatabaseHealth Tests

    [Fact]
    public async Task CheckDatabaseHealth_ReturnsOk_WhenDatabaseIsAccessible()
    {
        // Act
        var result = await _controller.CheckDatabaseHealth();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task CheckDatabaseHealth_ReturnsHealthyStatus_WhenDatabaseConnects()
    {
        // Act
        var result = await _controller.CheckDatabaseHealth();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value;
        
        var statusProperty = response?.GetType().GetProperty("Status");
        statusProperty.Should().NotBeNull();
        statusProperty!.GetValue(response).Should().Be("Healthy");
    }

    [Fact]
    public async Task CheckDatabaseHealth_ReturnsDatabaseType()
    {
        // Act
        var result = await _controller.CheckDatabaseHealth();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value;
        
        var databaseProperty = response?.GetType().GetProperty("Database");
        databaseProperty.Should().NotBeNull();
        databaseProperty!.GetValue(response).Should().Be("SQL Server");
    }

    [Fact]
    public async Task CheckDatabaseHealth_ReturnsTimestamp()
    {
        // Act
        var beforeCall = DateTime.UtcNow;
        var result = await _controller.CheckDatabaseHealth();
        var afterCall = DateTime.UtcNow;

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value;
        
        var timestampProperty = response?.GetType().GetProperty("Timestamp");
        timestampProperty.Should().NotBeNull();
        
        var timestampText = timestampProperty!.GetValue(response).Should().BeOfType<string>().Subject;
        DateTimeOffset.TryParseExact(
            timestampText,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp).Should().BeTrue();
        timestamp.UtcDateTime.Should().BeOnOrAfter(beforeCall).And.BeOnOrBefore(afterCall);
    }

    #endregion
}

/// <summary>
/// Separate test class for database failure scenarios
/// This class doesn't use IDisposable to avoid conflicts with disposed context
/// </summary>
public class HealthControllerDatabaseFailureTests
{
    [Fact]
    public async Task CheckDatabaseHealth_ReturnsOkWithUnhealthy_WhenDatabaseConnectionFails()
    {
        // Arrange - Create a context with invalid connection that will fail
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        
        // Dispose the context to simulate connection failure
        await context.DisposeAsync();
        
        var logger = new Mock<IAppLogger>();
        var repository = new HealthRepository(context);
        var healthHandler = new HealthHandler(repository, logger.Object);
        var controller = new HealthController(healthHandler, logger.Object);

        // Act
        var result = await controller.CheckDatabaseHealth();

        // Assert - Handler catches exception and returns false, so controller returns OK with Unhealthy status
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        
        var response = okResult.Value;
        var statusProperty = response?.GetType().GetProperty("Status");
        statusProperty.Should().NotBeNull();
        statusProperty!.GetValue(response).Should().Be("Unhealthy");
    }
}
