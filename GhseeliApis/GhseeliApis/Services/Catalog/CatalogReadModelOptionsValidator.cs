using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Catalog;

public sealed class CatalogReadModelOptionsValidator :
    IValidateOptions<CatalogReadModelOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        CatalogReadModelOptions options)
    {
        var failures = new List<string>();

        if (options.FreshWindowSeconds <= 0)
        {
            failures.Add("CatalogReadModel:FreshWindowSeconds must be greater than zero.");
        }

        if (options.MaxStaleWindowSeconds <= 0)
        {
            failures.Add("CatalogReadModel:MaxStaleWindowSeconds must be greater than zero.");
        }

        if (options.MaxStaleWindowSeconds < options.FreshWindowSeconds)
        {
            failures.Add("CatalogReadModel:MaxStaleWindowSeconds must be greater than or equal to FreshWindowSeconds.");
        }

        if (options.LeaseDurationSeconds <= 0)
        {
            failures.Add("CatalogReadModel:LeaseDurationSeconds must be greater than zero.");
        }

        if (options.LeaseDurationSeconds > options.MaxStaleWindowSeconds)
        {
            failures.Add("CatalogReadModel:LeaseDurationSeconds must be less than or equal to MaxStaleWindowSeconds.");
        }

        var providers = (IReadOnlyCollection<CatalogProviderRegistrationOptions>)
            (options.Providers ?? []);
        var duplicateProviders = providers
            .GroupBy(provider => provider.SourceCompanyId)
            .Where(group => group.Key == Guid.Empty || group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateProviders.Any(providerId => providerId == Guid.Empty))
        {
            failures.Add("CatalogReadModel:Providers entries must use a non-empty SourceCompanyId.");
        }

        foreach (var duplicateProviderId in duplicateProviders.Where(providerId => providerId != Guid.Empty))
        {
            failures.Add($"CatalogReadModel:Providers contains a duplicate SourceCompanyId '{duplicateProviderId:D}'.");
        }

        var duplicateOrders = providers
            .GroupBy(provider => provider.Order)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        foreach (var duplicateOrder in duplicateOrders)
        {
            failures.Add($"CatalogReadModel:Providers contains a duplicate Order value '{duplicateOrder}'.");
        }

        if (providers.Any(provider => provider.Order < 0))
        {
            failures.Add("CatalogReadModel:Providers entries must use a non-negative Order value.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
