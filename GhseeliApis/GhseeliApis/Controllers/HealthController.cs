using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

/// <summary>
/// Health check controller for monitoring application and database status
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("Health")]
public class HealthController : ControllerBase
{
    private readonly IHealthHandler _healthHandler;
    private readonly IAppLogger _logger;

    public HealthController(IHealthHandler healthHandler, IAppLogger logger)
    {
        _healthHandler = healthHandler;
        _logger = logger;
    }

    /// <summary>
    /// Check API health
    /// </summary>
    /// <returns>API health status</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult CheckApiHealth()
    {
        _logger.LogInfo("GET /api/health - API health check requested");

        var response = new
        {
            Status = "Healthy",
            Service = "Ghseeli APIs",
            Timestamp = DateTime.UtcNow,
            Version = "v1"
        };

        _logger.LogInfo($"GET /api/health - API is healthy, returning 200 OK at {response.Timestamp:yyyy-MM-dd HH:mm:ss}");
        return Ok(response);
    }

    /// <summary>
    /// Check database connection health
    /// </summary>
    /// <returns>Database health status</returns>
    [HttpGet("db")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CheckDatabaseHealth()
    {
        _logger.LogInfo("GET /api/health/db - Database health check requested");

        try
        {
            var startTime = DateTime.UtcNow;
            var canConnect = await _healthHandler.CheckDatabaseHealthAsync();
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var status = canConnect ? "Healthy" : "Unhealthy";

            var response = new
            {
                Status = status,
                Database = "Google Cloud SQL",
                Timestamp = DateTime.UtcNow,
                ResponseTime = $"{duration:F2}ms"
            };

            if (canConnect)
            {
                _logger.LogInfo($"GET /api/health/db - Database is healthy, response time: {duration:F2}ms, returning 200 OK");
            }
            else
            {
                _logger.LogWarning($"GET /api/health/db - Database is unhealthy, response time: {duration:F2}ms, returning 200 OK with Unhealthy status");
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("GET /api/health/db - Database health check failed with exception, returning 503 Service Unavailable", ex);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    Title = "Database Connection Failed",
                    Detail = ex.Message,
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Timestamp = DateTime.UtcNow
                });
        }
    }
}
