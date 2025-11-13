using GhseeliApis.DTOs.Vehicle;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehiclesController : ControllerBase
{
    private readonly IVehicleHandler _vehicleHandler;
    private readonly IAppLogger _logger;

    public VehiclesController(IVehicleHandler vehicleHandler, IAppLogger logger)
    {
        _vehicleHandler = vehicleHandler;
        _logger = logger;
    }

    /// <summary>
    /// Gets all vehicles for the current user
    /// </summary>
    [HttpGet("my-vehicles")]
    public async Task<IActionResult> GetMyVehicles()
    {
        try
        {
            // TODO: Get userId from authentication context
            var userId = Guid.NewGuid(); // Placeholder
            
            _logger.LogInfo($"GET /api/vehicles/my-vehicles - Getting vehicles for user {userId}");
            
            var vehicles = await _vehicleHandler.GetByUserIdAsync(userId);
            var response = vehicles.Select(v => new VehicleResponse
            {
                Id = v.Id,
                UserId = v.UserId,
                Make = v.Make,
                Model = v.Model,
                Year = v.Year,
                LicensePlate = v.LicensePlate,
                Color = v.Color
            });

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting user vehicles", ex);
            return StatusCode(500, "An error occurred while retrieving vehicles");
        }
    }

    /// <summary>
    /// Gets a specific vehicle by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            _logger.LogInfo($"GET /api/vehicles/{id}");
            
            var vehicle = await _vehicleHandler.GetByIdAsync(id);
            if (vehicle == null)
            {
                _logger.LogWarning($"Vehicle {id} not found");
                return NotFound(new { Message = "Vehicle not found" });
            }

            var response = new VehicleResponse
            {
                Id = vehicle.Id,
                UserId = vehicle.UserId,
                Make = vehicle.Make,
                Model = vehicle.Model,
                Year = vehicle.Year,
                LicensePlate = vehicle.LicensePlate,
                Color = vehicle.Color
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting vehicle {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Creates a new vehicle
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVehicleRequest request)
    {
        try
        {
            // TODO: Get userId from authentication context
            var userId = Guid.NewGuid(); // Placeholder
            
            _logger.LogInfo($"POST /api/vehicles - Creating vehicle for user {userId}");

            var vehicle = new Vehicle
            {
                Make = request.Make,
                Model = request.Model,
                Year = request.Year,
                LicensePlate = request.LicensePlate,
                Color = request.Color
            };

            // Validate the vehicle
            var validationResult = vehicle.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"POST /api/vehicles - Validation failed: {string.Join(", ", validationResult.Errors)}");
                return BadRequest(new
                {
                    Message = "Validation failed",
                    Errors = validationResult.Errors
                });
            }

            var created = await _vehicleHandler.CreateAsync(vehicle, userId);

            var response = new VehicleResponse
            {
                Id = created.Id,
                UserId = created.UserId,
                Make = created.Make,
                Model = created.Model,
                Year = created.Year,
                LicensePlate = created.LicensePlate,
                Color = created.Color
            };

            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error creating vehicle", ex);
            return StatusCode(500, "An error occurred while creating the vehicle");
        }
    }

    /// <summary>
    /// Updates a vehicle
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVehicleRequest request)
    {
        try
        {
            // TODO: Get userId from authentication context
            var userId = Guid.NewGuid(); // Placeholder
            
            _logger.LogInfo($"PUT /api/vehicles/{id}");

            var vehicle = new Vehicle
            {
                Make = request.Make,
                Model = request.Model,
                Year = request.Year,
                LicensePlate = request.LicensePlate,
                Color = request.Color
            };

            // Validate the vehicle
            var validationResult = vehicle.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"PUT /api/vehicles/{id} - Validation failed: {string.Join(", ", validationResult.Errors)}");
                return BadRequest(new
                {
                    Message = "Validation failed",
                    Errors = validationResult.Errors
                });
            }

            var updated = await _vehicleHandler.UpdateAsync(id, vehicle, userId);
            if (updated == null)
            {
                _logger.LogWarning($"Vehicle {id} not found or access denied");
                return NotFound(new { Message = "Vehicle not found or you don't have permission" });
            }

            var response = new VehicleResponse
            {
                Id = updated.Id,
                UserId = updated.UserId,
                Make = updated.Make,
                Model = updated.Model,
                Year = updated.Year,
                LicensePlate = updated.LicensePlate,
                Color = updated.Color
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating vehicle {id}", ex);
            return StatusCode(500, "An error occurred while updating the vehicle");
        }
    }

    /// <summary>
    /// Deletes a vehicle
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            // TODO: Get userId from authentication context
            var userId = Guid.NewGuid(); // Placeholder
            
            _logger.LogInfo($"DELETE /api/vehicles/{id}");

            var deleted = await _vehicleHandler.DeleteAsync(id, userId);
            if (!deleted)
            {
                _logger.LogWarning($"Vehicle {id} not found or access denied");
                return NotFound(new { Message = "Vehicle not found or you don't have permission" });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Cannot delete vehicle {id}: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error deleting vehicle {id}", ex);
            return StatusCode(500, "An error occurred while deleting the vehicle");
        }
    }
}
