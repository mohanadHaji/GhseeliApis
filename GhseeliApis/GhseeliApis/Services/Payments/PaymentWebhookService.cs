using System.Data;
using System.Security.Cryptography;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Services.Payments;

public interface IPaymentWebhookService
{
    Task ProcessAsync(
        VerifiedPaymentEvent paymentEvent,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken cancellationToken);
}

public sealed class PaymentWebhookService : IPaymentWebhookService
{
    private readonly ApplicationDbContext _db;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public PaymentWebhookService(
        ApplicationDbContext db,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task ProcessAsync(
        VerifiedPaymentEvent paymentEvent,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken cancellationToken)
    {
        const int attempts = 5;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await ProcessAttemptAsync(
                    paymentEvent,
                    rawBody,
                    cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt + 1 < attempts)
            {
                _db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }
    }

    private async Task ProcessAttemptAsync(
        VerifiedPaymentEvent paymentEvent,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken cancellationToken)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(rawBody.Span));
        Guid? matchedPaymentId = null;
        var record = await _db.PaymentWebhookEvents.SingleOrDefaultAsync(
            value => value.Provider == PaymentProviders.Lahza &&
                     value.EventId == paymentEvent.EventId,
            cancellationToken);
        if (record is not null)
        {
            if (!string.Equals(record.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                throw new CustomerPaymentException(
                    409,
                    CustomerPaymentErrorCodes.WebhookConflict);
            }
            if (record.State is "Completed" or "Quarantined" or "Deferred")
            {
                return;
            }
        }
        else
        {
            record = new PaymentWebhookEventRecord
            {
                Provider = PaymentProviders.Lahza,
                EventId = paymentEvent.EventId,
                EventType = paymentEvent.EventType,
                BodyHash = bodyHash,
                State = "Processing",
                CreatedAtUtc = _timeProvider.GetUtcNow()
            };
            _db.Add(record);
        }

        if (paymentEvent.Kind != PaymentEventKind.Ignored)
        {
            var payment = await _db.CustomerPayments
                .Include(value => value.CustomerBooking)
                .SingleOrDefaultAsync(
                    value => value.Provider == PaymentProviders.Lahza &&
                             value.ProviderReference == paymentEvent.ProviderReference,
                    cancellationToken);
            if (payment is null)
            {
                Quarantine(record, "payment_reference_not_found");
            }
            else if (!string.Equals(
                         paymentEvent.Currency,
                         payment.Currency,
                         StringComparison.OrdinalIgnoreCase))
            {
                Quarantine(record, "currency_mismatch");
            }
            else if (!string.IsNullOrWhiteSpace(payment.ProviderTransactionId) &&
                     !string.IsNullOrWhiteSpace(paymentEvent.ProviderTransactionId) &&
                     !string.Equals(
                         payment.ProviderTransactionId,
                         paymentEvent.ProviderTransactionId,
                         StringComparison.Ordinal))
            {
                Quarantine(record, "transaction_mismatch");
            }
            else if (IsPartialRefund(paymentEvent, payment))
            {
                record.CustomerPaymentId = payment.Id;
                record.ProviderReference = paymentEvent.ProviderReference;
                record.ProviderTransactionId = paymentEvent.ProviderTransactionId;
                record.Amount = paymentEvent.Amount;
                record.Currency = paymentEvent.Currency.ToUpperInvariant();
                record.State = "Completed";
                record.DispositionReason = "partial_refund_not_applied";
            }
            else if (paymentEvent.Amount != payment.MinorAmount)
            {
                Quarantine(record, "amount_mismatch");
            }
            else
            {
                matchedPaymentId = payment.Id;
                record.CustomerPaymentId = payment.Id;
                record.ProviderReference = paymentEvent.ProviderReference;
                record.ProviderTransactionId = paymentEvent.ProviderTransactionId;
                record.Amount = paymentEvent.Amount;
                record.Currency = paymentEvent.Currency.ToUpperInvariant();
                await ApplyEventAsync(
                    payment,
                    record,
                    paymentEvent,
                    cancellationToken);
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
            var completed = await _db.PaymentWebhookEvents
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.EventId == paymentEvent.EventId &&
                             value.Provider == PaymentProviders.Lahza &&
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
            var raced = await _db.PaymentWebhookEvents
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.Provider == PaymentProviders.Lahza &&
                             value.EventId == paymentEvent.EventId,
                    cancellationToken);
            if (raced is null ||
                !string.Equals(raced.BodyHash, bodyHash, StringComparison.Ordinal) ||
                raced.State is not ("Completed" or "Quarantined" or "Deferred"))
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
            $"Processed verified Lahza event {paymentEvent.EventId} for payment reference {paymentEvent.ProviderReference}.");
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
                var payment = await _db.CustomerPayments
                    .Include(value => value.CustomerBooking)
                    .SingleAsync(value => value.Id == paymentId, cancellationToken);
                var deferred = await _db.PaymentWebhookEvents
                    .Where(value =>
                        value.Provider == PaymentProviders.Lahza &&
                        value.CustomerPaymentId == paymentId &&
                        value.State == "Deferred" &&
                        value.DispositionReason == "refund_awaiting_success")
                    .ToListAsync(cancellationToken);

                if (payment.Status == PaymentStatus.Completed &&
                    !string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
                {
                    foreach (var receipt in deferred)
                    {
                        if (receipt.ProviderReference == payment.ProviderReference &&
                            receipt.ProviderTransactionId == payment.ProviderTransactionId &&
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

    private async Task ApplyEventAsync(
        CustomerPayment payment,
        PaymentWebhookEventRecord record,
        VerifiedPaymentEvent paymentEvent,
        CancellationToken cancellationToken)
    {
        payment.ProviderTransactionId ??= paymentEvent.ProviderTransactionId;
        payment.ProviderStatus = paymentEvent.EventType;
        payment.UpdatedAtUtc = _timeProvider.GetUtcNow();

        if (paymentEvent.Kind is PaymentEventKind.RefundPending or
            PaymentEventKind.RefundProcessing)
        {
            record.State = "Deferred";
            record.DispositionReason = paymentEvent.Kind ==
                                       PaymentEventKind.RefundPending
                ? "refund_pending"
                : "refund_processing";
            return;
        }

        if (paymentEvent.Kind == PaymentEventKind.Refunded &&
            payment.Status != PaymentStatus.Completed)
        {
            record.State = "Deferred";
            record.DispositionReason = "refund_awaiting_success";
            return;
        }

        if (paymentEvent.Kind == PaymentEventKind.RefundFailed)
        {
            record.DispositionReason = "refund_failed";
            return;
        }

        var next = CustomerPaymentTransitions.Apply(
            payment.Status,
            paymentEvent.Kind);
        if (next != payment.Status)
        {
            payment.Status = next;
            payment.CustomerBooking.IsPaid = next == PaymentStatus.Completed;
            payment.CustomerBooking.PaymentState = next.ToString();
        }

        if (paymentEvent.Kind == PaymentEventKind.Succeeded &&
            payment.Status == PaymentStatus.Completed)
        {
            await ApplyDeferredRefundAsync(payment, cancellationToken);
        }
    }

    private async Task ApplyDeferredRefundAsync(
        CustomerPayment payment,
        CancellationToken cancellationToken)
    {
        var deferred = await _db.PaymentWebhookEvents
            .Where(value =>
                value.CustomerPaymentId == payment.Id &&
                value.State == "Deferred" &&
                value.DispositionReason == "refund_awaiting_success")
            .ToArrayAsync(cancellationToken);
        foreach (var record in deferred)
        {
            payment.Status = PaymentStatus.Refunded;
            payment.CustomerBooking.IsPaid = false;
            payment.CustomerBooking.PaymentState = PaymentStatus.Refunded.ToString();
            record.State = "Completed";
            record.DispositionReason = "deferred_refund_applied";
            record.CompletedAtUtc = _timeProvider.GetUtcNow();
        }
    }

    private static void Quarantine(
        PaymentWebhookEventRecord record,
        string reason)
    {
        record.State = "Quarantined";
        record.DispositionReason = reason;
    }

    private static bool IsPartialRefund(
        VerifiedPaymentEvent paymentEvent,
        CustomerPayment payment) =>
        (paymentEvent.Kind is PaymentEventKind.RefundPending or
            PaymentEventKind.RefundProcessing or
            PaymentEventKind.Refunded or
            PaymentEventKind.RefundFailed) &&
        paymentEvent.Amount > 0 &&
        paymentEvent.Amount < payment.MinorAmount;
}
