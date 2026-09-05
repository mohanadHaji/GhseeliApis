using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Checkout;

public sealed class CheckoutDraftOptions
{
    public const string SectionName = "CheckoutDrafts";

    public int LifetimeMinutes { get; set; } = 30;
}

public sealed class CheckoutDraftOptionsValidator : IValidateOptions<CheckoutDraftOptions>
{
    public ValidateOptionsResult Validate(string? name, CheckoutDraftOptions options) =>
        options.LifetimeMinutes is >= 1 and <= 1_440
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{CheckoutDraftOptions.SectionName}:LifetimeMinutes must be between 1 and 1440.");
}
