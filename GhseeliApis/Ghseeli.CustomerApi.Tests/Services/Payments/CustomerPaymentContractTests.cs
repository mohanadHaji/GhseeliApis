using FluentAssertions;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Services.Payments;
using System.Text.Json;

namespace GhseeliApis.Tests.Services.Payments;

/// <summary>
/// Contract tests for the server-authoritative customer payment workflow.
/// </summary>
public sealed class CustomerPaymentContractTests
{
    [Fact]
    public void Initialization_states_are_exact_and_new_payments_default_to_not_started()
    {
        new[]
        {
            PaymentInitializationStates.NotStarted,
            PaymentInitializationStates.Initialized,
            PaymentInitializationStates.Ambiguous,
            PaymentInitializationStates.Legacy
        }.Should().Equal("NotStarted", "Initialized", "Ambiguous", "Legacy");

        new CustomerPayment().InitializationState
            .Should().Be(PaymentInitializationStates.NotStarted);
    }

    [Fact]
    public void Create_request_does_not_expose_client_authoritative_payment_fields()
    {
        var names = typeof(CreateCustomerPaymentIntentRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        names.Should().BeEquivalentTo(
            nameof(CreateCustomerPaymentIntentRequest.BookingId),
            nameof(CreateCustomerPaymentIntentRequest.Method));
        names.Should().NotContain(
            ["PaymentMethodId", "Amount", "Currency", "TransactionId", "Status", "Paid"]);
    }

    [Fact]
    public void Unknown_money_fields_are_ignored_for_forward_compatibility()
    {
        const string json =
            """{"bookingId":"11111111-1111-1111-1111-111111111111","method":"Card","amount":0.01,"currency":"USD"}""";

        var request = JsonSerializer.Deserialize<CreateCustomerPaymentIntentRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        request.Should().NotBeNull();
        request!.BookingId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        request.Method.Should().Be("Card");
    }

    [Fact]
    public void Payment_initialization_command_contains_no_client_payment_method()
    {
        typeof(PaymentInitializationCommand).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain("PaymentMethodId");
    }

    [Fact]
    public void Payment_initialization_command_uses_server_authoritative_values()
    {
        var command = new PaymentInitializationCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1234,
            "ILS",
            "customer@example.test",
            "GHSEELI-TEST-REFERENCE");

        command.Amount.Should().Be(1234);
        command.Currency.Should().Be("ILS");
        command.CustomerEmail.Should().Be("customer@example.test");
        command.ProviderReference.Should().Be("GHSEELI-TEST-REFERENCE");
    }

    [Theory]
    [InlineData("ILS", 10.01, 1001)]
    [InlineData("USD", 0.01, 1)]
    [InlineData("JOD", 999999.99, 99999999)]
    public void Minor_unit_conversion_is_exact(string currency, decimal amount, long expected)
    {
        CustomerPaymentMoney.ToMinorUnits(amount, currency).Should().Be(expected);
    }

    [Theory]
    [InlineData("JPY", 10)]
    [InlineData("ILS", 0)]
    [InlineData("USD", -1)]
    [InlineData("ILS", 1.001)]
    public void Minor_unit_conversion_rejects_unsupported_or_invalid_values(
        string currency,
        decimal amount)
    {
        var act = () => CustomerPaymentMoney.ToMinorUnits(amount, currency);
        act.Should().Throw<CustomerPaymentException>();
    }

    [Theory]
    [InlineData("ils")]
    [InlineData(" ILS")]
    [InlineData("ILS ")]
    [InlineData("IL-S")]
    [InlineData("JPY")]
    public void Currency_must_be_supported_canonical_uppercase(string currency)
    {
        var exception = Assert.Throws<CustomerPaymentException>(
            () => CustomerPaymentMoney.ToMinorUnits(10m, currency));

        exception.StatusCode.Should().Be(409);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.CurrencyUnsupported);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public void Invalid_total_is_not_payable(decimal amount)
    {
        var exception = Assert.Throws<CustomerPaymentException>(
            () => CustomerPaymentMoney.ToMinorUnits(amount, "ILS"));

        exception.StatusCode.Should().Be(409);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.AmountInvalid);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentEventKind.Succeeded, PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed, PaymentEventKind.Succeeded, PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Completed, PaymentEventKind.Refunded, PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Completed, PaymentEventKind.Failed, PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Refunded, PaymentEventKind.Succeeded, PaymentStatus.Refunded)]
    public void Webhook_transition_is_duplicate_and_out_of_order_safe(
        PaymentStatus current,
        PaymentEventKind eventKind,
        PaymentStatus expected)
    {
        CustomerPaymentTransitions.Apply(current, eventKind).Should().Be(expected);
    }
}
