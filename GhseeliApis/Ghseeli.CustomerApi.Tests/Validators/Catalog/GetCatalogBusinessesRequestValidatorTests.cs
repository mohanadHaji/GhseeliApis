using FluentAssertions;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Validators.Catalog;

namespace GhseeliApis.Tests.Validators.Catalog;

/// <summary>
/// Defines validation for catalog business search and ranking parameters.
/// </summary>
public sealed class GetCatalogBusinessesRequestValidatorTests
{
    private readonly GetCatalogBusinessesRequestValidator _validator = new();

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public async Task Validate_AllowedTop_IsValid(int top)
    {
        var result = await _validator.ValidateAsync(
            new GetCatalogBusinessesRequest { Top = top });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(11)]
    public async Task Validate_UnsupportedTop_UsesStableCode(int top)
    {
        var result = await _validator.ValidateAsync(
            new GetCatalogBusinessesRequest { Top = top });

        result.Errors.Should().ContainSingle(error =>
            error.PropertyName == nameof(GetCatalogBusinessesRequest.Top) &&
            error.ErrorCode == CatalogProblemCodes.TopInvalid);
    }

    [Fact]
    public async Task Validate_SearchOver100NormalizedCharacters_IsInvalid()
    {
        var result = await _validator.ValidateAsync(
            new GetCatalogBusinessesRequest
            {
                Search = $"  {new string('x', 101)}  "
            });

        result.Errors.Should().ContainSingle(error =>
            error.PropertyName == nameof(GetCatalogBusinessesRequest.Search));
    }

    [Fact]
    public async Task Validate_WhitespaceSearch_IsTreatedAsAbsent()
    {
        var result = await _validator.ValidateAsync(
            new GetCatalogBusinessesRequest { Search = "   " });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Exactly100NormalizedCharacters_IsAccepted()
    {
        var result = await _validator.ValidateAsync(
            new GetCatalogBusinessesRequest
            {
                Search = $"  {new string('x', 100)}  "
            });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_FormCLength_IsUsed()
    {
        var decomposed = string.Concat(Enumerable.Repeat("e\u0301", 100));

        var result = await _validator.ValidateAsync(
            new GetCatalogBusinessesRequest { Search = decomposed });

        result.IsValid.Should().BeTrue();
    }
}
