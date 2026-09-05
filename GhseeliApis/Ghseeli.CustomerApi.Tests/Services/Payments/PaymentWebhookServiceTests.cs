using System.Text;
using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GhseeliApis.Tests.Services.Payments;

/// <summary>
/// Tests durable verified-event matching, replay, and state transitions.
/// </summary>
public sealed class PaymentWebhookServiceTests
{
    [Fact]
    public async Task Verified_success_completes_payment_and_marks_booking_paid()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        var service = CreateService(db);

        await service.ProcessAsync(Event(payment, "evt_success", PaymentEventKind.Succeeded),
            Encoding.UTF8.GetBytes("{\"id\":\"evt_success\"}"), default);

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.CustomerBooking.IsPaid.Should().BeTrue();
        payment.CustomerBooking.PaymentState.Should().Be("Completed");
        (await db.PaymentWebhookEvents.SingleAsync()).State.Should().Be("Completed");
    }

    [Fact]
    public async Task Identical_event_replays_and_changed_body_conflicts()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        var service = CreateService(db);
        var paymentEvent = Event(payment, "evt_replay", PaymentEventKind.Succeeded);
        var canonicalBody = Encoding.UTF8.GetBytes(
            """{"event":"charge.success","data":{"reference":"GHSEELI-TEST"}}""");
        var alteredBytes = Encoding.UTF8.GetBytes(
            """{ "event":"charge.success","data":{"reference":"GHSEELI-TEST"}}""");
        await service.ProcessAsync(paymentEvent, canonicalBody, default);

        await service.ProcessAsync(paymentEvent, canonicalBody, default);
        var changed = () => service.ProcessAsync(paymentEvent, alteredBytes, default);

        (await changed.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.WebhookConflict);
        (await db.PaymentWebhookEvents.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("wrong-intent", 1000, "ILS")]
    [InlineData("GHSEELI-TEST", 999, "ILS")]
    [InlineData("GHSEELI-TEST", 1000, "USD")]
    public async Task Intent_amount_or_currency_mismatch_is_durably_quarantined_without_mutation(
        string intentId,
        long amount,
        string currency)
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        var original = Event(payment, "evt_bad", PaymentEventKind.Succeeded);
        var invalid = original with
        {
            ProviderReference = intentId,
            Amount = amount,
            Currency = currency
        };

        await CreateService(db).ProcessAsync(invalid, Encoding.UTF8.GetBytes("bad"), default);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.CustomerBooking.IsPaid.Should().BeFalse();
        var record = await db.PaymentWebhookEvents.SingleAsync();
        record.State.Should().Be("Quarantined");
        record.DispositionReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Failure_after_completion_and_success_after_refund_are_no_ops()
    {
        await using var db = CreateDb();
        var completed = AddPayment(db, PaymentStatus.Completed);
        completed.CustomerBooking.IsPaid = true;
        var service = CreateService(db);
        await service.ProcessAsync(
            Event(completed, "evt_late_failure", PaymentEventKind.Failed),
            Encoding.UTF8.GetBytes("late-failure"),
            default);
        completed.Status.Should().Be(PaymentStatus.Completed);
        completed.CustomerBooking.IsPaid.Should().BeTrue();

        completed.Status = PaymentStatus.Refunded;
        completed.CustomerBooking.IsPaid = false;
        completed.CustomerBooking.PaymentState = "Refunded";
        await db.SaveChangesAsync();
        await service.ProcessAsync(
            Event(completed, "evt_late_success", PaymentEventKind.Succeeded),
            Encoding.UTF8.GetBytes("late-success"),
            default);

        completed.Status.Should().Be(PaymentStatus.Refunded);
        completed.CustomerBooking.IsPaid.Should().BeFalse();
    }

    [Fact]
    public async Task Refund_requires_persisted_charge_match()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Completed);
        var invalid = Event(payment, "evt_refund", PaymentEventKind.Refunded) with
        {
            ProviderTransactionId = "ch_wrong"
        };

        await CreateService(db).ProcessAsync(
            invalid,
            Encoding.UTF8.GetBytes("refund"),
            default);
        payment.Status.Should().Be(PaymentStatus.Completed);
        (await db.PaymentWebhookEvents.SingleAsync()).State.Should().Be("Quarantined");
    }

    [Fact]
    public async Task Full_refund_before_success_is_deferred_then_converges_to_refunded()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        payment.ProviderTransactionId = null;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var refund = Event(payment, "evt_refund_first", PaymentEventKind.Refunded) with
        {
            ProviderTransactionId = "ch_deferred"
        };

        await service.ProcessAsync(
            refund,
            Encoding.UTF8.GetBytes("refund-first"),
            default);

        payment.Status.Should().Be(PaymentStatus.Pending);
        var deferred = await db.PaymentWebhookEvents.SingleAsync();
        deferred.State.Should().Be("Deferred");
        deferred.CustomerPaymentId.Should().Be(payment.Id);
        deferred.ProviderTransactionId.Should().Be("ch_deferred");

        var success = Event(payment, "evt_success_later", PaymentEventKind.Succeeded) with
        {
            ProviderTransactionId = "ch_deferred"
        };
        await service.ProcessAsync(
            success,
            Encoding.UTF8.GetBytes("success-later"),
            default);

        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.CustomerBooking.IsPaid.Should().BeFalse();
        payment.CustomerBooking.PaymentState.Should().Be("Refunded");
        deferred.State.Should().Be("Completed");
    }

    [Fact]
    public async Task Unknown_intent_is_durably_quarantined_and_duplicate_replays()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        var paymentEvent = Event(payment, "evt_unknown", PaymentEventKind.Succeeded) with
        {
            ProviderReference = "GHSEELI-UNKNOWN"
        };
        var service = CreateService(db);

        var rawBody = Encoding.UTF8.GetBytes("unknown-body");
        await service.ProcessAsync(paymentEvent, rawBody, default);
        await service.ProcessAsync(paymentEvent, rawBody, default);

        var record = await db.PaymentWebhookEvents.SingleAsync();
        record.State.Should().Be("Quarantined");
        record.DispositionReason.Should().Be("payment_reference_not_found");
        payment.Status.Should().Be(PaymentStatus.Pending);

        var changed = () => service.ProcessAsync(
            paymentEvent,
            Encoding.UTF8.GetBytes("changed-body"),
            default);
        (await changed.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.WebhookConflict);
    }

    private static PaymentWebhookService CreateService(ApplicationDbContext db) =>
        new(db, Mock.Of<IAppLogger>(), TimeProvider.System);

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static CustomerPayment AddPayment(
        ApplicationDbContext db,
        PaymentStatus status)
    {
        var booking = new CustomerBooking
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            OwnerDeviceId = Guid.NewGuid(),
            Status = "Confirmed",
            GrandTotal = 10m,
            Currency = "ILS",
            PaymentState = status.ToString()
        };
        var payment = new CustomerPayment
        {
            Id = Guid.NewGuid(),
            CustomerBooking = booking,
            CustomerBookingId = booking.Id,
            UserId = booking.UserId,
            OwnerDeviceId = booking.OwnerDeviceId,
            Amount = 10m,
            MinorAmount = 1000,
            Currency = "ILS",
            Method = PaymentMethod.Card,
            Status = status,
            IdempotencyKey = "key",
            RequestHash = new string('A', 64),
            Provider = PaymentProviders.Lahza,
            ProviderReference = "GHSEELI-TEST",
            ProviderTransactionId = "1001"
        };
        db.Add(payment);
        db.SaveChanges();
        return payment;
    }

    private static VerifiedPaymentEvent Event(
        CustomerPayment payment,
        string eventId,
        PaymentEventKind kind) =>
        new(
            eventId,
            kind.ToString(),
            kind,
            payment.ProviderReference!,
            payment.ProviderTransactionId,
            payment.MinorAmount,
            payment.Currency);
}
