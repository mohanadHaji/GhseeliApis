using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies deterministic service-area calculations and fail-closed booking behavior.
/// </summary>
public class ServiceAreaCalculatorTests
{
    private readonly ServiceAreaCalculator _calculator = new();

    [Fact]
    public void Evaluate_WhenNoActiveServiceAreaConfigured_ReturnsFailClosedNotConfigured()
    {
        var branch = CreateBranch();

        var result = _calculator.Evaluate(branch, null, null);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.ServiceAreaNotConfigured);
        result.Facts.ServiceAreaConfigured.Should().BeFalse();
        result.Facts.IsWithinServiceArea.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_WhenCustomerLocationMissing_ReturnsLocationRequired()
    {
        var branch = CreateBranch();
        var serviceArea = new BranchServiceArea
        {
            BranchId = branch.Id,
            RadiusKm = 10d,
            IsActive = true
        };

        var result = _calculator.Evaluate(branch, serviceArea, null);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.CustomerLocationRequired);
        result.Facts.CustomerLocationRequired.Should().BeTrue();
        result.Facts.UsedBranchCoordinates.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenServiceAreaIsInactive_ReturnsNotConfigured()
    {
        var branch = CreateBranch();
        var serviceArea = new BranchServiceArea
        {
            BranchId = branch.Id,
            RadiusKm = 10d,
            IsActive = false
        };

        var result = _calculator.Evaluate(branch, serviceArea, new AppointmentCustomerLocationFacts
        {
            Latitude = branch.Latitude!.Value,
            Longitude = branch.Longitude!.Value
        });

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.ServiceAreaNotConfigured);
    }

    [Fact]
    public void Evaluate_WhenImplicitCenterCannotResolve_ReturnsFailClosed()
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            NameAr = "الفرع الرئيسي",
            AddressAr = "العنوان"
        };
        var serviceArea = new BranchServiceArea
        {
            BranchId = branch.Id,
            RadiusKm = 10d,
            IsActive = true
        };

        var result = _calculator.Evaluate(branch, serviceArea, new AppointmentCustomerLocationFacts
        {
            Latitude = 24.7136,
            Longitude = 46.6753
        });

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.ServiceAreaNotConfigured);
        result.Facts.EffectiveCenterLatitude.Should().BeNull();
        result.Facts.EffectiveCenterLongitude.Should().BeNull();
    }

    [Fact]
    public void Evaluate_WhenCustomerIsWithinRadius_ReturnsDistanceAndSuccess()
    {
        var branch = CreateBranch();
        var serviceArea = new BranchServiceArea
        {
            BranchId = branch.Id,
            CenterLatitude = 24.7136,
            CenterLongitude = 46.6753,
            RadiusKm = 5d,
            IsActive = true
        };

        var result = _calculator.Evaluate(branch, serviceArea, new AppointmentCustomerLocationFacts
        {
            Latitude = 24.7136,
            Longitude = 46.6753
        });

        result.IsValid.Should().BeTrue();
        result.Facts.IsWithinServiceArea.Should().BeTrue();
        result.Facts.DistanceKm.Should().Be(0d);
        result.Facts.UsedBranchCoordinates.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_WhenCustomerIsExactlyOnRadiusBoundary_ReturnsSuccess()
    {
        var branch = CreateBranch(latitude: 0d, longitude: 0d);
        var serviceArea = new BranchServiceArea
        {
            BranchId = branch.Id,
            CenterLatitude = 0d,
            CenterLongitude = 0d,
            RadiusKm = 6371.0088d * Math.PI / 180d,
            IsActive = true
        };

        var result = _calculator.Evaluate(branch, serviceArea, new AppointmentCustomerLocationFacts
        {
            Latitude = 0d,
            Longitude = 1d
        });

        result.IsValid.Should().BeTrue();
        result.Facts.IsWithinServiceArea.Should().BeTrue();
        result.Facts.DistanceKm.Should().Be(111.1951d);
    }

    [Fact]
    public void Evaluate_WhenCrossingAntimeridianWithinRadius_ReturnsSuccess()
    {
        var branch = CreateBranch(latitude: 0d, longitude: 179.9d);
        var serviceArea = new BranchServiceArea
        {
            BranchId = branch.Id,
            CenterLatitude = 0d,
            CenterLongitude = 179.9d,
            RadiusKm = 30d,
            IsActive = true
        };

        var result = _calculator.Evaluate(branch, serviceArea, new AppointmentCustomerLocationFacts
        {
            Latitude = 0d,
            Longitude = -179.9d
        });

        result.IsValid.Should().BeTrue();
        result.Facts.IsWithinServiceArea.Should().BeTrue();
        result.Facts.DistanceKm.Should().BeLessThan(30d);
    }

    private static Branch CreateBranch(
        double latitude = 24.7136,
        double longitude = 46.6753)
    {
        return new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            NameAr = "الفرع الرئيسي",
            AddressAr = "العنوان",
            Latitude = latitude,
            Longitude = longitude
        };
    }
}
