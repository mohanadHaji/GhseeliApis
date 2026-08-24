using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Tests.Support;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Payments;
using Ghseeli.Common.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies SQL Server payment and webhook uniqueness invariants.
/// </summary>
public sealed class CustomerPaymentRelationalIntegrationTests
{
    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-PAYMENT-159")]
    public async Task Different_keys_cannot_create_two_payments_for_one_booking()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var payment = await SeedPaymentAsync(database);
        await using var context = database.CreateContext();
        context.CustomerPayments.Add(CreatePayment(
            payment.CustomerBookingId,
            payment.UserId,
            payment.OwnerDeviceId,
            "different-key"));

        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        await using var verify = database.CreateContext();
        (await verify.CustomerPayments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Stripe_event_id_is_durably_unique()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        await using (var first = database.CreateContext())
        {
            first.StripeWebhookEvents.Add(new StripeWebhookEventRecord
            {
                EventId = "evt_unique",
                BodyHash = new string('A', 64),
                EventType = "payment_intent.succeeded",
                State = "Completed"
            });
            await first.SaveChangesAsync();
        }

        await using var second = database.CreateContext();
        second.StripeWebhookEvents.Add(new StripeWebhookEventRecord
        {
            EventId = "evt_unique",
            BodyHash = new string('B', 64),
            EventType = "payment_intent.succeeded",
            State = "Completed"
        });
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Same_user_and_opaque_key_are_independent_across_devices_and_bookings()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var firstBooking = await SeedBookingAsync(database);
        CustomerBooking? secondBooking = null;
        await database.ExecuteAsync(context =>
        {
            secondBooking = new CustomerBooking
            {
                Id = Guid.NewGuid(),
                PublicReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                UserId = firstBooking.UserId,
                OwnerDeviceId = Guid.NewGuid(),
                BusinessReservationId = Guid.NewGuid(),
                BusinessWorkOrderId = Guid.NewGuid(),
                Status = "Confirmed",
                ProviderNameAr = "مزود",
                BranchNameAr = "فرع",
                VehicleType = "Car",
                AddressLine = "Address",
                Currency = "ILS",
                ServiceFeeMode = "None",
                GrandTotal = 10m,
                PaymentState = "Unpaid"
            };
            context.CustomerBookings.Add(secondBooking);
        });

        await using var context = database.CreateContext();
        context.CustomerPayments.AddRange(
            CreatePayment(
                firstBooking.Id,
                firstBooking.UserId,
                firstBooking.OwnerDeviceId,
                "device-local-key"),
            CreatePayment(
                secondBooking!.Id,
                secondBooking.UserId,
                secondBooking.OwnerDeviceId,
                "device-local-key"));

        var act = () => context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        (await context.CustomerPayments.CountAsync()).Should().Be(2);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-PAYMENT-159")]
    public async Task Concurrent_different_keys_replay_one_logical_payment_and_intent()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = await SeedBookingAsync(database);
        var barrier = new ExistingPaymentReadBarrier();
        await using var firstContext = database.CreateContext(barrier);
        await using var secondContext = database.CreateContext(barrier);
        var gateway = new Mock<IStripePaymentIntentGateway>();
        gateway.Setup(value => value.CreateAsync(
                It.IsAny<StripeIntentCreateCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeIntentCreateCommand command, CancellationToken _) =>
                new StripeIntentResult(
                    $"pi_{command.PaymentId:N}",
                    "requires_confirmation",
                    "client-secret",
                    null,
                    command.Amount,
                    command.Currency));
        var first = CreateService(firstContext, gateway.Object);
        var second = CreateService(secondContext, gateway.Object);
        var request = new GhseeliApis.DTOs.Payment.CreateCustomerPaymentIntentRequest
        {
            BookingId = booking.PublicReference,
            Method = "Card"
        };

        var results = await Task.WhenAll(
            first.CreateAsync(
                request, "concurrent-a", booking.UserId, booking.OwnerDeviceId, default),
            second.CreateAsync(
                request, "concurrent-b", booking.UserId, booking.OwnerDeviceId, default));

        results[0].Id.Should().Be(results[1].Id);
        await using var verify = database.CreateContext();
        (await verify.CustomerPayments.CountAsync()).Should().Be(1);
        (await verify.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(2);
        gateway.Verify(value => value.CreateAsync(
            It.IsAny<StripeIntentCreateCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-PAYMENT-159")]
    public async Task Parallel_same_key_requests_converge_to_identical_result_and_one_intent()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = await SeedBookingAsync(database);
        var barrier = new ExistingPaymentReadBarrier();
        await using var firstContext = database.CreateContext(barrier);
        await using var secondContext = database.CreateContext(barrier);
        var gateway = new Mock<IStripePaymentIntentGateway>();
        gateway.Setup(value => value.CreateAsync(
                It.IsAny<StripeIntentCreateCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeIntentCreateCommand command, CancellationToken _) =>
                new StripeIntentResult(
                    "pi_same_key", "requires_action", "same-secret", null,
                    command.Amount, command.Currency));
        var request = new GhseeliApis.DTOs.Payment.CreateCustomerPaymentIntentRequest
        {
            BookingId = booking.PublicReference,
            Method = "Card"
        };

        var results = await Task.WhenAll(
            CreateService(firstContext, gateway.Object).CreateAsync(
                request, "parallel-same", booking.UserId, booking.OwnerDeviceId, default),
            CreateService(secondContext, gateway.Object).CreateAsync(
                request, "parallel-same", booking.UserId, booking.OwnerDeviceId, default));

        results[0].Should().BeEquivalentTo(results[1]);
        await using var verify = database.CreateContext();
        (await verify.CustomerPayments.CountAsync()).Should().Be(1);
        (await verify.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(1);
        (await verify.CustomerPayments.SingleAsync()).PaymentIntentId.Should().Be("pi_same_key");
        gateway.Verify(value => value.CreateAsync(
            It.IsAny<StripeIntentCreateCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ambiguous_completion_commit_is_verified_relationally_without_second_provider_call()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = await SeedBookingAsync(database);
        var failpoint = new ThrowAfterIntentCompletionInterceptor();
        await using var context = database.CreateContext(failpoint);
        var gateway = new Mock<IStripePaymentIntentGateway>();
        gateway.Setup(value => value.CreateAsync(
                It.IsAny<StripeIntentCreateCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeIntentCreateCommand command, CancellationToken _) =>
                new StripeIntentResult(
                    "pi_ambiguous_commit", "requires_action", "secret", null,
                    command.Amount, command.Currency));

        var result = await CreateService(context, gateway.Object).CreateAsync(
            new GhseeliApis.DTOs.Payment.CreateCustomerPaymentIntentRequest
            {
                BookingId = booking.PublicReference,
                Method = "Card"
            },
            "ambiguous-commit",
            booking.UserId,
            booking.OwnerDeviceId,
            default);

        result.ClientSecret.Should().Be("secret");
        failpoint.WasTriggered.Should().BeTrue();
        await using var verify = database.CreateContext();
        (await verify.CustomerPayments.CountAsync()).Should().Be(1);
        (await verify.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(1);
        var payment = await verify.CustomerPayments.SingleAsync();
        payment.PaymentIntentId.Should().Be("pi_ambiguous_commit");
        payment.IntentLeaseOwnerToken.Should().BeNull();
        gateway.Verify(value => value.CreateAsync(
            It.IsAny<StripeIntentCreateCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("minor-amount")]
    [InlineData("currency")]
    public async Task Payment_money_constraints_reject_invalid_rows(string invalidField)
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = await SeedBookingAsync(database);
        await using var context = database.CreateContext();
        var payment = CreatePayment(
            booking.Id, booking.UserId, booking.OwnerDeviceId, $"invalid-{invalidField}");
        if (invalidField == "amount")
        {
            payment.Amount = 0;
        }
        else if (invalidField == "minor-amount")
        {
            payment.MinorAmount = 0;
        }
        else
        {
            payment.Currency = "JPY";
        }
        context.CustomerPayments.Add(payment);

        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("")]
    public async Task Webhook_state_constraint_rejects_invalid_state(string state)
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.StripeWebhookEvents.Add(new StripeWebhookEventRecord
        {
            EventId = $"evt_{Guid.NewGuid():N}",
            BodyHash = new string('A', 64),
            EventType = "payment_intent.succeeded",
            State = state
        });

        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData("provider-intent")]
    [InlineData("provider-idempotency")]
    public async Task Provider_identifiers_are_durably_unique(string identifier)
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var firstBooking = await SeedBookingAsync(database);
        var secondBooking = await SeedBookingAsync(database);
        await using var context = database.CreateContext();
        var first = CreatePayment(
            firstBooking.Id, firstBooking.UserId, firstBooking.OwnerDeviceId, "unique-a");
        var second = CreatePayment(
            secondBooking.Id, secondBooking.UserId, secondBooking.OwnerDeviceId, "unique-b");
        if (identifier == "provider-intent")
        {
            first.PaymentIntentId = second.PaymentIntentId = "pi_duplicate";
        }
        else
        {
            first.StripeIdempotencyKey = second.StripeIdempotencyKey = "stripe-duplicate";
        }
        context.CustomerPayments.AddRange(first, second);

        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Opaque_key_uniqueness_is_scoped_by_user_and_device()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var firstBooking = await SeedBookingAsync(database);
        var secondBooking = await SeedBookingAsync(database);
        await using var context = database.CreateContext();
        var first = CreatePayment(
            firstBooking.Id, firstBooking.UserId, firstBooking.OwnerDeviceId, "payment-a");
        var second = CreatePayment(
            secondBooking.Id, secondBooking.UserId, secondBooking.OwnerDeviceId, "payment-b");
        first.IdempotencyRecords.Add(CreateIdempotency(first, "same-opaque"));
        second.IdempotencyRecords.Add(CreateIdempotency(second, "same-opaque"));
        context.CustomerPayments.AddRange(first, second);

        var act = () => context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        (await context.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Same_scoped_opaque_key_is_durably_unique()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var firstBooking = await SeedBookingAsync(database);
        var secondBooking = await SeedBookingAsync(database);
        await using var context = database.CreateContext();
        var first = CreatePayment(
            firstBooking.Id, firstBooking.UserId, firstBooking.OwnerDeviceId, "payment-a");
        var second = CreatePayment(
            secondBooking.Id, secondBooking.UserId, secondBooking.OwnerDeviceId, "payment-b");
        var firstRecord = CreateIdempotency(first, "duplicate-opaque");
        var secondRecord = CreateIdempotency(second, "duplicate-opaque");
        secondRecord.UserId = firstRecord.UserId;
        secondRecord.OwnerDeviceId = firstRecord.OwnerDeviceId;
        first.IdempotencyRecords.Add(firstRecord);
        second.IdempotencyRecords.Add(secondRecord);
        context.CustomerPayments.AddRange(first, second);

        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Payment_requires_existing_booking_and_idempotency_requires_payment()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var missingPayment = CreatePayment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "missing-booking");
        context.CustomerPayments.Add(missingPayment);
        var paymentAct = () => context.SaveChangesAsync();
        await paymentAct.Should().ThrowAsync<DbUpdateException>();

        context.ChangeTracker.Clear();
        context.CustomerPaymentIdempotencyRecords.Add(new CustomerPaymentIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CustomerPaymentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            OwnerDeviceId = Guid.NewGuid(),
            IdempotencyKey = "missing-payment",
            RequestHash = new string('A', 64)
        });
        var idempotencyAct = () => context.SaveChangesAsync();
        await idempotencyAct.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Webhook_payment_relationship_rejects_missing_payment()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.StripeWebhookEvents.Add(new StripeWebhookEventRecord
        {
            EventId = $"evt_{Guid.NewGuid():N}",
            BodyHash = new string('A', 64),
            EventType = "payment_intent.succeeded",
            State = "Completed",
            CustomerPaymentId = Guid.NewGuid()
        });

        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Expired_owner_is_reclaimed_and_stale_owner_cannot_overwrite_winner()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var payment = await SeedPaymentAsync(database);
        var staleGateway = new BlockingGateway("pi_stale", "stale-secret");
        var winnerGateway = new Mock<IStripePaymentIntentGateway>();
        winnerGateway.Setup(value => value.CreateAsync(
                It.IsAny<StripeIntentCreateCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeIntentCreateCommand command, CancellationToken _) =>
                new StripeIntentResult(
                    "pi_winner",
                    "requires_confirmation",
                    "winner-secret",
                    null,
                    command.Amount,
                    command.Currency));
        await using var staleContext = database.CreateContext();
        await using var winnerContext = database.CreateContext();
        var staleService = CreateService(staleContext, staleGateway);
        var winnerService = CreateService(winnerContext, winnerGateway.Object);
        var request = new GhseeliApis.DTOs.Payment.CreateCustomerPaymentIntentRequest
        {
            BookingId = payment.CustomerBooking.PublicReference,
            Method = "Card"
        };

        var staleTask = staleService.CreateAsync(
            request,
            "stale-owner-key",
            payment.UserId,
            payment.OwnerDeviceId,
            default);
        await staleGateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await database.ExecuteAsync(async (
            GhseeliApis.Persistence.ApplicationDbContext context) =>
        {
            await context.CustomerPayments
                .Where(value => value.Id == payment.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        value => value.IntentLeaseExpiresAtUtc,
                        DateTimeOffset.UtcNow.AddSeconds(-1)));
        });

        var winner = await winnerService.CreateAsync(
            request,
            "winner-key",
            payment.UserId,
            payment.OwnerDeviceId,
            default);
        staleGateway.Release();
        var stale = await staleTask;

        winner.ClientSecret.Should().Be("winner-secret");
        stale.ClientSecret.Should().Be("winner-secret");
        await using var verify = database.CreateContext();
        var persisted = await verify.CustomerPayments.SingleAsync();
        persisted.PaymentIntentId.Should().Be("pi_winner");
        persisted.ClientSecret.Should().Be("winner-secret");
        persisted.IntentLeaseOwnerToken.Should().BeNull();
        persisted.IntentLeaseExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Canceled_lease_owner_releases_durable_lease()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var payment = await SeedPaymentAsync(database);
        var gateway = new BlockingGateway("pi_canceled", "canceled-secret");
        await using var context = database.CreateContext();
        var service = CreateService(context, gateway);
        var request = new GhseeliApis.DTOs.Payment.CreateCustomerPaymentIntentRequest
        {
            BookingId = payment.CustomerBooking.PublicReference,
            Method = "Card"
        };
        using var cancellation = new CancellationTokenSource();

        var createTask = service.CreateAsync(
            request,
            "canceled-owner-key",
            payment.UserId,
            payment.OwnerDeviceId,
            cancellation.Token);
        await gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        Func<Task> act = async () => await createTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        await using var verify = database.CreateContext();
        var persisted = await verify.CustomerPayments.SingleAsync();
        persisted.PaymentIntentId.Should().BeNull();
        persisted.IntentLeaseOwnerToken.Should().BeNull();
        persisted.IntentLeaseExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_refund_and_success_converge_to_refunded()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var payment = await SeedPaymentAsync(database);
        await database.ExecuteAsync(async (
            GhseeliApis.Persistence.ApplicationDbContext context) =>
        {
            await context.CustomerPayments
                .Where(value => value.Id == payment.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.PaymentIntentId, "pi_ordering")
                    .SetProperty(value => value.ChargeId, (string?)null));
        });
        var barrier = new WebhookPaymentReadBarrier();
        await using var refundContext = database.CreateContext(barrier);
        await using var successContext = database.CreateContext(barrier);
        var refundService = new StripeWebhookService(
            refundContext,
            Mock.Of<IAppLogger>(),
            TimeProvider.System);
        var successService = new StripeWebhookService(
            successContext,
            Mock.Of<IAppLogger>(),
            TimeProvider.System);
        var metadata = new Dictionary<string, string>
        {
            ["payment_id"] = payment.Id.ToString("D"),
            ["booking_id"] = payment.CustomerBookingId.ToString("D"),
            ["booking_reference"] = payment.CustomerBooking.PublicReference.ToString("D")
        };
        var refund = new VerifiedStripeEvent(
            "evt_concurrent_refund",
            "charge.refunded",
            StripePaymentEventKind.Refunded,
            "pi_ordering",
            "ch_ordering",
            payment.MinorAmount,
            payment.Currency,
            metadata);
        var success = new VerifiedStripeEvent(
            "evt_concurrent_success",
            "payment_intent.succeeded",
            StripePaymentEventKind.Succeeded,
            "pi_ordering",
            "ch_ordering",
            payment.MinorAmount,
            payment.Currency,
            metadata);

        await Task.WhenAll(
            refundService.ProcessAsync(refund, "refund-body", default),
            successService.ProcessAsync(success, "success-body", default));

        await using var verify = database.CreateContext();
        var persisted = await verify.CustomerPayments
            .Include(value => value.CustomerBooking)
            .SingleAsync();
        persisted.Status.Should().Be(PaymentStatus.Refunded);
        persisted.CustomerBooking.IsPaid.Should().BeFalse();
        persisted.CustomerBooking.PaymentState.Should().Be("Refunded");
        (await verify.StripeWebhookEvents.CountAsync()).Should().Be(2);
        (await verify.StripeWebhookEvents.CountAsync(value =>
            value.State == "Completed")).Should().Be(2);
    }

    private sealed class BlockingGateway(
        string paymentIntentId,
        string clientSecret) : IStripePaymentIntentGateway
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<StripeIntentCreateCommand> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StripeIntentResult> CreateAsync(
            StripeIntentCreateCommand command,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(command);
            await _release.Task.WaitAsync(cancellationToken);
            return new StripeIntentResult(
                paymentIntentId,
                "requires_confirmation",
                clientSecret,
                null,
                command.Amount,
                command.Currency);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class ExistingPaymentReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReads =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "FROM [CustomerPayments] AS [c]",
                    StringComparison.Ordinal) &&
                command.CommandText.Contains(
                    "[c].[CustomerBookingId]",
                    StringComparison.Ordinal) &&
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

    private sealed class WebhookPaymentReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReads =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "FROM [CustomerPayments] AS [c]",
                    StringComparison.Ordinal) &&
                command.CommandText.Contains(
                    "[c].[PaymentIntentId]",
                    StringComparison.Ordinal) &&
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

    private sealed class ThrowAfterIntentCompletionInterceptor : DbCommandInterceptor
    {
        private int _triggered;
        public bool WasTriggered => Volatile.Read(ref _triggered) == 1;

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("[PaymentIntentId]", StringComparison.Ordinal) &&
                command.CommandText.Contains("[ProviderStatus]", StringComparison.Ordinal) &&
                command.CommandText.Contains("[IntentLeaseOwnerToken]", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _triggered, 1) == 0)
            {
                throw new IOException("Simulated lost SQL commit acknowledgement.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private static async Task<CustomerPayment> SeedPaymentAsync(SqlServerCatalogDatabase database)
    {
        CustomerPayment? payment = null;
        await database.ExecuteAsync(context =>
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "payment-relational@example.com",
                NormalizedUserName = "PAYMENT-RELATIONAL@EXAMPLE.COM",
                Email = "payment-relational@example.com",
                NormalizedEmail = "PAYMENT-RELATIONAL@EXAMPLE.COM",
                FullName = "Payment Relational",
                IsActive = true
            };
            var booking = new CustomerBooking
            {
                Id = Guid.NewGuid(),
                PublicReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                User = user,
                UserId = user.Id,
                OwnerDeviceId = Guid.NewGuid(),
                BusinessReservationId = Guid.NewGuid(),
                BusinessWorkOrderId = Guid.NewGuid(),
                Status = "Confirmed",
                ProviderNameAr = "مزود",
                BranchNameAr = "فرع",
                VehicleType = "Car",
                AddressLine = "Address",
                Currency = "ILS",
                ServiceFeeMode = "None",
                GrandTotal = 10m,
                PaymentState = "Unpaid"
            };
            payment = CreatePayment(
                booking.Id, booking.UserId, booking.OwnerDeviceId, "first-key");
            payment.CustomerBooking = booking;
            context.CustomerPayments.Add(payment);
        });
        return payment!;
    }

    private static async Task<CustomerBooking> SeedBookingAsync(SqlServerCatalogDatabase database)
    {
        CustomerBooking? booking = null;
        await database.ExecuteAsync(context =>
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = $"payment-{Guid.NewGuid():N}@example.com",
                NormalizedUserName = $"PAYMENT-{Guid.NewGuid():N}@EXAMPLE.COM",
                Email = $"payment-{Guid.NewGuid():N}@example.com",
                NormalizedEmail = $"PAYMENT-{Guid.NewGuid():N}@EXAMPLE.COM",
                FullName = "Payment Concurrency",
                IsActive = true
            };
            booking = new CustomerBooking
            {
                Id = Guid.NewGuid(),
                PublicReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                User = user,
                UserId = user.Id,
                OwnerDeviceId = Guid.NewGuid(),
                BusinessReservationId = Guid.NewGuid(),
                BusinessWorkOrderId = Guid.NewGuid(),
                Status = "Confirmed",
                ProviderNameAr = "مزود",
                BranchNameAr = "فرع",
                VehicleType = "Car",
                AddressLine = "Address",
                Currency = "ILS",
                ServiceFeeMode = "None",
                GrandTotal = 10m,
                PaymentState = "Unpaid"
            };
            context.CustomerBookings.Add(booking);
        });
        return booking!;
    }

    private static CustomerPaymentService CreateService(
        GhseeliApis.Persistence.ApplicationDbContext context,
        IStripePaymentIntentGateway gateway)
    {
        var options = new Mock<IOptionsMonitor<StripeConfigurationOptions>>();
        options.SetupGet(value => value.CurrentValue).Returns(new StripeConfigurationOptions
        {
            PublishableKey = "pk_test_configured",
            SecretKey = "sk_test_configured"
        });
        return new CustomerPaymentService(
            context,
            gateway,
            options.Object,
            Mock.Of<IAppLogger>(),
            TimeProvider.System);
    }

    private static CustomerPayment CreatePayment(
        Guid bookingId,
        Guid userId,
        Guid deviceId,
        string key) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerBookingId = bookingId,
            UserId = userId,
            OwnerDeviceId = deviceId,
            Amount = 10m,
            MinorAmount = 1000,
            Currency = "ILS",
            Method = PaymentMethod.Card,
            Status = PaymentStatus.Pending,
            IdempotencyKey = key,
            RequestHash = new string('A', 64),
            StripeIdempotencyKey = $"stripe-{Guid.NewGuid():N}"
        };

    private static CustomerPaymentIdempotencyRecord CreateIdempotency(
        CustomerPayment payment,
        string key) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerPayment = payment,
            CustomerPaymentId = payment.Id,
            UserId = payment.UserId,
            OwnerDeviceId = payment.OwnerDeviceId,
            IdempotencyKey = key,
            RequestHash = new string('A', 64)
        };
}
