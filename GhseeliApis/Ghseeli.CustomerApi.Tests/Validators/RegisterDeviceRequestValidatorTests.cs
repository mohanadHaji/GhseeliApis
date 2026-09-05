using FluentAssertions;
using GhseeliApis.DTOs.Devices;
using GhseeliApis.Validators.Devices;

namespace GhseeliApis.Tests.Validators;

/// <summary>
/// Tests the device registration HTTP contract.
/// </summary>
public class RegisterDeviceRequestValidatorTests
{
    private readonly RegisterDeviceRequestValidator _validator = new();

    [Theory]
    [InlineData("iOS")]
    [InlineData("ios")]
    [InlineData("Android")]
    [InlineData("ANDROID")]
    public void Validate_SupportedPlatform_IsValid(string platform)
    {
        var result = _validator.Validate(new RegisterDeviceRequest
        {
            InstallationId = Guid.NewGuid(),
            Platform = platform,
            AppVersion = "1.2.3"
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyInstallationId_IsInvalid()
    {
        var result = _validator.Validate(new RegisterDeviceRequest
        {
            InstallationId = Guid.Empty,
            Platform = "iOS"
        });

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Windows")]
    [InlineData("1")]
    public void Validate_UnsupportedPlatform_IsInvalid(string platform)
    {
        var result = _validator.Validate(new RegisterDeviceRequest
        {
            InstallationId = Guid.NewGuid(),
            Platform = platform
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_OverlongAppVersion_IsInvalid()
    {
        var result = _validator.Validate(new RegisterDeviceRequest
        {
            InstallationId = Guid.NewGuid(),
            Platform = "Android",
            AppVersion = new string('a', 33)
        });

        result.IsValid.Should().BeFalse();
    }
}
