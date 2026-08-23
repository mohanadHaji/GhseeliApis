namespace GhseeliApis.Services.Checkout;

public static class CheckoutPricingProblemCodes
{
    public const string Invalid = "pricing_invalid";
    public const string SelectionInvalid = "pricing_selection_invalid";
    public const string SlotUnavailable = "pricing_slot_unavailable";
    public const string OutOfServiceArea = "pricing_out_of_service_area";
    public const string Unavailable = "pricing_unavailable";
    public const string UnsupportedMediaType = "pricing_unsupported_media_type";
    public const string RequestBodyTooLarge = "pricing_request_body_too_large";
}

public static class CheckoutPaymentCapabilityReasonCodes
{
    public const string ProviderUnavailable = "payment_method_provider_unavailable";
    public const string NotYetSupported = "payment_method_not_yet_supported";
}
