using FluentValidation;
using Ghseeli.Common.Logging;
using GhseeliApis.DataPartitioning;
using GhseeliApis.DTOs.Banners;
using GhseeliApis.Filters;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Banners;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/banners")]
[OptionalDeviceToken]
public sealed class BannersController : ControllerBase
{
    private readonly IBannerService _service;
    private readonly IDeviceRegistrationService _deviceService;
    private readonly ICustomerDataPartitionContext _dataPartition;

    public BannersController(
        IBannerService service,
        IDeviceRegistrationService deviceService,
        ICustomerDataPartitionContext dataPartition)
    {
        _service = service;
        _deviceService = deviceService;
        _dataPartition = dataPartition;
    }

    [HttpGet]
    [ProducesResponseType<PublicBannersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(
        [FromQuery] string? language = null,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await _service.GetPublicAsync(cancellationToken));
    }
}

[ApiController]
[Route("api/v1/admin/banners")]
[Authorize(Policy = "AdminPolicy")]
[AllowWithoutDeviceToken]
public sealed class AdminBannersController : ControllerBase
{
    private const long MaxRequestBodyBytes = 65_536;
    private readonly IBannerService _service;
    private readonly IValidator<CreateBannerRequest> _createValidator;
    private readonly IValidator<UpdateBannerRequest> _updateValidator;
    private readonly IAppLogger _logger;

    public AdminBannersController(
        IBannerService service,
        IValidator<CreateBannerRequest> createValidator,
        IValidator<UpdateBannerRequest> updateValidator,
        IAppLogger logger)
    {
        _service = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType<AdminBannersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await _service.GetAdminAsync(cancellationToken));
    }

    [HttpPost]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<AdminBannerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Post(
        [FromBody] CreateBannerRequest? request,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (request is null)
        {
            return ProblemResult(400, BannerProblemCodes.Invalid, language);
        }

        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemResult(validation.Errors, language);
        }

        var banner = await _service.CreateAsync(request, cancellationToken);
        _logger.LogInfo($"Banner {banner.Id} was created.");
        return StatusCode(StatusCodes.Status201Created, banner);
    }

    [HttpPut("{id:guid}")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<AdminBannerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> Put(
        Guid id,
        [FromBody] UpdateBannerRequest? request,
        [FromQuery] string? language,
        CancellationToken cancellationToken) =>
        ExecuteMutationAsync(
            id,
            request,
            language,
            cancellationToken,
            async value =>
            {
                var validation = await _updateValidator.ValidateAsync(
                    value, cancellationToken);
                if (!validation.IsValid)
                {
                    return ValidationProblemResult(validation.Errors, language);
                }

                var banner = await _service.UpdateAsync(id, value, cancellationToken);
                _logger.LogInfo($"Banner {id} was updated.");
                return Ok(banner);
            });

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] string? expectedRowVersion,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!BannerRowVersion.IsValid(expectedRowVersion))
        {
            return ProblemResult(400, BannerProblemCodes.Invalid, language);
        }

        try
        {
            await _service.DeleteAsync(id, expectedRowVersion!, cancellationToken);
            _logger.LogInfo($"Banner {id} was deleted.");
            return NoContent();
        }
        catch (BannerException exception)
        {
            _logger.LogWarning(
                $"Banner delete for {id} failed with code {exception.Code}.");
            return ProblemResult(exception.StatusCode, exception.Code, language);
        }
    }

    private async Task<IActionResult> ExecuteMutationAsync(
        Guid id,
        UpdateBannerRequest? request,
        string? language,
        CancellationToken cancellationToken,
        Func<UpdateBannerRequest, Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        if (request is null)
        {
            return ProblemResult(400, BannerProblemCodes.Invalid, language);
        }

        try
        {
            return await action(request);
        }
        catch (BannerException exception)
        {
            _logger.LogWarning(
                $"Banner update for {id} failed with code {exception.Code}.");
            return ProblemResult(exception.StatusCode, exception.Code, language);
        }
    }

    private ObjectResult ValidationProblemResult(
        IEnumerable<FluentValidation.Results.ValidationFailure> errors,
        string? language) =>
        ProblemResult(
            400,
            BannerProblemCodes.Invalid,
            language,
            errors
                .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) +
                                  error.PropertyName[1..])
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(_ => BannerProblemCodes.Invalid).ToArray()));

    private ObjectResult ProblemResult(
        int status,
        string code,
        string? language,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        var resolved = ConfigurationLanguageResolver.Resolve(
            language, Request.Headers.AcceptLanguage.ToString());
        return new ObjectResult(BannerProblemDetailsFactory.Create(
            status, code, resolved, HttpContext.TraceIdentifier, fieldErrors))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}
