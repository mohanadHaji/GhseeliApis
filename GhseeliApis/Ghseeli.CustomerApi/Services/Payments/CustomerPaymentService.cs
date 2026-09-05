using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Payments;

public static class CustomerPaymentErrorCodes
{
    public const string Invalid = "payment_request_invalid";
    public const string NotFound = "payment_not_found";
    public const string BookingNotFound = "booking_not_found";
    public const string Ineligible = "booking_not_payable";
    public const string AlreadyExists = "payment_already_exists";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string IdempotencyKeyRequired = "idempotency_key_required";
    public const string IdempotencyKeyInvalid = "idempotency_key_invalid";
    public const string MethodUnavailable = "payment_method_unavailable";
    public const string MethodNotYetSupported = "payment_method_not_yet_supported";
    public const string ProviderUnavailable = "payment_provider_unavailable";
    public const string AmountInvalid = "booking_not_payable";
    public const string RequestTooLarge = "payment_request_too_large";
    public const string CurrencyUnsupported = "booking_currency_not_supported";
    public const string GatewayAmbiguous = "payment_gateway_ambiguous";
    public const string SignatureMissing = "lahza_signature_missing";
    public const string SignatureInvalid = "lahza_signature_invalid";
    public const string EventInvalid = "lahza_event_invalid";
    public const string WebhookConflict = "lahza_webhook_conflict";
    public const string WebhookTooLarge = "lahza_webhook_too_large";
    public const string WebhookConfiguration = "lahza_webhook_configuration_invalid";
    public const string PaymentUnsupportedMediaType = "payment_unsupported_media_type";
    public const string WebhookUnsupportedMediaType = "lahza_webhook_unsupported_media_type";
}

public sealed class CustomerPaymentException : Exception
{
    public CustomerPaymentException(int statusCode, string code, string? message = null, Exception? inner = null)
        : base(message ?? code, inner)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public static class CustomerPaymentMoney
{
    private static readonly HashSet<string> SupportedCurrencies =
        new(StringComparer.Ordinal) { "ILS", "JOD", "USD" };

    public static long ToMinorUnits(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || !SupportedCurrencies.Contains(currency))
        {
            throw new CustomerPaymentException(409, CustomerPaymentErrorCodes.CurrencyUnsupported);
        }

        if (amount <= 0 || decimal.Round(amount, 2, MidpointRounding.ToEven) != amount)
        {
            throw new CustomerPaymentException(409, CustomerPaymentErrorCodes.AmountInvalid);
        }

        try
        {
            return checked(decimal.ToInt64(amount * 100m));
        }
        catch (OverflowException exception)
        {
            throw new CustomerPaymentException(
                409,
                CustomerPaymentErrorCodes.AmountInvalid,
                inner: exception);
        }
    }
}

public static class CustomerPaymentTransitions
{
    public static PaymentStatus Apply(PaymentStatus current, PaymentEventKind eventKind) =>
        (current, eventKind) switch
        {
            (PaymentStatus.Pending, PaymentEventKind.Succeeded) => PaymentStatus.Completed,
            (PaymentStatus.Failed, PaymentEventKind.Succeeded) => PaymentStatus.Completed,
            (PaymentStatus.Pending, PaymentEventKind.Failed or PaymentEventKind.Canceled) =>
                PaymentStatus.Failed,
            (PaymentStatus.Completed, PaymentEventKind.Refunded) => PaymentStatus.Refunded,
            _ => current
        };
}

public interface ICustomerPaymentService
{
    Task<CustomerPaymentResponse> CreateAsync(
        CreateCustomerPaymentIntentRequest request,
        string idempotencyKey,
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<CustomerPaymentResponse?> GetAsync(
        Guid paymentId,
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<CustomerPaymentResponse?> VerifyAsync(
        Guid paymentId,
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken);
}

public sealed class CustomerPaymentService : ICustomerPaymentService
{
    public const int MaxIdempotencyKeyLength = 128;
    private static readonly TimeSpan InitializationLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitializationLeasePollInterval = TimeSpan.FromMilliseconds(50);
    private const int MaxInitializationLeasePolls = 120;
    private readonly ApplicationDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly IOptionsMonitor<LahzaConfigurationOptions> _options;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public CustomerPaymentService(
        ApplicationDbContext db,
        IPaymentGateway gateway,
        IOptionsMonitor<LahzaConfigurationOptions> options,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _gateway = gateway;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<CustomerPaymentResponse> CreateAsync(
        CreateCustomerPaymentIntentRequest request,
        string idempotencyKey,
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        idempotencyKey ??= string.Empty;
        if (idempotencyKey.Length is < 1 or > MaxIdempotencyKeyLength ||
            idempotencyKey.Any(char.IsControl) ||
            idempotencyKey.Any(char.IsWhiteSpace))
        {
            throw new CustomerPaymentException(400, CustomerPaymentErrorCodes.IdempotencyKeyInvalid);
        }

        if (string.IsNullOrWhiteSpace(request.Method) ||
            (!string.Equals(request.Method, "Card", StringComparison.OrdinalIgnoreCase) &&
             !IsKnownDisabledMethod(request.Method)))
        {
            throw new CustomerPaymentException(400, CustomerPaymentErrorCodes.Invalid);
        }

        if (IsKnownDisabledMethod(request.Method))
        {
            throw new CustomerPaymentException(409, CustomerPaymentErrorCodes.MethodNotYetSupported);
        }

        var requestHash = HashCanonical(
            request.BookingId,
            "Card");
        var existingKey = await _db.Set<CustomerPaymentIdempotencyRecord>()
            .Include(value => value.CustomerPayment)
            .ThenInclude(value => value.CustomerBooking)
            .SingleOrDefaultAsync(
                value => value.UserId == userId &&
                         value.OwnerDeviceId == deviceId &&
                         value.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existingKey is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(existingKey.RequestHash),
                    Convert.FromHexString(requestHash)))
            {
                throw new CustomerPaymentException(409, CustomerPaymentErrorCodes.IdempotencyConflict);
            }

            return await EnsureGatewayInitializationAsync(
                existingKey.CustomerPayment,
                cancellationToken);
        }

        var booking = await _db.CustomerBookings.SingleOrDefaultAsync(
            value => value.PublicReference == request.BookingId &&
                     value.UserId == userId &&
                     value.OwnerDeviceId == deviceId,
            cancellationToken);
        if (booking is null)
        {
            throw new CustomerPaymentException(404, CustomerPaymentErrorCodes.BookingNotFound);
        }

        if (!string.Equals(booking.Status, "Pending", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(booking.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomerPaymentException(409, CustomerPaymentErrorCodes.Ineligible);
        }

        if (booking.IsPaid ||
            string.Equals(booking.PaymentState, "Refunded", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomerPaymentException(409, CustomerPaymentErrorCodes.AlreadyExists);
        }

        var minorAmount = CustomerPaymentMoney.ToMinorUnits(booking.GrandTotal, booking.Currency);
        var existingPayment = await _db.Set<CustomerPayment>()
            .Include(value => value.CustomerBooking)
            .SingleOrDefaultAsync(
                value => value.CustomerBookingId == booking.Id,
                cancellationToken);
        if (existingPayment is not null)
        {
            await AddIdempotencyRecordAsync(
                existingPayment,
                idempotencyKey,
                requestHash,
                userId,
                deviceId,
                cancellationToken);
            return await EnsureGatewayInitializationAsync(existingPayment, cancellationToken);
        }

        EnsureProviderConfigured();

        var paymentId = Guid.NewGuid();
        var payment = new CustomerPayment
        {
            Id = paymentId,
            CustomerBookingId = booking.Id,
            UserId = userId,
            OwnerDeviceId = deviceId,
            Amount = booking.GrandTotal,
            MinorAmount = minorAmount,
            Currency = booking.Currency,
            Method = Models.Enums.PaymentMethod.Card,
            Status = PaymentStatus.Pending,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Provider = PaymentProviders.Lahza,
            ProviderReference = CreateProviderReference(paymentId),
            InitializationState = PaymentInitializationStates.NotStarted,
            CreatedAtUtc = _timeProvider.GetUtcNow(),
            UpdatedAtUtc = _timeProvider.GetUtcNow()
        };
        _db.Add(payment);
        _db.Add(CreateIdempotencyRecord(
            payment,
            idempotencyKey,
            requestHash,
            userId,
            deviceId));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var racedKey = await _db.Set<CustomerPaymentIdempotencyRecord>()
                .Include(value => value.CustomerPayment)
                .ThenInclude(value => value.CustomerBooking)
                .SingleOrDefaultAsync(
                    value => value.UserId == userId &&
                             value.OwnerDeviceId == deviceId &&
                             value.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (racedKey is not null)
            {
                if (racedKey.RequestHash != requestHash)
                {
                    throw new CustomerPaymentException(
                        409,
                        CustomerPaymentErrorCodes.IdempotencyConflict);
                }
                payment = racedKey.CustomerPayment;
            }
            else
            {
                var racedPayment = await _db.Set<CustomerPayment>()
                    .Include(value => value.CustomerBooking)
                    .SingleOrDefaultAsync(
                        value => value.CustomerBookingId == booking.Id &&
                                 value.UserId == userId &&
                                 value.OwnerDeviceId == deviceId,
                        cancellationToken);
                if (racedPayment is null)
                {
                    throw;
                }
                await AddIdempotencyRecordAsync(
                    racedPayment,
                    idempotencyKey,
                    requestHash,
                    userId,
                    deviceId,
                    cancellationToken);
                payment = racedPayment;
            }
        }

        return await EnsureGatewayInitializationAsync(payment, cancellationToken);
    }

    private void EnsureProviderConfigured()
    {
        if (!LahzaConfiguration.IsConfigured(_options.CurrentValue))
        {
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.ProviderUnavailable);
        }
    }

    private static bool IsKnownDisabledMethod(string? method) =>
        string.Equals(method, "Wallet", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "CashOnArrival", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "ThirdParty", StringComparison.OrdinalIgnoreCase);

    public async Task<CustomerPaymentResponse?> GetAsync(
        Guid paymentId,
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var payment = await _db.Set<CustomerPayment>().AsNoTracking()
            .Include(value => value.CustomerBooking)
            .SingleOrDefaultAsync(
                value => value.Id == paymentId &&
                         value.UserId == userId &&
                         value.OwnerDeviceId == deviceId,
                cancellationToken);
        return payment is null ? null : Map(payment);
    }

    public async Task<CustomerPaymentResponse?> VerifyAsync(
        Guid paymentId,
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var payment = await _db.Set<CustomerPayment>()
            .Include(value => value.CustomerBooking)
            .SingleOrDefaultAsync(
                value => value.Id == paymentId &&
                         value.UserId == userId &&
                         value.OwnerDeviceId == deviceId,
                cancellationToken);
        if (payment is null)
        {
            return null;
        }
        if (!string.Equals(
                payment.Provider,
                PaymentProviders.Lahza,
                StringComparison.Ordinal))
        {
            throw new CustomerPaymentException(
                409,
                CustomerPaymentErrorCodes.MethodUnavailable);
        }

        EnsureProviderConfigured();
        var result = await _gateway.VerifyAsync(
            payment.ProviderReference!,
            cancellationToken);
        if (!string.Equals(
                result.ProviderReference,
                payment.ProviderReference,
                StringComparison.Ordinal) ||
            result.Amount != payment.MinorAmount ||
            !string.Equals(
                result.Currency,
                payment.Currency,
                StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(payment.ProviderTransactionId) &&
             !string.IsNullOrWhiteSpace(result.ProviderTransactionId) &&
             !string.Equals(
                 payment.ProviderTransactionId,
                 result.ProviderTransactionId,
                 StringComparison.Ordinal)))
        {
            throw new CustomerPaymentException(
                502,
                CustomerPaymentErrorCodes.GatewayAmbiguous);
        }

        payment.ProviderTransactionId ??= result.ProviderTransactionId;
        payment.ProviderStatus = result.Status;
        var kind = result.Status.ToLowerInvariant() switch
        {
            "success" => PaymentEventKind.Succeeded,
            "failed" or "abandoned" => PaymentEventKind.Failed,
            _ => PaymentEventKind.Ignored
        };
        var next = CustomerPaymentTransitions.Apply(payment.Status, kind);
        if (next != payment.Status)
        {
            payment.Status = next;
            payment.CustomerBooking.IsPaid = next == PaymentStatus.Completed;
            payment.CustomerBooking.PaymentState = next.ToString();
        }
        payment.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return Map(payment);
    }

    private async Task<CustomerPaymentResponse> EnsureGatewayInitializationAsync(
        CustomerPayment payment,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxInitializationLeasePolls; attempt++)
        {
            var persisted = await _db.Set<CustomerPayment>().AsNoTracking()
                .Include(value => value.CustomerBooking)
                .SingleAsync(value => value.Id == payment.Id, cancellationToken);
            if (!string.Equals(
                    persisted.Provider,
                    PaymentProviders.Lahza,
                    StringComparison.Ordinal))
            {
                return Map(persisted);
            }
            if (persisted.InitializationState == PaymentInitializationStates.Ambiguous)
            {
                throw new CustomerPaymentException(
                    503,
                    CustomerPaymentErrorCodes.GatewayAmbiguous);
            }
            if (!string.IsNullOrWhiteSpace(persisted.CheckoutUrl))
            {
                return Map(persisted);
            }

            EnsureProviderConfigured();

            var ownerToken = Guid.NewGuid();
            if (await TryAcquireInitializationLeaseAsync(
                    payment.Id,
                    ownerToken,
                    cancellationToken))
            {
                return await InitializeGatewayAsLeaseOwnerAsync(
                    persisted,
                    ownerToken,
                    cancellationToken);
            }

            await Task.Delay(InitializationLeasePollInterval, cancellationToken);
        }

        throw new CustomerPaymentException(
            503,
            CustomerPaymentErrorCodes.GatewayAmbiguous);
    }

    private async Task<CustomerPaymentResponse> InitializeGatewayAsLeaseOwnerAsync(
        CustomerPayment payment,
        Guid ownerToken,
        CancellationToken cancellationToken)
    {
        PaymentInitializationResult result;
        try
        {
            var customerEmail = await _db.Users
                .Where(value => value.Id == payment.UserId)
                .Select(value => value.Email)
                .SingleOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(customerEmail))
            {
                await ReleaseInitializationLeaseSafelyAsync(payment.Id, ownerToken);
                throw new CustomerPaymentException(
                    409,
                    CustomerPaymentErrorCodes.Ineligible);
            }

            result = await _gateway.InitializeAsync(
                new PaymentInitializationCommand(
                    payment.Id,
                    payment.CustomerBookingId,
                    payment.CustomerBooking.PublicReference,
                    payment.MinorAmount,
                    payment.Currency,
                    customerEmail,
                    payment.ProviderReference!),
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ReleaseInitializationLeaseSafelyAsync(payment.Id, ownerToken);
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }
        catch (OperationCanceledException)
        {
            await ReleaseInitializationLeaseSafelyAsync(payment.Id, ownerToken);
            throw;
        }
        catch (CustomerPaymentException exception)
        {
            if (exception.Code == CustomerPaymentErrorCodes.GatewayAmbiguous)
            {
                await MarkInitializationAmbiguousAsync(payment.Id, ownerToken);
            }
            else
            {
                await ReleaseInitializationLeaseSafelyAsync(payment.Id, ownerToken);
            }
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or TimeoutException)
        {
            await MarkInitializationAmbiguousAsync(payment.Id, ownerToken);
            _logger.LogWarning(
                $"Lahza transaction initialization outcome is ambiguous for server payment {payment.Id:D}: {exception.GetType().Name}.");
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }

        if (result.Amount != payment.MinorAmount ||
            !string.Equals(result.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                result.ProviderReference,
                payment.ProviderReference,
                StringComparison.Ordinal))
        {
            await ReleaseInitializationLeaseSafelyAsync(payment.Id, ownerToken);
            throw new CustomerPaymentException(502, CustomerPaymentErrorCodes.GatewayAmbiguous);
        }

        try
        {
            var completed = await CompleteInitializationLeaseAsync(
                payment.Id,
                ownerToken,
                result,
                cancellationToken);
            if (completed)
            {
                return Map(await LoadPaymentAsync(payment.Id, cancellationToken));
            }

            return await WaitForCompletedInitializationAsync(payment.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            // A commit acknowledgement can be lost after SQL has durably committed.
            _db.ChangeTracker.Clear();
            var verified = await LoadPaymentAsync(payment.Id, cancellationToken);
            if (verified is not null)
            {
                if (!string.IsNullOrWhiteSpace(verified.CheckoutUrl))
                {
                    return Map(verified);
                }
            }

            await ReleaseInitializationLeaseSafelyAsync(payment.Id, ownerToken);
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }

    }

    private async Task<bool> TryAcquireInitializationLeaseAsync(
        Guid paymentId,
        Guid ownerToken,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var expires = now.Add(InitializationLeaseDuration);
        if (_db.Database.IsRelational())
        {
            return await _db.Set<CustomerPayment>()
                .Where(value =>
                    value.Id == paymentId &&
                    value.CheckoutUrl == null &&
                    value.InitializationState == PaymentInitializationStates.NotStarted &&
                    (value.InitializationLeaseOwnerToken == null ||
                     value.InitializationLeaseExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.InitializationLeaseOwnerToken, ownerToken)
                    .SetProperty(value => value.InitializationLeaseExpiresAtUtc, expires)
                    .SetProperty(value => value.UpdatedAtUtc, now),
                    cancellationToken) == 1;
        }

        var payment = await _db.Set<CustomerPayment>()
            .SingleAsync(value => value.Id == paymentId, cancellationToken);
        if (payment.CheckoutUrl is not null ||
            payment.InitializationState != PaymentInitializationStates.NotStarted ||
            (payment.InitializationLeaseOwnerToken is not null &&
             payment.InitializationLeaseExpiresAtUtc > now))
        {
            return false;
        }
        payment.InitializationLeaseOwnerToken = ownerToken;
        payment.InitializationLeaseExpiresAtUtc = expires;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            var persistedOwner = await _db.Set<CustomerPayment>()
                .AsNoTracking()
                .Where(value => value.Id == paymentId)
                .Select(value => value.InitializationLeaseOwnerToken)
                .SingleAsync(cancellationToken);
            if (persistedOwner == ownerToken)
            {
                return true;
            }
            throw;
        }
        return true;
    }

    private async Task<bool> CompleteInitializationLeaseAsync(
        Guid paymentId,
        Guid ownerToken,
        PaymentInitializationResult result,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_db.Database.IsRelational())
        {
            return await _db.Set<CustomerPayment>()
                .Where(value =>
                    value.Id == paymentId &&
                    value.CheckoutUrl == null &&
                    value.InitializationLeaseOwnerToken == ownerToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.ProviderStatus, result.Status)
                    .SetProperty(value => value.CheckoutUrl, result.CheckoutUrl.ToString())
                    .SetProperty(
                        value => value.InitializationState,
                        PaymentInitializationStates.Initialized)
                    .SetProperty(value => value.InitializationLeaseOwnerToken, (Guid?)null)
                    .SetProperty(value => value.InitializationLeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(value => value.UpdatedAtUtc, now),
                    cancellationToken) == 1;
        }

        var payment = await _db.Set<CustomerPayment>()
            .SingleAsync(value => value.Id == paymentId, cancellationToken);
        if (payment.InitializationLeaseOwnerToken != ownerToken ||
            payment.CheckoutUrl is not null)
        {
            return false;
        }
        payment.ProviderStatus = result.Status;
        payment.CheckoutUrl = result.CheckoutUrl.ToString();
        payment.InitializationState = PaymentInitializationStates.Initialized;
        payment.InitializationLeaseOwnerToken = null;
        payment.InitializationLeaseExpiresAtUtc = null;
        payment.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ReleaseInitializationLeaseSafelyAsync(
        Guid paymentId,
        Guid ownerToken)
    {
        try
        {
            if (_db.Database.IsRelational())
            {
                await _db.Set<CustomerPayment>()
                    .Where(value =>
                        value.Id == paymentId &&
                        value.CheckoutUrl == null &&
                        value.InitializationLeaseOwnerToken == ownerToken)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.InitializationLeaseOwnerToken, (Guid?)null)
                        .SetProperty(value => value.InitializationLeaseExpiresAtUtc, (DateTimeOffset?)null),
                        CancellationToken.None);
                return;
            }

            var payment = await _db.Set<CustomerPayment>()
                .SingleOrDefaultAsync(value => value.Id == paymentId);
            if (payment?.InitializationLeaseOwnerToken == ownerToken &&
                payment.CheckoutUrl is null)
            {
                payment.InitializationLeaseOwnerToken = null;
                payment.InitializationLeaseExpiresAtUtc = null;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                $"Could not release payment initialization lease for server payment {paymentId:D}: {exception.GetType().Name}.");
        }
    }

    private async Task MarkInitializationAmbiguousAsync(
        Guid paymentId,
        Guid ownerToken)
    {
        try
        {
            if (_db.Database.IsRelational())
            {
                await _db.Set<CustomerPayment>()
                    .Where(value =>
                        value.Id == paymentId &&
                        value.CheckoutUrl == null &&
                        value.InitializationLeaseOwnerToken == ownerToken)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            value => value.InitializationState,
                            PaymentInitializationStates.Ambiguous)
                        .SetProperty(
                            value => value.ProviderStatus,
                            "initialization_ambiguous")
                        .SetProperty(
                            value => value.InitializationLeaseOwnerToken,
                            (Guid?)null)
                        .SetProperty(
                            value => value.InitializationLeaseExpiresAtUtc,
                            (DateTimeOffset?)null),
                        CancellationToken.None);
                return;
            }

            var payment = await _db.Set<CustomerPayment>()
                .SingleOrDefaultAsync(value => value.Id == paymentId);
            if (payment?.InitializationLeaseOwnerToken == ownerToken &&
                payment.CheckoutUrl is null)
            {
                payment.InitializationState = PaymentInitializationStates.Ambiguous;
                payment.ProviderStatus = "initialization_ambiguous";
                payment.InitializationLeaseOwnerToken = null;
                payment.InitializationLeaseExpiresAtUtc = null;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                $"Could not mark ambiguous initialization for server payment {paymentId:D}: {exception.GetType().Name}.");
        }
    }

    private async Task<CustomerPaymentResponse> WaitForCompletedInitializationAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxInitializationLeasePolls; attempt++)
        {
            var payment = await LoadPaymentAsync(paymentId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payment.CheckoutUrl))
            {
                return Map(payment);
            }
            await Task.Delay(InitializationLeasePollInterval, cancellationToken);
        }
        throw new CustomerPaymentException(503, CustomerPaymentErrorCodes.GatewayAmbiguous);
    }

    private async Task<CustomerPayment> LoadPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken) =>
        await _db.Set<CustomerPayment>().AsNoTracking()
            .Include(value => value.CustomerBooking)
            .SingleAsync(value => value.Id == paymentId, cancellationToken);

    private static string HashCanonical(Guid bookingId, string method)
    {
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{bookingId:D}\n{method}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task AddIdempotencyRecordAsync(
        CustomerPayment payment,
        string key,
        string requestHash,
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        _db.Add(CreateIdempotencyRecord(payment, key, requestHash, userId, deviceId));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var existing = await _db.Set<CustomerPaymentIdempotencyRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.UserId == userId &&
                             value.OwnerDeviceId == deviceId &&
                             value.IdempotencyKey == key,
                    cancellationToken);
            if (existing?.RequestHash != requestHash ||
                existing.CustomerPaymentId != payment.Id)
            {
                throw new CustomerPaymentException(
                    409,
                    CustomerPaymentErrorCodes.IdempotencyConflict);
            }
        }
    }

    private CustomerPaymentIdempotencyRecord CreateIdempotencyRecord(
        CustomerPayment payment,
        string key,
        string requestHash,
        Guid userId,
        Guid deviceId) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerPayment = payment,
            CustomerPaymentId = payment.Id,
            UserId = userId,
            OwnerDeviceId = deviceId,
            IdempotencyKey = key,
            RequestHash = requestHash,
            CreatedAtUtc = _timeProvider.GetUtcNow()
        };

    internal static CustomerPaymentResponse Map(CustomerPayment payment) =>
        new()
        {
            Id = payment.Id,
            BookingId = payment.CustomerBooking.PublicReference,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Method = payment.Method == Models.Enums.PaymentMethod.Card
                ? "Card"
                : payment.Method.ToString(),
            Status = payment.Status.ToString(),
            Provider = payment.Provider,
            ProviderStatus = payment.ProviderStatus,
            ProviderReference = payment.ProviderReference,
            CheckoutUrl = payment.Status == PaymentStatus.Pending
                ? payment.CheckoutUrl
                : null,
            CreatedAtUtc = payment.CreatedAtUtc
        };

    internal static string CreateProviderReference(Guid paymentId) =>
        $"GHSEELI-{paymentId:N}".ToUpperInvariant();
}
