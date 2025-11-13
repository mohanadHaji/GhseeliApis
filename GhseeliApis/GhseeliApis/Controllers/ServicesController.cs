using GhseeliApis.DTOs.Service;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServicesController : ControllerBase
{
    private readonly IServiceHandler _serviceHandler;
    private readonly IAppLogger _logger;

    public ServicesController(IServiceHandler serviceHandler, IAppLogger logger)
    {
        _serviceHandler = serviceHandler;
        _logger = logger;
    }

    /// <summary>
    /// Get all services
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            _logger.LogInfo("GET /api/services - Getting all services");

            var services = await _serviceHandler.GetAllAsync();
            var response = services.Select(s => new ServiceResponse
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                OptionCount = s.Options?.Count ?? 0
            });

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting all services", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get service by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            _logger.LogInfo($"GET /api/services/{id}");

            var service = await _serviceHandler.GetByIdAsync(id);
            if (service == null)
                return NotFound();

            var response = new ServiceResponse
            {
                Id = service.Id,
                Name = service.Name,
                Description = service.Description,
                OptionCount = service.Options?.Count ?? 0
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting service {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get service with options
    /// </summary>
    [HttpGet("{id:guid}/with-options")]
    public async Task<IActionResult> GetWithOptions(Guid id)
    {
        try
        {
            _logger.LogInfo($"GET /api/services/{id}/with-options");

            var service = await _serviceHandler.GetByIdWithOptionsAsync(id);
            if (service == null)
                return NotFound();

            var response = new ServiceResponse
            {
                Id = service.Id,
                Name = service.Name,
                Description = service.Description,
                OptionCount = service.Options?.Count ?? 0
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting service {id} with options", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Create a new service
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateServiceRequest request)
    {
        try
        {
            _logger.LogInfo($"POST /api/services - Creating service '{request.Name}'");

            var service = new Service
            {
                Name = request.Name,
                Description = request.Description
            };

            var created = await _serviceHandler.CreateAsync(service);

            var response = new ServiceResponse
            {
                Id = created.Id,
                Name = created.Name,
                Description = created.Description,
                OptionCount = 0
            };

            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Service creation failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error creating service", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Update a service
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceRequest request)
    {
        try
        {
            _logger.LogInfo($"PUT /api/services/{id}");

            var service = new Service
            {
                Name = request.Name,
                Description = request.Description
            };

            var updated = await _serviceHandler.UpdateAsync(id, service);
            if (updated == null)
                return NotFound();

            var response = new ServiceResponse
            {
                Id = updated.Id,
                Name = updated.Name,
                Description = updated.Description,
                OptionCount = updated.Options?.Count ?? 0
            };

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Service update failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating service {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Delete a service
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            _logger.LogInfo($"DELETE /api/services/{id}");

            var deleted = await _serviceHandler.DeleteAsync(id);
            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting service {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }
}
