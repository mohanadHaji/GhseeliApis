using FluentAssertions;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.IntegrationContracts.InternalHttp;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies internal-service authentication options fail fast for invalid security configuration.
/// </summary>
public class InternalServiceAuthenticationOptionsValidatorTests
{
    private readonly InternalServiceAuthenticationOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenServiceIdsAreDuplicatedExactly_Fails()
    {
        var options = CreateValidOptions();
        options.Services.Add(new InternalServiceDefinition
        {
            ServiceId = "customer-api-tests",
            ActiveSecret = "AnotherActiveSecret_Minimum32Chars",
            AllowedOperations = [InternalServiceOperationNames.CatalogSnapshot]
        });

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenNextSecretIsTooShort_Fails()
    {
        var options = CreateValidOptions();
        options.Services[0].NextSecret = "too-short";

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("NextSecret", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenAllowedOperationsContainUnknownValue_Fails()
    {
        var options = CreateValidOptions();
        options.Services[0].AllowedOperations =
        [
            InternalServiceOperationNames.CatalogSnapshot,
            "unknown_operation"
        ];

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("AllowedOperations", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenAllowedOperationsContainDuplicates_Fails()
    {
        var options = CreateValidOptions();
        options.Services[0].AllowedOperations =
        [
            InternalServiceOperationNames.CatalogSnapshot,
            InternalServiceOperationNames.CatalogSnapshot
        ];

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    private static InternalServiceAuthenticationOptions CreateValidOptions()
    {
        return new InternalServiceAuthenticationOptions
        {
            Services =
            [
                new InternalServiceDefinition
                {
                    ServiceId = "customer-api-tests",
                    ActiveSecret = "ValidatorTestActiveSecret_Minimum32Chars",
                    AllowedOperations =
                    [
                        InternalServiceOperationNames.CatalogSnapshot,
                        InternalServiceOperationNames.AppointmentValidate
                    ]
                }
            ]
        };
    }
}
