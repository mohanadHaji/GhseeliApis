using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Services.Payments;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Checkout;

public interface ICheckoutPaymentCapabilitiesService
{
    CheckoutPaymentCapabilitiesResponse GetCapabilities();
}

public sealed class CheckoutPaymentCapabilitiesService : ICheckoutPaymentCapabilitiesService
{
    private readonly IOptionsMonitor<LahzaConfigurationOptions> _lahzaOptionsMonitor;

    public CheckoutPaymentCapabilitiesService(
        IOptionsMonitor<LahzaConfigurationOptions> lahzaOptionsMonitor)
    {
        _lahzaOptionsMonitor = lahzaOptionsMonitor;
    }

    public CheckoutPaymentCapabilitiesResponse GetCapabilities()
    {
        var creditCardEnabled = LahzaConfiguration.IsConfigured(
            _lahzaOptionsMonitor.CurrentValue);

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

}
