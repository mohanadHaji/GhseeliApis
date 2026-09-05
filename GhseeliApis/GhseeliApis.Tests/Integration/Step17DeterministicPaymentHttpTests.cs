using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Payments;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Supplies deterministic HTTP and SQL evidence for Step 17 payment failure paths.
/// </summary>
public sealed class Step17DeterministicPaymentHttpTests
{
    private const string JwtSecret = "CheckoutDraftApiTestsSecret_Minimum32Chars";

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PAY-043")]
    public async Task STEP17_DET_PAY_043_UnconfiguredProviderFailsClosed()
    {
        var gateway = new ScriptedGateway(_ =>
            throw new InvalidOperationException("The gateway must not be called."));
        var fixture = CreateCustomerFixture();
        await using var factory = CreatePaymentFactory(fixture, gateway, lahzaConfigured: false);
        var booking = await SeedBookingAsync(factory, fixture);
        using var client = factory.CreateApiClient();

        using var response = await SendPaymentAsync(
            client,
            fixture,
            booking,
            "step17-pay-043");
        var body = await AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            CustomerPaymentErrorCodes.ProviderUnavailable);

        gateway.Commands.Should().BeEmpty();
        body.Should().NotContain("sk_test_");
        await using var verify = CreateContext(factory);
        (await verify.CustomerPayments.CountAsync()).Should().Be(0);
        (await verify.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(0);
        (await verify.CustomerBookings.SingleAsync()).IsPaid.Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PAY-044")]
    public async Task STEP17_DET_PAY_044_GatewayTimeoutIsRetrySafeAmbiguous()
    {
        var gateway = new ScriptedGateway(_ => throw new TimeoutException(
            "step17-provider-timeout-must-not-leak"));
        var fixture = CreateCustomerFixture();
        await using var factory = CreatePaymentFactory(fixture, gateway);
        var booking = await SeedBookingAsync(factory, fixture);
        using var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await SendPaymentAsync(
                client,
                fixture,
                booking,
                "step17-pay-044");
            var body = await AssertProblemAsync(
                response,
                HttpStatusCode.ServiceUnavailable,
                CustomerPaymentErrorCodes.GatewayAmbiguous);
            body.Should().NotContain("step17-provider-timeout-must-not-leak");
        }

        gateway.Commands.Should().ContainSingle();
        gateway.Commands.Select(command => command.PaymentId).Distinct()
            .Should().ContainSingle();
        gateway.Commands.Select(command => command.ProviderReference).Distinct()
            .Should().ContainSingle();
        await using var verify = CreateContext(factory);
        var payment = await verify.CustomerPayments.AsNoTracking().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.InitializationState.Should().Be(PaymentInitializationStates.Ambiguous);
        payment.ProviderReference.Should().StartWith("GHSEELI-");
        payment.CheckoutUrl.Should().BeNull();
        payment.InitializationLeaseOwnerToken.Should().BeNull();
        payment.InitializationLeaseExpiresAtUtc.Should().BeNull();
        (await verify.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(1);
        var persistedBooking = await verify.CustomerBookings.AsNoTracking().SingleAsync();
        persistedBooking.IsPaid.Should().BeFalse();
        persistedBooking.PaymentState.Should().Be("Unpaid");
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PAY-045")]
    public async Task STEP17_DET_PAY_045_GatewayMoneyMismatchReturnsBadGateway()
    {
        const string providerReference = "GHSEELI-STEP17-MISMATCH";
        var gateway = new ScriptedGateway(command => new PaymentInitializationResult(
            command.ProviderReference,
            "initialized",
            new Uri($"https://checkout.lahza.test/pay/{providerReference}"),
            command.Amount + 1,
            "USD"));
        var fixture = CreateCustomerFixture();
        await using var factory = CreatePaymentFactory(fixture, gateway);
        var booking = await SeedBookingAsync(factory, fixture);
        using var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await SendPaymentAsync(
                client,
                fixture,
                booking,
                "step17-pay-045");
            var body = await AssertProblemAsync(
                response,
                HttpStatusCode.BadGateway,
                CustomerPaymentErrorCodes.GatewayAmbiguous);
            body.Should().NotContain(providerReference)
                .And.NotContain("access-mismatch");
        }

        gateway.Commands.Should().HaveCount(2);
        gateway.Commands[1].Should().Be(gateway.Commands[0]);
        await using var verify = CreateContext(factory);
        var payment = await verify.CustomerPayments.AsNoTracking().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.ProviderReference.Should().StartWith("GHSEELI-");
        payment.CheckoutUrl.Should().BeNull();
        payment.InitializationLeaseOwnerToken.Should().BeNull();
        payment.InitializationLeaseExpiresAtUtc.Should().BeNull();
        (await verify.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(1);
        (await verify.CustomerBookings.AsNoTracking().SingleAsync()).IsPaid.Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PAY-046")]
    public async Task STEP17_DET_PAY_046_AmountMismatchIsDurableIgnoredAcknowledgement()
    {
        await AssertDurableMismatchAsync(
            "evt_step17_pay_046",
            payment => Event(payment, "evt_step17_pay_046") with
            {
                Amount = payment.MinorAmount + 1
            },
            "amount_mismatch");
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PAY-047")]
    public async Task STEP17_DET_PAY_047_CurrencyMismatchIsDurableIgnoredAcknowledgement()
    {
        await AssertDurableMismatchAsync(
            "evt_step17_pay_047",
            payment => Event(payment, "evt_step17_pay_047") with
            {
                Currency = "USD"
            },
            "currency_mismatch");
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PAY-048")]
    public async Task STEP17_DET_PAY_048_FailureAfterCompletionIsRecognizedNoOp()
    {
        await using var factory = CreateWebhookFactory();
        var payment = await SeedPaymentAsync(factory, PaymentStatus.Completed, paid: true);
        var paymentEvent = Event(
            payment,
            "evt_step17_pay_048",
            PaymentEventKind.RefundFailed,
            "refund.failed");
        using var client = factory.CreateApiClient();

        using var response = await SendWebhookAsync(
            client,
            BuildLahzaEvent(paymentEvent));

        await AssertAcknowledgementAsync(response);
        await using var verify = CreateContext(factory);
        var persisted = await verify.CustomerPayments
            .AsNoTracking()
            .Include(value => value.CustomerBooking)
            .SingleAsync();
        persisted.Status.Should().Be(PaymentStatus.Completed);
        persisted.CustomerBooking.IsPaid.Should().BeTrue();
        persisted.CustomerBooking.PaymentState.Should().Be("Completed");
        var receipt = await verify.PaymentWebhookEvents.AsNoTracking().SingleAsync();
        receipt.EventId.Should().Be("evt_step17_pay_048");
        receipt.EventType.Should().Be("refund.failed");
        receipt.State.Should().Be("Completed");
        receipt.DispositionReason.Should().Be("refund_failed");
    }

    private static async Task AssertDurableMismatchAsync(
        string eventId,
        Func<CustomerPayment, VerifiedPaymentEvent> createEvent,
        string expectedDisposition)
    {
        await using var factory = CreateWebhookFactory();
        var payment = await SeedPaymentAsync(factory, PaymentStatus.Pending, paid: false);
        var paymentEvent = createEvent(payment);
        using var client = factory.CreateApiClient();

        using var response = await SendWebhookAsync(
            client,
            BuildLahzaEvent(paymentEvent));

        await AssertAcknowledgementAsync(response);
        await using var verify = CreateContext(factory);
        var persisted = await verify.CustomerPayments
            .AsNoTracking()
            .Include(value => value.CustomerBooking)
            .SingleAsync();
        persisted.Status.Should().Be(PaymentStatus.Pending);
        persisted.CustomerBooking.IsPaid.Should().BeFalse();
        persisted.CustomerBooking.PaymentState.Should().Be("Unpaid");
        var receipt = await verify.PaymentWebhookEvents.AsNoTracking().SingleAsync();
        receipt.EventId.Should().Be(eventId);
        receipt.State.Should().Be("Quarantined");
        receipt.DispositionReason.Should().Be(expectedDisposition);
    }

    private static CheckoutDraftApiFactory CreatePaymentFactory(
        CustomerFixture fixture,
        IPaymentGateway gateway,
        bool lahzaConfigured = true) =>
        new(
            devices: [fixture.Device],
            settings: new Dictionary<string, string?>
            {
                ["Lahza:SecretKey"] = lahzaConfigured ? "sk_test_step17_local" : string.Empty
            },
            configureTestServices: services =>
            {
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton(gateway);
            });

    private static CheckoutDraftApiFactory CreateWebhookFactory() =>
        new(
            settings: new Dictionary<string, string?>
            {
                ["Lahza:SecretKey"] = "sk_test_step17_webhook"
            });

    private static async Task<BookingFixture> SeedBookingAsync(
        CheckoutDraftApiFactory factory,
        CustomerFixture fixture)
    {
        var booking = CreateBooking(fixture.UserId, fixture.Device.Id);
        await using var context = CreateContext(factory);
        context.Users.Add(CreateUser(fixture.UserId));
        context.CustomerBookings.Add(booking);
        await context.SaveChangesAsync();
        return new BookingFixture(booking.Id, booking.PublicReference);
    }

    private static async Task<CustomerPayment> SeedPaymentAsync(
        CheckoutDraftApiFactory factory,
        PaymentStatus status,
        bool paid)
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var booking = CreateBooking(userId, deviceId);
        booking.Status = status == PaymentStatus.Completed ? "Completed" : "Confirmed";
        booking.IsPaid = paid;
        booking.PaymentState = paid ? "Completed" : "Unpaid";
        var payment = new CustomerPayment
        {
            Id = Guid.NewGuid(),
            CustomerBooking = booking,
            CustomerBookingId = booking.Id,
            UserId = userId,
            OwnerDeviceId = deviceId,
            Amount = booking.GrandTotal,
            MinorAmount = 1000,
            Currency = "ILS",
            Method = PaymentMethod.Card,
            Status = status,
            IdempotencyKey = $"step17-{Guid.NewGuid():N}",
            RequestHash = new string('A', 64),
            Provider = PaymentProviders.Lahza,
            ProviderReference = $"GHSEELI-{Guid.NewGuid():N}".ToUpperInvariant(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await using var context = CreateContext(factory);
        context.Users.Add(CreateUser(userId));
        context.CustomerPayments.Add(payment);
        await context.SaveChangesAsync();
        return payment;
    }

    private static CustomerBooking CreateBooking(Guid userId, Guid deviceId) =>
        new()
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            UserId = userId,
            OwnerDeviceId = deviceId,
            BusinessReservationId = Guid.NewGuid(),
            BusinessWorkOrderId = Guid.NewGuid(),
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = Guid.NewGuid(),
            CatalogVersion = 17,
            ConfirmedDraftVersion = 1,
            Status = "Confirmed",
            StatusChangedAtUtc = DateTimeOffset.UtcNow,
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(1),
            RequestedSlotEndUtc = DateTimeOffset.UtcNow.AddHours(2),
            ProviderNameAr = "مزود",
            BranchNameAr = "فرع",
            VehicleType = "Car",
            AddressLine = "Test address",
            Currency = "ILS",
            BaseSubtotal = 10m,
            ItemSubtotal = 10m,
            TaxableSubtotal = 10m,
            GrandTotal = 10m,
            PaymentState = "Unpaid",
            TotalDurationMinutes = 60,
            QuotedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ServiceFeeMode = "None"
        };

    private static User CreateUser(Guid userId) =>
        new()
        {
            Id = userId,
            UserName = $"step17-{userId:N}@example.test",
            NormalizedUserName = $"STEP17-{userId:N}@EXAMPLE.TEST",
            Email = $"step17-{userId:N}@example.test",
            NormalizedEmail = $"STEP17-{userId:N}@EXAMPLE.TEST",
            FullName = "Step 17 Payment Test",
            IsActive = true
        };

    private static VerifiedPaymentEvent Event(
        CustomerPayment payment,
        string eventId,
        PaymentEventKind kind = PaymentEventKind.Succeeded,
        string eventType = "charge.success") =>
        new(
            eventId,
            eventType,
            kind,
            payment.ProviderReference!,
            null,
            payment.MinorAmount,
            payment.Currency);

    private static async Task<HttpResponseMessage> SendPaymentAsync(
        HttpClient client,
        CustomerFixture fixture,
        BookingFixture booking,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/payments/intents");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(fixture.UserId));
        request.Headers.Add("X-Device-Token", fixture.DeviceToken);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new
        {
            bookingId = booking.PublicReference,
            method = "Card"
        });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        string body)
    {
        const string secret = "sk_test_step17_webhook";
        var signature = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(secret),
                    Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/lahza/webhook");
        request.Headers.TryAddWithoutValidation(
            "X-Lahza-Signature",
            signature);
        request.Content = new StringContent(
            body,
            Encoding.UTF8,
            "application/json");
        return await client.SendAsync(request);
    }

    private static string BuildLahzaEvent(VerifiedPaymentEvent paymentEvent) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = paymentEvent.EventId,
            ["event"] = paymentEvent.EventType,
            ["data"] = new Dictionary<string, object?>
            {
                ["id"] = paymentEvent.ProviderTransactionId,
                ["reference"] = paymentEvent.ProviderReference,
                ["amount"] = paymentEvent.Amount,
                ["currency"] = paymentEvent.Currency
            }
        });

    private static async Task<string> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("status").GetInt32()
            .Should().Be((int)expectedStatus);
        document.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
        document.RootElement.GetProperty("type").GetString()
            .Should().Be($"https://api.ghseeli.example/errors/{expectedCode}");
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        return body;
    }

    private static async Task AssertAcknowledgementAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Should().ContainSingle();
        body.RootElement.GetProperty("received").GetBoolean().Should().BeTrue();
    }

    private static ApplicationDbContext CreateContext(CheckoutDraftApiFactory factory) =>
        factory.Services.CreateScope().ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

    private static CustomerFixture CreateCustomerFixture()
    {
        var token = CatalogTestSupport.CreateToken(43);
        return new CustomerFixture(
            Guid.NewGuid(),
            token,
            CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1)));
    }

    private static string CreateJwt(Guid userId)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "GhseeliApis.CheckoutDraftTests",
            audience: "GhseeliApis.CheckoutDraftClients",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim(ClaimTypes.Role, "User")
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials));
    }

    private sealed record CustomerFixture(
        Guid UserId,
        string DeviceToken,
        CustomerDevice Device);

    private sealed record BookingFixture(Guid Id, Guid PublicReference);

    private sealed class ScriptedGateway(
        Func<PaymentInitializationCommand, PaymentInitializationResult> action)
        : IPaymentGateway
    {
        public List<PaymentInitializationCommand> Commands { get; } = [];

        public Task<PaymentInitializationResult> InitializeAsync(
            PaymentInitializationCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(action(command));
        }

        public Task<PaymentVerificationResult> VerifyAsync(
            string providerReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PaymentVerificationResult(
                providerReference,
                "pending",
                null,
                0,
                "ILS"));
    }

}
