using System.Security.Claims;
using FluentValidation;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Filters;
using GhseeliApis.Middleware;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Services.Devices;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/bookings/{bookingId:guid}/review")]
[Authorize(Policy = "UserPolicy")]
public sealed class BookingReviewsController : ControllerBase
{
    private const long MaxRequestBodyBytes = 65_536;
    private readonly IBusinessReviewService _service;
    private readonly IValidator<PutBusinessReviewRequest> _validator;
    private readonly IAppLogger _logger;

    public BookingReviewsController(
        IBusinessReviewService service,
        IValidator<PutBusinessReviewRequest> validator,
        IAppLogger logger)
    {
        _service = service;
        _validator = validator;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType<OwnedBusinessReviewResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(
        Guid bookingId,
        [FromQuery] string? language,
        CancellationToken cancellationToken) =>
        ExecuteOwnedAsync(
            language,
            userId => _service.GetOwnedAsync(bookingId, userId, cancellationToken),
            value => Ok(value));

    [HttpPut]
    [EnforceJsonRequestContentType]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<OwnedBusinessReviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<OwnedBusinessReviewResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Put(
        Guid bookingId,
        [FromBody] PutBusinessReviewRequest? request,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (request is null)
        {
            return ProblemResult(400, BusinessReviewProblemCodes.Invalid, language);
        }
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ProblemResult(
                400,
                BusinessReviewProblemCodes.Invalid,
                language,
                validation.Errors
                    .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) +
                                      error.PropertyName[1..])
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(_ => BusinessReviewProblemCodes.Invalid).ToArray()));
        }

        return await ExecuteOwnedAsync(
            language,
            userId => _service.PutAsync(bookingId, userId, request, cancellationToken),
            value => value.Created
                ? StatusCode(StatusCodes.Status201Created, value.Review)
                : Ok(value.Review));
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid bookingId,
        [FromQuery] string? expectedRowVersion,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        if (expectedRowVersion is not null && !IsValidRowVersion(expectedRowVersion))
        {
            return ProblemResult(400, BusinessReviewProblemCodes.Invalid, language);
        }

        return await ExecuteOwnedAsync(
            language,
            async userId =>
            {
                await _service.DeleteAsync(
                    bookingId, userId, expectedRowVersion, cancellationToken);
                return true;
            },
            _ => NoContent());
    }

    private async Task<IActionResult> ExecuteOwnedAsync<T>(
        string? language,
        Func<Guid, Task<T>> action,
        Func<T, IActionResult> success)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            userId == Guid.Empty)
        {
            return ProblemResult(401, BusinessReviewProblemCodes.Invalid, language);
        }

        try
        {
            return success(await action(userId));
        }
        catch (BusinessReviewException exception)
        {
            _logger.LogWarning(
                $"Business review request failed with code {exception.Code}.");
            return ProblemResult(exception.StatusCode, exception.Code, language);
        }
    }

    private ObjectResult ProblemResult(
        int status,
        string code,
        string? language,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        var resolved = ConfigurationLanguageResolver.Resolve(
            language, Request.Headers.AcceptLanguage.ToString());
        return new ObjectResult(BusinessReviewProblemDetailsFactory.Create(
            status, code, resolved, HttpContext.TraceIdentifier, fieldErrors))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

    private static bool IsValidRowVersion(string value)
    {
        try
        {
            return Convert.FromBase64String(value).Length == 8;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

[ApiController]
[Route("api/v1/catalog/businesses/{businessId:guid}/reviews")]
[DemoSeedDataOnly]
public sealed class PublicBusinessReviewsController : ControllerBase
{
    private readonly IBusinessReviewService _service;
    private readonly IValidator<GetBusinessReviewsRequest> _validator;
    private readonly IDeviceRegistrationService _deviceService;
    private readonly ICustomerDataPartitionContext _dataPartition;

    public PublicBusinessReviewsController(
        IBusinessReviewService service,
        IValidator<GetBusinessReviewsRequest> validator,
        IDeviceRegistrationService deviceService,
        ICustomerDataPartitionContext dataPartition)
    {
        _service = service;
        _validator = validator;
        _deviceService = deviceService;
        _dataPartition = dataPartition;
    }

    [HttpGet]
    [ProducesResponseType<PublicBusinessReviewsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid businessId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? language = null,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        var request = new GetBusinessReviewsRequest
        {
            Page = page,
            PageSize = pageSize,
            Language = language
        };
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        var resolved = ConfigurationLanguageResolver.Resolve(
            language, Request.Headers.AcceptLanguage.ToString());
        if (!validation.IsValid)
        {
            var code = validation.Errors.Any(error =>
                error.ErrorCode == BusinessReviewProblemCodes.PaginationInvalid)
                ? BusinessReviewProblemCodes.PaginationInvalid
                : BusinessReviewProblemCodes.Invalid;
            return ProblemResult(400, code, resolved);
        }

        try
        {
            return Ok(await _service.GetPublicAsync(
                businessId, page, pageSize, cancellationToken));
        }
        catch (BusinessReviewException exception)
        {
            return ProblemResult(exception.StatusCode, exception.Code, resolved);
        }
    }

    private ObjectResult ProblemResult(int status, string code, string language) =>
        new(BusinessReviewProblemDetailsFactory.Create(
            status, code, language, HttpContext.TraceIdentifier))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };

}
