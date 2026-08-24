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
public sealed class StripeWebhookServiceTests
{
    [Fact]
    public async Task Verified_success_completes_payment_and_marks_booking_paid()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        var service = CreateService(db);

        await service.ProcessAsync(Event(payment, "evt_success", StripePaymentEventKind.Succeeded),
            "{\"id\":\"evt_success\"}", default);

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.CustomerBooking.IsPaid.Should().BeTrue();
        payment.CustomerBooking.PaymentState.Should().Be("Completed");
        (await db.StripeWebhookEvents.SingleAsync()).State.Should().Be("Completed");
    }

    [Fact]
    public async Task Identical_event_replays_and_changed_body_conflicts()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        var service = CreateService(db);
        var stripeEvent = Event(payment, "evt_replay", StripePaymentEventKind.Succeeded);
        await service.ProcessAsync(stripeEvent, "same-body", default);

        await service.ProcessAsync(stripeEvent, "same-body", default);
        var changed = () => service.ProcessAsync(stripeEvent, "changed-body", default);

        (await changed.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.WebhookConflict);
        (await db.StripeWebhookEvents.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("wrong-intent", 1000, "ILS")]
    [InlineData("pi_1", 999, "ILS")]
    [InlineData("pi_1", 1000, "USD")]
    public async Task Intent_amount_or_currency_mismatch_is_durably_quarantined_without_mutation(
        string intentId,
        long amount,
        string currency)
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        var original = Event(payment, "evt_bad", StripePaymentEventKind.Succeeded);
        var invalid = original with
        {
            PaymentIntentId = intentId,
            Amount = amount,
            Currency = currency
        };

        await CreateService(db).ProcessAsync(invalid, "bad", default);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.CustomerBooking.IsPaid.Should().BeFalse();
        var record = await db.StripeWebhookEvents.SingleAsync();
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
            Event(completed, "evt_late_failure", StripePaymentEventKind.Failed),
            "late-failure", default);
        completed.Status.Should().Be(PaymentStatus.Completed);
        completed.CustomerBooking.IsPaid.Should().BeTrue();

        completed.Status = PaymentStatus.Refunded;
        completed.CustomerBooking.IsPaid = false;
        completed.CustomerBooking.PaymentState = "Refunded";
        await db.SaveChangesAsync();
        await service.ProcessAsync(
            Event(completed, "evt_late_success", StripePaymentEventKind.Succeeded),
            "late-success", default);

        completed.Status.Should().Be(PaymentStatus.Refunded);
        completed.CustomerBooking.IsPaid.Should().BeFalse();
    }

    [Fact]
    public async Task Refund_requires_persisted_charge_match()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Completed);
        var invalid = Event(payment, "evt_refund", StripePaymentEventKind.Refunded) with
        {
            ChargeId = "ch_wrong"
        };

        await CreateService(db).ProcessAsync(invalid, "refund", default);
        payment.Status.Should().Be(PaymentStatus.Completed);
        (await db.StripeWebhookEvents.SingleAsync()).State.Should().Be("Quarantined");
    }

    [Fact]
    public async Task Full_refund_before_success_is_deferred_then_converges_to_refunded()
    {
        await using var db = CreateDb();
        var payment = AddPayment(db, PaymentStatus.Pending);
        payment.ChargeId = null;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var refund = Event(payment, "evt_refund_first", StripePaymentEventKind.Refunded) with
        {
            ChargeId = "ch_deferred"
        };

        await service.ProcessAsync(refund, "refund-first", default);

        payment.Status.Should().Be(PaymentStatus.Pending);
        var deferred = await db.StripeWebhookEvents.SingleAsync();
        deferred.State.Should().Be("Deferred");
        deferred.CustomerPaymentId.Should().Be(payment.Id);
        deferred.ChargeId.Should().Be("ch_deferred");

        var success = Event(payment, "evt_success_later", StripePaymentEventKind.Succeeded) with
        {
            ChargeId = "ch_deferred"
        };
        await service.ProcessAsync(success, "success-later", default);

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
        var stripeEvent = Event(payment, "evt_unknown", StripePaymentEventKind.Succeeded) with
        {
            PaymentIntentId = "pi_unknown"
        };
        var service = CreateService(db);

        await service.ProcessAsync(stripeEvent, "unknown-body", default);
        await service.ProcessAsync(stripeEvent, "unknown-body", default);

        var record = await db.StripeWebhookEvents.SingleAsync();
        record.State.Should().Be("Quarantined");
        record.DispositionReason.Should().Be("payment_intent_not_found");
        payment.Status.Should().Be(PaymentStatus.Pending);

        var changed = () => service.ProcessAsync(stripeEvent, "changed-body", default);
        (await changed.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.WebhookConflict);
    }

    private static StripeWebhookService CreateService(ApplicationDbContext db) =>
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
            StripeIdempotencyKey = "stripe-key",
            PaymentIntentId = "pi_1",
            ChargeId = "ch_1"
        };
        db.Add(payment);
        db.SaveChanges();
        return payment;
    }

    private static VerifiedStripeEvent Event(
        CustomerPayment payment,
        string eventId,
        StripePaymentEventKind kind) =>
        new(
            eventId,
            kind.ToString(),
            kind,
            payment.PaymentIntentId!,
            payment.ChargeId,
            payment.MinorAmount,
            payment.Currency,
            new Dictionary<string, string>
            {
                ["payment_id"] = payment.Id.ToString("D"),
                ["booking_id"] = payment.CustomerBookingId.ToString("D"),
                ["booking_reference"] = payment.CustomerBooking.PublicReference.ToString("D")
            });
}
