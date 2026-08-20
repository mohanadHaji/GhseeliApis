using Ghseeli.BusinessApi.Models;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Availability;

public class ServiceAreaCalculator : IServiceAreaCalculator
{
    private const double EarthRadiusKm = 6371.0088d;

    public ServiceAreaEvaluationResult Evaluate(
        Branch branch,
        BranchServiceArea? serviceArea,
        AppointmentCustomerLocationFacts? customerLocation)
    {
        ArgumentNullException.ThrowIfNull(branch);

        if (serviceArea is null || !serviceArea.IsActive)
        {
            return Invalid(
                AppointmentValidationErrorCodes.ServiceAreaNotConfigured,
                "No active service area is configured for the branch.",
                new AppointmentServiceAreaFacts
                {
                    ServiceAreaConfigured = false,
                    CustomerLocationRequired = false,
                    IsWithinServiceArea = false
                });
        }

        var (centerLatitude, centerLongitude, usedBranchCoordinates) =
            ResolveCenter(branch, serviceArea);

        var facts = new AppointmentServiceAreaFacts
        {
            ServiceAreaConfigured = true,
            CustomerLocationRequired = true,
            UsedBranchCoordinates = usedBranchCoordinates,
            RadiusKm = serviceArea.RadiusKm,
            EffectiveCenterLatitude = centerLatitude,
            EffectiveCenterLongitude = centerLongitude
        };

        if (!centerLatitude.HasValue || !centerLongitude.HasValue)
        {
            facts.IsWithinServiceArea = false;
            return Invalid(
                AppointmentValidationErrorCodes.ServiceAreaNotConfigured,
                "The active service area does not have a usable center point.",
                facts);
        }

        if (customerLocation is null)
        {
            facts.IsWithinServiceArea = false;
            return Invalid(
                AppointmentValidationErrorCodes.CustomerLocationRequired,
                "Customer location facts are required when the branch has an active service area.",
                facts);
        }

        var distanceKm = CalculateDistanceKm(
            centerLatitude.Value,
            centerLongitude.Value,
            customerLocation.Latitude,
            customerLocation.Longitude);

        facts.DistanceKm = Math.Round(distanceKm, 4, MidpointRounding.AwayFromZero);
        facts.IsWithinServiceArea = distanceKm <= serviceArea.RadiusKm;

        if (!facts.IsWithinServiceArea)
        {
            return Invalid(
                AppointmentValidationErrorCodes.OutOfServiceArea,
                "Customer location is outside the configured service area radius.",
                facts);
        }

        return new ServiceAreaEvaluationResult
        {
            IsValid = true,
            Facts = facts
        };
    }

    private static (double? Latitude, double? Longitude, bool UsedBranchCoordinates) ResolveCenter(
        Branch branch,
        BranchServiceArea serviceArea)
    {
        if (serviceArea.CenterLatitude.HasValue && serviceArea.CenterLongitude.HasValue)
        {
            return (serviceArea.CenterLatitude, serviceArea.CenterLongitude, false);
        }

        return (branch.Latitude, branch.Longitude, true);
    }

    private static double CalculateDistanceKm(
        double startLatitude,
        double startLongitude,
        double endLatitude,
        double endLongitude)
    {
        var deltaLatitude = DegreesToRadians(endLatitude - startLatitude);
        var deltaLongitude = DegreesToRadians(endLongitude - startLongitude);
        var startLatitudeRadians = DegreesToRadians(startLatitude);
        var endLatitudeRadians = DegreesToRadians(endLatitude);

        var haversine = Math.Pow(Math.Sin(deltaLatitude / 2d), 2d)
            + Math.Cos(startLatitudeRadians)
            * Math.Cos(endLatitudeRadians)
            * Math.Pow(Math.Sin(deltaLongitude / 2d), 2d);

        var centralAngle = 2d * Math.Asin(Math.Min(1d, Math.Sqrt(haversine)));
        return EarthRadiusKm * centralAngle;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    private static ServiceAreaEvaluationResult Invalid(
        string code,
        string message,
        AppointmentServiceAreaFacts facts)
    {
        return new ServiceAreaEvaluationResult
        {
            IsValid = false,
            Error = new AppointmentValidationIssue
            {
                Code = code,
                Message = message
            },
            Facts = facts
        };
    }
}
