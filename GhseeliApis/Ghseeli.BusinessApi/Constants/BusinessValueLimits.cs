namespace Ghseeli.BusinessApi.Constants;

public static class BusinessValueLimits
{
    public const decimal MaximumMoneyAmount = 1_000_000m;
    public const int MaximumDurationMinutes = 1_440;
    public const int MaximumSelectionQuantity = 100;
    public const int MaximumMinimumLeadMinutes = 43_200;
    public const int MaximumBookingHorizonDays = 365;
    public const int MaximumSlotDurationMinutes = 1_440;
    public const int MaximumConfiguredCapacity = 100;
    public const double MaximumServiceAreaRadiusKm = 500d;
}
