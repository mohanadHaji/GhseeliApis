using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Configuration;
using GhseeliApis.Repositories.Interfaces;

namespace GhseeliApis.Services.Configuration;

public interface ICustomerConfigurationService
{
    Task<ConfigurationResponse> GetActiveAsync(
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);
}

public sealed class CustomerConfigurationException : Exception
{
    public CustomerConfigurationException(string code, int statusCode, string message)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public sealed class CustomerConfigurationService : ICustomerConfigurationService
{
    private readonly ICustomerConfigurationRepository _repository;
    private readonly IAppLogger _logger;

    public CustomerConfigurationService(
        ICustomerConfigurationRepository repository,
        IAppLogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ConfigurationResponse> GetActiveAsync(
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ConfigurationLanguageResolver.Resolve(
            requestedLanguage,
            acceptLanguageHeader);

        try
        {
            var configuration = await _repository.GetActiveAsync(cancellationToken);
            if (configuration is null)
            {
                _logger.LogWarning("Customer configuration is unavailable because no active record exists.");
                throw new CustomerConfigurationException(
                    ConfigurationProblemCodes.Unavailable,
                    StatusCodes.Status503ServiceUnavailable,
                    "No active customer configuration exists.");
            }

            var response = new ConfigurationResponse
            {
                Language = language,
                Support = new SupportContactResponse
                {
                    Email = configuration.SupportEmail,
                    Phone = configuration.SupportPhone
                },
                Display = new DisplayConfigurationResponse
                {
                    Name = SelectLocalizedText(
                        language,
                        configuration.DisplayNameAr,
                        configuration.DisplayNameHe)
                },
                Legal = new LegalConfigurationResponse
                {
                    Notice = SelectLocalizedText(
                        language,
                        configuration.LegalNoticeAr,
                        configuration.LegalNoticeHe),
                    PrivacyPolicyUrl = configuration.PrivacyPolicyUrl,
                    TermsOfServiceUrl = configuration.TermsOfServiceUrl
                },
                Maintenance = new MaintenanceConfigurationResponse
                {
                    IsEnabled = configuration.IsMaintenanceModeEnabled,
                    Message = SelectLocalizedOptionalText(
                        language,
                        configuration.MaintenanceMessageAr,
                        configuration.MaintenanceMessageHe)
                }
            };

            _logger.LogInfo(
                $"Returned active customer configuration {configuration.Id} for language {language}.");
            return response;
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                "Customer configuration invariant violated: multiple active records were returned.",
                exception);
            throw new CustomerConfigurationException(
                ConfigurationProblemCodes.Unavailable,
                StatusCodes.Status503ServiceUnavailable,
                "Multiple active customer configurations were returned.");
        }
    }

    private static string SelectLocalizedText(
        string language,
        string arabicValue,
        string? hebrewValue)
    {
        return language == ConfigurationLanguageResolver.Hebrew
            ? ConfigurationTextNormalizer.NormalizeOptional(hebrewValue)
                ?? ConfigurationTextNormalizer.NormalizeRequired(arabicValue)
            : ConfigurationTextNormalizer.NormalizeRequired(arabicValue);
    }

    private static string? SelectLocalizedOptionalText(
        string language,
        string? arabicValue,
        string? hebrewValue)
    {
        return language == ConfigurationLanguageResolver.Hebrew
            ? ConfigurationTextNormalizer.NormalizeOptional(hebrewValue)
                ?? ConfigurationTextNormalizer.NormalizeOptional(arabicValue)
            : ConfigurationTextNormalizer.NormalizeOptional(arabicValue);
    }
}
