using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.Extensions.Options;

namespace Ghseeli.BusinessApi.InternalServices;

public sealed class InternalServiceAuthenticationOptions
{
    public const string SectionName = "InternalServiceAuthentication";

    public bool RequireHttps { get; set; } = true;
    public bool AllowInsecureHttpInDevelopment { get; set; }
    public int AllowedClockSkewSeconds { get; set; } = 120;
    public int NonceLifetimeSeconds { get; set; } = 300;
    public int IdempotencyLifetimeSeconds { get; set; } = 86_400;
    public int InProgressWaitMilliseconds { get; set; } = 5_000;
    public int InProgressPollMilliseconds { get; set; } = 50;
    public int MaxRequestBodyBytes { get; set; } = InternalServiceWireConstants.MaxRequestBodyBytes;
    public int MaxStoredResponseBytes { get; set; } = InternalServiceWireConstants.MaxStoredResponseBytes;
    public List<InternalServiceDefinition> Services { get; set; } = [];
}

public sealed class InternalServiceDefinition
{
    public string ServiceId { get; set; } = string.Empty;
    public string ActiveSecret { get; set; } = string.Empty;
    public string? NextSecret { get; set; }
    public List<string> AllowedOperations { get; set; } = [];
}

public sealed class InternalServiceAuthenticationOptionsValidator :
    IValidateOptions<InternalServiceAuthenticationOptions>
{
    private static readonly StringComparer ServiceIdComparer = StringComparer.Ordinal;
    private static readonly HashSet<string> KnownOperations = new(StringComparer.Ordinal)
    {
        InternalServiceOperationNames.CatalogSnapshot,
        InternalServiceOperationNames.AppointmentValidate,
        InternalServiceOperationNames.AppointmentAvailableSlots,
        InternalServiceOperationNames.ReservationCreate,
        InternalServiceOperationNames.ReservationStatusRead
    };

    public ValidateOptionsResult Validate(
        string? name,
        InternalServiceAuthenticationOptions options)
    {
        var failures = new List<string>();

        if (options.Services.Count == 0)
        {
            failures.Add(
                $"{InternalServiceAuthenticationOptions.SectionName}:Services must contain at least one service.");
        }

        if (options.AllowedClockSkewSeconds <= 0 ||
            options.NonceLifetimeSeconds <= 0 ||
            options.IdempotencyLifetimeSeconds <= 0 ||
            options.InProgressWaitMilliseconds <= 0 ||
            options.InProgressPollMilliseconds <= 0 ||
            options.MaxRequestBodyBytes <= 0 ||
            options.MaxStoredResponseBytes <= 0)
        {
            failures.Add(
                $"{InternalServiceAuthenticationOptions.SectionName} must use positive numeric limits.");
        }

        if (options.Services.Count > 0)
        {
            var duplicateServiceIds = options.Services
                .GroupBy(service => service.ServiceId, ServiceIdComparer)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateServiceIds.Length > 0)
            {
                failures.Add(
                    $"{InternalServiceAuthenticationOptions.SectionName}:Services contains duplicate ServiceId values using case-sensitive matching.");
            }
        }

        for (var index = 0; index < options.Services.Count; index++)
        {
            var service = options.Services[index];

            if (!InternalServiceHeaderValueValidator.IsValidServiceId(service.ServiceId))
            {
                failures.Add(
                    $"{InternalServiceAuthenticationOptions.SectionName}:Services:{index}:ServiceId is invalid.");
            }

            if (!HasValidSecret(service.ActiveSecret))
            {
                failures.Add(
                    $"{InternalServiceAuthenticationOptions.SectionName}:Services:{index}:ActiveSecret is invalid.");
            }

            if (service.NextSecret is not null && !HasValidSecret(service.NextSecret))
            {
                failures.Add(
                    $"{InternalServiceAuthenticationOptions.SectionName}:Services:{index}:NextSecret is invalid.");
            }

            if (service.AllowedOperations.Count == 0)
            {
                failures.Add(
                    $"{InternalServiceAuthenticationOptions.SectionName}:Services:{index}:AllowedOperations must contain at least one operation.");
                continue;
            }

            var invalidOperations = service.AllowedOperations
                .Where(operation => string.IsNullOrWhiteSpace(operation) || !KnownOperations.Contains(operation))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (invalidOperations.Length > 0)
            {
                failures.Add(
                    $"{InternalServiceAuthenticationOptions.SectionName}:Services:{index}:AllowedOperations contains invalid values.");
            }

            var duplicateOperations = service.AllowedOperations
                .GroupBy(operation => operation, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateOperations.Length > 0)
            {
                failures.Add(
                    $"{InternalServiceAuthenticationOptions.SectionName}:Services:{index}:AllowedOperations contains duplicate values.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool HasValidSecret(string? secret)
    {
        return !string.IsNullOrWhiteSpace(secret) &&
               secret.Length >= 32;
    }
}
