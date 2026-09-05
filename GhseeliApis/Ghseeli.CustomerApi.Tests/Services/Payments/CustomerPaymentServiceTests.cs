using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace GhseeliApis.Tests.Services.Payments;

/// <summary>
/// Unit tests for authoritative payment intent orchestration.
/// </summary>
public sealed class CustomerPaymentServiceTests
{
    [Fact]
    public async Task Uses_booking_money_and_does_not_mark_booking_paid()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db, grandTotal: 12.34m);
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(value => value.InitializeAsync(It.IsAny<PaymentInitializationCommand>(), default))
            .ReturnsAsync((PaymentInitializationCommand command, CancellationToken _) =>
                new PaymentInitializationResult(
                    command.ProviderReference,
                    "initialized",
                    new Uri($"https://checkout.lahza.test/pay/{command.ProviderReference}"),
                    command.Amount, command.Currency));
        var service = CreateService(db, gateway.Object);

        var result = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "key-1", fixture.userId, fixture.deviceId, default);

        result.Amount.Should().Be(12.34m);
        result.Provider.Should().Be(PaymentProviders.Lahza);
        result.ProviderReference.Should().StartWith("GHSEELI-");
        result.Status.Should().Be(PaymentStatus.Pending.ToString());
        fixture.booking.IsPaid.Should().BeFalse();
        fixture.booking.PaymentState.Should().Be("Unpaid");
        gateway.Verify(value => value.InitializeAsync(
            It.Is<PaymentInitializationCommand>(command =>
                command.Amount == 1234 && command.Currency == "ILS"),
            default), Times.Once);
    }

    [Fact]
    public async Task Same_key_and_request_replays_without_second_intent()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var gateway = SuccessfulGateway();
        var service = CreateService(db, gateway.Object);

        var first = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "same-key", fixture.userId, fixture.deviceId, default);
        var replay = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "same-key", fixture.userId, fixture.deviceId, default);

        replay.Id.Should().Be(first.Id);
        gateway.Verify(value => value.InitializeAsync(
            It.IsAny<PaymentInitializationCommand>(), default), Times.Once);
    }

    [Fact]
    public async Task Payment_snapshot_remains_immutable_after_booking_money_changes()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db, grandTotal: 12.34m);
        var gateway = SuccessfulGateway();
        var service = CreateService(db, gateway.Object);
        var first = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "snapshot-key", fixture.userId, fixture.deviceId, default);

        fixture.booking.GrandTotal = 99.99m;
        fixture.booking.Currency = "USD";
        await db.SaveChangesAsync();
        var replay = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "snapshot-key", fixture.userId, fixture.deviceId, default);

        replay.Amount.Should().Be(first.Amount).And.Be(12.34m);
        replay.Currency.Should().Be(first.Currency).And.Be("ILS");
        gateway.Verify(value => value.InitializeAsync(
            It.IsAny<PaymentInitializationCommand>(), default), Times.Once);
    }

    [Fact]
    public async Task Confirmed_booking_can_create_intent()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db, status: "Confirmed");

        var result = await CreateService(db, SuccessfulGateway().Object).CreateAsync(
            Request(fixture.booking.PublicReference),
            "confirmed-key", fixture.userId, fixture.deviceId, default);

        result.Status.Should().Be(PaymentStatus.Pending.ToString());
        result.CheckoutUrl.Should().StartWith("https://checkout.lahza.test/");
    }

    [Fact]
    public async Task Same_opaque_key_is_isolated_between_users()
    {
        await using var db = CreateDb();
        var first = AddBooking(db);
        var second = AddBooking(db);
        var gateway = SuccessfulGateway();
        var service = CreateService(db, gateway.Object);

        var results = await Task.WhenAll(
            service.CreateAsync(
                Request(first.booking.PublicReference),
                "shared-opaque-key", first.userId, first.deviceId, default),
            service.CreateAsync(
                Request(second.booking.PublicReference),
                "shared-opaque-key", second.userId, second.deviceId, default));

        results[0].Id.Should().NotBe(results[1].Id);
        (await db.CustomerPayments.CountAsync()).Should().Be(2);
        gateway.Verify(value => value.InitializeAsync(
            It.IsAny<PaymentInitializationCommand>(), default), Times.Exactly(2));
    }

    [Fact]
    public async Task Card_casing_has_one_canonical_request_hash_and_replays()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var gateway = SuccessfulGateway();
        var service = CreateService(db, gateway.Object);
        var lower = Request(fixture.booking.PublicReference);
        lower.Method = "card";
        var upper = Request(fixture.booking.PublicReference);
        upper.Method = "CARD";

        var first = await service.CreateAsync(
            lower, "casing-key", fixture.userId, fixture.deviceId, default);
        var replay = await service.CreateAsync(
            upper, "casing-key", fixture.userId, fixture.deviceId, default);

        replay.Id.Should().Be(first.Id);
        (await db.CustomerPaymentIdempotencyRecords.SingleAsync())
            .RequestHash.Should().Be((await db.CustomerPayments.SingleAsync()).RequestHash);
        gateway.Verify(value => value.InitializeAsync(
            It.IsAny<PaymentInitializationCommand>(), default), Times.Once);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Gateway_money_mismatch_is_stable_releases_lease_and_preserves_retry_identity(
        bool amountMismatch,
        bool currencyMismatch)
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var commands = new List<PaymentInitializationCommand>();
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(value => value.InitializeAsync(
                It.IsAny<PaymentInitializationCommand>(), default))
            .ReturnsAsync((PaymentInitializationCommand command, CancellationToken _) =>
            {
                commands.Add(command);
                return new PaymentInitializationResult(
                    command.ProviderReference,
                    "initialized",
                    new Uri($"https://checkout.lahza.test/pay/{command.ProviderReference}"),
                    amountMismatch ? command.Amount + 1 : command.Amount,
                    currencyMismatch ? "USD" : command.Currency);
            });
        var service = CreateService(db, gateway.Object);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var act = () => service.CreateAsync(
                Request(fixture.booking.PublicReference),
                "mismatch-key", fixture.userId, fixture.deviceId, default);
            var exception = (await act.Should().ThrowAsync<CustomerPaymentException>()).Which;
            exception.StatusCode.Should().Be(502);
            exception.Code.Should().Be(CustomerPaymentErrorCodes.GatewayAmbiguous);
        }

        var persisted = await db.CustomerPayments.AsNoTracking().SingleAsync();
        persisted.InitializationLeaseOwnerToken.Should().BeNull();
        persisted.InitializationLeaseExpiresAtUtc.Should().BeNull();
        commands.Should().HaveCount(2);
        commands.Select(value => (value.PaymentId, value.ProviderReference))
            .Distinct().Should().ContainSingle();
    }

    public static TheoryData<Exception> AmbiguousTransportExceptions => new()
    {
        new TimeoutException("timeout"),
        new IOException("io"),
        new HttpRequestException("http"),
        new OperationCanceledException("provider cancellation")
    };

    [Theory]
    [MemberData(nameof(AmbiguousTransportExceptions))]
    public async Task Ambiguous_transport_failure_maps_stably_and_releases_lease(Exception failure)
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(value => value.InitializeAsync(
                It.IsAny<PaymentInitializationCommand>(), default))
            .ThrowsAsync(failure);
        var service = CreateService(db, gateway.Object);

        var act = () => service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "transport-key", fixture.userId, fixture.deviceId, default);

        var exception = (await act.Should().ThrowAsync<CustomerPaymentException>()).Which;
        exception.StatusCode.Should().Be(503);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.GatewayAmbiguous);
        exception.InnerException.Should().BeSameAs(failure);
        var persisted = await db.CustomerPayments.AsNoTracking().SingleAsync();
        persisted.InitializationLeaseOwnerToken.Should().BeNull();
        persisted.InitializationLeaseExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Provider_success_then_local_commit_failure_retries_with_same_provider_identity()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new FailBeforeCommitDbContext(options, failOnAsyncSave: 3);
        var fixture = AddBooking(db);
        var gateway = new IdempotentFakeGateway();
        var service = CreateService(db, gateway);

        var first = () => service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "commit-failure-key", fixture.userId, fixture.deviceId, default);
        (await first.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.GatewayAmbiguous);
        var retry = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "commit-failure-key", fixture.userId, fixture.deviceId, default);

        retry.CheckoutUrl.Should().StartWith("https://checkout.lahza.test/");
        gateway.CreatedIntentCount.Should().Be(1);
        gateway.Commands.Should().HaveCount(2);
        gateway.Commands.Select(value => (value.PaymentId, value.ProviderReference))
            .Distinct().Should().ContainSingle();
        (await db.CustomerPayments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Same_key_with_changed_canonical_request_conflicts()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var other = AddBooking(db);
        var service = CreateService(db, SuccessfulGateway().Object);
        await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "same-key", fixture.userId, fixture.deviceId, default);

        var act = () => service.CreateAsync(
            Request(other.booking.PublicReference),
            "same-key", fixture.userId, fixture.deviceId, default);

        (await act.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.IdempotencyConflict);
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    [InlineData("NoShow")]
    public async Task Ineligible_booking_status_does_not_call_gateway(string status)
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db, status: status);
        var gateway = SuccessfulGateway();
        var service = CreateService(db, gateway.Object);

        var act = () => service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "key", fixture.userId, fixture.deviceId, default);

        (await act.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.Ineligible);
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Wrong_user_or_device_is_indistinguishable_not_found()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var service = CreateService(db, SuccessfulGateway().Object);

        var act = () => service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "key", fixture.userId, Guid.NewGuid(), default);

        var exception = (await act.Should().ThrowAsync<CustomerPaymentException>()).Which;
        exception.StatusCode.Should().Be(404);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.BookingNotFound);
    }

    [Fact]
    public async Task Unknown_booking_is_not_disclosed_by_provider_configuration()
    {
        await using var db = CreateDb();
        var service = CreateService(
            db,
            SuccessfulGateway().Object,
            lahzaConfigured: false);

        var act = () => service.CreateAsync(
            Request(Guid.NewGuid()),
            "key",
            Guid.NewGuid(),
            Guid.NewGuid(),
            default);

        var exception = (await act.Should().ThrowAsync<CustomerPaymentException>()).Which;
        exception.StatusCode.Should().Be(404);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.BookingNotFound);
    }

    [Fact]
    public async Task Completed_same_key_replay_does_not_depend_on_current_provider_configuration()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var gateway = SuccessfulGateway();
        var first = await CreateService(db, gateway.Object).CreateAsync(
            Request(fixture.booking.PublicReference),
            "stable-key",
            fixture.userId,
            fixture.deviceId,
            default);
        gateway.Invocations.Clear();

        var replay = await CreateService(
            db,
            gateway.Object,
            lahzaConfigured: false).CreateAsync(
                Request(fixture.booking.PublicReference),
                "stable-key",
                fixture.userId,
                fixture.deviceId,
                default);

        replay.Id.Should().Be(first.Id);
        replay.CheckoutUrl.Should().Be(first.CheckoutUrl);
        replay.ProviderReference.Should().Be(first.ProviderReference);
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Ambiguous_gateway_failure_requires_verification_with_stable_provider_reference()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var gateway = new Mock<IPaymentGateway>();
        gateway.SetupSequence(value => value.InitializeAsync(
                It.IsAny<PaymentInitializationCommand>(), default))
            .ThrowsAsync(new TimeoutException("timeout"))
            .ReturnsAsync((PaymentInitializationResult)null!);
        var service = CreateService(db, gateway.Object);

        var first = () => service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "retry-key", fixture.userId, fixture.deviceId, default);
        (await first.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.GatewayAmbiguous);
        var persisted = await db.CustomerPayments.SingleAsync();
        persisted.ProviderReference.Should().Be(
            CustomerPaymentService.CreateProviderReference(persisted.Id));
        persisted.InitializationState.Should().Be(PaymentInitializationStates.Ambiguous);

        var replayCreate = () => service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "retry-key",
            fixture.userId,
            fixture.deviceId,
            default);
        (await replayCreate.Should().ThrowAsync<CustomerPaymentException>())
            .Which.Code.Should().Be(CustomerPaymentErrorCodes.GatewayAmbiguous);
        gateway.Verify(value => value.InitializeAsync(
            It.IsAny<PaymentInitializationCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);

        gateway.Setup(value => value.VerifyAsync(
                persisted.ProviderReference,
                default))
            .ReturnsAsync(new PaymentVerificationResult(
                persisted.ProviderReference,
                "success",
                "1001",
                persisted.MinorAmount,
                persisted.Currency));
        var replay = await service.VerifyAsync(
            persisted.Id,
            fixture.userId,
            fixture.deviceId,
            default);

        replay.Should().NotBeNull();
        replay!.Id.Should().Be(persisted.Id);
        replay.Status.Should().Be(PaymentStatus.Completed.ToString());
        gateway.Verify(value => value.InitializeAsync(
            It.IsAny<PaymentInitializationCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Legacy_stripe_payment_with_eur_and_no_reference_remains_readable()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var payment = new CustomerPayment
        {
            Id = Guid.NewGuid(),
            CustomerBooking = fixture.booking,
            CustomerBookingId = fixture.booking.Id,
            UserId = fixture.userId,
            OwnerDeviceId = fixture.deviceId,
            Amount = 10m,
            MinorAmount = 1000,
            Currency = "EUR",
            Method = PaymentMethod.Card,
            Status = PaymentStatus.Completed,
            IdempotencyKey = "legacy-key",
            RequestHash = new string('A', 64),
            Provider = "Stripe",
            ProviderReference = null,
            InitializationState = PaymentInitializationStates.Legacy,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch
        };
        db.CustomerPayments.Add(payment);
        await db.SaveChangesAsync();

        var result = await CreateService(db, SuccessfulGateway().Object).GetAsync(
            payment.Id,
            fixture.userId,
            fixture.deviceId,
            default);

        result.Should().NotBeNull();
        result!.Provider.Should().Be("Stripe");
        result.ProviderReference.Should().BeNull();
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Wrong_device_cannot_replay_same_key_or_receive_client_secret()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var service = CreateService(db, SuccessfulGateway().Object);
        await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "owned-key", fixture.userId, fixture.deviceId, default);

        var act = () => service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "owned-key", fixture.userId, Guid.NewGuid(), default);

        var exception = (await act.Should().ThrowAsync<CustomerPaymentException>()).Which;
        exception.StatusCode.Should().Be(404);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.BookingNotFound);
    }

    [Fact]
    public async Task Different_key_for_same_owned_booking_replays_logical_payment()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var gateway = SuccessfulGateway();
        var service = CreateService(db, gateway.Object);
        var first = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "first-key", fixture.userId, fixture.deviceId, default);

        var replay = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "different-key", fixture.userId, fixture.deviceId, default);

        replay.Id.Should().Be(first.Id);
        replay.BookingId.Should().Be(fixture.booking.PublicReference);
        gateway.Verify(value => value.InitializeAsync(
            It.IsAny<PaymentInitializationCommand>(), default), Times.Once);
    }

    [Fact]
    public async Task Lost_commit_acknowledgement_returns_payment_with_public_booking_reference()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new CommitThenThrowDbContext(options, throwAfterAsyncSave: 2);
        var fixture = AddBooking(db);
        var service = CreateService(db, SuccessfulGateway().Object);

        var result = await service.CreateAsync(
            Request(fixture.booking.PublicReference),
            "post-commit-key",
            fixture.userId,
            fixture.deviceId,
            default);

        result.BookingId.Should().Be(fixture.booking.PublicReference);
        result.CheckoutUrl.Should().StartWith("https://checkout.lahza.test/");
    }

    [Theory]
    [InlineData("Wallet")]
    [InlineData("CashOnArrival")]
    [InlineData("ThirdParty")]
    public async Task Unsupported_method_returns_planned_conflict(string method)
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var request = Request(fixture.booking.PublicReference);
        request.Method = method;

        var act = () => CreateService(db, SuccessfulGateway().Object).CreateAsync(
            request, "method-key", fixture.userId, fixture.deviceId, default);

        var exception = (await act.Should().ThrowAsync<CustomerPaymentException>()).Which;
        exception.StatusCode.Should().Be(409);
        exception.Code.Should().Be("payment_method_not_yet_supported");
    }

    [Fact]
    public async Task Unknown_method_returns_request_invalid()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);
        var request = Request(fixture.booking.PublicReference);
        request.Method = "CreditCard";

        var act = () => CreateService(db, SuccessfulGateway().Object).CreateAsync(
            request, "method-key", fixture.userId, fixture.deviceId, default);

        var exception = (await act.Should().ThrowAsync<CustomerPaymentException>()).Which;
        exception.StatusCode.Should().Be(400);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.Invalid);
    }

    [Fact]
    public async Task Idempotency_key_with_embedded_whitespace_is_invalid()
    {
        await using var db = CreateDb();
        var fixture = AddBooking(db);

        var act = () => CreateService(db, SuccessfulGateway().Object).CreateAsync(
            Request(fixture.booking.PublicReference),
            "invalid key",
            fixture.userId,
            fixture.deviceId,
            default);

        var exception = (await act.Should().ThrowAsync<CustomerPaymentException>()).Which;
        exception.StatusCode.Should().Be(400);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.IdempotencyKeyInvalid);
    }

    private static Mock<IPaymentGateway> SuccessfulGateway()
    {
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(value => value.InitializeAsync(It.IsAny<PaymentInitializationCommand>(), default))
            .ReturnsAsync((PaymentInitializationCommand command, CancellationToken _) =>
                new PaymentInitializationResult(
                    command.ProviderReference,
                    "initialized",
                    new Uri($"https://checkout.lahza.test/pay/{command.ProviderReference}"),
                    command.Amount, command.Currency));
        return gateway;
    }

    private static CustomerPaymentService CreateService(
        ApplicationDbContext db,
        IPaymentGateway gateway,
        bool lahzaConfigured = true)
    {
        var options = new Mock<IOptionsMonitor<LahzaConfigurationOptions>>();
        options.SetupGet(value => value.CurrentValue).Returns(new LahzaConfigurationOptions
        {
            SecretKey = lahzaConfigured ? "sk_test_configured" : string.Empty
        });
        return new CustomerPaymentService(
            db,
            gateway,
            options.Object,
            Mock.Of<IAppLogger>(),
            TimeProvider.System);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (CustomerBooking booking, Guid userId, Guid deviceId) AddBooking(
        ApplicationDbContext db,
        decimal grandTotal = 50m,
        string status = "Pending")
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var booking = new CustomerBooking
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            UserId = userId,
            OwnerDeviceId = deviceId,
            Status = status,
            GrandTotal = grandTotal,
            Currency = "ILS",
            PaymentState = "Unpaid"
        };
        db.Users.Add(new User
        {
            Id = userId,
            UserName = $"payment-{userId:N}@example.test",
            NormalizedUserName = $"PAYMENT-{userId:N}@EXAMPLE.TEST",
            Email = $"payment-{userId:N}@example.test",
            NormalizedEmail = $"PAYMENT-{userId:N}@EXAMPLE.TEST",
            FullName = "Payment Test User",
            IsActive = true
        });
        db.CustomerBookings.Add(booking);
        db.SaveChanges();
        return (booking, userId, deviceId);
    }

    private static CreateCustomerPaymentIntentRequest Request(Guid bookingId) =>
        new()
        {
            BookingId = bookingId,
            Method = "Card"
        };

    private sealed class CommitThenThrowDbContext(
        DbContextOptions<ApplicationDbContext> options,
        int throwAfterAsyncSave) : ApplicationDbContext(options)
    {
        private int _asyncSaveCount;

        public override async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            if (Interlocked.Increment(ref _asyncSaveCount) == throwAfterAsyncSave)
            {
                throw new IOException("Simulated lost commit acknowledgement.");
            }

            return result;
        }
    }

    private sealed class FailBeforeCommitDbContext(
            DbContextOptions<ApplicationDbContext> options,
            int failOnAsyncSave) : ApplicationDbContext(options)
        {
            private int _asyncSaveCount;

            public override Task<int> SaveChangesAsync(
                CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref _asyncSaveCount) == failOnAsyncSave)
                {
                    throw new IOException("Simulated local commit failure.");
                }

                return base.SaveChangesAsync(cancellationToken);
            }
        }

    private sealed class IdempotentFakeGateway : IPaymentGateway
        {
            private readonly Dictionary<string, PaymentInitializationResult> _intents = [];

            public List<PaymentInitializationCommand> Commands { get; } = [];
            public int CreatedIntentCount => _intents.Count;

            public Task<PaymentInitializationResult> InitializeAsync(
                PaymentInitializationCommand command,
                CancellationToken cancellationToken)
            {
                Commands.Add(command);
                if (!_intents.TryGetValue(command.ProviderReference, out var result))
                {
                    result = new PaymentInitializationResult(
                        command.ProviderReference,
                        "initialized",
                        new Uri($"https://checkout.lahza.test/pay/{command.ProviderReference}"),
                        command.Amount, command.Currency);
                    _intents.Add(command.ProviderReference, result);
                }
                return Task.FromResult(result);
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
