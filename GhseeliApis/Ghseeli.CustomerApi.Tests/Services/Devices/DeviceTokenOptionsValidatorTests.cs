using FluentAssertions;
using GhseeliApis.Services.Devices;

namespace GhseeliApis.Tests.Services.Devices;

/// <summary>
/// Tests startup validation for device-token security limits.
/// </summary>
public class DeviceTokenOptionsValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3661)]
    public void Validate_InvalidLifetime_Fails(int lifetimeDays)
    {
        var result = new DeviceTokenOptionsValidator().Validate(
            null,
            new DeviceTokenOptions { LifetimeDays = lifetimeDays });

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = new DeviceTokenOptionsValidator().Validate(null, new DeviceTokenOptions());

        result.Succeeded.Should().BeTrue();
    }
}
