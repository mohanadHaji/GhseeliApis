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

public sealed class Step17DeterministicPaymentGateway : IPaymentGateway
{
    public Task<PaymentInitializationResult> InitializeAsync(
        PaymentInitializationCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accessCode = $"step17-{command.PaymentId:N}";
        var result = new PaymentInitializationResult(
            command.ProviderReference,
            "initialized",
            new Uri($"https://checkout.lahza.test/{accessCode}"),
            command.Amount,
            command.Currency);
        return Task.FromResult(result);
    }

    public Task<PaymentVerificationResult> VerifyAsync(
        string providerReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PaymentVerificationResult(
            providerReference,
            "pending",
            null,
            0,
            string.Empty));
    }
}
