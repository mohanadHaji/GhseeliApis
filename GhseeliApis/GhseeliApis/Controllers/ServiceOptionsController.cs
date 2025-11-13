using GhseeliApis.DTOs.ServiceOption;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServiceOptionsController : ControllerBase
{
    private readonly IServiceOptionHandler _serviceOptionHandler;
    private readonly IAppLogger _logger;

    public ServiceOptionsController(IServiceOptionHandler serviceOptionHandler, IAppLogger logger)
    {
        _serviceOptionHandler = serviceOptionHandler;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            _logger.LogInfo("GET /api/serviceoptions - Getting all service options");

            var serviceOptions = await _serviceOptionHandler.GetAllAsync();
            var response = serviceOptions.Select(so => MapToResponse(so));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting all service options", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            _logger.LogInfo($"GET /api/serviceoptions/{id}");

            var serviceOption = await _serviceOptionHandler.GetByIdAsync(id);
            if (serviceOption == null)
                return NotFound();

            var response = MapToResponse(serviceOption);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting service option {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpGet("service/{serviceId:guid}")]
    public async Task<IActionResult> GetByServiceId(Guid serviceId)
    {
        try
        {
            _logger.LogInfo($"GET /api/serviceoptions/service/{serviceId}");

            var serviceOptions = await _serviceOptionHandler.GetByServiceIdAsync(serviceId);
            var response = serviceOptions.Select(so => MapToResponse(so));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting service options for service {serviceId}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpGet("company/{companyId:guid}")]
    public async Task<IActionResult> GetByCompanyId(Guid companyId)
    {
        try
        {
            _logger.LogInfo($"GET /api/serviceoptions/company/{companyId}");

            var serviceOptions = await _serviceOptionHandler.GetByCompanyIdAsync(companyId);
            var response = serviceOptions.Select(so => MapToResponse(so));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting service options for company {companyId}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateServiceOptionRequest request)
    {
        try
        {
            _logger.LogInfo($"POST /api/serviceoptions - Creating service option '{request.Name}'");

            var serviceOption = new ServiceOption
            {
                ServiceId = request.ServiceId,
                CompanyId = request.CompanyId,
                Name = request.Name,
                Description = request.Description,
                DurationMinutes = request.DurationMinutes,
                Price = request.Price
            };

            var created = await _serviceOptionHandler.CreateAsync(serviceOption);

            var response = MapToResponse(created);
            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Service option creation failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error creating service option", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceOptionRequest request)
    {
        try
        {
            _logger.LogInfo($"PUT /api/serviceoptions/{id}");

            var serviceOption = new ServiceOption
            {
                Name = request.Name,
                Description = request.Description,
                DurationMinutes = request.DurationMinutes,
                Price = request.Price
            };

            var updated = await _serviceOptionHandler.UpdateAsync(id, serviceOption);
            if (updated == null)
                return NotFound();

            var response = MapToResponse(updated);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Service option update failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating service option {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            _logger.LogInfo($"DELETE /api/serviceoptions/{id}");

            var deleted = await _serviceOptionHandler.DeleteAsync(id);
            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting service option {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    private static ServiceOptionResponse MapToResponse(ServiceOption serviceOption)
    {
        return new ServiceOptionResponse
        {
            Id = serviceOption.Id,
            ServiceId = serviceOption.ServiceId,
            CompanyId = serviceOption.CompanyId,
            Name = serviceOption.Name,
            Description = serviceOption.Description,
            DurationMinutes = serviceOption.DurationMinutes,
            Price = serviceOption.Price,
            ServiceName = serviceOption.Service?.Name ?? string.Empty,
            CompanyName = serviceOption.Company?.Name
        };
    }
}
