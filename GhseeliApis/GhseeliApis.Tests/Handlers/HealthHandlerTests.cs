using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Persistence;
using GhseeliApis.Handlers;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for HealthHandler
/// </summary>
public class HealthHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IAppLogger> _logger = new();
    private readonly IHealthRepository _repository;
    private readonly HealthHandler _handler;

    public HealthHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _context = new ApplicationDbContext(options);
        _repository = new HealthRepository(_context);
        _handler = new HealthHandler(_repository, _logger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task CheckDatabaseHealthAsync_ReturnsTrue_WhenDatabaseIsAccessible()
    {
        // Act
        var result = await _handler.CheckDatabaseHealthAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckDatabaseHealthAsync_CanBeCalledMultipleTimes()
    {
        // Act
        var result1 = await _handler.CheckDatabaseHealthAsync();
        var result2 = await _handler.CheckDatabaseHealthAsync();
        var result3 = await _handler.CheckDatabaseHealthAsync();

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        result3.Should().BeTrue();
    }
}

/// <summary>
/// Tests for HealthHandler error scenarios
/// </summary>
public class HealthHandlerErrorTests
{
    [Fact]
    public async Task CheckDatabaseHealthAsync_ReturnsFalse_WhenDatabaseIsNotAccessible()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.DisposeAsync();
        
        var logger = new Mock<IAppLogger>();
        var repository = new HealthRepository(context);
        var handler = new HealthHandler(repository, logger.Object);

        // Act
        var result = await handler.CheckDatabaseHealthAsync();

        // Assert
        result.Should().BeFalse();
    }
}
