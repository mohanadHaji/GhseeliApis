using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.Bookings;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Business;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace GhseeliApis.Services.Bookings;

public interface IBookingStatusInboxService
{
    Task<BookingStatusCallbackResponse> ApplyAsync(
        BookingStatusChangedMessage message,
        string requestHash,
        bool reconciliation,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BookingStatusCallbackResponse> ReconcileAsync(
        Guid bookingReference,
        string correlationId,
        CancellationToken cancellationToken);

    Task<BookingStatusReadResponse?> GetCurrentAsync(
        Guid bookingReference,
        CancellationToken cancellationToken);
}

public sealed class BookingStatusInboxService : IBookingStatusInboxService
{
    private readonly ApplicationDbContext _context;
    private readonly IBusinessApiClient _businessClient;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger _logger;

    public BookingStatusInboxService(
        ApplicationDbContext context,
        IBusinessApiClient businessClient,
        TimeProvider timeProvider,
        IAppLogger logger)
    {
        _context = context;
        _businessClient = businessClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BookingStatusCallbackResponse> ApplyAsync(
        BookingStatusChangedMessage message,
        string requestHash,
        bool reconciliation,
        string correlationId,
        CancellationToken cancellationToken)
    {
        Validate(message, requestHash);
        var strategy = _context.Database.CreateExecutionStrategy();

        async Task<BookingStatusCallbackResponse> Operation(CancellationToken token)
        {
            _context.ChangeTracker.Clear();

            var existing = await _context.ProcessedBookingStatusMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.EventId == message.EventId, token);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    throw new BookingStatusInboxException(
                        BookingStatusErrorCodes.EventConflict,
                        "The event identifier was already used with different content.");
                }

                var replayBooking = await _context.CustomerBookings
                    .AsNoTracking()
                    .SingleAsync(value => value.Id == existing.CustomerBookingId, token);
                return Response(message.EventId, replayBooking, existing.Applied, !existing.Applied);
            }

            var booking = await LoadBookingForUpdateAsync(message.BookingReference, token);
            if (booking is null)
            {
                throw new BookingStatusInboxException(
                    BookingStatusErrorCodes.NotFound,
                    "The booking reference was not found.");
            }
            if (booking.BusinessReservationId != message.ReservationId ||
                booking.BusinessWorkOrderId != message.WorkOrderId)
            {
                throw new BookingStatusInboxException(
                    BookingStatusErrorCodes.ReferenceMismatch,
                    "The business reservation or work-order reference does not match.");
            }

            var stale = message.Sequence < booking.BusinessStatusSequence;
            var applied = false;
            if (!stale)
            {
                if (message.Sequence == booking.BusinessStatusSequence)
                {
                    var reconciliationEventId = CreateReconciliationEventId(
                        booking.PublicReference,
                        message.Sequence);
                    var reconciliationApplied = await _context
                        .ProcessedBookingStatusMessages
                        .AsNoTracking()
                        .AnyAsync(
                            value =>
                                value.EventId == reconciliationEventId &&
                                value.CustomerBookingId == booking.Id &&
                                value.Status == message.Status &&
                                value.Sequence == message.Sequence &&
                                value.Applied,
                            token);
                    var statusMatches = string.Equals(
                        booking.Status,
                        message.Status,
                        StringComparison.Ordinal);
                    if (!statusMatches ||
                        (!reconciliation && !reconciliationApplied))
                    {
                        throw new BookingStatusInboxException(
                            BookingStatusErrorCodes.TransitionInvalid,
                            "A new event cannot reuse the current status sequence.");
                    }
                }
                else
                {
                    var allowed = CustomerBookingStatusTransitionRules.CanTransition(
                        booking.Status,
                        message.Status);
                    if (!allowed)
                    {
                        throw new BookingStatusInboxException(
                            BookingStatusErrorCodes.TransitionInvalid,
                            $"Transition from {booking.Status} to {message.Status} is not allowed.");
                    }

                    booking.Status = message.Status;
                    booking.BusinessStatusSequence = message.Sequence;
                    booking.StatusChangedAtUtc = message.OccurredAtUtc;
                    applied = true;
                }
            }

            _context.ProcessedBookingStatusMessages.Add(new ProcessedBookingStatusMessage
            {
                EventId = message.EventId,
                CustomerBookingId = booking.Id,
                RequestHash = requestHash,
                Status = message.Status,
                Sequence = message.Sequence,
                Applied = applied,
                ProcessedAtUtc = _timeProvider.GetUtcNow()
            });
            await _context.SaveChangesAsync(token);

            _logger.LogInfo(
                $"Processed booking status event. BookingReference={booking.PublicReference:D}, EventId={message.EventId:D}, Status={message.Status}, Sequence={message.Sequence}, Applied={applied}, CorrelationId={correlationId}.");
            return Response(message.EventId, booking, applied, stale);
        }

        async Task<bool> VerifySucceeded(CancellationToken token)
        {
            _context.ChangeTracker.Clear();
            var persisted = await _context.ProcessedBookingStatusMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.EventId == message.EventId, token);
            return persisted is not null &&
                string.Equals(persisted.RequestHash, requestHash, StringComparison.Ordinal);
        }

        try
        {
            return _context.Database.IsRelational()
                ? await Microsoft.EntityFrameworkCore.Storage.RelationalExecutionStrategyExtensions
                    .ExecuteInTransactionAsync<BookingStatusCallbackResponse>(
                        strategy,
                        Operation,
                        VerifySucceeded,
                        IsolationLevel.Serializable,
                        cancellationToken)
                : await strategy.ExecuteAsync(() => Operation(cancellationToken));
        }
        catch (DbUpdateException)
        {
            if (!await VerifySucceeded(cancellationToken))
            {
                throw;
            }
            var persisted = await _context.ProcessedBookingStatusMessages
                .AsNoTracking()
                .SingleAsync(value => value.EventId == message.EventId, cancellationToken);
            var booking = await _context.CustomerBookings
                .AsNoTracking()
                .SingleAsync(value => value.Id == persisted.CustomerBookingId, cancellationToken);
            return Response(message.EventId, booking, persisted.Applied, !persisted.Applied);
        }
    }

    public async Task<BookingStatusCallbackResponse> ReconcileAsync(
        Guid bookingReference,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var authoritative = await _businessClient.GetReservationStatusAsync(
            bookingReference,
            cancellationToken);
        if (authoritative is null)
        {
            throw new BookingStatusInboxException(
                BookingStatusErrorCodes.NotFound,
                "The authoritative booking reference was not found.");
        }
        ValidateAuthoritative(authoritative);
        var current = await GetCurrentAsync(bookingReference, cancellationToken);
        if (current is null)
        {
            throw new BookingStatusInboxException(
                BookingStatusErrorCodes.NotFound,
                "The booking reference was not found.");
        }
        if (authoritative.BookingReference != bookingReference)
        {
            throw new BookingStatusInboxException(
                BookingStatusErrorCodes.ReferenceMismatch,
                "The authoritative booking reference does not match.");
        }
        if (current.Sequence > authoritative.Sequence ||
            (current.Sequence == authoritative.Sequence &&
             !string.Equals(current.Status, authoritative.Status, StringComparison.Ordinal)))
        {
            throw new BookingStatusInboxException(
                BookingStatusErrorCodes.TransitionInvalid,
                "The authoritative status sequence conflicts with the Customer booking.");
        }
        if (current.Sequence == authoritative.Sequence)
        {
            return new BookingStatusCallbackResponse
            {
                BookingReference = current.BookingReference,
                Status = current.Status,
                Sequence = current.Sequence,
                Applied = false,
                Stale = false
            };
        }
        var eventId = CreateReconciliationEventId(bookingReference, authoritative.Sequence);
        var message = new BookingStatusChangedMessage
        {
            ContractVersion = authoritative.ContractVersion,
            EventId = eventId,
            BookingReference = authoritative.BookingReference,
            ReservationId = authoritative.ReservationId,
            WorkOrderId = authoritative.WorkOrderId,
            Status = authoritative.Status,
            Sequence = authoritative.Sequence,
            OccurredAtUtc = authoritative.ChangedAtUtc
        };
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"{bookingReference:D}|{authoritative.Sequence}|{authoritative.Status}")))
            .ToLowerInvariant();
        return await ApplyAsync(message, hash, true, correlationId, cancellationToken);
    }

    private static void ValidateAuthoritative(AuthoritativeBookingStatusResponse response)
    {
        if (response.ContractVersion != BookingStatusContract.Version ||
            response.BookingReference == Guid.Empty ||
            response.ReservationId == Guid.Empty ||
            response.WorkOrderId == Guid.Empty ||
            !BookingStatuses.All.Contains(response.Status) ||
            response.Sequence < 0 ||
            response.ChangedAtUtc == default ||
            response.ChangedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new BookingStatusInboxException(
                BookingStatusErrorCodes.Invalid,
                "The authoritative booking status contract is invalid.");
        }
    }

    public async Task<BookingStatusReadResponse?> GetCurrentAsync(
        Guid bookingReference,
        CancellationToken cancellationToken)
    {
        var booking = await _context.CustomerBookings.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.PublicReference == bookingReference,
                cancellationToken);
        return booking is null
            ? null
            : new BookingStatusReadResponse
            {
                BookingReference = booking.PublicReference,
                ReservationId = booking.BusinessReservationId,
                WorkOrderId = booking.BusinessWorkOrderId,
                Status = booking.Status,
                Sequence = booking.BusinessStatusSequence,
                ChangedAtUtc = booking.StatusChangedAtUtc
            };
    }

    private async Task<CustomerBooking?> LoadBookingForUpdateAsync(
        Guid bookingReference,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            return await _context.CustomerBookings
                .FromSqlInterpolated(
                    $"SELECT * FROM [CustomerBookings] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicReference] = {bookingReference}")
                .SingleOrDefaultAsync(cancellationToken);
        }
        return await _context.CustomerBookings
            .SingleOrDefaultAsync(value => value.PublicReference == bookingReference, cancellationToken);
    }

    private static void Validate(BookingStatusChangedMessage message, string requestHash)
    {
        if (message is null ||
            message.ContractVersion != BookingStatusContract.Version ||
            message.EventId == Guid.Empty ||
            message.BookingReference == Guid.Empty ||
            message.ReservationId == Guid.Empty ||
            message.WorkOrderId == Guid.Empty ||
            !BookingStatuses.All.Contains(message.Status) ||
            message.Sequence <= 0 ||
            message.OccurredAtUtc == default ||
            message.OccurredAtUtc.Offset != TimeSpan.Zero ||
            !IsSha256(requestHash))
        {
            throw new BookingStatusInboxException(
                BookingStatusErrorCodes.Invalid,
                "The booking status contract is invalid.");
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static BookingStatusCallbackResponse Response(
        Guid eventId,
        CustomerBooking booking,
        bool applied,
        bool stale) => new()
        {
            EventId = eventId,
            BookingReference = booking.PublicReference,
            Status = booking.Status,
            Sequence = booking.BusinessStatusSequence,
            Applied = applied,
            Stale = stale
        };

    private static Guid CreateReconciliationEventId(Guid bookingReference, long sequence)
    {
        Span<byte> input = stackalloc byte[24];
        bookingReference.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], sequence);
        return new Guid(System.Security.Cryptography.SHA256.HashData(input)[..16]);
    }
}

public sealed class BookingStatusInboxException : Exception
{
    public BookingStatusInboxException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

internal static class CustomerBookingStatusTransitionRules
{
    public static bool CanTransition(string current, string target) =>
        BookingStatuses.CanTransition(current, target);
}
