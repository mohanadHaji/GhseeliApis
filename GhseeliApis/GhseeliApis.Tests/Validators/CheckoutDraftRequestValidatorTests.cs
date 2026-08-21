using FluentAssertions;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Validators.Checkout;

namespace GhseeliApis.Tests.Validators;

/// <summary>
/// Tests the checkout draft request validators.
/// </summary>
public class CheckoutDraftRequestValidatorTests
{
    private readonly CreateCheckoutDraftRequestValidator _createValidator = new();
    private readonly UpdateCheckoutDraftRequestValidator _updateValidator = new();

    [Fact]
    public void Validate_RejectsZeroWidthRequiredText_AndNonFiniteCoordinates()
    {
        var request = CreateValidCreateRequest();
        request.Vehicle.VehicleType = "\u200B";
        request.Location.AddressLine = "\u200B";
        request.Location.Latitude = double.NaN;
        request.Location.Longitude = double.PositiveInfinity;

        var result = _createValidator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain([
            "Vehicle.VehicleType",
            "Location.AddressLine",
            "Location.Latitude",
            "Location.Longitude"
        ]);
    }

    [Fact]
    public void Validate_RejectsDuplicateOfferings_AndDuplicateAddonChoices()
    {
        var offeringSourceId = Guid.NewGuid();
        var addonChoiceSourceId = Guid.NewGuid();
        var request = CreateValidCreateRequest();
        request.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = offeringSourceId,
                Selections =
                [
                    new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = addonChoiceSourceId,
                        Quantity = 1
                    },
                    new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = addonChoiceSourceId,
                        Quantity = 0
                    }
                ]
            },
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = offeringSourceId
            }
        ];

        var result = _createValidator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorCode == "checkout_duplicate_offering" &&
            error.PropertyName == "Items");
        result.Errors.Should().Contain(error =>
            error.ErrorCode == "checkout_duplicate_addon_choice" &&
            error.PropertyName == "Items[0].Selections");
    }

    [Fact]
    public void Validate_RejectsNullItemAndSelectionElements()
    {
        var firstOfferingId = Guid.NewGuid();
        var secondOfferingId = Guid.NewGuid();
        var request = CreateValidCreateRequest();
        request.Items =
        [
            null!,
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = secondOfferingId,
                Selections = [null!]
            }
        ];

        var result = _createValidator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == "Items[0]" &&
            error.ErrorCode == "checkout_required");
        result.Errors.Should().Contain(error =>
            error.PropertyName == "Items[1].Selections[0]" &&
            error.ErrorCode == "checkout_required");
    }

    [Fact]
    public void Validate_UpdateRejectsNonPositiveExpectedVersion()
    {
        var request = new UpdateCheckoutDraftRequest
        {
            ExpectedVersion = 0,
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = Guid.NewGuid(),
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(2),
            Vehicle = new CheckoutDraftVehicleRequest
            {
                VehicleType = "SUV"
            },
            Location = new CheckoutDraftLocationRequest
            {
                AddressLine = "Main street",
                Latitude = 32.1,
                Longitude = 34.8
            },
            Items =
            [
                new CheckoutDraftItemRequest
                {
                    OfferingSourceId = Guid.NewGuid()
                }
            ]
        };

        var result = _updateValidator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error =>
            error.PropertyName == nameof(UpdateCheckoutDraftRequest.ExpectedVersion) &&
            error.ErrorCode == "checkout_expected_version_invalid");
    }

    [Theory]
    [InlineData(-90d, -180d)]
    [InlineData(90d, 180d)]
    public void Validate_AllowsInclusiveCoordinateBounds(double latitude, double longitude)
    {
        var request = CreateValidCreateRequest();
        request.Location.Latitude = latitude;
        request.Location.Longitude = longitude;

        var result = _createValidator.Validate(request);

        result.Errors.Should().NotContain(error =>
            error.PropertyName == "Location.Latitude" ||
            error.PropertyName == "Location.Longitude");
    }

    private static CreateCheckoutDraftRequest CreateValidCreateRequest() =>
        new()
        {
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = Guid.NewGuid(),
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(2),
            Vehicle = new CheckoutDraftVehicleRequest
            {
                VehicleType = "Sedan",
                LicensePlate = "12-345-67",
                Make = "Toyota",
                Model = "Corolla",
                Color = "Blue"
            },
            Location = new CheckoutDraftLocationRequest
            {
                AddressLine = "الشارع 1",
                City = "حيفا",
                Area = "الكرمل",
                Latitude = 32.1,
                Longitude = 34.8
            },
            Items =
            [
                new CheckoutDraftItemRequest
                {
                    OfferingSourceId = Guid.NewGuid(),
                    Selections =
                    [
                        new CheckoutDraftSelectionRequest
                        {
                            AddonChoiceSourceId = Guid.NewGuid(),
                            Quantity = 1
                        }
                    ]
                }
            ]
        };
}
