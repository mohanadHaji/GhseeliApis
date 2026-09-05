using FluentAssertions;
using Ghseeli.BusinessApi.Validators.Internal;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Tests.Validators.Internal;

/// <summary>
/// Defines malformed internal available-slot request boundaries.
/// </summary>
public sealed class AvailableSlotsRequestValidatorTests
{
    private readonly AvailableSlotsRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_Succeeds()
    {
        _validator.Validate(ValidRequest()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingScopeDateCurrencyAndItems_ReturnsFieldErrors()
    {
        var result = _validator.Validate(new AvailableSlotsRequest());

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain(
        [
            nameof(AvailableSlotsRequest.CompanyId),
            nameof(AvailableSlotsRequest.BranchId),
            nameof(AvailableSlotsRequest.Date),
            nameof(AvailableSlotsRequest.Currency),
            nameof(AvailableSlotsRequest.Items)
        ]);
    }

    [Fact]
    public void Validate_DuplicateOfferingsAndSelections_ReturnsErrors()
    {
        var request = ValidRequest();
        var offeringId = request.Items.Single().OfferingId;
        var choiceId = Guid.NewGuid();
        request.Items =
        [
            new AvailableSlotsItemRequest
            {
                OfferingId = offeringId,
                SelectedAddons =
                [
                    new ValidateAppointmentAddonSelectionRequest
                    {
                        AddonChoiceId = choiceId,
                        Quantity = 1
                    },
                    new ValidateAppointmentAddonSelectionRequest
                    {
                        AddonChoiceId = choiceId,
                        Quantity = 1
                    }
                ]
            },
            new AvailableSlotsItemRequest { OfferingId = offeringId }
        ];

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains(
            "Duplicate offerings", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains(
            "Duplicate add-on", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UnsupportedContractNonPositiveVersionAndExtremeDate_ReturnsErrors()
    {
        var request = ValidRequest();
        request.ContractVersion = "v999";
        request.ExpectedCatalogVersion = 0;
        request.Date = DateOnly.MaxValue;

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain(
        [
            nameof(AvailableSlotsRequest.ContractVersion),
            nameof(AvailableSlotsRequest.ExpectedCatalogVersion),
            nameof(AvailableSlotsRequest.Date)
        ]);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(91, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(0, 181)]
    public void Validate_InvalidCoordinates_ReturnsErrors(double latitude, double longitude)
    {
        var request = ValidRequest();
        request.CustomerLocation = new AppointmentCustomerLocationFacts
        {
            Latitude = latitude,
            Longitude = longitude
        };

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    private static AvailableSlotsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        BranchId = Guid.NewGuid(),
        Date = new DateOnly(2026, 9, 7),
        Currency = "ILS",
        Items =
        [
            new AvailableSlotsItemRequest
            {
                OfferingId = Guid.NewGuid()
            }
        ]
    };
}
