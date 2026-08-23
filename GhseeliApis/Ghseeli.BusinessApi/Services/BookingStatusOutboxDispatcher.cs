using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.Common.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace Ghseeli.BusinessApi.Services;

public sealed class BookingStatusOutboxOptions
{
    public const string SectionName = "BookingStatusOutbox";
    public int LeaseSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 8;
    public int BaseRetrySeconds { get; set; } = 2;
    public int MaxRetrySeconds { get; set; } = 300;
    public int PollIntervalMilliseconds { get; set; } = 5000;
    public int FailureBackoffMilliseconds { get; set; } = 1000;
}

public interface IBookingStatusOutboxDispatcher
{
    Task<bool> DeliverNextAsync(CancellationToken cancellationToken);
}

public sealed class BookingStatusOutboxDispatcher : IBookingStatusOutboxDispatcher
{
    private readonly BusinessDbContext _context;
    private readonly ICustomerBookingStatusClient _client;
    private readonly Services.Availability.ISystemClock _clock;
    private readonly IAppLogger _logger;
    private readonly BookingStatusOutboxOptions _options;
    private readonly string _leaseOwner;

    public BookingStatusOutboxDispatcher(
        BusinessDbContext context,
        ICustomerBookingStatusClient client,
        Services.Availability.ISystemClock clock,
        IAppLogger logger,
        IOptions<BookingStatusOutboxOptions>? options = null)
    {
        _context = context;
        _client = client;
        _clock = clock;
        _logger = logger;
        _options = options?.Value ?? new BookingStatusOutboxOptions();
        _leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    public async Task<bool> DeliverNextAsync(CancellationToken cancellationToken)
    {
        BookingStatusOutboxMessage? lease;
        try
        {
            lease = await TryAcquireLeaseAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Booking status outbox lease acquisition failed.");
            return false;
        }
        if (lease is null)
        {
            return false;
        }

        try
        {
            await _client.DeliverAsync(
                lease.RequestJson,
                lease.Id,
                CreateTransportIdempotencyKey(lease.Id, lease.DeliveryGeneration),
                lease.CorrelationId,
                cancellationToken);
            try
            {
                await CompleteLeaseAsync(lease.Id, lease.LeaseToken!.Value, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(
                    $"Booking status outbox completion persistence failed. EventId={lease.Id:D}.");
                await TryFailLeaseAsync(lease, cancellationToken);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await TryFailLeaseAsync(lease, cancellationToken, exception);
        }

        return true;
    }

    private async Task TryFailLeaseAsync(
        BookingStatusOutboxMessage lease,
        CancellationToken cancellationToken,
        Exception? exception = null)
    {
        try
        {
            await FailLeaseAsync(
                lease,
                exception ?? new InvalidOperationException("Delivery completion was not persisted."),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                $"Booking status outbox failure persistence failed. EventId={lease.Id:D}.");
        }
    }

    private async Task<BookingStatusOutboxMessage?> TryAcquireLeaseAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            _context.ChangeTracker.Clear();
            var now = _clock.UtcNow;
            var candidate = await _context.BookingStatusOutboxMessages
                .Where(message =>
                    (message.DeliveryState == BookingStatusOutboxStates.Pending &&
                     message.NextAttemptAtUtc <= now ||
                     message.DeliveryState == BookingStatusOutboxStates.Leased &&
                     message.LeaseExpiresAtUtc <= now) &&
                    !_context.BookingStatusOutboxMessages.Any(earlier =>
                        earlier.AppointmentReservationId == message.AppointmentReservationId &&
                        earlier.Sequence < message.Sequence &&
                        (earlier.DeliveryState == BookingStatusOutboxStates.Pending ||
                         earlier.DeliveryState == BookingStatusOutboxStates.Leased ||
                         earlier.DeliveryState == BookingStatusOutboxStates.DeadLetter)))
                .OrderBy(message => message.CreatedAtUtc)
                .ThenBy(message => message.Sequence)
                .FirstOrDefaultAsync(cancellationToken);
            if (candidate is null)
            {
                return null;
            }

            candidate.DeliveryState = BookingStatusOutboxStates.Leased;
            candidate.LeaseToken = Guid.NewGuid();
            candidate.LeaseOwner = _leaseOwner;
            candidate.LeaseExpiresAtUtc = now.AddSeconds(Math.Max(1, _options.LeaseSeconds));
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return candidate;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A different replica acquired the durable lease.
            }
        }
        return null;
    }

    private async Task CompleteLeaseAsync(
        Guid eventId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        if (!_context.Database.IsRelational())
        {
            var message = await LeaseQuery(eventId, leaseToken).SingleOrDefaultAsync(cancellationToken);
            if (message is null)
            {
                return;
            }
            message.DeliveryState = BookingStatusOutboxStates.Delivered;
            message.DeliveredAtUtc = now;
            message.AttemptCount++;
            message.LastErrorCode = null;
            ClearLease(message);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInfo($"Delivered booking status event. EventId={eventId:D}.");
            return;
        }
        var updated = await LeaseQuery(eventId, leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.DeliveryState, BookingStatusOutboxStates.Delivered)
                .SetProperty(message => message.DeliveredAtUtc, now)
                .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1)
                .SetProperty(message => message.LastErrorCode, (string?)null)
                .SetProperty(message => message.LeaseToken, (Guid?)null)
                .SetProperty(message => message.LeaseOwner, (string?)null)
                .SetProperty(message => message.LeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken);
        if (updated == 1)
        {
            _logger.LogInfo($"Delivered booking status event. EventId={eventId:D}.");
        }
    }

    private async Task FailLeaseAsync(
        BookingStatusOutboxMessage lease,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        _context.ChangeTracker.Clear();
        var attempts = lease.AttemptCount + 1;
        var permanent = IsPermanent(exception);
        var deadLetter = permanent || attempts >= Math.Max(1, _options.MaxAttempts);
        var errorCode = GetErrorCode(exception);
        var delaySeconds = Math.Min(
            Math.Max(1, _options.MaxRetrySeconds),
            Math.Max(1, _options.BaseRetrySeconds) *
            Math.Pow(2, Math.Min(attempts - 1, 16)));
        if (!_context.Database.IsRelational())
        {
            var message = await LeaseQuery(lease.Id, lease.LeaseToken!.Value)
                .SingleOrDefaultAsync(cancellationToken);
            if (message is null)
            {
                return;
            }
            message.DeliveryState = deadLetter
                ? BookingStatusOutboxStates.DeadLetter
                : BookingStatusOutboxStates.Pending;
            message.AttemptCount = attempts;
            message.LastErrorCode = errorCode;
            message.NextAttemptAtUtc = now.AddSeconds(delaySeconds);
            message.DeadLetteredAtUtc = deadLetter ? now : null;
            ClearLease(message);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                $"Booking status delivery failed. EventId={lease.Id:D}, Attempt={attempts}, DeadLetter={deadLetter}.");
            return;
        }
        var updated = await LeaseQuery(lease.Id, lease.LeaseToken!.Value)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.DeliveryState,
                    deadLetter ? BookingStatusOutboxStates.DeadLetter : BookingStatusOutboxStates.Pending)
                .SetProperty(message => message.AttemptCount, attempts)
                .SetProperty(message => message.LastErrorCode, errorCode)
                .SetProperty(message => message.NextAttemptAtUtc, now.AddSeconds(delaySeconds))
                .SetProperty(message => message.DeadLetteredAtUtc,
                    deadLetter ? now : (DateTimeOffset?)null)
                .SetProperty(message => message.LeaseToken, (Guid?)null)
                .SetProperty(message => message.LeaseOwner, (string?)null)
                .SetProperty(message => message.LeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken);
        if (updated == 1)
        {
            _logger.LogWarning(
                $"Booking status delivery failed. EventId={lease.Id:D}, Attempt={attempts}, DeadLetter={deadLetter}.");
        }
    }

    private IQueryable<BookingStatusOutboxMessage> LeaseQuery(Guid eventId, Guid leaseToken) =>
        _context.BookingStatusOutboxMessages.Where(message =>
            message.Id == eventId &&
            message.DeliveryState == BookingStatusOutboxStates.Leased &&
            message.LeaseToken == leaseToken &&
            message.LeaseOwner == _leaseOwner);

    private static void ClearLease(BookingStatusOutboxMessage message)
    {
        message.LeaseToken = null;
        message.LeaseOwner = null;
        message.LeaseExpiresAtUtc = null;
    }

    private static bool IsPermanent(Exception exception) =>
        exception is HttpRequestException { StatusCode: HttpStatusCode status } &&
        (int)status is >= 400 and < 500 &&
        status is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests;

    private static string GetErrorCode(Exception exception) =>
        exception is HttpRequestException { StatusCode: { } status }
            ? $"HTTP_{(int)status}"
            : exception.GetType().Name[..Math.Min(64, exception.GetType().Name.Length)];

    internal static string CreateTransportIdempotencyKey(Guid eventId, int generation) =>
        generation <= 0
            ? $"booking-status-{eventId:N}"
            : $"booking-status-{eventId:N}-g{generation}";
}

public sealed class BookingStatusOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<CustomerBookingStatusClientOptions> _clientOptions;
    private readonly BookingStatusOutboxOptions _outboxOptions;
    private readonly IAppLogger _logger;

    public BookingStatusOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CustomerBookingStatusClientOptions> clientOptions,
        IOptions<BookingStatusOutboxOptions> outboxOptions,
        IAppLogger logger)
    {
        _scopeFactory = scopeFactory;
        _clientOptions = clientOptions;
        _outboxOptions = outboxOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleFailed = false;
            try
            {
                if (!_clientOptions.Value.DisableDeliveryInTesting)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dispatcher = scope.ServiceProvider
                        .GetRequiredService<IBookingStatusOutboxDispatcher>();
                    while (await dispatcher.DeliverNextAsync(stoppingToken))
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                cycleFailed = true;
                _logger.LogError("Booking status outbox background cycle failed.");
            }

            try
            {
                var delayMilliseconds = cycleFailed
                    ? Math.Clamp(_outboxOptions.FailureBackoffMilliseconds, 1, 30000)
                    : Math.Clamp(_outboxOptions.PollIntervalMilliseconds, 1, 30000);
                await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
