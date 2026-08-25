using FluentAssertions;
using Ghseeli.BusinessApi.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Verifies fail-closed validation for Business API rate limiting and trusted proxies.
/// </summary>
public sealed class BusinessRateLimitingConfigurationTests
{
    [Fact]
    public void Validate_WhenDefaultsAreUsed_Succeeds()
    {
        var validator = CreateValidator();

        var result = validator.Validate(null, new BusinessRateLimitingOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("permit")]
    [InlineData("window")]
    [InlineData("queue")]
    [InlineData("partition")]
    public void Validate_WhenRateLimitConfigurationIsUnsafe_Fails(string invalidSetting)
    {
        var options = new BusinessRateLimitingOptions();
        switch (invalidSetting)
        {
            case "permit":
                options.InvalidInternal.PermitLimit = 0;
                break;
            case "window":
                options.ValidInternal.WindowSeconds = -1;
                break;
            case "queue":
                options.QueueLimit = 1;
                break;
            case "partition":
                options.MissingPartitionKey = " ";
                break;
        }

        var result = CreateValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "0.0.0.0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99")]
    public void Validate_WhenTrustedProxyConfigurationIsUnsafe_Fails(
        string key,
        string value)
    {
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            [key] = value
        });

        var result = validator.Validate(null, new BusinessRateLimitingOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure =>
            failure.StartsWith("ForwardedHeaders:", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenTrustedProxyConfigurationIsSpecificAndValid_Succeeds()
    {
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.10",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.1.0.0/16"
        });

        var result = validator.Validate(null, new BusinessRateLimitingOptions());

        result.Succeeded.Should().BeTrue();
    }

    private static BusinessRateLimitingOptionsValidator CreateValidator(
        Dictionary<string, string?>? values = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
        return new BusinessRateLimitingOptionsValidator(configuration);
    }
}
