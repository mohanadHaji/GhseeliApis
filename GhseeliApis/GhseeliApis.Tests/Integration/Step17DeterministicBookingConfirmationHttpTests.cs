using System.Collections.Concurrent;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.DTOs.Booking;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Business;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Executes the Step 17 deterministic booking-confirmation quality gate over HTTP.
/// </summary>
public sealed class Step17DeterministicBookingConfirmationHttpTests
{
    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-019")]
    public async Task STEP17_DET_BOOK_019_StaleConfirmationVersionCreatesNothing()
    {
        await using var fixture = await CustomerConfirmationFixture.CreateAsync();

        using var response = await fixture.ConfirmAsync(
            fixture.DraftVersion - 1,
            policyAcknowledged: true);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "checkout_draft_version_conflict");
        fixture.CustomerBusinessClient.CreateReservationRequests.Should().Be(0);
        (await fixture.CountCustomerBookingsAsync()).Should().Be(0);
        (await fixture.CountConfirmationAttemptsAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-020")]
    public async Task STEP17_DET_BOOK_020_MissingPolicyAcknowledgementIsRejected()
    {
        await using var fixture = await CustomerConfirmationFixture.CreateAsync();

        using var response = await fixture.ConfirmAsync(
            fixture.DraftVersion,
            policyAcknowledged: false);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "booking_request_invalid");
        fixture.CustomerBusinessClient.CreateReservationRequests.Should().Be(0);
        (await fixture.CountCustomerBookingsAsync()).Should().Be(0);
        (await fixture.CountConfirmationAttemptsAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-021")]
    public async Task STEP17_DET_BOOK_021_SlotUnavailableMapsToReservationRejected()
    {
        await using var fixture = await CustomerConfirmationFixture.CreateAsync();
        fixture.CustomerBusinessClient.CreateReservationHandler = (_, _, _) =>
            throw new BusinessApiConflictException(
                "The slot is unavailable.",
                "step17-book-021",
                ReservationErrorCodes.SlotUnavailable);

        using var response = await fixture.ConfirmAsync(
            fixture.DraftVersion,
            policyAcknowledged: true);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "booking_reservation_rejected",
            ReservationErrorCodes.SlotUnavailable);
        (await fixture.CountCustomerBookingsAsync()).Should().Be(0);
        (await fixture.CountConfirmationAttemptsAsync()).Should().Be(0);
        fixture.Backend.ReservationCount.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-022")]
    public async Task STEP17_DET_BOOK_022_PriceChangedUsesResolvedCustomerContract()
    {
        await using var fixture = await CustomerConfirmationFixture.CreateAsync();
        fixture.CustomerBusinessClient.CreateReservationHandler = (_, _, _) =>
            throw new BusinessApiConflictException(
                "The authoritative price changed.",
                "step17-book-022",
                ReservationErrorCodes.PriceChanged);

        using var response = await fixture.ConfirmAsync(
            fixture.DraftVersion,
            policyAcknowledged: true);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "checkout_draft_requires_reprice",
            ReservationErrorCodes.PriceChanged);
        (await fixture.CountCustomerBookingsAsync()).Should().Be(0);
        (await fixture.CountConfirmationAttemptsAsync()).Should().Be(0);
        fixture.Backend.ReservationCount.Should().Be(0);
        var state = await fixture.ReadDraftStateAsync();
        state.RequiresReprice.Should().BeTrue();
        state.PublicVersion.Should().Be(fixture.DraftVersion + 1);
        state.HasPricingSnapshot.Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-023")]
    public async Task STEP17_DET_BOOK_023_ConcurrentIdenticalConfirmationsReplayOneBooking()
    {
        await using var fixture = await CustomerConfirmationFixture.CreateAsync();
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var first = fixture.ConfirmAsync(
            fixture.DraftVersion,
            policyAcknowledged: true,
            release.Task);
        var second = fixture.ConfirmAsync(
            fixture.DraftVersion,
            policyAcknowledged: true,
            release.Task);
        release.TrySetResult();

        using var firstResponse = await first;
        using var secondResponse = await second;
        var firstBody = await ReadJsonAsync(firstResponse);
        var secondBody = await ReadJsonAsync(secondResponse);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, firstBody.RootElement.ToString());
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK, secondBody.RootElement.ToString());
        secondBody.RootElement.GetProperty("reference").GetGuid()
            .Should().Be(firstBody.RootElement.GetProperty("reference").GetGuid());
        secondBody.RootElement.GetProperty("businessReservationId").GetGuid()
            .Should().Be(firstBody.RootElement.GetProperty("businessReservationId").GetGuid());
        secondBody.RootElement.GetProperty("businessWorkOrderId").GetGuid()
            .Should().Be(firstBody.RootElement.GetProperty("businessWorkOrderId").GetGuid());
        (await fixture.CountConfirmationAttemptsAsync()).Should().Be(1);
        (await fixture.CountCustomerBookingsAsync()).Should().Be(1);
        fixture.Backend.ReservationCount.Should().Be(1);
        fixture.Backend.WorkOrderCount.Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-024")]
    public async Task STEP17_DET_BOOK_024_AcceptedBusinessReservationSurvivesCustomerCommitFailure()
    {
        await using var fixture = await CustomerConfirmationFixture.CreateAsync(
            failCustomerBookingCommitOnce: true);

        using var response = await fixture.ConfirmAsync(
            fixture.DraftVersion,
            policyAcknowledged: true);

        await AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "booking_confirmation_unavailable");
        fixture.Backend.ReservationCount.Should().Be(1);
        fixture.Backend.WorkOrderCount.Should().Be(1);
        (await fixture.CountCustomerBookingsAsync()).Should().Be(0);
        (await fixture.CountConfirmationAttemptsAsync()).Should().Be(1);
        var state = await fixture.ReadDraftStateAsync();
        state.ClaimedVersion.Should().Be(fixture.DraftVersion);
        state.ClaimedReference.Should().NotBeNull();
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-025")]
    public async Task STEP17_DET_BOOK_025_RetryAfterAmbiguousAcceptConvergesExactlyOnce()
    {
        await using var fixture = await CustomerConfirmationFixture.CreateAsync(
            failCustomerBookingCommitOnce: true);

        using var ambiguous = await fixture.ConfirmAsync(
            fixture.DraftVersion,
            policyAcknowledged: true);
        await AssertProblemAsync(
            ambiguous,
            HttpStatusCode.ServiceUnavailable,
            "booking_confirmation_unavailable");

        using var recovered = await fixture.ConfirmAsync(
            fixture.DraftVersion,
            policyAcknowledged: true);
        var body = await ReadJsonAsync(recovered);

        recovered.StatusCode.Should().Be(HttpStatusCode.OK, body.RootElement.ToString());
        var accepted = fixture.Backend.SingleReservation;
        body.RootElement.GetProperty("reference").GetGuid().Should().Be(accepted.BookingReference);
        body.RootElement.GetProperty("businessReservationId").GetGuid()
            .Should().Be(accepted.ReservationId);
        body.RootElement.GetProperty("businessWorkOrderId").GetGuid()
            .Should().Be(accepted.WorkOrderId);
        fixture.Backend.ReservationCount.Should().Be(1);
        fixture.Backend.WorkOrderCount.Should().Be(1);
        (await fixture.CountCustomerBookingsAsync()).Should().Be(1);
        (await fixture.CountConfirmationAttemptsAsync()).Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-026")]
    public async Task STEP17_DET_BOOK_026_ConcurrentReservationCapacityOneHasSingleWinner()
    {
        await using var business = await ReflectionBusinessReservationFactory.CreateAsync();
        var firstRequest = business.CreateReservationRequest();
        var secondRequest = business.CreateReservationRequest();

        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = business.SendReservationAsync(
            firstRequest,
            $"step17-book-026-a-{Guid.NewGuid():N}",
            release.Task);
        var second = business.SendReservationAsync(
            secondRequest,
            $"step17-book-026-b-{Guid.NewGuid():N}",
            release.Task);
        release.TrySetResult();

        using var firstResponse = await first;
        using var secondResponse = await second;
        var success = new[] { firstResponse, secondResponse }
            .Single(response => response.StatusCode == HttpStatusCode.OK);
        var rejection = new[] { firstResponse, secondResponse }
            .Single(response => response.StatusCode == HttpStatusCode.Conflict);

        (await success.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();
        await AssertProblemAsync(
            rejection,
            HttpStatusCode.Conflict,
            ReservationErrorCodes.SlotUnavailable);
        (await business.CountRowsAsync("AppointmentReservations")).Should().Be(1);
        (await business.CountRowsAsync("WorkOrders")).Should().Be(1);
        (await business.CountRowsAsync("WorkOrderItems")).Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-027")]
    public async Task STEP17_DET_BOOK_027_ReorderedReservationReplayIsCanonical()
    {
        await using var business = await ReflectionBusinessReservationFactory.CreateAsync();
        var request = business.CreateReservationRequest(includeSecondItem: true);
        var reordered = Clone(request);
        reordered.Items = reordered.Items.Reverse().ToArray();
        foreach (var item in reordered.Items)
        {
            item.SelectedAddons = item.SelectedAddons.Reverse().ToArray();
        }

        using var firstResponse = await business.SendReservationAsync(
            request,
            $"step17-book-027-a-{Guid.NewGuid():N}");
        using var replayResponse = await business.SendReservationAsync(
            reordered,
            $"step17-book-027-b-{Guid.NewGuid():N}");
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var replayBody = await replayResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, firstBody);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK, replayBody);
        var firstResult = JsonSerializer.Deserialize<CreateReservationResponse>(
            firstBody,
            ContractJsonOptions);
        var replayResult = JsonSerializer.Deserialize<CreateReservationResponse>(
            replayBody,
            ContractJsonOptions);
        replayResult.Should().BeEquivalentTo(firstResult);
        (await business.CountRowsAsync("AppointmentReservations")).Should().Be(1);
        (await business.CountRowsAsync("WorkOrders")).Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-BOOK-028")]
    public async Task STEP17_DET_BOOK_028_ChangedSemanticOrderConflicts()
    {
        await using var business = await ReflectionBusinessReservationFactory.CreateAsync();
        var request = business.CreateReservationRequest();
        var conflicting = Clone(request);
        conflicting.Items.Single().SelectedAddons.First().Quantity++;

        using var firstResponse = await business.SendReservationAsync(
            request,
            $"step17-book-028-a-{Guid.NewGuid():N}");
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var originalHash = await business.ReadSingleStringAsync(
            "SELECT [RequestHash] FROM [dbo].[AppointmentReservations]");

        using var conflictResponse = await business.SendReservationAsync(
            conflicting,
            $"step17-book-028-b-{Guid.NewGuid():N}");

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, firstBody);
        await AssertProblemAsync(
            conflictResponse,
            HttpStatusCode.Conflict,
            ReservationErrorCodes.Invalid);
        (await business.CountRowsAsync("AppointmentReservations")).Should().Be(1);
        (await business.CountRowsAsync("WorkOrders")).Should().Be(1);
        (await business.ReadSingleStringAsync(
                "SELECT [RequestHash] FROM [dbo].[AppointmentReservations]"))
            .Should().Be(originalHash);
    }

    private static CreateReservationRequest Clone(CreateReservationRequest request) =>
        JsonSerializer.Deserialize<CreateReservationRequest>(
            JsonSerializer.Serialize(request, ContractJsonOptions),
            ContractJsonOptions)!;

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string? businessErrorCode = null)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, payload);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("code").GetString().Should().Be(code);
        if (businessErrorCode is not null)
        {
            document.RootElement.GetProperty("businessErrorCode")
                .GetString().Should().Be(businessErrorCode);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload);
    }

    private static readonly JsonSerializerOptions ContractJsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    private sealed class CustomerConfirmationFixture : IAsyncDisposable
    {
        private const string JwtSecret =
            "CheckoutDraftApiTestsSecret_Minimum32Chars";
        private readonly CheckoutDraftApiFactory _factory;
        private readonly HttpClient _client;
        private readonly Guid _userId;
        private readonly string _deviceToken;
        private readonly Guid _orderGuid;

        private CustomerConfirmationFixture(
            CheckoutDraftApiFactory factory,
            HttpClient client,
            Guid userId,
            string deviceToken,
            Guid orderGuid,
            int draftVersion,
            DeterministicReservationBackend backend)
        {
            _factory = factory;
            _client = client;
            _userId = userId;
            _deviceToken = deviceToken;
            _orderGuid = orderGuid;
            DraftVersion = draftVersion;
            Backend = backend;
        }

        public int DraftVersion { get; }
        public DeterministicReservationBackend Backend { get; }
        public ScriptedBusinessApiClient CustomerBusinessClient =>
            _factory.BusinessApiClient;

        public static async Task<CustomerConfirmationFixture> CreateAsync(
            bool failCustomerBookingCommitOnce = false)
        {
            var now = new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero);
            var companyId = Guid.NewGuid();
            var branchId = Guid.NewGuid();
            var offeringId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var choiceId = Guid.NewGuid();
            var snapshot = CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 17,
                branchId: branchId,
                offeringId: offeringId,
                addonGroupId: groupId,
                addonChoiceId: choiceId);
            var token = CatalogTestSupport.CreateToken(117);
            var device = CatalogTestSupport.CreateDevice(
                token,
                now.AddDays(30));
            var commitFailpoint = new FailCustomerBookingCommitOnceInterceptor();
            var factory = new CheckoutDraftApiFactory(
                snapshot,
                [device],
                utcNow: now,
                configureTestServices: services =>
                {
                    using var provider = services.BuildServiceProvider();
                    using var configuredContext =
                        provider.GetRequiredService<ApplicationDbContext>();
                    var connectionString =
                        configuredContext.Database.GetConnectionString()
                        ?? throw new InvalidOperationException(
                            "Customer relational connection string is unavailable.");

                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options =>
                    {
                        options.UseSqlServer(connectionString, sql =>
                        {
                            sql.EnableRetryOnFailure(5);
                            sql.CommandTimeout(60);
                            sql.UseCompatibilityLevel(120);
                        });
                        options.AddInterceptors(commitFailpoint);
                    });
                });
            var client = factory.CreateApiClient();
            var userId = Guid.NewGuid();
            var orderGuid = Guid.NewGuid();
            const int version = 4;
            var backend = new DeterministicReservationBackend(groupId);

            factory.BusinessApiClient.CreateReservationHandler =
                (request, _, _) => Task.FromResult(backend.Accept(request));

            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Users.Add(new User
            {
                Id = userId,
                UserName = $"step17-book-{userId:N}@example.test",
                NormalizedUserName = $"STEP17-BOOK-{userId:N}@EXAMPLE.TEST",
                Email = $"step17-book-{userId:N}@example.test",
                NormalizedEmail = $"STEP17-BOOK-{userId:N}@EXAMPLE.TEST",
                FullName = "Step 17 Customer",
                IsActive = true
            });
            context.CheckoutDrafts.Add(CreatePricedDraft(
                orderGuid,
                device.Id,
                snapshot,
                now,
                version));
            await context.SaveChangesAsync();
            commitFailpoint.Enabled = failCustomerBookingCommitOnce;

            return new CustomerConfirmationFixture(
                factory,
                client,
                userId,
                token,
                orderGuid,
                version,
                backend);
        }

        public async Task<HttpResponseMessage> ConfirmAsync(
            int expectedVersion,
            bool policyAcknowledged,
            Task? startBarrier = null)
        {
            if (startBarrier is not null)
            {
                await startBarrier;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/bookings/from-draft?language=ar");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                CreateJwt(_userId));
            request.Headers.TryAddWithoutValidation("X-Device-Token", _deviceToken);
            request.Headers.TryAddWithoutValidation(
                "X-Order-Guid",
                _orderGuid.ToString("D"));
            request.Content = JsonContent.Create(new ConfirmBookingFromDraftRequest
            {
                ExpectedVersion = expectedVersion,
                CancellationPolicyAcknowledged = policyAcknowledged
            });
            return await _client.SendAsync(request);
        }

        public async Task<int> CountCustomerBookingsAsync()
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>()
                .CustomerBookings.CountAsync();
        }

        public async Task<int> CountConfirmationAttemptsAsync()
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>()
                .BookingConfirmationAttempts.CountAsync();
        }

        public async Task<DraftState> ReadDraftStateAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await context.CheckoutDrafts
                .AsNoTracking()
                .Where(draft => draft.OrderGuid == _orderGuid)
                .Select(draft => new DraftState(
                    draft.PublicVersion,
                    draft.RequiresReprice,
                    draft.PricingSnapshot != null,
                    draft.ConfirmationClaimedVersion,
                    draft.ConfirmationBookingReference))
                .SingleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _factory.DisposeAsync();
        }

        private static CheckoutDraft CreatePricedDraft(
            Guid orderGuid,
            Guid deviceId,
            CatalogSnapshotResponse snapshot,
            DateTimeOffset now,
            int version)
        {
            var offering = snapshot.Categories.Single().Offerings.Single();
            var group = offering.AddonGroups.Single();
            var choice = group.Choices.Single();
            var baseSubtotal = offering.BasePrice;
            var addonSubtotal = choice.PriceAdjustment;
            var itemSubtotal = baseSubtotal + addonSubtotal;
            var duration = offering.DurationMinutes +
                choice.DurationAdjustmentMinutes;

            return new CheckoutDraft
            {
                Id = Guid.NewGuid(),
                OrderGuid = orderGuid,
                OwnerDeviceId = deviceId,
                BusinessSourceId = snapshot.Company.Id,
                BranchSourceId = snapshot.Branches.Single().Id,
                CatalogVersion = snapshot.CatalogVersion,
                PublicVersion = version,
                RequestedSlotStartUtc = now.AddHours(2),
                VehicleType = "Sedan",
                LicensePlate = "12-345-67",
                VehicleMake = "Toyota",
                VehicleModel = "Corolla",
                VehicleColor = "Blue",
                AddressLine = "Street 17",
                City = "Haifa",
                Area = "Carmel",
                Latitude = 32.1m,
                Longitude = 34.8m,
                RequiresReprice = false,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now.AddMinutes(30),
                PricingSnapshot = new CheckoutDraftPricingSnapshot
                {
                    Id = Guid.NewGuid(),
                    CatalogVersion = snapshot.CatalogVersion,
                    Currency = "ILS",
                    QuotedAtUtc = now,
                    BaseSubtotal = baseSubtotal,
                    AddonSubtotal = addonSubtotal,
                    ItemSubtotal = itemSubtotal,
                    ServiceFee = 0m,
                    ServiceFeeMode = "None",
                    ServiceFeeFlatAmount = 0m,
                    ServiceFeePercentageRate = 0m,
                    TaxableSubtotal = itemSubtotal,
                    TaxRatePercent = 0m,
                    TaxAppliesToServiceFee = false,
                    Tax = 0m,
                    GrandTotal = itemSubtotal,
                    TotalDurationMinutes = duration,
                    Items =
                    [
                        new CheckoutDraftPricingItemSnapshot
                        {
                            Id = Guid.NewGuid(),
                            OfferingSourceId = offering.Id,
                            DisplayOrder = 0,
                            BaseSubtotal = baseSubtotal,
                            AddonSubtotal = addonSubtotal,
                            ItemSubtotal = itemSubtotal,
                            TotalDurationMinutes = duration,
                            Selections =
                            [
                                new CheckoutDraftPricingSelectionSnapshot
                                {
                                    Id = Guid.NewGuid(),
                                    AddonGroupSourceId = group.Id,
                                    AddonChoiceSourceId = choice.Id,
                                    SelectionType = group.SelectionType,
                                    Quantity = 1,
                                    UnitPriceAdjustment = choice.PriceAdjustment,
                                    TotalPriceAdjustment = choice.PriceAdjustment,
                                    UnitDurationAdjustmentMinutes =
                                        choice.DurationAdjustmentMinutes,
                                    TotalDurationAdjustmentMinutes =
                                        choice.DurationAdjustmentMinutes,
                                    DisplayOrder = 0
                                }
                            ]
                        }
                    ]
                }
            };
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
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, "User")
                ],
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: credentials));
        }
    }

    private sealed class DeterministicReservationBackend(Guid addonGroupId)
    {
        private readonly ConcurrentDictionary<Guid, CreateReservationResponse> _reservations =
            new();

        public int ReservationCount => _reservations.Count;
        public int WorkOrderCount => _reservations.Count;
        public CreateReservationResponse SingleReservation => _reservations.Values.Single();

        public CreateReservationResponse Accept(CreateReservationRequest request) =>
            _reservations.GetOrAdd(request.OrderGuid, _ => new CreateReservationResponse
            {
                BookingReference = request.BookingReference,
                ReservationId = Guid.NewGuid(),
                WorkOrderId = Guid.NewGuid(),
                Status = ReservationStatuses.Pending,
                CatalogVersion = request.ExpectedCatalogVersion,
                Currency = request.Currency,
                ItemSubtotal = request.ExpectedItemSubtotal,
                TotalDurationMinutes = request.ExpectedTotalDurationMinutes,
                RequestedSlotStartUtc = request.RequestedSlotStartUtc,
                RequestedSlotEndUtc = request.RequestedSlotStartUtc.AddMinutes(
                    request.ExpectedTotalDurationMinutes),
                Items = request.Items.Select(item => new ReservationAcceptedItem
                {
                    OfferingId = item.OfferingId,
                    BaseSubtotal = item.ExpectedBaseSubtotal,
                    AddonSubtotal = item.ExpectedAddonSubtotal,
                    ItemSubtotal = item.ExpectedItemSubtotal,
                    TotalDurationMinutes = item.ExpectedDurationMinutes,
                    Selections = item.SelectedAddons.Select(selection =>
                        new NormalizedAddonSelection
                        {
                            AddonGroupId = addonGroupId,
                            AddonChoiceId = selection.AddonChoiceId,
                            SelectionType = "MultipleChoice",
                            Quantity = selection.Quantity,
                            UnitPriceAdjustment = 9.5m,
                            TotalPriceAdjustment = 9.5m * selection.Quantity,
                            UnitDurationAdjustmentMinutes = 5,
                            TotalDurationAdjustmentMinutes = 5 * selection.Quantity
                        }).ToArray()
                }).ToArray()
            });
    }

    private sealed record DraftState(
        int PublicVersion,
        bool RequiresReprice,
        bool HasPricingSnapshot,
        int? ClaimedVersion,
        Guid? ClaimedReference);

    private sealed class FailCustomerBookingCommitOnceInterceptor :
        SaveChangesInterceptor
    {
        private int _failed;

        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled &&
                eventData.Context is not null &&
                eventData.Context.ChangeTracker
                    .Entries<CustomerBooking>()
                    .Any(entry => entry.State == EntityState.Added) &&
                Interlocked.CompareExchange(ref _failed, 1, 0) == 0)
            {
                throw new DbUpdateException(
                    "Step 17 injected Customer booking commit failure.");
            }

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed class ReflectionBusinessReservationFactory : IAsyncDisposable
    {
        private const string ServiceId = "step17-customer-api";
        private const string Secret =
            "step17-business-reservation-secret-at-least-32-characters";
        private readonly string _databaseName =
            $"GhseeliStep17Booking_{Guid.NewGuid():N}";
        private readonly Assembly _businessAssembly;
        private readonly Type _dbContextType;
        private readonly object _rootFactory;
        private readonly object _configuredFactory;
        private readonly IServiceProvider _services;
        private readonly HttpClient _client;

        private ReflectionBusinessReservationFactory(
            Assembly businessAssembly,
            Type dbContextType,
            object rootFactory,
            object configuredFactory,
            IServiceProvider services,
            HttpClient client,
            string databaseName,
            Guid branchId,
            DateTimeOffset slotStartUtc)
        {
            _businessAssembly = businessAssembly;
            _dbContextType = dbContextType;
            _rootFactory = rootFactory;
            _configuredFactory = configuredFactory;
            _services = services;
            _client = client;
            _databaseName = databaseName;
            BranchId = branchId;
            SlotStartUtc = slotStartUtc;
        }

        public Guid BranchId { get; }
        public DateTimeOffset SlotStartUtc { get; }

        private string ConnectionString =>
            $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

        public static async Task<ReflectionBusinessReservationFactory> CreateAsync()
        {
            var businessAssemblyPath = FindBusinessAssembly();
            CopyBusinessDependencyContext(businessAssemblyPath);
            var businessAssembly = BusinessAssemblyLoader.LoadFrom(
                businessAssemblyPath);
            var programType = businessAssembly.GetType("Program", throwOnError: true)!;
            var dbContextType = businessAssembly.GetType(
                "Ghseeli.BusinessApi.Persistence.BusinessDbContext",
                throwOnError: true)!;
            var factoryType = typeof(WebApplicationFactory<>).MakeGenericType(programType);
            var rootFactory = Activator.CreateInstance(factoryType)!;
            var databaseName = $"GhseeliStep17Booking_{Guid.NewGuid():N}";
            var connectionString =
                $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
            var validationInterface = businessAssembly.GetType(
                "Ghseeli.BusinessApi.Services.Interfaces.IAppointmentValidationService",
                throwOnError: true)!;
            var validationProxy = DispatchProxy.Create(
                validationInterface,
                typeof(AppointmentValidationDispatchProxy));

            var configuredFactory = factoryType.GetMethods()
                .Single(method =>
                    method.Name == "WithWebHostBuilder" &&
                    method.GetParameters().Length == 1)
                .Invoke(rootFactory,
                [
                    (Action<IWebHostBuilder>)(builder =>
                    {
                        ConfigureBusinessSettings(builder, connectionString);
                        builder.ConfigureServices(services =>
                        {
                            ReplaceBusinessDbContext(
                                services,
                                dbContextType,
                                connectionString);
                            services.RemoveAll(validationInterface);
                            services.AddSingleton(validationInterface, validationProxy);
                        });
                    })
                ])!;

            var configuredType = configuredFactory.GetType();
            var client = (HttpClient)configuredType.GetMethods()
                .Single(method =>
                    method.Name == "CreateClient" &&
                    method.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(WebApplicationFactoryClientOptions))
                .Invoke(configuredFactory,
                [
                    new WebApplicationFactoryClientOptions
                    {
                        BaseAddress = new Uri("https://localhost")
                    }
                ])!;
            var services = (IServiceProvider)configuredType
                .GetProperty("Services")!
                .GetValue(configuredFactory)!;
            var branchId = Guid.NewGuid();
            var slotStart = new DateTimeOffset(
                DateTime.UtcNow.Date.AddDays(1).AddHours(10),
                TimeSpan.Zero);
            var result = new ReflectionBusinessReservationFactory(
                businessAssembly,
                dbContextType,
                rootFactory,
                configuredFactory,
                services,
                client,
                databaseName,
                branchId,
                slotStart);
            await result.ResetAndSeedAsync();
            return result;
        }

        public CreateReservationRequest CreateReservationRequest(
            bool includeSecondItem = false)
        {
            var first = CreateItem(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"));
            var items = new List<CreateReservationItemRequest> { first };
            if (includeSecondItem)
            {
                items.Add(CreateItem(
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"),
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2")));
            }

            return new CreateReservationRequest
            {
                BookingReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                BranchId = BranchId,
                ExpectedCatalogVersion = 17,
                RequestedSlotStartUtc = SlotStartUtc,
                Currency = "ILS",
                ExpectedItemSubtotal = 50m * items.Count,
                ExpectedTotalDurationMinutes = 30 * items.Count,
                Customer = new ReservationCustomerSnapshot
                {
                    Name = "Step 17 Customer"
                },
                Vehicle = new ReservationVehicleSnapshot
                {
                    VehicleType = "Sedan"
                },
                Location = new ReservationLocationSnapshot
                {
                    AddressLine = "Street 17",
                    Latitude = 32.1,
                    Longitude = 34.8
                },
                CancellationPolicyAcknowledged = true,
                Items = items
            };
        }

        public async Task<HttpResponseMessage> SendReservationAsync(
            CreateReservationRequest request,
            string idempotencyKey,
            Task? startBarrier = null)
        {
            if (startBarrier is not null)
            {
                await startBarrier;
            }

            using var message = CreateSignedRequest(
                _client,
                request,
                idempotencyKey);
            return await _client.SendAsync(message);
        }

        public async Task<long> CountRowsAsync(string table)
        {
            if (table.Any(character => !char.IsAsciiLetterOrDigit(character)))
            {
                throw new ArgumentException("Table name is invalid.", nameof(table));
            }

            return await ReadInt64Async(
                $"SELECT COUNT_BIG(*) FROM [dbo].[{table}]");
        }

        public async Task<string> ReadSingleStringAsync(string commandText)
        {
            await using var scope = _services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider
                .GetRequiredService(_dbContextType);
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            return (string)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var context = (DbContext)scope.ServiceProvider
                    .GetRequiredService(_dbContextType);
                await context.Database.EnsureDeletedAsync();
            }
            finally
            {
                if (_configuredFactory is IAsyncDisposable configuredAsync)
                {
                    await configuredAsync.DisposeAsync();
                }
                else if (_configuredFactory is IDisposable configured)
                {
                    configured.Dispose();
                }

                if (_rootFactory is IAsyncDisposable rootAsync)
                {
                    await rootAsync.DisposeAsync();
                }
                else if (_rootFactory is IDisposable root)
                {
                    root.Dispose();
                }
            }
        }

        private static CreateReservationItemRequest CreateItem(
            Guid offeringId,
            Guid firstChoiceId,
            Guid secondChoiceId) =>
            new()
            {
                OfferingId = offeringId,
                ExpectedBaseSubtotal = 50m,
                ExpectedAddonSubtotal = 0m,
                ExpectedItemSubtotal = 50m,
                ExpectedDurationMinutes = 30,
                SelectedAddons =
                [
                    new ValidateAppointmentAddonSelectionRequest
                    {
                        AddonChoiceId = firstChoiceId,
                        Quantity = 1
                    },
                    new ValidateAppointmentAddonSelectionRequest
                    {
                        AddonChoiceId = secondChoiceId,
                        Quantity = 2
                    }
                ]
            };

        private async Task ResetAndSeedAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider
                .GetRequiredService(_dbContextType);
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();

            var companyId = Guid.NewGuid();
            var company = CreateBusinessEntity(
                "Ghseeli.BusinessApi.Models.Company",
                ("Id", companyId),
                ("NameAr", "شركة الاختبار"),
                ("IsActive", true),
                ("CatalogVersion", 17L));
            var branch = CreateBusinessEntity(
                "Ghseeli.BusinessApi.Models.Branch",
                ("Id", BranchId),
                ("CompanyId", companyId),
                ("NameAr", "الفرع"),
                ("AddressAr", "العنوان"),
                ("IsActive", true));
            var settings = CreateBusinessEntity(
                "Ghseeli.BusinessApi.Models.BranchAvailabilitySettings",
                ("Id", Guid.NewGuid()),
                ("BranchId", BranchId),
                ("TimeZoneId", "UTC"),
                ("MinimumLeadMinutes", 0),
                ("BookingHorizonDays", 30),
                ("IsActive", true));
            var recurring = CreateBusinessEntity(
                "Ghseeli.BusinessApi.Models.BranchRecurringSchedule",
                ("Id", Guid.NewGuid()),
                ("BranchId", BranchId),
                ("DayOfWeek", SlotStartUtc.DayOfWeek),
                ("StartLocalTime", TimeSpan.FromHours(8)),
                ("EndLocalTime", TimeSpan.FromHours(18)),
                ("SlotDurationMinutes", 15),
                ("Capacity", 1),
                ("IsActive", true));
            context.AddRange(company, branch, settings, recurring);
            await context.SaveChangesAsync();
        }

        private object CreateBusinessEntity(
            string typeName,
            params (string Property, object Value)[] values)
        {
            var type = _businessAssembly.GetType(typeName, throwOnError: true)!;
            var entity = Activator.CreateInstance(type)!;
            foreach (var (property, value) in values)
            {
                type.GetProperty(property)!.SetValue(entity, value);
            }

            return entity;
        }

        private async Task<long> ReadInt64Async(string commandText)
        {
            await using var scope = _services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider
                .GetRequiredService(_dbContextType);
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        private static void ConfigureBusinessSettings(
            IWebHostBuilder builder,
            string connectionString)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "ConnectionStrings:BusinessConnection",
                connectionString);
            builder.UseSetting(
                "BusinessJwtSettings:SecretKey",
                "step17-business-jwt-secret-at-least-32-characters");
            builder.UseSetting(
                "BusinessJwtSettings:Issuer",
                "Ghseeli.Step17.Business");
            builder.UseSetting(
                "BusinessJwtSettings:Audience",
                "Ghseeli.Step17.BusinessClients");
            builder.UseSetting(
                "InternalServiceAuthentication:RequireHttps",
                "true");
            builder.UseSetting(
                "InternalServiceAuthentication:AllowedClockSkewSeconds",
                "120");
            builder.UseSetting(
                "InternalServiceAuthentication:NonceLifetimeSeconds",
                "300");
            builder.UseSetting(
                "InternalServiceAuthentication:IdempotencyLifetimeSeconds",
                "300");
            builder.UseSetting(
                "InternalServiceAuthentication:Services:0:ServiceId",
                ServiceId);
            builder.UseSetting(
                "InternalServiceAuthentication:Services:0:ActiveSecret",
                Secret);
            builder.UseSetting(
                "InternalServiceAuthentication:Services:0:AllowedOperations:0",
                InternalServiceOperationNames.ReservationCreate);
            builder.UseSetting(
                "CustomerBookingStatusClient:DisableDeliveryInTesting",
                "true");
            builder.UseSetting(
                "BusinessRateLimiting:ValidInternal:PermitLimit",
                "1000");
            builder.UseSetting(
                "BusinessRateLimiting:ValidInternal:WindowSeconds",
                "60");
        }

        private static void ReplaceBusinessDbContext(
            IServiceCollection services,
            Type dbContextType,
            string connectionString)
        {
            foreach (var descriptor in services
                         .Where(descriptor =>
                             descriptor.ServiceType == dbContextType ||
                             descriptor.ServiceType.IsGenericType &&
                             descriptor.ServiceType.GetGenericTypeDefinition() ==
                                 typeof(DbContextOptions<>) &&
                             descriptor.ServiceType.GenericTypeArguments[0] ==
                                 dbContextType)
                         .ToArray())
            {
                services.Remove(descriptor);
            }

            var addDbContext = typeof(EntityFrameworkServiceCollectionExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method =>
                    method.Name == nameof(EntityFrameworkServiceCollectionExtensions.AddDbContext) &&
                    method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 &&
                    method.GetParameters().Length == 4 &&
                    method.GetParameters()[1].ParameterType ==
                        typeof(Action<DbContextOptionsBuilder>));
            addDbContext.MakeGenericMethod(dbContextType).Invoke(
                null,
                [
                    services,
                    (Action<DbContextOptionsBuilder>)(options =>
                        options.UseSqlServer(
                            connectionString,
                            sql => sql.EnableRetryOnFailure())),
                    ServiceLifetime.Scoped,
                    ServiceLifetime.Scoped
                ]);
        }

        private static HttpRequestMessage CreateSignedRequest(
            HttpClient client,
            CreateReservationRequest body,
            string idempotencyKey)
        {
            const string relativeUri = "/api/v1/internal/reservations";
            var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(
                body,
                ContractJsonOptions);
            var timestamp = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");
            var nonce = WebEncoders.Base64UrlEncode(
                RandomNumberGenerator.GetBytes(32));
            var absoluteUri = new Uri(client.BaseAddress!, relativeUri);
            var canonical = InternalServiceCanonicalRequest.Build(
                ServiceId,
                HttpMethod.Post.Method,
                absoluteUri.AbsolutePath,
                Array.Empty<KeyValuePair<string, string?>>(),
                timestamp,
                nonce,
                idempotencyKey,
                InternalServiceCanonicalRequest.ComputeSha256Hex(bodyBytes));
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            var signature = Convert.ToHexString(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
            var request = new HttpRequestMessage(HttpMethod.Post, relativeUri)
            {
                Content = new ByteArrayContent(bodyBytes)
            };
            request.Content.Headers.ContentType = new("application/json");
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.ServiceIdHeaderName,
                ServiceId);
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.TimestampHeaderName,
                timestamp);
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.NonceHeaderName,
                nonce);
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.SignatureHeaderName,
                signature);
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.IdempotencyKeyHeaderName,
                idempotencyKey);
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.CorrelationIdHeaderName,
                $"step17-book-{Guid.NewGuid():N}");
            return request;
        }

        private static string FindBusinessAssembly()
        {
            var solutionRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                ".."));
            var bin = Path.Combine(solutionRoot, "Ghseeli.BusinessApi", "bin");
            var assembly = Directory.Exists(bin)
                ? Directory.EnumerateFiles(
                        bin,
                        "Ghseeli.BusinessApi.dll",
                        SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            return assembly ?? throw new InvalidOperationException(
                "Build Ghseeli.BusinessApi before running the Step 17 cross-API booking tests.");
        }

        private static void CopyBusinessDependencyContext(string assemblyPath)
        {
            var source = Path.ChangeExtension(assemblyPath, ".deps.json");
            if (!File.Exists(source))
            {
                throw new InvalidOperationException(
                    $"The Business API dependency context was not found at '{source}'.");
            }

            var destination = Path.Combine(
                AppContext.BaseDirectory,
                "Ghseeli.BusinessApi.deps.json");
            File.Copy(source, destination, overwrite: true);
        }
    }

    private class AppointmentValidationDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args)
        {
            if (targetMethod?.Name != "ValidateAsync" ||
                args is null ||
                args.Length != 1 ||
                args[0] is not ValidateAppointmentRequest request)
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            return Task.FromResult(new ValidateAppointmentResponse
            {
                ContractVersion = BusinessCatalogContract.Version,
                Valid = true,
                CatalogVersion = request.ExpectedCatalogVersion ?? 17,
                Currency = request.Currency,
                BranchId = request.BranchId,
                OfferingId = request.OfferingId,
                BaseSubtotal = 50m,
                AddonSubtotal = 0m,
                TotalPrice = 50m,
                TotalDurationMinutes = 30,
                NormalizedSelections = request.SelectedAddons
                    .Select(selection => new NormalizedAddonSelection
                    {
                        AddonGroupId =
                            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                        AddonChoiceId = selection.AddonChoiceId,
                        SelectionType = "MultipleChoice",
                        Quantity = selection.Quantity
                    })
                    .ToArray()
            });
        }
    }
}
