using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace GhseeliApis.Services.Payments;

public sealed record VerifiedStripeEvent(
    string EventId,
    string EventType,
    StripePaymentEventKind Kind,
    string PaymentIntentId,
    string? ChargeId,
    long Amount,
    string Currency,
    IReadOnlyDictionary<string, string> Metadata,
    bool IsFullyRefunded = true);

public interface IStripeWebhookParser
{
    VerifiedStripeEvent Parse(string rawBody, string signature, string webhookSecret);
}

public sealed class StripeWebhookParser : IStripeWebhookParser
{
    public VerifiedStripeEvent Parse(string rawBody, string signature, string webhookSecret)
    {
        try
        {
            EventUtility.ValidateSignature(rawBody, signature, webhookSecret);
        }
        catch (Exception exception) when (exception is StripeException or ArgumentException)
        {
            throw new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.SignatureInvalid,
                inner: exception);
        }

        Event stripeEvent;
        try
        {
            var eventBody = rawBody;
            var eventObject = JsonNode.Parse(rawBody)?.AsObject()
                ?? throw new InvalidOperationException("Stripe event body must be an object.");
            if (eventObject["api_version"] is null)
            {
                eventObject["api_version"] = StripeConfiguration.ApiVersion;
            }
            if (eventObject["created"] is null)
            {
                eventObject["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            if (eventObject["livemode"] is null)
            {
                eventObject["livemode"] = false;
            }
            if (eventObject["pending_webhooks"] is null)
            {
                eventObject["pending_webhooks"] = 0;
            }
            if (!eventObject.ContainsKey("request"))
            {
                eventObject.Add("request", null);
            }
            eventBody = eventObject.ToJsonString();
            stripeEvent = EventUtility.ParseEvent(
                eventBody,
                throwOnApiVersionMismatch: false);
        }
        catch (Exception exception)
        {
            throw new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.EventInvalid,
                inner: exception);
        }

        try
        {
            return stripeEvent.Data.Object switch
            {
                PaymentIntent intent => new VerifiedStripeEvent(
                    stripeEvent.Id,
                    stripeEvent.Type,
                    MapKind(stripeEvent.Type),
                    intent.Id,
                    intent.LatestChargeId,
                    intent.Amount,
                    intent.Currency,
                    intent.Metadata),
                Charge charge => new VerifiedStripeEvent(
                    stripeEvent.Id,
                    stripeEvent.Type,
                    MapKind(stripeEvent.Type),
                    charge.PaymentIntentId,
                    charge.Id,
                    charge.Amount,
                    charge.Currency,
                    charge.Metadata,
                    charge.Refunded),
                _ => new VerifiedStripeEvent(
                    stripeEvent.Id,
                    stripeEvent.Type,
                    StripePaymentEventKind.Ignored,
                    string.Empty,
                    null,
                    0,
                    string.Empty,
                    new Dictionary<string, string>())
            };
        }
        catch (Exception exception)
        {
            throw new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.EventInvalid,
                inner: exception);
        }
    }

    private static StripePaymentEventKind MapKind(string eventType) =>
        eventType switch
        {
            Events.PaymentIntentSucceeded => StripePaymentEventKind.Succeeded,
            Events.PaymentIntentPaymentFailed => StripePaymentEventKind.Failed,
            Events.PaymentIntentCanceled => StripePaymentEventKind.Canceled,
            Events.ChargeRefunded => StripePaymentEventKind.Refunded,
            _ => StripePaymentEventKind.Ignored
        };
}

public interface IStripeWebhookService
{
    Task ProcessAsync(
        VerifiedStripeEvent stripeEvent,
        string rawBody,
        CancellationToken cancellationToken);
}

public sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly ApplicationDbContext _db;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public StripeWebhookService(
        ApplicationDbContext db,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task ProcessAsync(
        VerifiedStripeEvent stripeEvent,
        string rawBody,
        CancellationToken cancellationToken)
    {
        var bodyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)));
        Guid? matchedPaymentId = null;
        var record = await _db.Set<StripeWebhookEventRecord>()
            .SingleOrDefaultAsync(value => value.EventId == stripeEvent.EventId, cancellationToken);
        if (record is not null)
        {
            if (!string.Equals(record.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                throw new CustomerPaymentException(409, CustomerPaymentErrorCodes.WebhookConflict);
            }

            if (record.State is "Completed" or "Quarantined" or "Deferred")
            {
                return;
            }
        }
        else
        {
            record = new StripeWebhookEventRecord
            {
                EventId = stripeEvent.EventId,
                EventType = stripeEvent.EventType,
                BodyHash = bodyHash,
                State = "Processing",
                CreatedAtUtc = _timeProvider.GetUtcNow()
            };
            _db.Add(record);
        }

        if (stripeEvent.Kind != StripePaymentEventKind.Ignored)
        {
            var payment = await _db.Set<CustomerPayment>()
                .Include(value => value.CustomerBooking)
                .SingleOrDefaultAsync(
                    value => value.PaymentIntentId == stripeEvent.PaymentIntentId,
                    cancellationToken);
            if (payment is null)
            {
                Quarantine(record, "payment_intent_not_found");
            }
            else
            {
                var disposition = VerifyEvent(payment, stripeEvent);
                if (disposition is not null)
                {
                    Quarantine(record, disposition);
                }
                else
                {
                    matchedPaymentId = payment.Id;
                    if (stripeEvent.Kind == StripePaymentEventKind.Refunded &&
                        stripeEvent.IsFullyRefunded &&
                        string.IsNullOrWhiteSpace(payment.ChargeId))
                    {
                        if (string.IsNullOrWhiteSpace(stripeEvent.ChargeId))
                        {
                            Quarantine(record, "charge_missing");
                        }
                        else
                        {
                            DeferRefund(record, payment, stripeEvent);
                        }
                    }
                    else if (stripeEvent.Kind == StripePaymentEventKind.Refunded &&
                             stripeEvent.IsFullyRefunded &&
                             !string.Equals(
                                 payment.ChargeId,
                                 stripeEvent.ChargeId,
                                 StringComparison.Ordinal))
                    {
                        Quarantine(record, "charge_mismatch");
                    }
                    else
                    {
                        var effectiveKind = stripeEvent.Kind == StripePaymentEventKind.Refunded &&
                                            !stripeEvent.IsFullyRefunded
                            ? StripePaymentEventKind.Ignored
                            : stripeEvent.Kind;
                        if (effectiveKind == StripePaymentEventKind.Ignored)
                        {
                            record.CustomerPaymentId = payment.Id;
                            record.PaymentIntentId = stripeEvent.PaymentIntentId;
                            record.ChargeId = stripeEvent.ChargeId;
                            record.Amount = stripeEvent.Amount;
                            record.Currency = stripeEvent.Currency.ToUpperInvariant();
                            record.DispositionReason = "partial_refund_ignored";
                        }
                        ApplyTransition(payment, stripeEvent, effectiveKind);
                        if (stripeEvent.Kind == StripePaymentEventKind.Succeeded &&
                            !string.IsNullOrWhiteSpace(payment.ChargeId))
                        {
                            await ApplyDeferredRefundsAsync(
                                payment,
                                stripeEvent,
                                cancellationToken);
                        }
                    }
                }
            }
        }

        if (record.State == "Processing")
        {
            record.State = "Completed";
        }
        record.CompletedAtUtc = _timeProvider.GetUtcNow();
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var completed = await _db.Set<StripeWebhookEventRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.EventId == stripeEvent.EventId &&
                             value.BodyHash == bodyHash &&
                    (value.State == "Completed" ||
                     value.State == "Quarantined" ||
                     value.State == "Deferred"),
                    cancellationToken);
            if (completed is null)
            {
                throw;
            }
        }
        catch (DbUpdateException) when (_db.Entry(record).State == EntityState.Added)
        {
            _db.Entry(record).State = EntityState.Detached;
            var raced = await _db.Set<StripeWebhookEventRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.EventId == stripeEvent.EventId, cancellationToken);
            if (raced is null)
            {
                throw;
            }
            if (raced.BodyHash != bodyHash)
            {
                throw new CustomerPaymentException(409, CustomerPaymentErrorCodes.WebhookConflict);
            }

            if (raced.State is not ("Completed" or "Quarantined" or "Deferred"))
            {
                throw;
            }
        }

        if (matchedPaymentId.HasValue && _db.Database.IsRelational())
        {
            await ConvergePersistedDeferredRefundAsync(
                matchedPaymentId.Value,
                cancellationToken);
        }

        _logger.LogInfo(
            $"Processed verified Stripe event {stripeEvent.EventId} for payment intent {stripeEvent.PaymentIntentId}.");
    }

    private async Task ConvergePersistedDeferredRefundAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        const int attempts = 5;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var converged = false;
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                _db.ChangeTracker.Clear();
                var payment = await _db.Set<CustomerPayment>()
                    .Include(value => value.CustomerBooking)
                    .SingleAsync(value => value.Id == paymentId, cancellationToken);
                var deferred = await _db.Set<StripeWebhookEventRecord>()
                    .Where(value =>
                        value.CustomerPaymentId == paymentId &&
                        value.State == "Deferred")
                    .ToListAsync(cancellationToken);
                if (payment.Status == PaymentStatus.Completed &&
                    !string.IsNullOrWhiteSpace(payment.ChargeId))
                {
                    foreach (var receipt in deferred)
                    {
                        if (receipt.PaymentIntentId == payment.PaymentIntentId &&
                            receipt.ChargeId == payment.ChargeId &&
                            receipt.Amount == payment.MinorAmount &&
                            string.Equals(
                                receipt.Currency,
                                payment.Currency,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            payment.Status = PaymentStatus.Refunded;
                            payment.ProviderStatus = receipt.EventType;
                            payment.UpdatedAtUtc = _timeProvider.GetUtcNow();
                            payment.CustomerBooking.IsPaid = false;
                            payment.CustomerBooking.PaymentState =
                                PaymentStatus.Refunded.ToString();
                            receipt.State = "Completed";
                            receipt.DispositionReason = "deferred_refund_applied";
                            receipt.CompletedAtUtc = _timeProvider.GetUtcNow();
                            converged = true;
                        }
                        else
                        {
                            Quarantine(receipt, "deferred_refund_mismatch");
                            receipt.CompletedAtUtc = _timeProvider.GetUtcNow();
                        }
                    }
                }

                if (deferred.Count > 0)
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            });

            if (converged)
            {
                return;
            }

            if (attempt + 1 < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }
    }

    private static string? VerifyEvent(
        CustomerPayment payment,
        VerifiedStripeEvent stripeEvent)
    {
        if (stripeEvent.Amount != payment.MinorAmount)
        {
            return "amount_mismatch";
        }
        if (!string.Equals(stripeEvent.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return "currency_mismatch";
        }
        if (!stripeEvent.Metadata.TryGetValue("payment_id", out var paymentId) ||
            !Guid.TryParse(paymentId, out var parsedPaymentId) ||
            parsedPaymentId != payment.Id)
        {
            return "payment_metadata_mismatch";
        }
        if (!stripeEvent.Metadata.TryGetValue("booking_id", out var bookingId) ||
            !Guid.TryParse(bookingId, out var parsedBookingId) ||
            parsedBookingId != payment.CustomerBookingId)
        {
            return "booking_metadata_mismatch";
        }
        if (!stripeEvent.Metadata.TryGetValue("booking_reference", out var bookingReference) ||
            !Guid.TryParse(bookingReference, out var parsedReference) ||
            parsedReference != payment.CustomerBooking.PublicReference)
        {
            return "booking_reference_mismatch";
        }

        return null;
    }

    private void ApplyTransition(
        CustomerPayment payment,
        VerifiedStripeEvent stripeEvent,
        StripePaymentEventKind effectiveKind)
    {
        var next = CustomerPaymentTransitions.Apply(payment.Status, effectiveKind);
        if (next == payment.Status)
        {
            return;
        }

        payment.Status = next;
        payment.ProviderStatus = stripeEvent.EventType;
        payment.UpdatedAtUtc = _timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(stripeEvent.ChargeId))
        {
            payment.ChargeId = stripeEvent.ChargeId;
        }
        payment.CustomerBooking.IsPaid = next == PaymentStatus.Completed;
        payment.CustomerBooking.PaymentState = next.ToString();
    }

    private async Task ApplyDeferredRefundsAsync(
        CustomerPayment payment,
        VerifiedStripeEvent successEvent,
        CancellationToken cancellationToken)
    {
        var deferred = await _db.Set<StripeWebhookEventRecord>()
            .Where(value =>
                value.CustomerPaymentId == payment.Id &&
                value.State == "Deferred")
            .ToListAsync(cancellationToken);
        foreach (var receipt in deferred)
        {
            if (receipt.PaymentIntentId == successEvent.PaymentIntentId &&
                receipt.ChargeId == payment.ChargeId &&
                receipt.Amount == payment.MinorAmount &&
                string.Equals(
                    receipt.Currency,
                    payment.Currency,
                    StringComparison.OrdinalIgnoreCase))
            {
                ApplyTransition(
                    payment,
                    successEvent with { EventType = receipt.EventType },
                    StripePaymentEventKind.Refunded);
                receipt.State = "Completed";
                receipt.DispositionReason = "deferred_refund_applied";
            }
            else
            {
                Quarantine(receipt, "deferred_refund_mismatch");
            }
            receipt.CompletedAtUtc = _timeProvider.GetUtcNow();
        }
    }

    private static void DeferRefund(
        StripeWebhookEventRecord record,
        CustomerPayment payment,
        VerifiedStripeEvent stripeEvent)
    {
        record.State = "Deferred";
        record.DispositionReason = "refund_awaiting_success";
        record.CustomerPaymentId = payment.Id;
        record.PaymentIntentId = stripeEvent.PaymentIntentId;
        record.ChargeId = stripeEvent.ChargeId;
        record.Amount = stripeEvent.Amount;
        record.Currency = stripeEvent.Currency.ToUpperInvariant();
    }

    private static void Quarantine(
        StripeWebhookEventRecord record,
        string reason)
    {
        record.State = "Quarantined";
        record.DispositionReason = reason;
    }
}
