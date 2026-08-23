using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace GhseeliApis.Services.Checkout;

public sealed class CheckoutPricingOptions
{
    public const string SectionName = "CheckoutPricing";

    public string Currency { get; set; } = "ILS";
    public decimal TaxRatePercent { get; set; }
    public bool TaxAppliesToServiceFee { get; set; }
    public CheckoutServiceFeeOptions ServiceFee { get; set; } = new();
}

public sealed class CheckoutServiceFeeOptions
{
    public string Mode { get; set; } = CheckoutServiceFeeMode.None;
    public decimal FlatAmount { get; set; }
    public decimal PercentageRate { get; set; }
}

public static class CheckoutServiceFeeMode
{
    public const string None = "None";
    public const string Flat = "Flat";
    public const string Percentage = "Percentage";
}

public sealed class CheckoutPricingOptionsValidator : IValidateOptions<CheckoutPricingOptions>
{
    private static readonly Regex CurrencyPattern = new(
        "^[A-Za-z]{3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ValidateOptionsResult Validate(string? name, CheckoutPricingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ServiceFee);

        if (string.IsNullOrWhiteSpace(options.Currency) ||
            !CurrencyPattern.IsMatch(options.Currency.Trim()))
        {
            return ValidateOptionsResult.Fail(
                $"{CheckoutPricingOptions.SectionName}:Currency must be a three-letter currency code.");
        }

        if (options.TaxRatePercent is < 0m or > 100m)
        {
            return ValidateOptionsResult.Fail(
                $"{CheckoutPricingOptions.SectionName}:TaxRatePercent must be between 0 and 100.");
        }

        if (!IsSupportedMode(options.ServiceFee.Mode))
        {
            return ValidateOptionsResult.Fail(
                $"{CheckoutPricingOptions.SectionName}:ServiceFee:Mode must be None, Flat, or Percentage.");
        }

        if (options.ServiceFee.FlatAmount < 0m)
        {
            return ValidateOptionsResult.Fail(
                $"{CheckoutPricingOptions.SectionName}:ServiceFee:FlatAmount cannot be negative.");
        }

        if (options.ServiceFee.PercentageRate is < 0m or > 100m)
        {
            return ValidateOptionsResult.Fail(
                $"{CheckoutPricingOptions.SectionName}:ServiceFee:PercentageRate must be between 0 and 100.");
        }

        if (string.Equals(options.ServiceFee.Mode, CheckoutServiceFeeMode.None, StringComparison.Ordinal) &&
            (options.ServiceFee.FlatAmount != 0m || options.ServiceFee.PercentageRate != 0m))
        {
            return ValidateOptionsResult.Fail(
                $"{CheckoutPricingOptions.SectionName}:ServiceFee amounts must be zero when Mode is None.");
        }

        if (string.Equals(options.ServiceFee.Mode, CheckoutServiceFeeMode.Flat, StringComparison.Ordinal) &&
            options.ServiceFee.PercentageRate != 0m)
        {
            return ValidateOptionsResult.Fail(
                $"{CheckoutPricingOptions.SectionName}:ServiceFee:PercentageRate must be zero when Mode is Flat.");
        }

        if (string.Equals(options.ServiceFee.Mode, CheckoutServiceFeeMode.Percentage, StringComparison.Ordinal) &&
            options.ServiceFee.FlatAmount != 0m)
        {
            return ValidateOptionsResult.Fail(
                $"{CheckoutPricingOptions.SectionName}:ServiceFee:FlatAmount must be zero when Mode is Percentage.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsSupportedMode(string? mode) =>
        string.Equals(mode, CheckoutServiceFeeMode.None, StringComparison.Ordinal) ||
        string.Equals(mode, CheckoutServiceFeeMode.Flat, StringComparison.Ordinal) ||
        string.Equals(mode, CheckoutServiceFeeMode.Percentage, StringComparison.Ordinal);
}
