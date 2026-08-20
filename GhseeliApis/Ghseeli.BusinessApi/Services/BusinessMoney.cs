using Ghseeli.BusinessApi.Constants;

namespace Ghseeli.BusinessApi.Services;

public static class BusinessMoney
{
    public static decimal RoundToCurrency(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public static bool IsWithinSupportedRange(decimal value)
    {
        var normalized = RoundToCurrency(value);
        return normalized >= 0m && normalized <= BusinessValueLimits.MaximumMoneyAmount;
    }
}
