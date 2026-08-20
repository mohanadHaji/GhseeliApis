using Ghseeli.BusinessApi.Models;

namespace Ghseeli.BusinessApi.Services.Availability;

public interface ITimeZoneAvailabilityResolver
{
    AvailabilityResolutionResult Resolve(
        Branch branch,
        DateTime requestedSlotStartUtc,
        int totalDurationMinutes);
}
