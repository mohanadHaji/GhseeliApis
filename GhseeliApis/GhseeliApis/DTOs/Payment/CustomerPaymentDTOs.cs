namespace GhseeliApis.DTOs.Payment;

public sealed class CreateCustomerPaymentIntentRequest
{
    public Guid BookingId { get; set; }
    public string Method { get; set; } = string.Empty;
}

public sealed class CustomerPaymentResponse
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ClientSecret { get; set; }
    public string? PublishableKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
