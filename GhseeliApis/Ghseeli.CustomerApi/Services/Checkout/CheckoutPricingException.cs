namespace GhseeliApis.Services.Checkout;

public sealed class CheckoutPricingException : Exception
{
    public CheckoutPricingException(
        string code,
        int statusCode,
        string message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        FieldErrors = fieldErrors;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }
}
