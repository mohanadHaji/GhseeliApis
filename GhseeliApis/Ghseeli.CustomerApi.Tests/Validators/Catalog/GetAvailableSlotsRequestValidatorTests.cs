using FluentAssertions;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Tests.Support;
using GhseeliApis.Validators.Catalog;

namespace GhseeliApis.Tests.Validators.Catalog;

/// <summary>
/// Defines customer available-slot request validation boundaries.
/// </summary>
public sealed class GetAvailableSlotsRequestValidatorTests
{
    private readonly ManualTimeProvider _time = new(
        new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Validate_TodayAndMaximumDate_AreAccepted()
    {
        var validator = new GetAvailableSlotsRequestValidator(_time);

        validator.Validate(ValidRequest(new DateOnly(2026, 9, 5)))
            .IsValid.Should().BeTrue();
        validator.Validate(ValidRequest(new DateOnly(2027, 9, 6)))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(2026, 9, 4)]
    [InlineData(2027, 9, 7)]
    public void Validate_DateOutsideSearchBound_IsRejected(int year, int month, int day)
    {
        var result = new GetAvailableSlotsRequestValidator(_time)
            .Validate(ValidRequest(new DateOnly(year, month, day)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(GetAvailableSlotsRequest.Date));
    }

    [Fact]
    public void Validate_DuplicateOfferingsAndAddons_AreRejected()
    {
        var request = ValidRequest(new DateOnly(2026, 9, 7));
        var offeringId = request.Items.Single().OfferingId;
        var choiceId = Guid.NewGuid();
        request.Items =
        [
            new CatalogAvailableSlotsItemRequest
            {
                OfferingId = offeringId,
                SelectedAddons =
                [
                    new CatalogAvailableSlotsSelectionRequest
                    {
                        AddonChoiceId = choiceId,
                        Quantity = 1
                    },
                    new CatalogAvailableSlotsSelectionRequest
                    {
                        AddonChoiceId = choiceId,
                        Quantity = 1
                    }
                ]
            },
            new CatalogAvailableSlotsItemRequest { OfferingId = offeringId }
        ];

        var result = new GetAvailableSlotsRequestValidator(_time).Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains(
            "Duplicate offerings", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains(
            "Duplicate add-on", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_PartialLocation_IsRejected()
    {
        var request = ValidRequest(new DateOnly(2026, 9, 7));
        request.CustomerLocation = new CatalogAvailableSlotsLocationRequest
        {
            Latitude = 32.08
        };

        var result = new GetAvailableSlotsRequestValidator(_time).Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName.EndsWith(
                nameof(CatalogAvailableSlotsLocationRequest.Longitude),
                StringComparison.Ordinal));
    }

    private static GetAvailableSlotsRequest ValidRequest(DateOnly date) => new()
    {
        Date = date,
        Language = "ar",
        Items =
        [
            new CatalogAvailableSlotsItemRequest
            {
                OfferingId = Guid.NewGuid()
            }
        ]
    };
}
