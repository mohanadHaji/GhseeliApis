using FluentValidation;
using FluentValidation.AspNetCore;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Filters;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/checkout")]
public sealed class CheckoutPricingController : ControllerBase
{
    private const string OrderGuidHeaderName = "X-Order-Guid";
    private const long MaxPricingRequestBodyBytes = 65_536;
    private readonly ICheckoutPricingService _service;
    private readonly IValidator<GetCheckoutDraftRequest> _queryValidator;
    private readonly IValidator<RepriceCheckoutDraftRequest> _requestValidator;
    private readonly IAppLogger _logger;

    public CheckoutPricingController(
        ICheckoutPricingService service,
        IValidator<GetCheckoutDraftRequest> queryValidator,
        IValidator<RepriceCheckoutDraftRequest> requestValidator,
        IAppLogger logger)
    {
        _service = service;
        _queryValidator = queryValidator;
        _requestValidator = requestValidator;
        _logger = logger;
    }

    [HttpPost("reprice")]
    [EnforceJsonRequestContentType]
    [RequestSizeLimit(MaxPricingRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(
        MaxPricingRequestBodyBytes,
        CheckoutPricingProblemCodes.RequestBodyTooLarge)]
    [ProducesResponseType<CheckoutDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Reprice(
        [CustomizeValidator(Skip = true)]
        [FromBody] RepriceCheckoutDraftRequest request,
        [FromHeader(Name = OrderGuidHeaderName)] string? orderGuid,
        [FromQuery] string? language,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        ApplyNoStore();

        var validationProblem = await ValidateAsync(
            new GetCheckoutDraftRequest
            {
                Language = language
            },
            _queryValidator,
            acceptLanguage,
            cancellationToken);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        validationProblem = await ValidateAsync(
            request,
            _requestValidator,
            acceptLanguage,
            cancellationToken);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        if (!Guid.TryParse(orderGuid, out var parsedOrderGuid) || parsedOrderGuid == Guid.Empty)
        {
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                CheckoutPricingProblemCodes.Invalid,
                language,
                acceptLanguage,
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["orderGuid"] = [CheckoutDraftFieldErrorCodes.GuidRequired]
                });
        }

        try
        {
            return Ok(await _service.RepriceDraftAsync(
                parsedOrderGuid,
                request,
                RequireDeviceId(),
                language,
                acceptLanguage,
                cancellationToken));
        }
        catch (CheckoutPricingException exception)
        {
            _logger.LogWarning($"Draft pricing failed with code {exception.Code}.");
            return ProblemResult(exception.StatusCode, exception.Code, language, acceptLanguage, exception.FieldErrors);
        }
    }

    private async Task<ObjectResult?> ValidateAsync<TRequest>(
        TRequest request,
        IValidator<TRequest> validator,
        string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (validation.IsValid)
        {
            return null;
        }

        var errorLanguage = ConfigurationLanguageResolver.ResolveFromHeader(acceptLanguage);
        var code = validation.Errors.Any(error => error.ErrorCode == ConfigurationProblemCodes.LanguageInvalid)
            ? ConfigurationProblemCodes.LanguageInvalid
            : CheckoutPricingProblemCodes.Invalid;

        _logger.LogWarning(
            $"Draft pricing request validation failed with {validation.Errors.Count} error(s).");

        var problem = CheckoutPricingProblemDetailsFactory.Create(
            StatusCodes.Status400BadRequest,
            code,
            errorLanguage,
            HttpContext.TraceIdentifier,
            validation.Errors
                .GroupBy(error => ToCamelCasePath(error.PropertyName), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(error => CheckoutPricingProblemDetailsFactory.LocalizeFieldMessage(
                            error.ErrorCode,
                            errorLanguage))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.Ordinal));

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }

    private ObjectResult ProblemResult(
        int statusCode,
        string code,
        string? requestedLanguage,
        string? acceptLanguage,
        IReadOnlyDictionary<string, string[]>? fieldErrors)
    {
        var language = ConfigurationLanguageResolver.Resolve(requestedLanguage, acceptLanguage);
        var localizedFieldErrors = fieldErrors?
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .Select(fieldCode => CheckoutPricingProblemDetailsFactory.LocalizeFieldMessage(
                        fieldCode,
                        language))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var problem = CheckoutPricingProblemDetailsFactory.Create(
            statusCode,
            code,
            language,
            HttpContext.TraceIdentifier,
            localizedFieldErrors);

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    private Guid RequireDeviceId()
    {
        var deviceId = HttpContext.GetDeviceId();
        if (deviceId.HasValue)
        {
            return deviceId.Value;
        }

        throw new InvalidOperationException("Device identity is missing from the request context.");
    }

    private void ApplyNoStore()
    {
        Response.Headers.CacheControl = "no-store";
    }

    private static string ToCamelCasePath(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var lowerNextLetter = true;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (lowerNextLetter && char.IsLetter(character))
            {
                buffer[index] = char.ToLowerInvariant(character);
                lowerNextLetter = false;
                continue;
            }

            buffer[index] = character;
            lowerNextLetter = character is '.' or ']';
        }

        return new string(buffer);
    }
}
