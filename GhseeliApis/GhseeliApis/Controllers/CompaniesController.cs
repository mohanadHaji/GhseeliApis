using GhseeliApis.DTOs.Company;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyHandler _companyHandler;
    private readonly IAppLogger _logger;

    public CompaniesController(ICompanyHandler companyHandler, IAppLogger logger)
    {
        _companyHandler = companyHandler;
        _logger = logger;
    }

    /// <summary>
    /// Get all companies
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            _logger.LogInfo("GET /api/companies - Getting all companies");

            var companies = await _companyHandler.GetAllAsync();
            var response = companies.Select(c => new CompanyListResponse
            {
                Id = c.Id,
                Name = c.Name,
                ServiceAreaDescription = c.ServiceAreaDescription,
                ServiceOptionCount = c.ServiceOptions?.Count ?? 0
            });

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting all companies", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get company by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            _logger.LogInfo($"GET /api/companies/{id}");

            var company = await _companyHandler.GetByIdAsync(id);
            if (company == null)
                return NotFound();

            var response = new CompanyResponse
            {
                Id = company.Id,
                Name = company.Name,
                Phone = company.Phone,
                Description = company.Description,
                ServiceAreaDescription = company.ServiceAreaDescription
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting company {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get companies by service area
    /// </summary>
    [HttpGet("area/{area}")]
    public async Task<IActionResult> GetByArea(string area)
    {
        try
        {
            _logger.LogInfo($"GET /api/companies/area/{area}");

            var companies = await _companyHandler.GetByServiceAreaAsync(area);
            var response = companies.Select(c => new CompanyListResponse
            {
                Id = c.Id,
                Name = c.Name,
                ServiceAreaDescription = c.ServiceAreaDescription,
                ServiceOptionCount = c.ServiceOptions?.Count ?? 0
            });

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting companies in area '{area}'", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Create a new company
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateCompanyRequest request)
    {
        try
        {
            _logger.LogInfo($"POST /api/companies - Creating company '{request.Name}'");

            var company = new Company
            {
                Name = request.Name,
                Phone = request.Phone,
                Description = request.Description,
                ServiceAreaDescription = request.ServiceAreaDescription
            };

            var created = await _companyHandler.CreateAsync(company);

            var response = new CompanyResponse
            {
                Id = created.Id,
                Name = created.Name,
                Phone = created.Phone,
                Description = created.Description,
                ServiceAreaDescription = created.ServiceAreaDescription
            };

            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Company creation failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error creating company", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Update a company
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyRequest request)
    {
        try
        {
            _logger.LogInfo($"PUT /api/companies/{id}");

            var company = new Company
            {
                Name = request.Name,
                Phone = request.Phone,
                Description = request.Description,
                ServiceAreaDescription = request.ServiceAreaDescription
            };

            var updated = await _companyHandler.UpdateAsync(id, company);
            if (updated == null)
                return NotFound();

            var response = new CompanyResponse
            {
                Id = updated.Id,
                Name = updated.Name,
                Phone = updated.Phone,
                Description = updated.Description,
                ServiceAreaDescription = updated.ServiceAreaDescription
            };

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Company update failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating company {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Delete a company
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            _logger.LogInfo($"DELETE /api/companies/{id}");

            var deleted = await _companyHandler.DeleteAsync(id);
            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting company {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }
}
