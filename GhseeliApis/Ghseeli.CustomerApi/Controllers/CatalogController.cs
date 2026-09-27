using FluentValidation;
using FluentValidation.AspNetCore;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Filters;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/catalog")]
[OptionalDeviceToken]
public sealed class CatalogController : ControllerBase
{
    private const long MaxAvailableSlotsRequestBodyBytes = 65_536;
    private readonly ICatalogReadModelService _service;
    private readonly IValidator<GetCatalogCategoriesRequest> _categoriesValidator;
    private readonly IValidator<GetCatalogBusinessesRequest> _businessesValidator;
    private readonly IValidator<GetCatalogBusinessOfferingsRequest> _businessOfferingsValidator;
    private readonly IValidator<GetCatalogResourceRequest> _resourceValidator;
    private readonly IValidator<GetAvailableSlotsRequest> _availableSlotsValidator;
    private readonly IAvailableSlotsQueryService _availableSlotsService;
    private readonly IValidator<AvailabilitySearchRequest> _availabilitySearchValidator;
    private readonly IAvailabilityDiscoveryQueryService _availabilityDiscoveryService;
    private readonly IAppLogger _logger;
    private readonly IDeviceRegistrationService? _deviceService;
    private readonly ICustomerDataPartitionContext? _dataPartition;

    public CatalogController(
        ICatalogReadModelService service,
        IValidator<GetCatalogCategoriesRequest> categoriesValidator,
        IValidator<GetCatalogBusinessesRequest> businessesValidator,
        IValidator<GetCatalogBusinessOfferingsRequest> businessOfferingsValidator,
        IValidator<GetCatalogResourceRequest> resourceValidator,
        IValidator<GetAvailableSlotsRequest> availableSlotsValidator,
        IAvailableSlotsQueryService availableSlotsService,
        IValidator<AvailabilitySearchRequest> availabilitySearchValidator,
        IAvailabilityDiscoveryQueryService availabilityDiscoveryService,
        IAppLogger logger,
        IDeviceRegistrationService? deviceService = null,
        ICustomerDataPartitionContext? dataPartition = null)
    {
        _service = service;
        _categoriesValidator = categoriesValidator;
        _businessesValidator = businessesValidator;
        _businessOfferingsValidator = businessOfferingsValidator;
        _resourceValidator = resourceValidator;
        _availableSlotsValidator = availableSlotsValidator;
        _availableSlotsService = availableSlotsService;
        _availabilitySearchValidator = availabilitySearchValidator;
        _availabilityDiscoveryService = availabilityDiscoveryService;
        _logger = logger;
        _deviceService = deviceService;
        _dataPartition = dataPartition;
    }

    [HttpPost("businesses/availability-search")]
    [EnforceJsonRequestContentType]
    [RequestSizeLimit(MaxAvailableSlotsRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(MaxAvailableSlotsRequestBodyBytes)]
    [ProducesResponseType<AvailabilitySearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SearchAvailability(
        [CustomizeValidator(Skip = true)]
        [FromBody]
        AvailabilitySearchRequest request,
        [FromQuery] string? language,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();
        request.Language = language;
        var validation = await _availabilitySearchValidator.ValidateAsync(
            request,
            cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemResult(validation, acceptLanguage);
        }

        try
        {
            return Ok(await _availabilityDiscoveryService.SearchAsync(
                request,
                acceptLanguage,
                cancellationToken));
        }
        catch (CatalogReadModelException exception)
        {
            _logger.LogWarning(
                $"Availability discovery rejected. Code={exception.Code}, Status={exception.StatusCode}.");
            return CatalogProblemResult(
                exception.StatusCode,
                exception.Code,
                request.Language,
                acceptLanguage);
        }
    }

    [HttpPost("businesses/{businessId:guid}/branches/{branchId:guid}/available-slots")]
    [EnforceJsonRequestContentType]
    [RequestSizeLimit(MaxAvailableSlotsRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(MaxAvailableSlotsRequestBodyBytes)]
    [ProducesResponseType<CatalogAvailableSlotsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAvailableSlots(
        Guid businessId,
        Guid branchId,
        [CustomizeValidator(Skip = true)]
        [FromBody]
        GetAvailableSlotsRequest request,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();
        var validation = await _availableSlotsValidator.ValidateAsync(
            request,
            cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemResult(validation, acceptLanguage);
        }

        try
        {
            return Ok(await _availableSlotsService.GetAsync(
                businessId,
                branchId,
                request,
                acceptLanguage,
                cancellationToken));
        }
        catch (CatalogReadModelException exception)
        {
            return CatalogProblemResult(
                exception.StatusCode,
                exception.Code,
                request.Language,
                acceptLanguage);
        }
    }

    [HttpGet("categories")]
    [ProducesResponseType<CatalogCategoriesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCategories(
        [FromQuery] string? language,
        [FromQuery] Guid? businessId,
        [FromQuery] bool refresh,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();

        var request = new GetCatalogCategoriesRequest
        {
            Language = language,
            BusinessId = businessId,
            Refresh = refresh
        };

        var validation = await _categoriesValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemResult(validation, acceptLanguage);
        }

        try
        {
            return Ok(await _service.GetCategoriesAsync(request, acceptLanguage, cancellationToken));
        }
        catch (CatalogReadModelException exception)
        {
            return CatalogProblemResult(exception.StatusCode, exception.Code, language, acceptLanguage);
        }
    }

    [HttpGet("businesses")]
    [ProducesResponseType<CatalogBusinessesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBusinesses(
        [FromQuery] string? language,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? search,
        [FromQuery] string? top,
        [FromQuery] bool refresh,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();
        int? parsedTop = null;
        if (top is not null && (!int.TryParse(top, out var topValue) || topValue is not (5 or 10)))
        {
            return CatalogProblemResult(
                StatusCodes.Status400BadRequest,
                CatalogProblemCodes.TopInvalid,
                language,
                acceptLanguage);
        }
        if (top is not null)
        {
            parsedTop = int.Parse(top);
        }

        var request = new GetCatalogBusinessesRequest
        {
            Language = language,
            BranchId = branchId,
            CategoryId = categoryId,
            Search = search,
            Top = parsedTop,
            Refresh = refresh
        };

        var validation = await _businessesValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemResult(validation, acceptLanguage);
        }

        try
        {
            return Ok(await _service.GetBusinessesAsync(request, acceptLanguage, cancellationToken));
        }
        catch (CatalogReadModelException exception)
        {
            return CatalogProblemResult(exception.StatusCode, exception.Code, language, acceptLanguage);
        }
    }

    [NonAction]
    public Task<IActionResult> GetBusinesses(
        string? language,
        Guid? branchId,
        Guid? categoryId,
        bool refresh,
        string? acceptLanguage,
        CancellationToken cancellationToken) =>
        GetBusinesses(
            language,
            branchId,
            categoryId,
            search: null,
            top: null,
            refresh,
            acceptLanguage,
            cancellationToken);

    [HttpGet("businesses/{id:guid}")]
    [ProducesResponseType<CatalogBusinessDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBusiness(
        Guid id,
        [FromQuery] string? language,
        [FromQuery] bool refresh,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();
        var request = new GetCatalogResourceRequest
        {
            Language = language,
            Refresh = refresh
        };

        var validation = await _resourceValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemResult(validation, acceptLanguage);
        }

        try
        {
            return Ok(await _service.GetBusinessAsync(id, request, acceptLanguage, cancellationToken));
        }
        catch (CatalogReadModelException exception)
        {
            return CatalogProblemResult(exception.StatusCode, exception.Code, language, acceptLanguage);
        }
    }

    [HttpGet("businesses/{id:guid}/offerings")]
    [ProducesResponseType<CatalogBusinessOfferingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBusinessOfferings(
        Guid id,
        [FromQuery] string? language,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool refresh,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();
        var request = new GetCatalogBusinessOfferingsRequest
        {
            Language = language,
            BranchId = branchId,
            CategoryId = categoryId,
            Refresh = refresh
        };

        var validation = await _businessOfferingsValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemResult(validation, acceptLanguage);
        }

        try
        {
            return Ok(await _service.GetBusinessOfferingsAsync(
                id,
                request,
                acceptLanguage,
                cancellationToken));
        }
        catch (CatalogReadModelException exception)
        {
            return CatalogProblemResult(exception.StatusCode, exception.Code, language, acceptLanguage);
        }
    }

    [HttpGet("offerings/{id:guid}")]
    [ProducesResponseType<CatalogOfferingDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetOffering(
        Guid id,
        [FromQuery] string? language,
        [FromQuery] bool refresh,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();

        var request = new GetCatalogResourceRequest
        {
            Language = language,
            Refresh = refresh
        };

        var validation = await _resourceValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemResult(validation, acceptLanguage);
        }

        try
        {
            return Ok(await _service.GetOfferingAsync(id, request, acceptLanguage, cancellationToken));
        }
        catch (CatalogReadModelException exception)
        {
            return CatalogProblemResult(exception.StatusCode, exception.Code, language, acceptLanguage);
        }
    }

    private ObjectResult ValidationProblemResult(
        FluentValidation.Results.ValidationResult validation,
        string? acceptLanguage)
    {
        var errorLanguage = ConfigurationLanguageResolver.ResolveFromHeader(acceptLanguage);
        var code = DetermineValidationCode(validation);

        _logger.LogWarning(
            $"Catalog request validation failed with {validation.Errors.Count} error(s).");

        var problem = CatalogProblemDetailsFactory.Create(
            StatusCodes.Status400BadRequest,
            code,
            errorLanguage,
            HttpContext.TraceIdentifier,
            BuildFieldErrors(validation, errorLanguage));

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }

    private ObjectResult CatalogProblemResult(
        int statusCode,
        string code,
        string? requestedLanguage,
        string? acceptLanguage)
    {
        var language = ConfigurationLanguageResolver.Resolve(requestedLanguage, acceptLanguage);
        _logger.LogWarning($"Catalog request failed with code {code}.");

        var problem = CatalogProblemDetailsFactory.Create(
            statusCode,
            code,
            language,
            HttpContext.TraceIdentifier);

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    private static string DetermineValidationCode(
        FluentValidation.Results.ValidationResult validation)
    {
        return validation.Errors.Any(error => error.ErrorCode == ConfigurationProblemCodes.LanguageInvalid)
            ? ConfigurationProblemCodes.LanguageInvalid
            : validation.Errors.Any(error => error.ErrorCode == CatalogProblemCodes.TopInvalid)
                ? CatalogProblemCodes.TopInvalid
            : CatalogProblemCodes.FilterMismatch;
    }

    private static Dictionary<string, string[]> BuildFieldErrors(
        FluentValidation.Results.ValidationResult validation,
        string language)
    {
        return validation.Errors
            .GroupBy(error => ToCamelCase(error.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(error => CatalogProblemDetailsFactory.LocalizeFieldMessage(
                        error.ErrorCode,
                        language))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return string.Create(value.Length, value, static (buffer, source) =>
        {
            buffer[0] = char.ToLowerInvariant(source[0]);
            source.AsSpan(1).CopyTo(buffer[1..]);
        });
    }

    private void ApplyNoStore()
    {
        Response.Headers.CacheControl = "no-store";
    }

}
