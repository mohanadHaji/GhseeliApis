using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Services;

public interface IBookingStatusService
{
    Task<TransitionWorkOrderResponse?> TransitionAsync(
        Guid userId,
        bool isAdmin,
        Guid workOrderId,
        string targetStatus,
        string correlationId,
        CancellationToken cancellationToken,
        string? idempotencyKey = null);

    Task<AuthoritativeBookingStatusResponse?> GetAuthoritativeAsync(
        Guid bookingReference,
        CancellationToken cancellationToken);
}

public sealed class BookingStatusService : IBookingStatusService
{
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();
    private readonly BusinessDbContext _context;
    private readonly Services.Availability.ISystemClock _clock;
    private readonly IAppLogger _logger;

    public BookingStatusService(
        BusinessDbContext context,
        Services.Availability.ISystemClock clock,
        IAppLogger logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TransitionWorkOrderResponse?> TransitionAsync(
        Guid userId,
        bool isAdmin,
        Guid workOrderId,
        string targetStatus,
        string correlationId,
        CancellationToken cancellationToken,
        string? idempotencyKey = null)
    {
        var eventId = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid()
            : CreateStableEventId(workOrderId, idempotencyKey);
        var occurredAtUtc = _clock.UtcNow;
        var stableCorrelationId =
            InternalServiceHeaderValueValidator.GetOrCreateCorrelationId(correlationId);
        long? expectedSequence = null;
        var strategy = _context.Database.CreateExecutionStrategy();

        async Task<TransitionWorkOrderResponse?> Operation(CancellationToken token)
        {
            _context.ChangeTracker.Clear();
            var workOrder = await _context.WorkOrders
                .Include(value => value.AppointmentReservation)
                    .ThenInclude(value => value.StatusOutboxMessages)
                .SingleOrDefaultAsync(value => value.PublicId == workOrderId, token);
            if (workOrder is null)
            {
                return null;
            }

            if (!BookingStatuses.All.Contains(targetStatus))
            {
                throw new BookingStatusRejectedException(
                    BookingStatusErrorCodes.Invalid,
                    "The requested status is invalid.");
            }

            var existingEvent = workOrder.AppointmentReservation.StatusOutboxMessages
                .SingleOrDefault(value => value.Id == eventId);
            if (existingEvent is not null)
            {
                expectedSequence ??= existingEvent.Sequence;
                return IsExactTransition(existingEvent, workOrderId, targetStatus, expectedSequence)
                    ? Map(workOrder.AppointmentReservation, eventId)
                    : throw new DbUpdateException("The transition identity conflicts with persisted state.");
            }

            if (!isAdmin && !await IsAuthorizedAsync(
                    userId,
                    workOrder.AppointmentReservation.BranchId,
                    token))
            {
                return null;
            }

            var current = BookingStatuses.Normalize(workOrder.Status);
            if (string.Equals(current, targetStatus, StringComparison.Ordinal) ||
                !BookingStatusTransitionRules.CanTransition(current, targetStatus))
            {
                throw new BookingStatusRejectedException(
                    BookingStatusErrorCodes.TransitionInvalid,
                    $"Transition from {current} to {targetStatus} is not allowed.");
            }

            var reservation = workOrder.AppointmentReservation;
            var nextSequence = reservation.StatusSequence + 1;
            expectedSequence ??= nextSequence;
            if (expectedSequence != nextSequence)
            {
                throw new DbUpdateConcurrencyException(
                    "The work order changed while the transition was retried.");
            }

            reservation.Status = targetStatus;
            reservation.StatusSequence = nextSequence;
            reservation.StatusChangedAtUtc = occurredAtUtc;
            workOrder.Status = targetStatus;

            var message = new BookingStatusChangedMessage
            {
                EventId = eventId,
                BookingReference = reservation.CustomerBookingReference,
                ReservationId = reservation.PublicId,
                WorkOrderId = workOrder.PublicId,
                Status = targetStatus,
                Sequence = nextSequence,
                OccurredAtUtc = occurredAtUtc
            };
            var requestJson = JsonSerializer.Serialize(message, JsonOptions);
            _context.BookingStatusOutboxMessages.Add(new BookingStatusOutboxMessage
            {
                Id = eventId,
                AppointmentReservationId = reservation.Id,
                WorkOrderPublicId = workOrder.PublicId,
                Status = targetStatus,
                Sequence = nextSequence,
                RequestJson = requestJson,
                RequestHash = InternalServiceCanonicalRequest.ComputeSha256Hex(
                    System.Text.Encoding.UTF8.GetBytes(requestJson)),
                CorrelationId = stableCorrelationId,
                CreatedAtUtc = occurredAtUtc,
                NextAttemptAtUtc = occurredAtUtc
            });
            await _context.SaveChangesAsync(token);
            return Map(reservation, eventId);
        }

        async Task<bool> VerifySucceeded(CancellationToken token)
        {
            _context.ChangeTracker.Clear();
            var persisted = await _context.BookingStatusOutboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == eventId, token);
            return persisted is not null &&
                IsExactTransition(persisted, workOrderId, targetStatus, expectedSequence);
        }

        try
        {
            var result = _context.Database.IsRelational()
                ? await Microsoft.EntityFrameworkCore.Storage.RelationalExecutionStrategyExtensions
                    .ExecuteInTransactionAsync<TransitionWorkOrderResponse?>(
                        strategy,
                        Operation,
                        VerifySucceeded,
                        IsolationLevel.Serializable,
                        cancellationToken)
                : await strategy.ExecuteAsync(() => Operation(cancellationToken));
            if (result is not null)
            {
                _logger.LogInfo(
                    $"Work-order status changed. WorkOrderId={workOrderId:D}, To={targetStatus}, EventId={eventId:D}, CorrelationId={stableCorrelationId}.");
            }
            return result;
        }
        catch (DbUpdateException)
        {
            if (!await VerifySucceeded(cancellationToken))
            {
                throw;
            }
            var reservation = await _context.AppointmentReservations
                .Include(value => value.WorkOrder)
                .AsNoTracking()
                .SingleAsync(value => value.WorkOrder.PublicId == workOrderId, cancellationToken);
            return Map(reservation, eventId);
        }
    }

    private static Guid CreateStableEventId(Guid workOrderId, string idempotencyKey)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"work-order-transition:{workOrderId:D}:{idempotencyKey}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private async Task<bool> IsAuthorizedAsync(
        Guid userId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var companyId = await _context.Branches
            .Where(branch => branch.Id == branchId)
            .Select(branch => (Guid?)branch.CompanyId)
            .SingleOrDefaultAsync(cancellationToken);
        return companyId.HasValue && await _context.BusinessUserAssignments.AnyAsync(
            assignment =>
                assignment.UserId == userId &&
                assignment.IsActive &&
                assignment.CompanyId == companyId.Value &&
                (assignment.Role == BusinessMembershipRole.Owner &&
                 assignment.BranchId == null ||
                 assignment.Role == BusinessMembershipRole.Employee &&
                 assignment.BranchId == branchId),
            cancellationToken);
    }

    private static bool IsExactTransition(
        BookingStatusOutboxMessage message,
        Guid workOrderId,
        string status,
        long? sequence) =>
        message.WorkOrderPublicId == workOrderId &&
        string.Equals(message.Status, status, StringComparison.Ordinal) &&
        sequence.HasValue &&
        message.Sequence == sequence.Value;

    public async Task<AuthoritativeBookingStatusResponse?> GetAuthoritativeAsync(
        Guid bookingReference,
        CancellationToken cancellationToken)
    {
        var reservation = await _context.AppointmentReservations
            .Include(value => value.WorkOrder)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.CustomerBookingReference == bookingReference,
                cancellationToken);
        return reservation is null
            ? null
            : new AuthoritativeBookingStatusResponse
            {
                BookingReference = reservation.CustomerBookingReference,
                ReservationId = reservation.PublicId,
                WorkOrderId = reservation.WorkOrder.PublicId,
                Status = reservation.Status,
                Sequence = reservation.StatusSequence,
                ChangedAtUtc = reservation.StatusChangedAtUtc
            };
    }

    private static TransitionWorkOrderResponse Map(
        AppointmentReservation reservation,
        Guid eventId) => new()
        {
            EventId = eventId,
            BookingReference = reservation.CustomerBookingReference,
            ReservationId = reservation.PublicId,
            WorkOrderId = reservation.WorkOrder.PublicId,
            Status = reservation.Status,
            Sequence = reservation.StatusSequence,
            ChangedAtUtc = reservation.StatusChangedAtUtc
        };
}

public static class BookingStatusTransitionRules
{
    public static bool CanTransition(string current, string target) =>
        BookingStatuses.CanTransition(current, target);
}

public sealed class BookingStatusRejectedException : Exception
{
    public BookingStatusRejectedException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
