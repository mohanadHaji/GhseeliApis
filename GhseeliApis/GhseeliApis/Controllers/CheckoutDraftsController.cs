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
[Route("api/v1/checkout/drafts")]
public sealed class CheckoutDraftsController : ControllerBase
{
    private const long MaxDraftRequestBodyBytes = 65_536;
    private readonly ICheckoutDraftService _service;
    private readonly IValidator<GetCheckoutDraftRequest> _queryValidator;
    private readonly IValidator<CreateCheckoutDraftRequest> _createValidator;
    private readonly IValidator<UpdateCheckoutDraftRequest> _updateValidator;
    private readonly IAppLogger _logger;

    public CheckoutDraftsController(
        ICheckoutDraftService service,
        IValidator<GetCheckoutDraftRequest> queryValidator,
        IValidator<CreateCheckoutDraftRequest> createValidator,
        IValidator<UpdateCheckoutDraftRequest> updateValidator,
        IAppLogger logger)
    {
        _service = service;
        _queryValidator = queryValidator;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxDraftRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(MaxDraftRequestBodyBytes)]
    [ProducesResponseType<CheckoutDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Create(
        [CustomizeValidator(Skip = true)]
        [FromBody] CreateCheckoutDraftRequest request,
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
            _createValidator,
            acceptLanguage,
            cancellationToken);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        try
        {
            return Ok(await _service.CreateAsync(
                request,
                RequireDeviceId(),
                language,
                acceptLanguage,
                cancellationToken));
        }
        catch (CheckoutDraftException exception)
        {
            _logger.LogWarning($"Checkout draft create failed with code {exception.Code}.");
            return ProblemResult(exception.StatusCode, exception.Code, language, acceptLanguage, exception.FieldErrors);
        }
    }

    [HttpGet("{orderGuid:guid}")]
    [ProducesResponseType<CheckoutDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Get(
        Guid orderGuid,
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

        try
        {
            return Ok(await _service.GetAsync(
                orderGuid,
                RequireDeviceId(),
                language,
                acceptLanguage,
                cancellationToken));
        }
        catch (CheckoutDraftException exception)
        {
            _logger.LogWarning($"Checkout draft read failed with code {exception.Code}.");
            return ProblemResult(exception.StatusCode, exception.Code, language, acceptLanguage, exception.FieldErrors);
        }
    }

    [HttpPut("{orderGuid:guid}")]
    [RequestSizeLimit(MaxDraftRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(MaxDraftRequestBodyBytes)]
    [ProducesResponseType<CheckoutDraftResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Update(
        Guid orderGuid,
        [CustomizeValidator(Skip = true)]
        [FromBody] UpdateCheckoutDraftRequest request,
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
            _updateValidator,
            acceptLanguage,
            cancellationToken);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        try
        {
            return Ok(await _service.UpdateAsync(
                orderGuid,
                request,
                RequireDeviceId(),
                language,
                acceptLanguage,
                cancellationToken));
        }
        catch (CheckoutDraftException exception)
        {
            _logger.LogWarning($"Checkout draft update failed with code {exception.Code}.");
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
            : CheckoutDraftProblemCodes.Invalid;

        _logger.LogWarning(
            $"Checkout draft request validation failed with {validation.Errors.Count} error(s).");

        var problem = CheckoutDraftProblemDetailsFactory.Create(
            StatusCodes.Status400BadRequest,
            code,
            errorLanguage,
            HttpContext.TraceIdentifier,
            validation.Errors
                .GroupBy(error => ToCamelCasePath(error.PropertyName), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(error => CheckoutDraftProblemDetailsFactory.LocalizeFieldMessage(
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
                    .Select(fieldCode => CheckoutDraftProblemDetailsFactory.LocalizeFieldMessage(
                        fieldCode,
                        language))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var problem = CheckoutDraftProblemDetailsFactory.Create(
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
