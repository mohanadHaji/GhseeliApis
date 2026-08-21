using FluentAssertions;
using GhseeliApis.DTOs.Configuration;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Validators.Configuration;

namespace GhseeliApis.Tests.Validators.Configuration;

/// <summary>
/// Verifies supported configuration language query values.
/// </summary>
public class GetConfigurationRequestValidatorTests
{
    private readonly GetConfigurationRequestValidator _validator = new();

    [Theory]
    [InlineData("en")]
    [InlineData("arabic")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-")]
    [InlineData(";q=1")]
    [InlineData("he-IL")]
    [InlineData("ar-SA")]
    [InlineData("he;q=0.8")]
    [InlineData("ar,he")]
    public void Validate_UnsupportedLanguageOverride_Fails(string language)
    {
        var result = _validator.Validate(new GetConfigurationRequest
        {
            Language = language
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].PropertyName.Should().Be(nameof(GetConfigurationRequest.Language));
        result.Errors[0].ErrorCode.Should().Be(ConfigurationProblemCodes.LanguageInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ar")]
    [InlineData("AR")]
    [InlineData("he")]
    [InlineData("He")]
    [InlineData(" he ")]
    public void Validate_SupportedLanguageOverride_Succeeds(string? language)
    {
        var result = _validator.Validate(new GetConfigurationRequest
        {
            Language = language
        });

        result.IsValid.Should().BeTrue();
    }
}
