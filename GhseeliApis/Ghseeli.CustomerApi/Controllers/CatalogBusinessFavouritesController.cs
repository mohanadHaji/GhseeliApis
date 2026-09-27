using System.Security.Claims;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/catalog/businesses/{businessId:guid}/favourite")]
[Authorize(Policy = "UserPolicy")]
[OptionalDeviceToken]
public sealed class CatalogBusinessFavouritesController : ControllerBase
{
    private readonly IBusinessFavouriteService _service;
    private readonly IDeviceRegistrationService _deviceService;
    private readonly ICustomerDataPartitionContext _dataPartition;

    public CatalogBusinessFavouritesController(
        IBusinessFavouriteService service,
        IDeviceRegistrationService deviceService,
        ICustomerDataPartitionContext dataPartition)
    {
        _service = service;
        _deviceService = deviceService;
        _dataPartition = dataPartition;
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Put(
        Guid businessId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            businessId,
            (userId, token) => _service.AddAsync(businessId, userId, token),
            cancellationToken);

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(
        Guid businessId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            businessId,
            (userId, token) => _service.DeleteAsync(businessId, userId, token),
            cancellationToken);

    private async Task<IActionResult> ExecuteAsync(
        Guid businessId,
        Func<Guid, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            userId == Guid.Empty)
        {
            return Unauthorized();
        }

        try
        {
            await action(userId, cancellationToken);
            return NoContent();
        }
        catch (CatalogReadModelException exception)
        {
            var language = Services.Configuration.ConfigurationLanguageResolver.Resolve(
                Request.Query["language"].ToString(),
                Request.Headers.AcceptLanguage.ToString());
            var problem = CatalogProblemDetailsFactory.Create(
                exception.StatusCode,
                exception.Code,
                language,
                HttpContext.TraceIdentifier);
            return new ObjectResult(problem)
            {
                StatusCode = exception.StatusCode,
                ContentTypes = { "application/problem+json" }
            };
        }
    }

}
