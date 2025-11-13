using GhseeliApis.DTOs.Address;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AddressesController : ControllerBase
{
    private readonly IUserAddressHandler _addressHandler;
    private readonly IAppLogger _logger;

    public AddressesController(IUserAddressHandler addressHandler, IAppLogger logger)
    {
        _addressHandler = addressHandler;
        _logger = logger;
    }

    [HttpGet("my-addresses")]
    public async Task<IActionResult> GetMyAddresses()
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"GET /api/addresses/my-addresses - Getting addresses for user {userId}");
            
            var addresses = await _addressHandler.GetByUserIdAsync(userId);
            var response = addresses.Select(a => new AddressResponse
            {
                Id = a.Id,
                UserId = a.UserId,
                AddressLine = a.AddressLine,
                City = a.City,
                Area = a.Area,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                IsPrimary = a.IsPrimary
            });

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting user addresses", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            _logger.LogInfo($"GET /api/addresses/{id}");
            
            var address = await _addressHandler.GetByIdAsync(id);
            if (address == null)
                return NotFound();

            var response = new AddressResponse
            {
                Id = address.Id,
                UserId = address.UserId,
                AddressLine = address.AddressLine,
                City = address.City,
                Area = address.Area,
                Latitude = address.Latitude,
                Longitude = address.Longitude,
                IsPrimary = address.IsPrimary
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting address {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAddressRequest request)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"POST /api/addresses - Creating address for user {userId}");

            var address = new UserAddress
            {
                AddressLine = request.AddressLine,
                City = request.City,
                Area = request.Area,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                IsPrimary = request.IsPrimary
            };

            // Validate the address
            var validationResult = address.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"POST /api/addresses - Validation failed: {string.Join(", ", validationResult.Errors)}");
                return BadRequest(new
                {
                    Message = "Validation failed",
                    Errors = validationResult.Errors
                });
            }

            var created = await _addressHandler.CreateAsync(address, userId);

            var response = new AddressResponse
            {
                Id = created.Id,
                UserId = created.UserId,
                AddressLine = created.AddressLine,
                City = created.City,
                Area = created.Area,
                Latitude = created.Latitude,
                Longitude = created.Longitude,
                IsPrimary = created.IsPrimary
            };

            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error creating address", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAddressRequest request)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"PUT /api/addresses/{id}");

            var address = new UserAddress
            {
                AddressLine = request.AddressLine,
                City = request.City,
                Area = request.Area,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                IsPrimary = request.IsPrimary
            };

            // Validate the address
            var validationResult = address.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"PUT /api/addresses/{id} - Validation failed: {string.Join(", ", validationResult.Errors)}");
                return BadRequest(new
                {
                    Message = "Validation failed",
                    Errors = validationResult.Errors
                });
            }

            var updated = await _addressHandler.UpdateAsync(id, address, userId);
            if (updated == null)
                return NotFound();

            var response = new AddressResponse
            {
                Id = updated.Id,
                UserId = updated.UserId,
                AddressLine = updated.AddressLine,
                City = updated.City,
                Area = updated.Area,
                Latitude = updated.Latitude,
                Longitude = updated.Longitude,
                IsPrimary = updated.IsPrimary
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating address {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"DELETE /api/addresses/{id}");

            var deleted = await _addressHandler.DeleteAsync(id, userId);
            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Cannot delete address {id}: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting address {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    [HttpPut("{id:guid}/set-primary")]
    public async Task<IActionResult> SetAsPrimary(Guid id)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"PUT /api/addresses/{id}/set-primary");

            var result = await _addressHandler.SetAsPrimaryAsync(id, userId);
            if (!result)
                return NotFound();

            return Ok(new { Message = "Address set as primary" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error setting address {id} as primary", ex);
            return StatusCode(500, "An error occurred");
        }
    }
}
