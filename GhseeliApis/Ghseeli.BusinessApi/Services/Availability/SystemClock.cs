namespace Ghseeli.BusinessApi.Services.Availability;

public class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
