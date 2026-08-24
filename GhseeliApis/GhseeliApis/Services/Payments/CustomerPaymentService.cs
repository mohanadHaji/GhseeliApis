using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;

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
    public const string SignatureMissing = "stripe_signature_missing";
    public const string SignatureInvalid = "stripe_signature_invalid";
    public const string EventInvalid = "stripe_event_invalid";
    public const string WebhookConflict = "stripe_webhook_conflict";
    public const string WebhookTooLarge = "stripe_webhook_too_large";
    public const string WebhookConfiguration = "stripe_webhook_configuration_invalid";
    public const string PaymentUnsupportedMediaType = "payment_unsupported_media_type";
    public const string WebhookUnsupportedMediaType = "stripe_webhook_unsupported_media_type";
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
        new(StringComparer.Ordinal) { "ILS", "USD", "EUR" };

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

public enum StripePaymentEventKind
{
    Succeeded,
    Failed,
    Canceled,
    Refunded,
    Ignored
}

public static class CustomerPaymentTransitions
{
    public static PaymentStatus Apply(PaymentStatus current, StripePaymentEventKind eventKind) =>
        (current, eventKind) switch
        {
            (PaymentStatus.Pending, StripePaymentEventKind.Succeeded) => PaymentStatus.Completed,
            (PaymentStatus.Failed, StripePaymentEventKind.Succeeded) => PaymentStatus.Completed,
            (PaymentStatus.Pending, StripePaymentEventKind.Failed or StripePaymentEventKind.Canceled) =>
                PaymentStatus.Failed,
            (PaymentStatus.Completed, StripePaymentEventKind.Refunded) => PaymentStatus.Refunded,
            _ => current
        };
}

public sealed record StripeIntentCreateCommand(
    Guid PaymentId,
    Guid BookingId,
    Guid BookingReference,
    long Amount,
    string Currency,
    string IdempotencyKey);

public sealed record StripeIntentResult(
    string PaymentIntentId,
    string Status,
    string? ClientSecret,
    string? ChargeId,
    long Amount,
    string Currency);

public interface IStripePaymentIntentGateway
{
    Task<StripeIntentResult> CreateAsync(
        StripeIntentCreateCommand command,
        CancellationToken cancellationToken);
}

public sealed class StripePaymentIntentGateway : IStripePaymentIntentGateway
{
    private readonly IOptionsMonitor<StripeConfigurationOptions> _options;

    public StripePaymentIntentGateway(IOptionsMonitor<StripeConfigurationOptions> options)
    {
        _options = options;
    }

    public async Task<StripeIntentResult> CreateAsync(
        StripeIntentCreateCommand command,
        CancellationToken cancellationToken)
    {
        var client = new StripeClient(_options.CurrentValue.SecretKey.Trim());
        var service = new PaymentIntentService(client);
        var intent = await service.CreateAsync(
            BuildCreateOptions(command),
            new RequestOptions { IdempotencyKey = command.IdempotencyKey },
            cancellationToken);

        return new StripeIntentResult(
            intent.Id,
            intent.Status,
            intent.ClientSecret,
            intent.LatestChargeId,
            intent.Amount,
            intent.Currency);
    }

    public static PaymentIntentCreateOptions BuildCreateOptions(
        StripeIntentCreateCommand command) =>
        new()
        {
            Amount = command.Amount,
            Currency = command.Currency.ToLowerInvariant(),
            Confirm = false,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                AllowRedirects = "never"
            },
            Metadata = new Dictionary<string, string>
            {
                ["payment_id"] = command.PaymentId.ToString("D"),
                ["booking_id"] = command.BookingId.ToString("D"),
                ["booking_reference"] = command.BookingReference.ToString("D")
            }
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
}

public sealed class CustomerPaymentService : ICustomerPaymentService
{
    public const int MaxIdempotencyKeyLength = 128;
    private static readonly TimeSpan IntentLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IntentLeasePollInterval = TimeSpan.FromMilliseconds(50);
    private const int MaxIntentLeasePolls = 120;
    private readonly ApplicationDbContext _db;
    private readonly IStripePaymentIntentGateway _gateway;
    private readonly IOptionsMonitor<StripeConfigurationOptions> _options;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public CustomerPaymentService(
        ApplicationDbContext db,
        IStripePaymentIntentGateway gateway,
        IOptionsMonitor<StripeConfigurationOptions> options,
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

            return await EnsureGatewayIntentAsync(
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
            return await EnsureGatewayIntentAsync(existingPayment, cancellationToken);
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
            StripeIdempotencyKey = $"ghseeli-payment-{paymentId:N}",
            ProviderPublishableKey = _options.CurrentValue.PublishableKey.Trim(),
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

        return await EnsureGatewayIntentAsync(payment, cancellationToken);
    }

    private void EnsureProviderConfigured()
    {
        if (!CheckoutPaymentCapabilitiesService.IsStripeConfigured(_options.CurrentValue))
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

    private async Task<CustomerPaymentResponse> EnsureGatewayIntentAsync(
        CustomerPayment payment,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxIntentLeasePolls; attempt++)
        {
            var persisted = await _db.Set<CustomerPayment>().AsNoTracking()
                .Include(value => value.CustomerBooking)
                .SingleAsync(value => value.Id == payment.Id, cancellationToken);
            if (!string.IsNullOrWhiteSpace(persisted.PaymentIntentId))
            {
                return Map(persisted);
            }

            EnsureProviderConfigured();

            var ownerToken = Guid.NewGuid();
            if (await TryAcquireIntentLeaseAsync(
                    payment.Id,
                    ownerToken,
                    cancellationToken))
            {
                return await CreateGatewayIntentAsLeaseOwnerAsync(
                    persisted,
                    ownerToken,
                    cancellationToken);
            }

            await Task.Delay(IntentLeasePollInterval, cancellationToken);
        }

        throw new CustomerPaymentException(
            503,
            CustomerPaymentErrorCodes.GatewayAmbiguous);
    }

    private async Task<CustomerPaymentResponse> CreateGatewayIntentAsLeaseOwnerAsync(
        CustomerPayment payment,
        Guid ownerToken,
        CancellationToken cancellationToken)
    {
        StripeIntentResult result;
        try
        {
            result = await _gateway.CreateAsync(
                new StripeIntentCreateCommand(
                    payment.Id,
                    payment.CustomerBookingId,
                    payment.CustomerBooking.PublicReference,
                    payment.MinorAmount,
                    payment.Currency,
                    payment.StripeIdempotencyKey),
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ReleaseIntentLeaseSafelyAsync(payment.Id, ownerToken);
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }
        catch (OperationCanceledException)
        {
            await ReleaseIntentLeaseSafelyAsync(payment.Id, ownerToken);
            throw;
        }
        catch (StripeException exception)
        {
            await ReleaseIntentLeaseSafelyAsync(payment.Id, ownerToken);
            _logger.LogWarning(
                $"Stripe intent creation was not confirmed for server payment {payment.Id:D}: {exception.GetType().Name}.");
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or TimeoutException)
        {
            await ReleaseIntentLeaseSafelyAsync(payment.Id, ownerToken);
            _logger.LogWarning(
                $"Stripe intent creation outcome is ambiguous for server payment {payment.Id:D}: {exception.GetType().Name}.");
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }

        if (result.Amount != payment.MinorAmount ||
            !string.Equals(result.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            await ReleaseIntentLeaseSafelyAsync(payment.Id, ownerToken);
            throw new CustomerPaymentException(502, CustomerPaymentErrorCodes.GatewayAmbiguous);
        }

        try
        {
            var completed = await CompleteIntentLeaseAsync(
                payment.Id,
                ownerToken,
                result,
                cancellationToken);
            if (completed)
            {
                return Map(await LoadPaymentAsync(payment.Id, cancellationToken));
            }

            return await WaitForCompletedIntentAsync(payment.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            // A commit acknowledgement can be lost after SQL has durably committed.
            _db.ChangeTracker.Clear();
            var verified = await LoadPaymentAsync(payment.Id, cancellationToken);
            if (verified is not null)
            {
                if (!string.IsNullOrWhiteSpace(verified.PaymentIntentId))
                {
                    return Map(verified);
                }
            }

            await ReleaseIntentLeaseSafelyAsync(payment.Id, ownerToken);
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }

    }

    private async Task<bool> TryAcquireIntentLeaseAsync(
        Guid paymentId,
        Guid ownerToken,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var expires = now.Add(IntentLeaseDuration);
        if (_db.Database.IsRelational())
        {
            return await _db.Set<CustomerPayment>()
                .Where(value =>
                    value.Id == paymentId &&
                    value.PaymentIntentId == null &&
                    (value.IntentLeaseOwnerToken == null ||
                     value.IntentLeaseExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.IntentLeaseOwnerToken, ownerToken)
                    .SetProperty(value => value.IntentLeaseExpiresAtUtc, expires)
                    .SetProperty(value => value.UpdatedAtUtc, now),
                    cancellationToken) == 1;
        }

        var payment = await _db.Set<CustomerPayment>()
            .SingleAsync(value => value.Id == paymentId, cancellationToken);
        if (payment.PaymentIntentId is not null ||
            (payment.IntentLeaseOwnerToken is not null &&
             payment.IntentLeaseExpiresAtUtc > now))
        {
            return false;
        }
        payment.IntentLeaseOwnerToken = ownerToken;
        payment.IntentLeaseExpiresAtUtc = expires;
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
                .Select(value => value.IntentLeaseOwnerToken)
                .SingleAsync(cancellationToken);
            if (persistedOwner == ownerToken)
            {
                return true;
            }
            throw;
        }
        return true;
    }

    private async Task<bool> CompleteIntentLeaseAsync(
        Guid paymentId,
        Guid ownerToken,
        StripeIntentResult result,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_db.Database.IsRelational())
        {
            return await _db.Set<CustomerPayment>()
                .Where(value =>
                    value.Id == paymentId &&
                    value.PaymentIntentId == null &&
                    value.IntentLeaseOwnerToken == ownerToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.PaymentIntentId, result.PaymentIntentId)
                    .SetProperty(value => value.ProviderStatus, result.Status)
                    .SetProperty(value => value.ClientSecret, result.ClientSecret)
                    .SetProperty(value => value.ChargeId, result.ChargeId)
                    .SetProperty(value => value.IntentLeaseOwnerToken, (Guid?)null)
                    .SetProperty(value => value.IntentLeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(value => value.UpdatedAtUtc, now),
                    cancellationToken) == 1;
        }

        var payment = await _db.Set<CustomerPayment>()
            .SingleAsync(value => value.Id == paymentId, cancellationToken);
        if (payment.IntentLeaseOwnerToken != ownerToken ||
            payment.PaymentIntentId is not null)
        {
            return false;
        }
        payment.PaymentIntentId = result.PaymentIntentId;
        payment.ProviderStatus = result.Status;
        payment.ClientSecret = result.ClientSecret;
        payment.ChargeId = result.ChargeId;
        payment.IntentLeaseOwnerToken = null;
        payment.IntentLeaseExpiresAtUtc = null;
        payment.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ReleaseIntentLeaseSafelyAsync(Guid paymentId, Guid ownerToken)
    {
        try
        {
            if (_db.Database.IsRelational())
            {
                await _db.Set<CustomerPayment>()
                    .Where(value =>
                        value.Id == paymentId &&
                        value.PaymentIntentId == null &&
                        value.IntentLeaseOwnerToken == ownerToken)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.IntentLeaseOwnerToken, (Guid?)null)
                        .SetProperty(value => value.IntentLeaseExpiresAtUtc, (DateTimeOffset?)null),
                        CancellationToken.None);
                return;
            }

            var payment = await _db.Set<CustomerPayment>()
                .SingleOrDefaultAsync(value => value.Id == paymentId);
            if (payment?.IntentLeaseOwnerToken == ownerToken &&
                payment.PaymentIntentId is null)
            {
                payment.IntentLeaseOwnerToken = null;
                payment.IntentLeaseExpiresAtUtc = null;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                $"Could not release payment intent lease for server payment {paymentId:D}: {exception.GetType().Name}.");
        }
    }

    private async Task<CustomerPaymentResponse> WaitForCompletedIntentAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxIntentLeasePolls; attempt++)
        {
            var payment = await LoadPaymentAsync(paymentId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payment.PaymentIntentId))
            {
                return Map(payment);
            }
            await Task.Delay(IntentLeasePollInterval, cancellationToken);
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
            ProviderStatus = payment.ProviderStatus,
            ClientSecret = payment.Status == PaymentStatus.Pending
                ? payment.ClientSecret
                : null,
            PublishableKey = payment.Status == PaymentStatus.Pending
                ? payment.ProviderPublishableKey
                : null,
            CreatedAtUtc = payment.CreatedAtUtc
        };
}
