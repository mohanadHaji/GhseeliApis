using FluentAssertions;
using Ghseeli.BusinessApi.DTOs.Availability;
using Ghseeli.BusinessApi.Validators.Availability;
using Ghseeli.BusinessApi.Validators.Internal;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Tests.Validators.Availability;

/// <summary>
/// Verifies FluentValidation request rules for availability management and internal appointment validation.
/// </summary>
public class AvailabilityRequestValidatorsTests
{
    [Theory]
    [InlineData("oversized-companies")]
    [InlineData("oversized-branches")]
    public void AvailabilityDiscoveryRequestValidator_RejectsInvalidCandidateSets(
        string condition)
    {
        var candidates = Enumerable.Range(0, condition == "oversized-companies" ? 51 : 1)
            .Select(_ => new AvailabilityDiscoveryCompanyCandidate
            {
                CompanyId = Guid.NewGuid(),
                BranchIds = Enumerable.Range(
                        0,
                        condition == "oversized-branches" ? 26 : 1)
                    .Select(_ => Guid.NewGuid())
                    .ToArray()
            })
            .ToList();
        if (condition == "empty")
        {
            candidates.Clear();
        }
        else if (condition == "duplicate-company")
        {
            candidates.Add(new AvailabilityDiscoveryCompanyCandidate
            {
                CompanyId = candidates[0].CompanyId,
                BranchIds = [Guid.NewGuid()]
            });
        }
        else if (condition == "duplicate-branch")
        {
            candidates[0].BranchIds =
                [candidates[0].BranchIds.First(), candidates[0].BranchIds.First()];
        }

        var result = new AvailabilityDiscoveryRequestValidator().Validate(
            new AvailabilityDiscoveryRequest
            {
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                PreferredLocalTime = new TimeOnly(10),
                Candidates = candidates
            });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-029")]
    public void AvailabilityDiscoveryRequestValidator_EmptyCandidates_HasExactPath()
    {
        var result = new AvailabilityDiscoveryRequestValidator().Validate(
            new AvailabilityDiscoveryRequest
            {
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                PreferredLocalTime = new TimeOnly(10),
                Candidates = []
            });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error =>
            error.PropertyName == nameof(AvailabilityDiscoveryRequest.Candidates));
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-030")]
    public void AvailabilityDiscoveryRequestValidator_DuplicateCompanyAndBranch_HaveExactPaths()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var result = new AvailabilityDiscoveryRequestValidator().Validate(
            new AvailabilityDiscoveryRequest
            {
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                PreferredLocalTime = new TimeOnly(10),
                Candidates =
                [
                    new AvailabilityDiscoveryCompanyCandidate
                    {
                        CompanyId = companyId,
                        BranchIds = [branchId, branchId]
                    },
                    new AvailabilityDiscoveryCompanyCandidate
                    {
                        CompanyId = companyId,
                        BranchIds = [Guid.NewGuid()]
                    }
                ]
            });

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain(
            nameof(AvailabilityDiscoveryRequest.Candidates),
            "Candidates[0].BranchIds");
    }
    [Fact]
    public void UpdateBranchAvailabilitySettingsRequestValidator_RejectsBlankTimeZoneId()
    {
        var validator = new UpdateBranchAvailabilitySettingsRequestValidator();
        var request = new UpdateBranchAvailabilitySettingsRequest
        {
            TimeZoneId = " ",
            MinimumLeadMinutes = 0,
            BookingHorizonDays = 30,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(UpdateBranchAvailabilitySettingsRequest.TimeZoneId));
    }

    [Fact]
    public void UpdateBranchAvailabilitySettingsRequestValidator_AcceptsExactMaximumLeadAndHorizon()
    {
        var validator = new UpdateBranchAvailabilitySettingsRequestValidator();
        var request = new UpdateBranchAvailabilitySettingsRequest
        {
            TimeZoneId = "UTC",
            MinimumLeadMinutes = 43_200,
            BookingHorizonDays = 365,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateBranchAvailabilitySettingsRequestValidator_RejectsLeadMinutesAboveMaximum()
    {
        var validator = new UpdateBranchAvailabilitySettingsRequestValidator();
        var request = new UpdateBranchAvailabilitySettingsRequest
        {
            TimeZoneId = "UTC",
            MinimumLeadMinutes = 43_201,
            BookingHorizonDays = 30,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(UpdateBranchAvailabilitySettingsRequest.MinimumLeadMinutes));
    }

    [Fact]
    public void CreateAvailabilityOverrideRequestValidator_RequiresOverrideDate()
    {
        var validator = new CreateAvailabilityOverrideRequestValidator();
        var request = new CreateAvailabilityOverrideRequest
        {
            IsClosed = true,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(CreateAvailabilityOverrideRequest.OverrideDate));
    }

    [Fact]
    public void CreateAvailabilityOverrideRequestValidator_RejectsCapacityAboveOperationalLimit()
    {
        var validator = new CreateAvailabilityOverrideRequestValidator();
        var request = new CreateAvailabilityOverrideRequest
        {
            OverrideDate = new DateOnly(2026, 8, 24),
            IsClosed = false,
            StartLocalTime = TimeSpan.FromHours(9),
            EndLocalTime = TimeSpan.FromHours(12),
            SlotDurationMinutes = 30,
            Capacity = 101,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(CreateAvailabilityOverrideRequest.Capacity));
    }

    [Fact]
    public void UpsertBranchServiceAreaRequestValidator_RejectsSingleCenterCoordinate()
    {
        var validator = new UpsertBranchServiceAreaRequestValidator();
        var request = new UpsertBranchServiceAreaRequest
        {
            CenterLatitude = 24.7136,
            RadiusKm = 10d,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorMessage.Contains("supplied together", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpsertBranchServiceAreaRequestValidator_RejectsRadiusAboveOperationalLimit()
    {
        var validator = new UpsertBranchServiceAreaRequestValidator();
        var request = new UpsertBranchServiceAreaRequest
        {
            CenterLatitude = 24.7136,
            CenterLongitude = 46.6753,
            RadiusKm = 500.01d,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(UpsertBranchServiceAreaRequest.RadiusKm));
    }

    [Fact]
    public void ValidateAppointmentRequestValidator_RejectsOutOfRangeCustomerLatitude()
    {
        var validator = new ValidateAppointmentRequestValidator();
        var request = new ValidateAppointmentRequest
        {
            BranchId = Guid.NewGuid(),
            OfferingId = Guid.NewGuid(),
            RequestedSlotStartUtc = DateTimeOffset.UtcNow,
            Currency = "ILS",
            CustomerLocation = new AppointmentCustomerLocationFacts
            {
                Latitude = 91d,
                Longitude = 46.6753
            }
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName.Contains(nameof(AppointmentCustomerLocationFacts.Latitude), StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAppointmentRequestValidator_RejectsSelectionQuantityAboveOperationalLimit()
    {
        var validator = new ValidateAppointmentRequestValidator();
        var request = new ValidateAppointmentRequest
        {
            BranchId = Guid.NewGuid(),
            OfferingId = Guid.NewGuid(),
            RequestedSlotStartUtc = DateTimeOffset.UtcNow,
            Currency = "ILS",
            SelectedAddons =
            [
                new ValidateAppointmentAddonSelectionRequest
                {
                    AddonChoiceId = Guid.NewGuid(),
                    Quantity = 101
                }
            ]
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName.Contains(nameof(ValidateAppointmentAddonSelectionRequest.Quantity), StringComparison.Ordinal));
    }
}
