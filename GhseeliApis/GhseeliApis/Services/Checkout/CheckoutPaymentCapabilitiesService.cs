using GhseeliApis.DTOs.Checkout;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Checkout;

public interface ICheckoutPaymentCapabilitiesService
{
    CheckoutPaymentCapabilitiesResponse GetCapabilities();
}

public sealed class CheckoutPaymentCapabilitiesService : ICheckoutPaymentCapabilitiesService
{
    private readonly IOptionsMonitor<StripeConfigurationOptions> _stripeOptionsMonitor;

    public CheckoutPaymentCapabilitiesService(
        IOptionsMonitor<StripeConfigurationOptions> stripeOptionsMonitor)
    {
        _stripeOptionsMonitor = stripeOptionsMonitor;
    }

    public CheckoutPaymentCapabilitiesResponse GetCapabilities()
    {
        var stripeOptions = _stripeOptionsMonitor.CurrentValue;
        var creditCardEnabled = IsStripeConfigured(stripeOptions);

        return new CheckoutPaymentCapabilitiesResponse
        {
            Methods =
            [
                CreateCapability(
                    method: "CreditCard",
                    enabled: creditCardEnabled,
                    reasonCode: creditCardEnabled
                        ? null
                        : CheckoutPaymentCapabilityReasonCodes.ProviderUnavailable),
                CreateCapability(
                    method: "Wallet",
                    enabled: false,
                    reasonCode: CheckoutPaymentCapabilityReasonCodes.NotYetSupported),
                CreateCapability(
                    method: "CashOnArrival",
                    enabled: false,
                    reasonCode: CheckoutPaymentCapabilityReasonCodes.NotYetSupported),
                CreateCapability(
                    method: "ThirdParty",
                    enabled: false,
                    reasonCode: CheckoutPaymentCapabilityReasonCodes.NotYetSupported)
            ]
        };
    }

    private static CheckoutPaymentMethodCapabilityResponse CreateCapability(
        string method,
        bool enabled,
        string? reasonCode) =>
        new()
        {
            Method = method,
            Enabled = enabled,
            ReasonCode = reasonCode
        };

    private static bool IsStripeConfigured(StripeConfigurationOptions options) =>
        IsConfiguredKey(options.PublishableKey, "pk_") &&
        IsConfiguredKey(options.SecretKey, "sk_");

    private static bool IsConfiguredKey(string? value, string expectedPrefix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Contains("_HERE", StringComparison.OrdinalIgnoreCase);
    }
}
