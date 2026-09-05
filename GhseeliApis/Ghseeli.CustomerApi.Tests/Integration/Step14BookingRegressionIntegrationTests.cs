using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.DTOs.Booking;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Payments;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Protects booking HTTP and persistence contracts from Step 14 payment changes.
/// </summary>
public sealed class Step14BookingRegressionIntegrationTests
{
    private const string JwtSecret = "CheckoutDraftApiTestsSecret_Minimum32Chars";

    [Fact]
    [Trait("ScenarioId", "STEP14-REGRESSION-BOOKING-DETAIL-066")]
    public async Task PaymentLifecycle_ChangesOnlyBookingPaidFieldsAndPreservesImmutableSnapshot()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = CreateCustomerBooking();
        await database.ExecuteAsync(context =>
        {
            context.Users.Add(booking.User);
            context.CustomerBookings.Add(booking);
        });
        var before = await ReadSnapshotAsync(database, booking.Id);

        await using (var context = database.CreateContext())
        {
            var gateway = new Mock<IPaymentGateway>();
            gateway.Setup(value => value.InitializeAsync(
                    It.IsAny<PaymentInitializationCommand>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((PaymentInitializationCommand command, CancellationToken _) =>
                    new PaymentInitializationResult(
                        command.ProviderReference,
                        "initialized",
                        new Uri($"https://checkout.lahza.test/pay/{command.ProviderReference}"),
                        command.Amount,
                        command.Currency));
            var options = Options("pk_test_regression", "sk_test_regression");
            var service = new CustomerPaymentService(
                context,
                gateway.Object,
                options,
                new TestAppLogger(),
                TimeProvider.System);

            var response = await service.CreateAsync(
                new CreateCustomerPaymentIntentRequest
                {
                    BookingId = booking.PublicReference,
                    Method = "Card"
                },
                "booking-regression",
                booking.UserId,
                booking.OwnerDeviceId,
                default);

            response.Status.Should().Be("Pending");
        }

        var pending = await ReadSnapshotAsync(database, booking.Id);
        pending.Immutable.Should().BeEquivalentTo(before.Immutable);
        pending.IsPaid.Should().BeFalse();
        pending.PaymentState.Should().Be("Unpaid");

        await ApplyWebhookAsync(
            database,
            "evt_step14_success",
            PaymentEventKind.Succeeded,
            "charge.success",
            "ch_step14");
        var completed = await ReadSnapshotAsync(database, booking.Id);
        completed.Immutable.Should().BeEquivalentTo(before.Immutable);
        completed.IsPaid.Should().BeTrue();
        completed.PaymentState.Should().Be("Completed");

        await ApplyWebhookAsync(
            database,
            "evt_step14_refund",
            PaymentEventKind.Refunded,
            "refund.processed",
            "ch_step14");
        var refunded = await ReadSnapshotAsync(database, booking.Id);
        refunded.Immutable.Should().BeEquivalentTo(before.Immutable);
        refunded.IsPaid.Should().BeFalse();
        refunded.PaymentState.Should().Be("Refunded");
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, false, "Pending")]
    [InlineData(PaymentStatus.Completed, true, "Completed")]
    [InlineData(PaymentStatus.Refunded, false, "Refunded")]
    [Trait("ScenarioId", "STEP14-REGRESSION-STATUS-CALLBACK-069")]
    public async Task SignedStatusCallback_ChangesWorkflowOnlyAndPreservesPaymentAuthority(
        PaymentStatus paymentStatus,
        bool isPaid,
        string paymentState)
    {
        await using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        CustomerBooking booking;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            booking = await context.CustomerBookings.SingleAsync();
            booking.GrandTotal = 123.45m;
            booking.Currency = "ILS";
            booking.IsPaid = isPaid;
            booking.PaymentState = paymentState;
            context.CustomerPayments.Add(CreatePayment(booking, paymentStatus));
            await context.SaveChangesAsync();
        }
        var before = await ReadAuthorityAsync(factory, booking.Id);
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        using var request = CreateSignedStatusRequest(client, message);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await ReadAuthorityAsync(factory, booking.Id);
        after.Should().BeEquivalentTo(before);
        using var scopeAfter = factory.Services.CreateScope();
        var persisted = await scopeAfter.ServiceProvider
            .GetRequiredService<ApplicationDbContext>()
            .CustomerBookings.SingleAsync();
        persisted.Status.Should().Be(BookingStatuses.Confirmed);
        persisted.BusinessStatusSequence.Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP14-REGRESSION-BOOKING-CONFIRM-142")]
    [Trait("ScenarioId", "STEP15-CUSTOMER-BOOKING-CONFIRM-070")]
    public async Task FromDraftRoute_RemainsWiredWithExactExistingResponseContract()
    {
        var token = CatalogTestSupport.CreateToken(142);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        var userId = Guid.NewGuid();
        var orderGuid = Guid.NewGuid();
        var expected = new ConfirmedBookingResponse
        {
            Id = Guid.NewGuid(),
            Reference = Guid.NewGuid(),
            OrderGuid = orderGuid,
            BusinessReservationId = Guid.NewGuid(),
            BusinessWorkOrderId = Guid.NewGuid(),
            Status = BookingStatuses.Pending,
            DraftVersion = 7,
            Language = "ar",
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(2),
            RequestedSlotEndUtc = DateTimeOffset.UtcNow.AddHours(3),
            ProviderName = "مزود",
            BranchName = "فرع",
            Currency = "ILS",
            GrandTotal = 123.45m,
            TotalDurationMinutes = 60,
            Items =
            [
                new ConfirmedBookingItemResponse
                {
                    OfferingSourceId = Guid.NewGuid(),
                    ServiceName = "خدمة",
                    ItemSubtotal = 123.45m,
                    TotalDurationMinutes = 60
                }
            ]
        };
        var service = new Mock<IBookingConfirmationService>();
        service.Setup(value => value.ConfirmAsync(
                orderGuid,
                It.Is<ConfirmBookingFromDraftRequest>(request =>
                    request.ExpectedVersion == 7 &&
                    request.CancellationPolicyAcknowledged),
                device.Id,
                userId,
                "ar",
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        await using var factory = new CheckoutDraftApiFactory(
            devices: [device],
            configureTestServices: services =>
            {
                services.RemoveAll<IBookingConfirmationService>();
                services.AddSingleton(service.Object);
            });
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=ar");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(userId));
        request.Headers.Add("X-Device-Token", token);
        request.Headers.Add("X-Order-Guid", orderGuid.ToString("D"));
        request.Content = JsonContent.Create(new ConfirmBookingFromDraftRequest
        {
            ExpectedVersion = 7,
            CancellationPolicyAcknowledged = true
        });

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.EnumerateObject().Select(value => value.Name)
            .Should().BeEquivalentTo(
                "id", "reference", "orderGuid", "businessReservationId",
                "businessWorkOrderId", "status", "draftVersion", "language",
                "requestedSlotStartUtc", "requestedSlotEndUtc", "providerName",
                "branchName", "currency", "grandTotal", "totalDurationMinutes", "items");
        document.RootElement.GetProperty("reference").GetGuid().Should().Be(expected.Reference);
        document.RootElement.GetProperty("grandTotal").GetDecimal().Should().Be(123.45m);
        document.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
        service.VerifyAll();
    }

    [Fact]
    [Trait("ScenarioId", "STEP14-REGRESSION-INTERNAL-BOOKING-READ-143")]
    public async Task SignedInternalRead_IsStableAcrossPaymentLifecycleAndExcludesSecrets()
    {
        await using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        Guid bookingId;
        Guid reference;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await context.CustomerBookings.SingleAsync();
            bookingId = booking.Id;
            reference = booking.PublicReference;
            context.CustomerPayments.Add(CreatePayment(booking, PaymentStatus.Pending));
            booking.PaymentState = "Pending";
            await context.SaveChangesAsync();
        }

        var pendingBody = await ReadInternalBookingAsync(client, reference);
        await SetPaymentLifecycleAsync(factory, bookingId, PaymentStatus.Completed, true, "Completed");
        var completedBody = await ReadInternalBookingAsync(client, reference);
        await SetPaymentLifecycleAsync(factory, bookingId, PaymentStatus.Refunded, false, "Refunded");
        var refundedBody = await ReadInternalBookingAsync(client, reference);

        completedBody.Should().Be(pendingBody);
        refundedBody.Should().Be(pendingBody);
        using var document = JsonDocument.Parse(pendingBody);
        document.RootElement.EnumerateObject().Select(value => value.Name)
            .Should().BeEquivalentTo(
                "contractVersion", "bookingReference", "reservationId", "workOrderId",
                "status", "sequence", "changedAtUtc");
        pendingBody.ToLowerInvariant().Should().NotContain("payment");
        pendingBody.ToLowerInvariant().Should().NotContain("secret");
        pendingBody.ToLowerInvariant().Should().NotContain("lahza");
    }

    private static CustomerBooking CreateCustomerBooking()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"step14-{Guid.NewGuid():N}@example.com",
            Email = $"step14-{Guid.NewGuid():N}@example.com",
            FullName = "Step 14 Customer"
        };
        return new CustomerBooking
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            User = user,
            UserId = user.Id,
            OwnerDeviceId = Guid.NewGuid(),
            BusinessReservationId = Guid.NewGuid(),
            BusinessWorkOrderId = Guid.NewGuid(),
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = Guid.NewGuid(),
            CatalogVersion = 17,
            ConfirmedDraftVersion = 4,
            Status = BookingStatuses.Pending,
            StatusChangedAtUtc = DateTimeOffset.UtcNow,
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(2),
            RequestedSlotEndUtc = DateTimeOffset.UtcNow.AddHours(3),
            ProviderNameAr = "مزود",
            ProviderNameHe = "ספק",
            BranchNameAr = "فرع",
            BranchNameHe = "סניף",
            VehicleType = "Sedan",
            LicensePlate = "12-345-67",
            VehicleMake = "Make",
            VehicleModel = "Model",
            VehicleColor = "Blue",
            AddressLine = "Address",
            City = "City",
            Area = "Area",
            Latitude = 32.1m,
            Longitude = 34.8m,
            Currency = "ILS",
            BaseSubtotal = 100m,
            AddonSubtotal = 10m,
            ItemSubtotal = 110m,
            ServiceFee = 5m,
            ServiceFeeMode = "Flat",
            ServiceFeeFlatAmount = 5m,
            ServiceFeePercentageRate = 0m,
            TaxableSubtotal = 115m,
            TaxRatePercent = 7.347826m,
            TaxAppliesToServiceFee = true,
            Tax = 8.45m,
            GrandTotal = 123.45m,
            IsPaid = false,
            PaymentState = "Unpaid",
            TotalDurationMinutes = 60,
            QuotedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Items =
            [
                new CustomerBookingItem
                {
                    OfferingSourceId = Guid.NewGuid(),
                    ServiceNameAr = "خدمة",
                    ServiceNameHe = "שירות",
                    DisplayOrder = 0,
                    BaseSubtotal = 100m,
                    AddonSubtotal = 10m,
                    ItemSubtotal = 110m,
                    TotalDurationMinutes = 60,
                    Selections =
                    [
                        new CustomerBookingSelection
                        {
                            AddonGroupSourceId = Guid.NewGuid(),
                            AddonChoiceSourceId = Guid.NewGuid(),
                            AddonGroupNameAr = "مجموعة",
                            AddonChoiceNameAr = "إضافة",
                            SelectionType = "SingleChoice",
                            Quantity = 1,
                            UnitPriceAdjustment = 10m,
                            TotalPriceAdjustment = 10m,
                            UnitDurationAdjustmentMinutes = 5,
                            TotalDurationAdjustmentMinutes = 5,
                            DisplayOrder = 0
                        }
                    ]
                }
            ]
        };
    }

    private static CustomerPayment CreatePayment(
        CustomerBooking booking,
        PaymentStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerBooking = booking,
            CustomerBookingId = booking.Id,
            UserId = booking.UserId,
            OwnerDeviceId = booking.OwnerDeviceId,
            Amount = booking.GrandTotal,
            MinorAmount = 12345,
            Currency = booking.Currency,
            Method = PaymentMethod.Card,
            Status = status,
            IdempotencyKey = "step14-regression",
            RequestHash = new string('a', 64),
            Provider = PaymentProviders.Lahza,
            ProviderReference = "GHSEELI-STEP14-BOOKING-REGRESSION",
            ProviderTransactionId = status is PaymentStatus.Completed or PaymentStatus.Refunded
                ? "ch_step14"
                : null,
            ProviderStatus = status.ToString(),
            CheckoutUrl = "https://checkout.lahza.test/pay/GHSEELI-STEP14-BOOKING-REGRESSION",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

    private static IOptionsMonitor<LahzaConfigurationOptions> Options(
        string publishableKey,
        string secretKey)
    {
        var options = new Mock<IOptionsMonitor<LahzaConfigurationOptions>>();
        options.SetupGet(value => value.CurrentValue).Returns(new LahzaConfigurationOptions
        {
            SecretKey = secretKey
        });
        return options.Object;
    }

    private static async Task ApplyWebhookAsync(
        SqlServerCatalogDatabase database,
        string eventId,
        PaymentEventKind kind,
        string eventType,
        string? chargeId = null)
    {
        await using var context = database.CreateContext();
        var payment = await context.CustomerPayments
            .Include(value => value.CustomerBooking)
            .SingleAsync();
        var service = new PaymentWebhookService(
            context,
            new TestAppLogger(),
            TimeProvider.System);
        await service.ProcessAsync(
            new VerifiedPaymentEvent(
                eventId,
                eventType,
                kind,
                payment.ProviderReference!,
                chargeId,
                12345,
                "ILS"),
            Encoding.UTF8.GetBytes($"{{\"id\":\"{eventId}\"}}"),
            default);
    }

    private static async Task<BookingSnapshot> ReadSnapshotAsync(
        SqlServerCatalogDatabase database,
        Guid bookingId)
    {
        await using var context = database.CreateContext();
        var booking = await context.CustomerBookings.AsNoTracking()
            .Include(value => value.Items)
            .ThenInclude(value => value.Selections)
            .SingleAsync(value => value.Id == bookingId);
        return new BookingSnapshot(
            new
            {
                booking.PublicReference,
                booking.OrderGuid,
                booking.BusinessReservationId,
                booking.BusinessWorkOrderId,
                booking.BusinessSourceId,
                booking.BranchSourceId,
                booking.CatalogVersion,
                booking.ConfirmedDraftVersion,
                booking.ProviderNameAr,
                booking.ProviderNameHe,
                booking.BranchNameAr,
                booking.BranchNameHe,
                booking.VehicleType,
                booking.LicensePlate,
                booking.VehicleMake,
                booking.VehicleModel,
                booking.VehicleColor,
                booking.AddressLine,
                booking.City,
                booking.Area,
                booking.Latitude,
                booking.Longitude,
                booking.Currency,
                booking.BaseSubtotal,
                booking.AddonSubtotal,
                booking.ItemSubtotal,
                booking.ServiceFee,
                booking.ServiceFeeMode,
                booking.ServiceFeeFlatAmount,
                booking.ServiceFeePercentageRate,
                booking.TaxableSubtotal,
                booking.TaxRatePercent,
                booking.TaxAppliesToServiceFee,
                booking.Tax,
                booking.GrandTotal,
                booking.TotalDurationMinutes,
                Items = booking.Items.OrderBy(value => value.DisplayOrder).Select(item => new
                {
                    item.OfferingSourceId,
                    item.ServiceNameAr,
                    item.ServiceNameHe,
                    item.BaseSubtotal,
                    item.AddonSubtotal,
                    item.ItemSubtotal,
                    item.TotalDurationMinutes,
                    Selections = item.Selections.OrderBy(value => value.DisplayOrder).Select(selection => new
                    {
                        selection.AddonGroupSourceId,
                        selection.AddonChoiceSourceId,
                        selection.AddonGroupNameAr,
                        selection.AddonGroupNameHe,
                        selection.AddonChoiceNameAr,
                        selection.AddonChoiceNameHe,
                        selection.SelectionType,
                        selection.Quantity,
                        selection.UnitPriceAdjustment,
                        selection.TotalPriceAdjustment,
                        selection.UnitDurationAdjustmentMinutes,
                        selection.TotalDurationAdjustmentMinutes,
                        selection.IsDefaultApplied
                    }).ToArray()
                }).ToArray()
            },
            booking.IsPaid,
            booking.PaymentState);
    }

    private static async Task SeedAsync(
        CheckoutDraftApiFactory factory,
        CustomerBooking booking,
        CustomerPayment payment)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Users.Add(booking.User);
        context.CustomerBookings.Add(booking);
        context.CustomerPayments.Add(payment);
        await context.SaveChangesAsync();
    }

    private static async Task<PaymentAuthority> ReadAuthorityAsync(
        WebApplicationFactory<Program> factory,
        Guid bookingId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await context.CustomerBookings.AsNoTracking()
            .Include(value => value.Payment)
            .SingleAsync(value => value.Id == bookingId);
        return new PaymentAuthority(
            booking.GrandTotal,
            booking.Currency,
            booking.IsPaid,
            booking.PaymentState,
            booking.Payment!.Amount,
            booking.Payment.Currency,
            booking.Payment.Status,
            booking.Payment.ProviderReference,
            booking.Payment.ProviderTransactionId,
            booking.Payment.CheckoutUrl);
    }

    private static HttpRequestMessage CreateSignedStatusRequest(
        HttpClient client,
        BookingStatusChangedMessage message)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        var uri = new Uri(client.BaseAddress!, "/api/v1/internal/bookings/status");
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var key = $"step14-{Guid.NewGuid():N}";
        var canonical = InternalServiceCanonicalRequest.Build(
            BookingStatusCallbackFactory.ServiceId,
            "POST",
            uri.AbsolutePath,
            [],
            timestamp,
            nonce,
            key,
            InternalServiceCanonicalRequest.ComputeSha256Hex(body));
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(BookingStatusCallbackFactory.Secret));
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        AddInternalHeaders(
            request,
            timestamp,
            nonce,
            Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant(),
            key);
        return request;
    }

    private static async Task<string> ReadInternalBookingAsync(
        HttpClient client,
        Guid reference)
    {
        var uri = new Uri(
            client.BaseAddress!,
            $"/api/v1/internal/bookings/{reference:D}");
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var canonical = InternalServiceCanonicalRequest.Build(
            BookingStatusCallbackFactory.ServiceId,
            "GET",
            uri.AbsolutePath,
            [],
            timestamp,
            nonce,
            string.Empty,
            InternalServiceCanonicalRequest.ComputeSha256Hex([]));
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(BookingStatusCallbackFactory.Secret));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddInternalHeaders(
            request,
            timestamp,
            nonce,
            Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant());
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static void AddInternalHeaders(
        HttpRequestMessage request,
        string timestamp,
        string nonce,
        string signature,
        string? idempotencyKey = null)
    {
        request.Headers.Add(
            InternalServiceWireConstants.ServiceIdHeaderName,
            BookingStatusCallbackFactory.ServiceId);
        request.Headers.Add(InternalServiceWireConstants.TimestampHeaderName, timestamp);
        request.Headers.Add(InternalServiceWireConstants.NonceHeaderName, nonce);
        request.Headers.Add(InternalServiceWireConstants.SignatureHeaderName, signature);
        request.Headers.Add(InternalServiceWireConstants.CorrelationIdHeaderName, "corr-step14");
        if (idempotencyKey is not null)
        {
            request.Headers.Add(
                InternalServiceWireConstants.IdempotencyKeyHeaderName,
                idempotencyKey);
        }
    }

    private static async Task SetPaymentLifecycleAsync(
        WebApplicationFactory<Program> factory,
        Guid bookingId,
        PaymentStatus status,
        bool isPaid,
        string paymentState)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await context.CustomerBookings
            .Include(value => value.Payment)
            .SingleAsync(value => value.Id == bookingId);
        booking.IsPaid = isPaid;
        booking.PaymentState = paymentState;
        booking.Payment!.Status = status;
        await context.SaveChangesAsync();
    }

    private static string CreateJwt(Guid userId)
    {
        var token = new JwtSecurityToken(
            issuer: "GhseeliApis.CheckoutDraftTests",
            audience: "GhseeliApis.CheckoutDraftClients",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "User")
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record BookingSnapshot(object Immutable, bool IsPaid, string PaymentState);

    private sealed record PaymentAuthority(
        decimal BookingGrandTotal,
        string BookingCurrency,
        bool IsPaid,
        string PaymentState,
        decimal PaymentAmount,
        string PaymentCurrency,
        PaymentStatus Status,
        string? ProviderReference,
        string? ProviderTransactionId,
        string? CheckoutUrl);
}
