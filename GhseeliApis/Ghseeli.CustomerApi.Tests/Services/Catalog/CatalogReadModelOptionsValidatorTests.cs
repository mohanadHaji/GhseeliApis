using FluentAssertions;
using GhseeliApis.Services.Catalog;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Tests.Services.Catalog;

/// <summary>
/// Validates startup configuration for customer catalog read models.
/// </summary>
public class CatalogReadModelOptionsValidatorTests
{
    private readonly CatalogReadModelOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenProductionSafeDefaultsAreUsed_Succeeds()
    {
        var options = new CatalogReadModelOptions
        {
            FreshWindowSeconds = 300,
            MaxStaleWindowSeconds = 3600,
            LeaseDurationSeconds = 60,
            Providers = []
        };

        var result = _validator.Validate(Options.DefaultName, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenProviderIdentifiersOrWindowsAreInvalid_Fails()
    {
        var duplicateCompanyId = Guid.NewGuid();
        var options = new CatalogReadModelOptions
        {
            FreshWindowSeconds = 0,
            MaxStaleWindowSeconds = 30,
            LeaseDurationSeconds = 60,
            Providers =
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = duplicateCompanyId,
                    Order = 0
                },
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = duplicateCompanyId,
                    Order = 0
                },
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = Guid.Empty,
                    Order = -1
                }
            ]
        };

        var result = _validator.Validate(Options.DefaultName, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure =>
            failure.Contains("FreshWindowSeconds", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure =>
            failure.Contains("LeaseDurationSeconds", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure =>
            failure.Contains("duplicate SourceCompanyId", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure =>
            failure.Contains("non-empty SourceCompanyId", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure =>
            failure.Contains("duplicate Order", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure =>
            failure.Contains("non-negative Order", StringComparison.Ordinal));
    }
}
