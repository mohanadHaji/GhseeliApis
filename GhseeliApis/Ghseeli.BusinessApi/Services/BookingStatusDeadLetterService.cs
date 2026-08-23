using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.Common.Logging;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Ghseeli.BusinessApi.Services;

public static class DeadLetterRequeueOutcomes
{
    public const string Requeued = "Requeued";
    public const string AlreadyRequeued = "AlreadyRequeued";
}

public sealed record DeadLetterRequeueResponse(
    Guid EventId,
    string Outcome,
    string DeliveryState,
    int Generation);

public sealed class DeadLetterRequeueConflictException : Exception
{
    public DeadLetterRequeueConflictException() : base("The event is currently being delivered.")
    {
    }
}

public interface IBookingStatusDeadLetterService
{
    Task<DeadLetterRequeueResponse?> RequeueAsync(
        Guid adminUserId,
        Guid eventId,
        string requestId,
        CancellationToken cancellationToken);
}

public sealed class BookingStatusDeadLetterService : IBookingStatusDeadLetterService
{
    private readonly BusinessDbContext _context;
    private readonly Services.Availability.ISystemClock _clock;
    private readonly IAppLogger _logger;

    public BookingStatusDeadLetterService(
        BusinessDbContext context,
        Services.Availability.ISystemClock clock,
        IAppLogger logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DeadLetterRequeueResponse?> RequeueAsync(
        Guid adminUserId,
        Guid eventId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var now = new DateTimeOffset(
            DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc));
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
        {
            throw new ArgumentException("A bounded requeue request ID is required.", nameof(requestId));
        }

        _context.ChangeTracker.Clear();
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        var current = _context.Database.IsRelational()
            ? await _context.BookingStatusOutboxMessages
                .FromSqlInterpolated(
                    $"SELECT * FROM [BookingStatusOutboxMessages] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {eventId}")
                .SingleOrDefaultAsync(cancellationToken)
            : await _context.BookingStatusOutboxMessages
                .SingleOrDefaultAsync(value => value.Id == eventId, cancellationToken);
        var existingHistory = await _context.BookingStatusRequeueHistory
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.BookingStatusOutboxMessageId == eventId &&
                value.RequestId == requestId,
                cancellationToken);
        if (current is null)
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            _logger.LogWarning(
                $"Booking status dead-letter requeue target was not found. AdminUserId={adminUserId:D}, EventId={eventId:D}.");
            return null;
        }

        if (existingHistory is not null)
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return Audit(
                adminUserId,
                eventId,
                DeadLetterRequeueOutcomes.AlreadyRequeued,
                current.DeliveryState,
                existingHistory.Generation);
        }
        if (current.DeliveryState != BookingStatusOutboxStates.DeadLetter)
        {
            _logger.LogWarning(
                $"Booking status dead-letter requeue target was not dead-lettered. AdminUserId={adminUserId:D}, EventId={eventId:D}.");
            throw new DeadLetterRequeueConflictException();
        }

        var generation = checked(current.DeliveryGeneration + 1);
        current.DeliveryGeneration = generation;
        current.DeliveryState = BookingStatusOutboxStates.Pending;
        current.AttemptCount = 0;
        current.NextAttemptAtUtc = now;
        current.DeadLetteredAtUtc = null;
        current.LastErrorCode = null;
        current.LeaseToken = null;
        current.LeaseOwner = null;
        current.LeaseExpiresAtUtc = null;
        current.RequeuedAtUtc = now;
        current.RequeuedByAdminUserId = adminUserId;
        current.RequeueRequestId = requestId;
        _context.BookingStatusRequeueHistory.Add(new BookingStatusRequeueHistory
        {
            BookingStatusOutboxMessageId = eventId,
            Generation = generation,
            AdminUserId = adminUserId,
            RequestId = requestId,
            RequeuedAtUtc = now
        });
        await _context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return Audit(
            adminUserId,
            eventId,
            DeadLetterRequeueOutcomes.Requeued,
            BookingStatusOutboxStates.Pending,
            generation);
    }

    private DeadLetterRequeueResponse Audit(
        Guid adminUserId,
        Guid eventId,
        string outcome,
        string state,
        int generation)
    {
        _logger.LogInfo(
            $"Booking status dead-letter requeue resolved. AdminUserId={adminUserId:D}, EventId={eventId:D}, Outcome={outcome}.");
        return new DeadLetterRequeueResponse(eventId, outcome, state, generation);
    }
}
