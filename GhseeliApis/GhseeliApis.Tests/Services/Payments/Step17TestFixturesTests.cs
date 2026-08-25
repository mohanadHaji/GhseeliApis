using FluentAssertions;
using GhseeliApis.Services.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace GhseeliApis.Tests.Services.Payments;

/// <summary>
/// Verifies the Development-only Step 17 deterministic payment boundary.
/// </summary>
public sealed class Step17TestFixturesTests
{
    [Fact]
    public void Validate_EnabledInProduction_FailsClosed()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName)
            .Returns(Environments.Production);
        var validator = new Step17TestFixturesOptionsValidator(
            environment.Object);

        var result = validator.Validate(
            null,
            new Step17TestFixturesOptions
            {
                Enabled = true,
                PaymentGateway = Step17PaymentGatewayNames.DeterministicFake
            });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("only in Development");
    }

    [Fact]
    public void Validate_EnabledInStaging_FailsClosed()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName)
            .Returns(Environments.Staging);
        var validator = new Step17TestFixturesOptionsValidator(
            environment.Object);

        var result = validator.Validate(
            null,
            new Step17TestFixturesOptions
            {
                Enabled = true,
                PaymentGateway = Step17PaymentGatewayNames.DeterministicFake
            });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("only in Development");
    }

    [Fact]
    public void Validate_EnabledInDevelopmentWithDeterministicFake_Succeeds()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName)
            .Returns(Environments.Development);
        var validator = new Step17TestFixturesOptionsValidator(
            environment.Object);

        var result = validator.Validate(
            null,
            new Step17TestFixturesOptions
            {
                Enabled = true,
                PaymentGateway = Step17PaymentGatewayNames.DeterministicFake
            });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_DisabledWithGatewaySelection_FailsClosed()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName)
            .Returns(Environments.Development);
        var validator = new Step17TestFixturesOptionsValidator(
            environment.Object);

        var result = validator.Validate(
            null,
            new Step17TestFixturesOptions
            {
                Enabled = false,
                PaymentGateway = Step17PaymentGatewayNames.DeterministicFake
            });

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public async Task DeterministicFake_ReturnsStableLocalIntentWithoutNetwork()
    {
        var gateway = new Step17DeterministicPaymentIntentGateway();
        var command = new StripeIntentCreateCommand(
            Guid.Parse("17000000-0000-4000-8000-000000000001"),
            Guid.Parse("17000000-0000-4000-8000-000000000002"),
            Guid.Parse("17000000-0000-4000-8000-000000000003"),
            6050,
            "ILS",
            "step17-test-key");

        var first = await gateway.CreateAsync(command, CancellationToken.None);
        var second = await gateway.CreateAsync(command, CancellationToken.None);

        first.Should().Be(second);
        first.PaymentIntentId.Should()
            .Be("pi_step17_17000000000040008000000000000001");
        first.Status.Should().Be("requires_confirmation");
        first.ClientSecret.Should().Be(
            "pi_step17_17000000000040008000000000000001_secret_" +
            "17000000000040008000000000000002");
        first.ChargeId.Should().BeNull();
        first.Amount.Should().Be(6050);
        first.Currency.Should().Be("ils");
    }

    [Fact]
    public void DevelopmentFixtureSettings_RegisterDeterministicGateway()
    {
        using var factory = new Integration.CustomerConfigurationApiFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Step17TestFixtures:Enabled", "true");
                builder.UseSetting(
                    "Step17TestFixtures:PaymentGateway",
                    Step17PaymentGatewayNames.DeterministicFake);
            });
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IStripePaymentIntentGateway>()
            .Should().BeOfType<Step17DeterministicPaymentIntentGateway>();
    }

    [Fact]
    public void ProductionHost_RejectsEnabledStep17FixturesDuringStartup()
    {
        Action start = () =>
        {
            using var factory = new Integration.CustomerConfigurationApiFactory()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment(Environments.Production);
                    builder.UseSetting("Step17TestFixtures:Enabled", "true");
                    builder.UseSetting(
                        "Step17TestFixtures:PaymentGateway",
                        Step17PaymentGatewayNames.DeterministicFake);
                });
            _ = factory.Services;
        };

        start.Should().Throw<Exception>()
            .WithMessage("*Step17TestFixtures*only in Development*");
    }
}
