using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Payments;

public static class Step17PaymentGatewayNames
{
    public const string DeterministicFake = "DeterministicFake";
}

public sealed class Step17TestFixturesOptions
{
    public const string SectionName = "Step17TestFixtures";

    public bool Enabled { get; set; }
    public string PaymentGateway { get; set; } = string.Empty;
}

public sealed class Step17TestFixturesOptionsValidator :
    IValidateOptions<Step17TestFixturesOptions>
{
    private readonly IHostEnvironment _environment;

    public Step17TestFixturesOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(
        string? name,
        Step17TestFixturesOptions options)
    {
        var gateway = options.PaymentGateway?.Trim() ?? string.Empty;
        if (!options.Enabled)
        {
            return gateway.Length == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    "Step17TestFixtures:PaymentGateway requires Enabled=true.");
        }

        if (!_environment.IsDevelopment())
        {
            return ValidateOptionsResult.Fail(
                "Step17TestFixtures can be enabled only in Development.");
        }

        return string.Equals(
            gateway,
            Step17PaymentGatewayNames.DeterministicFake,
            StringComparison.Ordinal)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Step17TestFixtures:PaymentGateway must be DeterministicFake.");
    }
}

public sealed class Step17DeterministicPaymentIntentGateway :
    IStripePaymentIntentGateway
{
    public Task<StripeIntentResult> CreateAsync(
        StripeIntentCreateCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var intentId = $"pi_step17_{command.PaymentId:N}";
        var result = new StripeIntentResult(
            intentId,
            "requires_confirmation",
            $"{intentId}_secret_{command.BookingId:N}",
            null,
            command.Amount,
            command.Currency.ToLowerInvariant());
        return Task.FromResult(result);
    }
}
