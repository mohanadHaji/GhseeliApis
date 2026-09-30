using FluentAssertions;
using Ghseeli.IntegrationContracts.Vehicles;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Tests.Support;
using GhseeliApis.Validators.Catalog;

namespace GhseeliApis.Tests.Validators.Catalog;

/// <summary>
/// Verifies customer availability-search request boundaries.
/// </summary>
public sealed class AvailabilitySearchRequestValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 25, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("past-date")]
    [InlineData("future-date")]
    [InlineData("business-vertical")]
    [InlineData("category")]
    [InlineData("latitude-only")]
    [InlineData("latitude-range")]
    [InlineData("longitude-range")]
    public void Validate_RejectsInvalidDateCategoryAndLocation(string condition)
    {
        var request = ValidRequest();
        switch (condition)
        {
            case "past-date":
                request.Date = new DateOnly(2026, 9, 24);
                break;
            case "future-date":
                request.Date = new DateOnly(2027, 9, 27);
                break;
            case "category":
                request.CategoryId = Guid.Empty;
                break;
            case "business-vertical":
                request.BusinessVerticalId = Guid.Empty;
                break;
            case "latitude-only":
                request.Latitude = 31.7;
                break;
            case "latitude-range":
                request.Latitude = 91;
                request.Longitude = 35;
                break;
            case "longitude-range":
                request.Latitude = 31.7;
                request.Longitude = 181;
                break;
        }

        var result = new AvailabilitySearchRequestValidator(
            new ManualTimeProvider(Now)).Validate(request);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("business-vertical", CatalogProblemCodes.BusinessVerticalInvalid)]
    [InlineData("category", CatalogProblemCodes.CategoryInvalid)]
    [InlineData("latitude-only", CatalogProblemCodes.LocationInvalid)]
    public void Validate_UsesStableFieldSpecificCodes(string condition, string expectedCode)
    {
        var request = ValidRequest();
        switch (condition)
        {
            case "business-vertical":
                request.BusinessVerticalId = Guid.Empty;
                break;
            case "category":
                request.CategoryId = Guid.Empty;
                break;
            default:
                request.Latitude = 31.7;
                break;
        }

        var result = new AvailabilitySearchRequestValidator(
            new ManualTimeProvider(Now)).Validate(request);

        result.Errors.Should().Contain(error => error.ErrorCode == expectedCode);
    }

    [Fact]
    public void Validate_AcceptsTodayAndCompleteBoundaryLocation()
    {
        var request = ValidRequest();
        request.Latitude = -90;
        request.Longitude = 180;

        var result = new AvailabilitySearchRequestValidator(
            new ManualTimeProvider(Now)).Validate(request);

        result.IsValid.Should().BeTrue();
    }

    private static AvailabilitySearchRequest ValidRequest() => new()
    {
        VehicleType = VehicleType.Sedan,
        Date = new DateOnly(2026, 9, 25),
        PreferredLocalTime = new TimeOnly(10, 30)
    };
}
