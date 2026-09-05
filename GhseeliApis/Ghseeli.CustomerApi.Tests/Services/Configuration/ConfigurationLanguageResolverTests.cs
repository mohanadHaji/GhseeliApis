using FluentAssertions;
using GhseeliApis.Services.Configuration;

namespace GhseeliApis.Tests.Services.Configuration;

/// <summary>
/// Verifies Accept-Language and query override selection rules.
/// </summary>
public class ConfigurationLanguageResolverTests
{
    public static TheoryData<string> MalformedOrUnsupportedHeaders => new()
    {
        "-",
        ";q=1",
        " , , ",
        "en-US,en;q=0.9",
        "he;q=bogus",
        "he;q=1.5",
        "he;q=0",
        "*;q=0.8"
    };

    public static TheoryData<string?, string?, string> PathologicalInputs => new()
    {
        { "-", "-, ;q=1, he-IL;q=0.8", ConfigurationLanguageResolver.Arabic },
        { ";q=1", "-, ;q=1", ConfigurationLanguageResolver.Arabic },
        { " \t ", "he-IL ; q=0.8, ar;q=0.7", ConfigurationLanguageResolver.Arabic },
        { null, " , , ar-SA ; q=0.5 , he;q=0 ", ConfigurationLanguageResolver.Arabic },
        { "he", "-, ;q=1, ar;q=0.9", ConfigurationLanguageResolver.Hebrew }
    };

    [Fact]
    public void Resolve_SupportedQueryOverride_TakesPrecedence()
    {
        var language = ConfigurationLanguageResolver.Resolve("he", "ar");

        language.Should().Be(ConfigurationLanguageResolver.Hebrew);
    }

    [Fact]
    public void Resolve_HeaderWithQualityValues_SelectsBestSupportedLanguage()
    {
        var language = ConfigurationLanguageResolver.Resolve(
            null,
            "en-US,en;q=0.9,he-IL;q=0.8,ar;q=0.7");

        language.Should().Be(ConfigurationLanguageResolver.Hebrew);
    }

    [Fact]
    public void Resolve_HeaderWithMalformedSegmentsAndSupportedWeightedLanguage_SelectsSupportedLanguage()
    {
        var language = ConfigurationLanguageResolver.ResolveFromHeader(
            "-, ;q=1, en-US;q=0.9, he-IL;q=0.8");

        language.Should().Be(ConfigurationLanguageResolver.Hebrew);
    }

    [Theory]
    [MemberData(nameof(MalformedOrUnsupportedHeaders))]
    public void Resolve_MalformedOrUnsupportedHeader_FallsBackToArabic(string acceptLanguageHeader)
    {
        var language = ConfigurationLanguageResolver.Resolve(null, acceptLanguageHeader);

        language.Should().Be(ConfigurationLanguageResolver.Arabic);
    }

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
    public void TryNormalizeOverride_UnsupportedExplicitValue_ReturnsFalse(string language)
    {
        var isSupported = ConfigurationLanguageResolver.TryNormalizeOverride(language, out var normalized);

        isSupported.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ar", ConfigurationLanguageResolver.Arabic)]
    [InlineData(" AR ", ConfigurationLanguageResolver.Arabic)]
    [InlineData("he", ConfigurationLanguageResolver.Hebrew)]
    [InlineData(" He ", ConfigurationLanguageResolver.Hebrew)]
    public void TryNormalizeOverride_ExactSupportedCodes_Succeed(string language, string expected)
    {
        var isSupported = ConfigurationLanguageResolver.TryNormalizeOverride(language, out var normalized);

        isSupported.Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(PathologicalInputs))]
    public void Resolve_PathologicalInputs_ReturnsStableSupportedFallback(
        string? overrideLanguage,
        string? acceptLanguageHeader,
        string expected)
    {
        var language = ConfigurationLanguageResolver.Resolve(
            overrideLanguage,
            acceptLanguageHeader);

        language.Should().Be(expected);
    }
}
