using FluentAssertions;
using GhseeliApis.DTOs.Banners;
using GhseeliApis.Validators.Banners;

namespace GhseeliApis.Tests.Validators.Banners;

/// <summary>
/// Freezes banner image, ordering, and row-version validation.
/// </summary>
public sealed class BannerRequestValidatorsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://cdn.example.test/banner.png")]
    [InlineData("https://user:password@cdn.example.test/banner.png")]
    [InlineData("/banner.png")]
    public void Create_rejects_missing_or_unsafe_image_urls(string? imageUrl)
    {
        var result = new CreateBannerRequestValidator().Validate(new CreateBannerRequest
        {
            ImageUrl = imageUrl!,
            DisplayOrder = 1,
            IsActive = true
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "ImageUrl");
    }

    [Fact]
    public void Create_accepts_exactly_500_character_https_url_and_rejects_501()
    {
        const string prefix = "https://cdn.example.test/";
        var validator = new CreateBannerRequestValidator();

        var accepted = validator.Validate(new CreateBannerRequest
        {
            ImageUrl = prefix + new string('a', 500 - prefix.Length),
            DisplayOrder = 0,
            IsActive = true
        });
        var rejected = validator.Validate(new CreateBannerRequest
        {
            ImageUrl = prefix + new string('a', 501 - prefix.Length),
            DisplayOrder = 10000,
            IsActive = true
        });

        accepted.IsValid.Should().BeTrue();
        rejected.Errors.Should().Contain(error => error.PropertyName == "ImageUrl");
    }

    [Theory]
    [InlineData("https://user:password@cdn.example.test/banner.png")]
    [InlineData("https:///banner.png")]
    [InlineData("https://")]
    public void Create_rejects_credentials_and_missing_or_empty_hosts(string imageUrl)
    {
        var result = new CreateBannerRequestValidator().Validate(new CreateBannerRequest
        {
            ImageUrl = imageUrl,
            DisplayOrder = 1,
            IsActive = true
        });

        result.Errors.Should().Contain(error => error.PropertyName == "ImageUrl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void Create_accepts_display_order_contract_boundaries(int displayOrder)
    {
        var result = new CreateBannerRequestValidator().Validate(new CreateBannerRequest
        {
            ImageUrl = "https://cdn.example.test/banner.png",
            DisplayOrder = displayOrder,
            IsActive = true
        });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void Create_rejects_display_order_outside_contract(int displayOrder)
    {
        var result = new CreateBannerRequestValidator().Validate(new CreateBannerRequest
        {
            ImageUrl = "https://cdn.example.test/banner.png",
            DisplayOrder = displayOrder,
            IsActive = true
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "DisplayOrder");
    }

    [Fact]
    public void Update_requires_an_eight_byte_base64_rowversion()
    {
        var validator = new UpdateBannerRequestValidator();
        var valid = validator.Validate(new UpdateBannerRequest
        {
            ImageUrl = "https://cdn.example.test/banner.png",
            DisplayOrder = 10,
            IsActive = false,
            ExpectedRowVersion = Convert.ToBase64String(new byte[8])
        });
        var invalid = validator.Validate(new UpdateBannerRequest
        {
            ImageUrl = "https://cdn.example.test/banner.png",
            DisplayOrder = 10,
            IsActive = false,
            ExpectedRowVersion = Convert.ToBase64String(new byte[7])
        });

        valid.IsValid.Should().BeTrue();
        invalid.Errors.Should().Contain(error => error.PropertyName == "ExpectedRowVersion");
    }
}
