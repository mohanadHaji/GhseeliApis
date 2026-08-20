namespace Ghseeli.BusinessApi.Services.Availability;

public interface ISystemClock
{
    DateTime UtcNow { get; }
}
