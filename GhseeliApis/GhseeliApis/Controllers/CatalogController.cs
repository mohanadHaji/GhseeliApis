using FluentValidation;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/catalog")]
public sealed class CatalogController : ControllerBase
{
    private readonly ICatalogReadModelService _service;
    private readonly IValidator<GetCatalogCategoriesRequest> _categoriesValidator;
    private readonly IValidator<GetCatalogBusinessesRequest> _businessesValidator;
    private readonly IValidator<GetCatalogBusinessOfferingsRequest> _businessOfferingsValidator;
    private readonly IValidator<GetCatalogResourceRequest> _resourceValidator;
    private readonly IAppLogger _logger;

    public CatalogController(
        ICatalogReadModelService service,
        IValidator<GetCatalogCategoriesRequest> categoriesValidator,
        IValidator<GetCatalogBusinessesRequest> businessesValidator,
        IValidator<GetCatalogBusinessOfferingsRequest> businessOfferingsValidator,
        IValidator<GetCatalogResourceRequest> resourceValidator,
        IAppLogger logger)
    {
        _service = service;
        _categoriesValidator = categoriesValidator;
        _businessesValidator = businessesValidator;
        _businessOfferingsValidator = businessOfferingsValidator;
        _resourceValidator = resourceValidator;
        _logger = logger;
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
        [FromQuery] bool refresh,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();

        var request = new GetCatalogBusinessesRequest
        {
            Language = language,
            BranchId = branchId,
            CategoryId = categoryId,
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
