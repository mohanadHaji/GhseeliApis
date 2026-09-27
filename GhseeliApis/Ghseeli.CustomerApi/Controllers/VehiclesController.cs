using GhseeliApis.DTOs.Vehicle;
using GhseeliApis.Handlers.Interfaces;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Ghseeli.IntegrationContracts.Vehicles;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VehiclesController : ControllerBase
{
    private const string VehicleTypeInvalid = "vehicle_type_invalid";
    private const string VehicleImageUrlInvalid = "vehicle_image_url_invalid";
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
    [ProducesResponseType(typeof(IEnumerable<VehicleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMyVehicles()
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
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
                Color = v.Color,
                VehicleType = v.VehicleType,
                ImageUrl = v.ImageUrl
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
    [ProducesResponseType(typeof(VehicleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (vehicle.UserId != userId)
            {
                return NotFound(new { Code = "vehicle_not_found", Message = "Vehicle not found" });
            }

            var response = new VehicleResponse
            {
                Id = vehicle.Id,
                UserId = vehicle.UserId,
                Make = vehicle.Make,
                Model = vehicle.Model,
                Year = vehicle.Year,
                LicensePlate = vehicle.LicensePlate,
                Color = vehicle.Color,
                VehicleType = vehicle.VehicleType,
                ImageUrl = vehicle.ImageUrl
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
    [ProducesResponseType(typeof(VehicleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateVehicleRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
            _logger.LogInfo($"POST /api/vehicles - Creating vehicle for user {userId}");

            var requestValidation = ValidateVehicleRequest(request.VehicleType, request.ImageUrl);
            if (requestValidation is not null)
            {
                return requestValidation;
            }

            var vehicle = new Vehicle
            {
                Make = request.Make,
                Model = request.Model,
                Year = request.Year,
                LicensePlate = request.LicensePlate,
                Color = request.Color,
                VehicleType = request.VehicleType!.Value,
                ImageUrl = request.ImageUrl
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
                Color = created.Color,
                VehicleType = created.VehicleType,
                ImageUrl = created.ImageUrl
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
    [ProducesResponseType(typeof(VehicleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVehicleRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
            _logger.LogInfo($"PUT /api/vehicles/{id}");

            var requestValidation = ValidateVehicleRequest(request.VehicleType, request.ImageUrl);
            if (requestValidation is not null)
            {
                return requestValidation;
            }

            var vehicle = new Vehicle
            {
                Make = request.Make,
                Model = request.Model,
                Year = request.Year,
                LicensePlate = request.LicensePlate,
                Color = request.Color,
                VehicleType = request.VehicleType!.Value,
                ImageUrl = request.ImageUrl
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
                Color = updated.Color,
                VehicleType = updated.VehicleType,
                ImageUrl = updated.ImageUrl
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
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

    private ObjectResult? ValidateVehicleRequest(
        VehicleType? vehicleType,
        string? imageUrl)
    {
        if (!vehicleType.HasValue)
        {
            return VehicleProblem(VehicleTypeInvalid, "vehicleType");
        }

        if (!Validation.VehicleImageUrlValidation.IsValid(imageUrl))
        {
            return VehicleProblem(VehicleImageUrlInvalid, "imageUrl");
        }

        return null;
    }

    private ObjectResult VehicleProblem(string code, string field)
    {
        var result = new ObjectResult(new
        {
            Code = code,
            Errors = new Dictionary<string, string[]>
            {
                [field] = [code]
            }
        })
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
