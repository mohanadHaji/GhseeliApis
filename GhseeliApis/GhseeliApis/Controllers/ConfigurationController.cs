using FluentValidation;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Configuration;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/configuration")]
public sealed class ConfigurationController : ControllerBase
{
    private readonly ICustomerConfigurationService _service;
    private readonly IValidator<GetConfigurationRequest> _validator;
    private readonly IAppLogger _logger;

    public ConfigurationController(
        ICustomerConfigurationService service,
        IValidator<GetConfigurationRequest> validator,
        IAppLogger logger)
    {
        _service = service;
        _validator = validator;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType<ConfigurationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(
        [FromQuery(Name = "language")] string? language,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        var request = new GetConfigurationRequest
        {
            Language = language
        };
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errorLanguage = ConfigurationLanguageResolver.ResolveFromHeader(acceptLanguage);
            _logger.LogWarning(
                $"Configuration request validation failed with {validationResult.Errors.Count} error(s).");
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                ConfigurationProblemCodes.LanguageInvalid,
                errorLanguage,
                BuildFieldErrors(validationResult, errorLanguage));
        }

        try
        {
            var response = await _service.GetActiveAsync(
                language,
                acceptLanguage,
                cancellationToken);
            return Ok(response);
        }
        catch (CustomerConfigurationException exception)
        {
            var selectedLanguage = ConfigurationLanguageResolver.Resolve(language, acceptLanguage);
            _logger.LogWarning($"Configuration retrieval failed with code {exception.Code}.");
            return ProblemResult(
                exception.StatusCode,
                exception.Code,
                selectedLanguage);
        }
    }

    private ObjectResult ProblemResult(
        int statusCode,
        string code,
        string language,
        IDictionary<string, string[]>? fieldErrors = null)
    {
        var problem = ConfigurationProblemDetailsFactory.Create(
            statusCode,
            code,
            language,
            HttpContext.TraceIdentifier,
            fieldErrors);

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    private static Dictionary<string, string[]> BuildFieldErrors(
        FluentValidation.Results.ValidationResult validationResult,
        string language)
    {
        return validationResult.Errors
            .GroupBy(error => ToCamelCase(error.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(error => ConfigurationProblemDetailsFactory.LocalizeFieldMessage(
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
}
