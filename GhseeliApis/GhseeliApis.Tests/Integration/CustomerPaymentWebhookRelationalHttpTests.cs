using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Payments;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Proves Stripe webhook acknowledgement and state convergence against SQL Server.
/// </summary>
public sealed class CustomerPaymentWebhookRelationalHttpTests
{
    [Fact]
    public async Task PartialRefund_ForCompletedBooking_IsDurablyIgnoredWithoutChangingPaidState()
    {
        var parser = new ControlledRelationalWebhookParser();
        await using var factory = new RelationalWebhookApiFactory(parser);
        using var client = factory.CreateApiClient();
        var payment = await factory.SeedPaymentAsync(PaymentStatus.Completed, paid: true, "ch_partial");
        parser.Add("partial", CreateEvent(
            payment,
            "evt_partial",
            StripePaymentEventKind.Refunded,
            "charge.refunded",
            "ch_partial",
            isFullyRefunded: false));

        using var response = await SendAsync(client, "partial");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verify = factory.CreateContext();
        var persisted = await verify.CustomerPayments
            .Include(value => value.CustomerBooking)
            .SingleAsync();
        persisted.Status.Should().Be(PaymentStatus.Completed);
        persisted.CustomerBooking.Status.Should().Be("Completed");
        persisted.CustomerBooking.IsPaid.Should().BeTrue();
        persisted.CustomerBooking.PaymentState.Should().Be("Completed");
        var receipt = await verify.StripeWebhookEvents.SingleAsync();
        receipt.State.Should().Be("Completed");
        receipt.CustomerPaymentId.Should().Be(payment.Id);
        receipt.DispositionReason.Should().Be("partial_refund_ignored");
    }

    [Fact]
    public async Task ParallelIdenticalDeliveries_ReturnSafeResponsesAndPersistOneTransition()
    {
        var barrier = new WebhookPaymentReadBarrier();
        var parser = new ControlledRelationalWebhookParser();
        await using var factory = new RelationalWebhookApiFactory(parser, barrier);
        using var client = factory.CreateApiClient();
        var payment = await factory.SeedPaymentAsync();
        parser.Add("duplicate", CreateEvent(
            payment,
            "evt_parallel_duplicate",
            StripePaymentEventKind.Succeeded,
            "payment_intent.succeeded",
            "ch_duplicate"));
        barrier.Arm();

        var responses = await Task.WhenAll(
            SendAsync(client, "duplicate"),
            SendAsync(client, "duplicate"));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in responses)
        {
            response.Dispose();
        }
        await using var verify = factory.CreateContext();
        var persisted = await verify.CustomerPayments
            .Include(value => value.CustomerBooking)
            .SingleAsync();
        persisted.Status.Should().Be(PaymentStatus.Completed);
        persisted.CustomerBooking.IsPaid.Should().BeTrue();
        (await verify.StripeWebhookEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TransientPersistenceFailure_IsRetryableAndRetryAppliesExactlyOnce()
    {
        var failpoint = new FailNextSaveChangesInterceptor();
        var parser = new ControlledRelationalWebhookParser();
        await using var factory = new RelationalWebhookApiFactory(parser, failpoint);
        using var client = factory.CreateApiClient();
        var payment = await factory.SeedPaymentAsync();
        parser.Add("transient", CreateEvent(
            payment,
            "evt_transient",
            StripePaymentEventKind.Succeeded,
            "payment_intent.succeeded",
            "ch_transient"));
        failpoint.Arm();

        using var first = await SendAsync(client, "transient");

        first.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await AssertUnchangedAsync(factory, PaymentStatus.Pending, paid: false, receipts: 0);

        using var retry = await SendAsync(client, "transient");

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertUnchangedAsync(factory, PaymentStatus.Completed, paid: true, receipts: 1);
    }

    [Fact]
    public async Task FailureBetweenPaymentAndBookingWrites_RollsBackAndRetryCommitsAtomically()
    {
        var failpoint = new FailSecondWebhookUpdateInterceptor();
        var parser = new ControlledRelationalWebhookParser();
        await using var factory = new RelationalWebhookApiFactory(parser, failpoint);
        using var client = factory.CreateApiClient();
        var payment = await factory.SeedPaymentAsync();
        parser.Add("atomic", CreateEvent(
            payment,
            "evt_atomic",
            StripePaymentEventKind.Succeeded,
            "payment_intent.succeeded",
            "ch_atomic"));
        failpoint.Arm();

        using var first = await SendAsync(client, "atomic");

        first.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        failpoint.ObservedUpdates.Should().Be(2);
        await AssertUnchangedAsync(factory, PaymentStatus.Pending, paid: false, receipts: 0);

        using var retry = await SendAsync(client, "atomic");

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertUnchangedAsync(factory, PaymentStatus.Completed, paid: true, receipts: 1);
    }

    [Fact]
    public async Task ConcurrentRefundBeforeSuccessAndSuccess_ConvergeWithoutUnsafeResponses()
    {
        var barrier = new WebhookPaymentReadBarrier();
        var parser = new ControlledRelationalWebhookParser();
        await using var factory = new RelationalWebhookApiFactory(parser, barrier);
        using var client = factory.CreateApiClient();
        var payment = await factory.SeedPaymentAsync();
        parser.Add("refund", CreateEvent(
            payment,
            "evt_concurrent_refund_http",
            StripePaymentEventKind.Refunded,
            "charge.refunded",
            "ch_concurrent"));
        parser.Add("success", CreateEvent(
            payment,
            "evt_concurrent_success_http",
            StripePaymentEventKind.Succeeded,
            "payment_intent.succeeded",
            "ch_concurrent"));
        barrier.Arm();

        var responses = await Task.WhenAll(
            SendAsync(client, "refund"),
            SendAsync(client, "success"));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in responses)
        {
            response.Dispose();
        }
        await using var verify = factory.CreateContext();
        var persisted = await verify.CustomerPayments
            .Include(value => value.CustomerBooking)
            .SingleAsync();
        persisted.Status.Should().Be(PaymentStatus.Refunded);
        persisted.CustomerBooking.IsPaid.Should().BeFalse();
        persisted.CustomerBooking.PaymentState.Should().Be("Refunded");
        (await verify.StripeWebhookEvents.CountAsync()).Should().Be(2);
        (await verify.StripeWebhookEvents.CountAsync(value => value.State == "Completed"))
            .Should().Be(2);
    }

    private static VerifiedStripeEvent CreateEvent(
        CustomerPayment payment,
        string eventId,
        StripePaymentEventKind kind,
        string eventType,
        string chargeId,
        bool isFullyRefunded = true) =>
        new(
            eventId,
            eventType,
            kind,
            payment.PaymentIntentId!,
            chargeId,
            payment.MinorAmount,
            payment.Currency,
            new Dictionary<string, string>
            {
                ["payment_id"] = payment.Id.ToString("D"),
                ["booking_id"] = payment.CustomerBookingId.ToString("D"),
                ["booking_reference"] = payment.CustomerBooking.PublicReference.ToString("D")
            },
            isFullyRefunded);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook");
        request.Headers.TryAddWithoutValidation("Stripe-Signature", "controlled");
        request.Content = new StringContent(body, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.SendAsync(request);
    }

    private static async Task AssertUnchangedAsync(
        RelationalWebhookApiFactory factory,
        PaymentStatus status,
        bool paid,
        int receipts)
    {
        await using var verify = factory.CreateContext();
        var persisted = await verify.CustomerPayments
            .Include(value => value.CustomerBooking)
            .SingleAsync();
        persisted.Status.Should().Be(status);
        persisted.CustomerBooking.IsPaid.Should().Be(paid);
        (await verify.StripeWebhookEvents.CountAsync()).Should().Be(receipts);
    }

    private sealed class ControlledRelationalWebhookParser : IStripeWebhookParser
    {
        private readonly Dictionary<string, VerifiedStripeEvent> _events =
            new(StringComparer.Ordinal);

        public void Add(string body, VerifiedStripeEvent stripeEvent) =>
            _events.Add(body, stripeEvent);

        public VerifiedStripeEvent Parse(string rawBody, string signature, string webhookSecret) =>
            _events[rawBody];
    }

    private sealed class FailNextSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int _armed;

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _armed, 0) == 1
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new InvalidOperationException("Controlled transient persistence failure."))
                : ValueTask.FromResult(result);
    }

    private sealed class FailSecondWebhookUpdateInterceptor : DbCommandInterceptor
    {
        private int _armed;
        private int _observedUpdates;

        public int ObservedUpdates => Volatile.Read(ref _observedUpdates);

        public void Arm()
        {
            Interlocked.Exchange(ref _observedUpdates, 0);
            Interlocked.Exchange(ref _armed, 1);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1 &&
                IsAggregateUpdate(command) &&
                Volatile.Read(ref _observedUpdates) == 1)
            {
                Interlocked.Increment(ref _observedUpdates);
                Interlocked.Exchange(ref _armed, 0);
                return ValueTask.FromException<InterceptionResult<DbDataReader>>(
                    new InvalidOperationException("Controlled failure between aggregate writes."));
            }

            return ValueTask.FromResult(result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1 &&
                IsAggregateUpdate(command) &&
                Volatile.Read(ref _observedUpdates) == 0)
            {
                Interlocked.Increment(ref _observedUpdates);
            }

            return ValueTask.FromResult(result);
        }

        private static bool IsAggregateUpdate(DbCommand command) =>
            command.CommandText.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) &&
            (command.CommandText.Contains("[CustomerPayments]", StringComparison.Ordinal) ||
             command.CommandText.Contains("[CustomerBookings]", StringComparison.Ordinal));
    }

    private sealed class WebhookPaymentReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReads =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;
        private int _readCount;

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1 &&
                command.CommandText.Contains("FROM [CustomerPayments] AS [c]", StringComparison.Ordinal) &&
                command.CommandText.Contains("[c].[PaymentIntentId]", StringComparison.Ordinal) &&
                Interlocked.Increment(ref _readCount) <= 2)
            {
                if (Volatile.Read(ref _readCount) == 2)
                {
                    _bothReads.TrySetResult();
                }
                await _bothReads.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }

    private sealed class RelationalWebhookApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly SqlServerCatalogDatabase _database =
            SqlServerCatalogDatabase.CreateAsync().GetAwaiter().GetResult();
        private readonly IStripeWebhookParser _parser;
        private readonly IInterceptor[] _interceptors;

        public RelationalWebhookApiFactory(
            IStripeWebhookParser parser,
            params IInterceptor[] interceptors)
        {
            _parser = parser;
            _interceptors = interceptors;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CustomerConnection", _database.ConnectionString);
            builder.UseSetting("JwtSettings:SecretKey", "WebhookApiTestsSecret_Minimum32Characters");
            builder.UseSetting("JwtSettings:Issuer", "GhseeliApis.WebhookTests");
            builder.UseSetting("JwtSettings:Audience", "GhseeliApis.WebhookClients");
            builder.UseSetting("Stripe:WebhookSecret", "whsec_controlled");
            builder.UseSetting("Stripe:PublishableKey", string.Empty);
            builder.UseSetting("Stripe:SecretKey", string.Empty);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<IStripeWebhookParser>();
                services.AddSingleton<IStripeWebhookParser>(_parser);
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseSqlServer(_database.ConnectionString, sql =>
                    {
                        sql.EnableRetryOnFailure(5);
                        sql.CommandTimeout(60);
                        sql.UseCompatibilityLevel(120);
                        sql.MaxBatchSize(1);
                    });
                    options.AddInterceptors(_interceptors);
                });
            });
        }

        public HttpClient CreateApiClient() =>
            CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

        public ApplicationDbContext CreateContext() => _database.CreateContext();

        public async Task<CustomerPayment> SeedPaymentAsync(
            PaymentStatus status = PaymentStatus.Pending,
            bool paid = false,
            string? chargeId = null)
        {
            CustomerPayment? payment = null;
            await _database.ExecuteAsync(context =>
            {
                var userId = Guid.NewGuid();
                var booking = new CustomerBooking
                {
                    Id = Guid.NewGuid(),
                    PublicReference = Guid.NewGuid(),
                    OrderGuid = Guid.NewGuid(),
                    User = new User
                    {
                        Id = userId,
                        UserName = $"webhook-{Guid.NewGuid():N}@example.com",
                        NormalizedUserName = $"WEBHOOK-{Guid.NewGuid():N}@EXAMPLE.COM",
                        Email = $"webhook-{Guid.NewGuid():N}@example.com",
                        NormalizedEmail = $"WEBHOOK-{Guid.NewGuid():N}@EXAMPLE.COM",
                        FullName = "Webhook Test",
                        IsActive = true
                    },
                    UserId = userId,
                    OwnerDeviceId = Guid.NewGuid(),
                    BusinessReservationId = Guid.NewGuid(),
                    BusinessWorkOrderId = Guid.NewGuid(),
                    Status = status == PaymentStatus.Completed ? "Completed" : "Confirmed",
                    ProviderNameAr = "مزود",
                    BranchNameAr = "فرع",
                    VehicleType = "Car",
                    AddressLine = "Address",
                    Currency = "ILS",
                    ServiceFeeMode = "None",
                    GrandTotal = 10m,
                    IsPaid = paid,
                    PaymentState = status.ToString()
                };
                payment = new CustomerPayment
                {
                    Id = Guid.NewGuid(),
                    CustomerBooking = booking,
                    CustomerBookingId = booking.Id,
                    UserId = userId,
                    OwnerDeviceId = booking.OwnerDeviceId,
                    Amount = 10m,
                    MinorAmount = 1000,
                    Currency = "ILS",
                    Method = PaymentMethod.Card,
                    Status = status,
                    IdempotencyKey = $"key-{Guid.NewGuid():N}",
                    RequestHash = new string('A', 64),
                    StripeIdempotencyKey = $"stripe-{Guid.NewGuid():N}",
                    PaymentIntentId = $"pi_{Guid.NewGuid():N}",
                    ChargeId = chargeId
                };
                context.CustomerPayments.Add(payment);
            });
            return payment!;
        }

        public new async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

}
