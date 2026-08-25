using FluentAssertions;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Internal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Provides Step 17 relational HTTP evidence for deterministic booking-status delivery.
/// </summary>
public sealed class Step17DeterministicBookingStatusHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-029")]
    public async Task STEP17_DET_STATUS_029_CallbackTransportReplayExecutesOnce()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = CreateClient(factory);
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var key = $"step17-status-029-{Guid.NewGuid():N}";

        using var firstRequest = CreateSignedPost(
            client, CallbackPath, body, key);
        using var first = await client.SendAsync(firstRequest);
        using var replayRequest = CreateSignedPost(
            client, CallbackPath, body, key);
        using var replay = await client.SendAsync(replayRequest);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadAsStringAsync();
        (await replay.Content.ReadAsStringAsync()).Should().Be(firstBody);
        var result = JsonSerializer.Deserialize<BookingStatusCallbackResponse>(
            firstBody, JsonOptions)!;
        result.Applied.Should().BeTrue();
        result.Stale.Should().BeFalse();

        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            1,
            inboxCount: 1,
            transportCount: 1);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transport = await context.CustomerInternalIdempotencyRecords
            .AsNoTracking().SingleAsync();
        transport.State.Should().Be(CustomerInternalIdempotencyState.Completed);
        transport.OwnerToken.Should().BeNull();
        transport.LeaseExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-030")]
    public async Task STEP17_DET_STATUS_030_ChangedEventBodyConflicts()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = CreateClient(factory);
        var original = factory.Message(BookingStatuses.Confirmed, 1);

        using var firstRequest = CreateSignedCallback(client, original);
        using var first = await client.SendAsync(firstRequest);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        string originalHash;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            originalHash = (await context.ProcessedBookingStatusMessages
                .AsNoTracking().SingleAsync()).RequestHash;
        }

        var changed = factory.Message(BookingStatuses.InProgress, 2);
        changed.EventId = original.EventId;
        using var conflictRequest = CreateSignedCallback(client, changed);
        using var conflict = await client.SendAsync(conflictRequest);

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemCodeAsync(conflict, BookingStatusErrorCodes.EventConflict);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            1,
            inboxCount: 1,
            transportCount: 2);
        using var verifyScope = factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await verify.ProcessedBookingStatusMessages.AsNoTracking().SingleAsync())
            .RequestHash.Should().Be(originalHash);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-031")]
    public async Task STEP17_DET_STATUS_031_OlderSequenceIsRecordedAsStale()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = CreateClient(factory);
        using var currentRequest = CreateSignedCallback(
            client, factory.Message(BookingStatuses.Confirmed, 2));
        using var current = await client.SendAsync(currentRequest);
        current.StatusCode.Should().Be(HttpStatusCode.OK);

        var staleMessage = factory.Message(BookingStatuses.Pending, 1);
        using var staleRequest = CreateSignedCallback(client, staleMessage);
        using var stale = await client.SendAsync(staleRequest);
        var staleResult = await stale.Content
            .ReadFromJsonAsync<BookingStatusCallbackResponse>(JsonOptions);

        stale.StatusCode.Should().Be(HttpStatusCode.OK);
        staleResult!.Applied.Should().BeFalse();
        staleResult.Stale.Should().BeTrue();
        staleResult.Status.Should().Be(BookingStatuses.Confirmed);
        staleResult.Sequence.Should().Be(2);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            2,
            inboxCount: 2,
            transportCount: 2);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedStale = await context.ProcessedBookingStatusMessages
            .AsNoTracking().SingleAsync(value => value.EventId == staleMessage.EventId);
        persistedStale.Applied.Should().BeFalse();
        persistedStale.Sequence.Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-032")]
    public async Task STEP17_DET_STATUS_032_TerminalBookingRejectsNewTransition()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = CreateClient(factory);
        await SendSuccessfulCallbackAsync(
            client, factory.Message(BookingStatuses.Confirmed, 1));
        await SendSuccessfulCallbackAsync(
            client, factory.Message(BookingStatuses.InProgress, 2));
        await SendSuccessfulCallbackAsync(
            client, factory.Message(BookingStatuses.Completed, 3));

        using var invalidRequest = CreateSignedCallback(
            client, factory.Message(BookingStatuses.InProgress, 4));
        using var invalid = await client.SendAsync(invalidRequest);

        invalid.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemCodeAsync(invalid, BookingStatusErrorCodes.TransitionInvalid);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Completed,
            3,
            inboxCount: 3,
            transportCount: 4);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-033")]
    public async Task STEP17_DET_STATUS_033_AllowedSequenceGapApplies()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = CreateClient(factory);
        using var request = CreateSignedCallback(
            client, factory.Message(BookingStatuses.Confirmed, 2));

        using var response = await client.SendAsync(request);
        var result = await response.Content
            .ReadFromJsonAsync<BookingStatusCallbackResponse>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.Applied.Should().BeTrue();
        result.Stale.Should().BeFalse();
        result.Status.Should().Be(BookingStatuses.Confirmed);
        result.Sequence.Should().Be(2);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            2,
            inboxCount: 1,
            transportCount: 1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-034")]
    public async Task STEP17_DET_STATUS_034_DisallowedSequenceGapIsRejected()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = CreateClient(factory);
        using var request = CreateSignedCallback(
            client, factory.Message(BookingStatuses.Completed, 2));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemCodeAsync(response, BookingStatusErrorCodes.TransitionInvalid);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Pending,
            0,
            inboxCount: 0,
            transportCount: 1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-035")]
    public async Task STEP17_DET_STATUS_035_ResponseAbortAfterCommitReplaysExactlyOnce()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var initializationClient = CreateClient(factory);
        using var abortedClient = new HttpClient(
            new ThrowAfterResponseHandler(factory.Server.CreateHandler()))
        {
            BaseAddress = new Uri("https://localhost")
        };
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var key = $"step17-status-035-{Guid.NewGuid():N}";
        using var firstRequest = CreateSignedPost(
            abortedClient, CallbackPath, body, key);

        await FluentActions.Awaiting(() => abortedClient.SendAsync(firstRequest))
            .Should().ThrowAsync<HttpRequestException>();
        await factory.WaitForTransportCompletionAsync(key);

        using var retryRequest = CreateSignedPost(
            initializationClient, CallbackPath, body, key);
        using var retry = await initializationClient.SendAsync(retryRequest);

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await retry.Content
            .ReadFromJsonAsync<BookingStatusCallbackResponse>(JsonOptions);
        result!.EventId.Should().Be(message.EventId);
        result.Applied.Should().BeTrue();
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            1,
            inboxCount: 1,
            transportCount: 1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-036")]
    public async Task STEP17_DET_STATUS_036_ClientCancellationDoesNotDuplicateCallback()
    {
        await using var factory = new BookingStatusCallbackFactory(
            useSqlServer: true,
            pauseAfterCommit: true);
        using var client = CreateClient(factory);
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var key = $"step17-status-036-{Guid.NewGuid():N}";
        using var firstRequest = CreateSignedPost(client, CallbackPath, body, key);
        using var cancellation = new CancellationTokenSource();

        var firstTask = client.SendAsync(firstRequest, cancellation.Token);
        await factory.DomainCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        factory.ContinueAfterCommit.TrySetResult();
        await FluentActions.Awaiting(() => firstTask)
            .Should().ThrowAsync<OperationCanceledException>();
        await factory.WaitForTransportCompletionAsync(key);

        using var retryRequest = CreateSignedPost(client, CallbackPath, body, key);
        using var retry = await client.SendAsync(retryRequest);

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.EndpointExecutionCount.Should().Be(1);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            1,
            inboxCount: 1,
            transportCount: 1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-037")]
    public async Task STEP17_DET_STATUS_037_TransientRenewalFailureRecoversAtHttpLayer()
    {
        var leaseControl = new LeaseFaultControl(transientRenewalFailures: 1);
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        await using var owner = new BookingStatusCallbackFactory(
            useSqlServer: true,
            timeProvider: clock,
            pauseAfterCommit: true);
        await using var factory = owner.WithWebHostBuilder(builder =>
        {
            ConfigureShortLease(builder);
            builder.ConfigureServices(services =>
                ReplaceLeaseService(services, leaseControl));
        });
        using var client = CreateClient(factory);
        var message = owner.Message(BookingStatuses.Confirmed, 1);
        using var request = CreateSignedCallback(client, message);

        var responseTask = client.SendAsync(request);
        await owner.DomainCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => leaseControl.RenewCalls >= 1);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        await WaitUntilAsync(() => leaseControl.RenewCalls >= 2);
        owner.ContinueAfterCommit.TrySetResult();
        using var response = await responseTask;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        leaseControl.RenewCalls.Should().BeInRange(2, 10);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            1,
            inboxCount: 1,
            transportCount: 1);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = await context.CustomerInternalIdempotencyRecords
            .AsNoTracking().SingleAsync();
        record.State.Should().Be(CustomerInternalIdempotencyState.Completed);
        record.OwnerToken.Should().BeNull();
        record.LeaseExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-038")]
    public async Task STEP17_DET_STATUS_038_StaleLeaseOwnerGetsUnavailableAndCannotOverwrite()
    {
        var leaseControl = new LeaseFaultControl(loseOwnershipOnRenewal: true);
        var endpointControl = new FirstEndpointPauseControl();
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        await using var owner = new BookingStatusCallbackFactory(
            useSqlServer: true,
            timeProvider: clock);
        await using var factory = owner.WithWebHostBuilder(builder =>
        {
            ConfigureShortLease(builder);
            builder.UseSetting(
                "CustomerInternalServiceAuthentication:InProgressRecoverySeconds",
                "2");
            builder.UseSetting(
                "CustomerInternalServiceAuthentication:InProgressLeaseRenewalFraction",
                "0.5");
            builder.ConfigureServices(services =>
            {
                ReplaceLeaseService(services, leaseControl);
                services.RemoveAll<IBookingStatusInboxService>();
                services.AddScoped<BookingStatusInboxService>();
                services.AddScoped<IBookingStatusInboxService>(provider =>
                    new PauseFirstBookingStatusInboxService(
                        provider.GetRequiredService<BookingStatusInboxService>(),
                        endpointControl));
            });
        });
        using var client = CreateClient(factory);
        var message = owner.Message(BookingStatuses.Confirmed, 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var key = $"step17-status-038-{Guid.NewGuid():N}";
        using var staleRequest = CreateSignedPost(client, CallbackPath, body, key);

        var staleTask = client.SendAsync(staleRequest);
        await endpointControl.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => leaseControl.RenewCalls >= 1);
        clock.Advance(TimeSpan.FromSeconds(3));
        await WaitForLeaseExpiryAsync(factory, key, clock);

        using var successorRequest = CreateSignedPost(client, CallbackPath, body, key);
        using var successor = await client.SendAsync(successorRequest);
        successor.StatusCode.Should().Be(HttpStatusCode.OK);
        var successorBody = await successor.Content.ReadAsByteArrayAsync();
        endpointControl.Release.TrySetResult();
        using var stale = await staleTask;

        stale.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await AssertProblemCodeAsync(
            stale, InternalServiceProblemCodes.IdempotencyUnavailable);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            1,
            inboxCount: 1,
            transportCount: 1);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = await context.CustomerInternalIdempotencyRecords
            .AsNoTracking().SingleAsync();
        record.State.Should().Be(CustomerInternalIdempotencyState.Completed);
        record.ResponseStatusCode.Should().Be((int)HttpStatusCode.OK);
        record.ResponseBody.Should().Equal(successorBody);
        record.OwnerToken.Should().BeNull();
        record.LeaseExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-039")]
    public async Task STEP17_DET_STATUS_039_ReconcileAndCallbackRaceConverges()
    {
        var barrier = new ReconcileCallbackBarrier();
        await using var owner = new BookingStatusCallbackFactory(useSqlServer: true);
        var callback = owner.Message(BookingStatuses.Confirmed, 1);
        var businessClient = new AuthoritativeStatusBusinessClient(new()
        {
            BookingReference = callback.BookingReference,
            ReservationId = callback.ReservationId,
            WorkOrderId = callback.WorkOrderId,
            Status = BookingStatuses.Confirmed,
            Sequence = 1,
            ChangedAtUtc = callback.OccurredAtUtc
        });
        await using var factory = owner.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "CustomerInternalServiceAuthentication:Services:0:AllowedOperations:2",
                InternalServiceOperationNames.BookingStatusReconcile);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessApiClient>();
                services.AddSingleton<IBusinessApiClient>(businessClient);
                services.RemoveAll<IBookingStatusInboxService>();
                services.AddScoped<BookingStatusInboxService>();
                services.AddScoped<IBookingStatusInboxService>(provider =>
                    new BarrierBookingStatusInboxService(
                        provider.GetRequiredService<BookingStatusInboxService>(),
                        barrier));
            });
        });
        using var client = CreateClient(factory);
        var callbackBody = JsonSerializer.SerializeToUtf8Bytes(callback, JsonOptions);
        using var reconcileRequest = CreateSignedPost(
            client,
            $"/api/v1/internal/bookings/{callback.BookingReference:D}/reconcile",
            [],
            $"step17-status-039-reconcile-{Guid.NewGuid():N}");
        using var callbackRequest = CreateSignedPost(
            client,
            CallbackPath,
            callbackBody,
            $"step17-status-039-callback-{Guid.NewGuid():N}");

        var responses = await Task.WhenAll(
            client.SendAsync(reconcileRequest),
            client.SendAsync(callbackRequest));

        responses.Should().OnlyContain(value => value.StatusCode == HttpStatusCode.OK);
        var results = await Task.WhenAll(responses.Select(response =>
            response.Content.ReadFromJsonAsync<BookingStatusCallbackResponse>(JsonOptions)));
        results.Should().OnlyContain(value =>
            value != null &&
            value.Status == BookingStatuses.Confirmed &&
            value.Sequence == 1);
        await AssertCustomerStateAsync(
            factory,
            BookingStatuses.Confirmed,
            1,
            inboxCount: null,
            transportCount: 2);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages
            .AsNoTracking()
            .CountAsync(value => value.EventId == callback.EventId)).Should().Be(1);
        (await context.ProcessedBookingStatusMessages
            .AsNoTracking()
            .CountAsync(value => value.Applied)).Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-040")]
    public async Task STEP17_DET_STATUS_040_DeadLetterHeadBlocksLaterCallback()
    {
        await using var customer = new BookingStatusCallbackFactory(useSqlServer: true);
        using var customerClient = CreateClient(customer);
        var customerBooking = customer.Message(BookingStatuses.Confirmed, 1);
        await using var business = await ReflectionBusinessApiFactory.CreateAsync();
        var reservationId = Guid.NewGuid();
        var firstEventId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();
        await business.SeedOutboxPairAsync(
            reservationId,
            customerBooking.BookingReference,
            firstEventId,
            secondEventId);

        var delivered = await business.DeliverNextAsync();

        delivered.Should().BeFalse();
        var rows = await business.ReadOutboxAsync(firstEventId, secondEventId);
        rows.Should().HaveCount(2);
        rows[0].Should().Be(new BusinessOutboxRow(
            firstEventId, "DeadLetter", 0, 1));
        rows[1].Should().Be(new BusinessOutboxRow(
            secondEventId, "Pending", 0, 2));

        using var readRequest = CreateSignedGet(
            customerClient,
            $"/api/v1/internal/bookings/{customerBooking.BookingReference:D}");
        using var read = await customerClient.SendAsync(readRequest);
        var current = await read.Content
            .ReadFromJsonAsync<BookingStatusReadResponse>(JsonOptions);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        current!.Status.Should().Be(BookingStatuses.Pending);
        current.Sequence.Should().Be(0);
        await AssertCustomerStateAsync(
            customer,
            BookingStatuses.Pending,
            0,
            inboxCount: 0,
            transportCount: 0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-041")]
    public async Task STEP17_DET_STATUS_041_AdminRequeueIsIdempotentAtGenerationOne()
    {
        await using var business = await ReflectionBusinessApiFactory.CreateAsync();
        var eventId = await business.SeedOutboxEventAsync("DeadLetter");
        using var admin = business.CreateAdminClient();
        var key = $"step17-status-041-{Guid.NewGuid():N}";

        using var firstRequest = new HttpRequestMessage(
            HttpMethod.Post, ReflectionBusinessApiFactory.RequeueRoute(eventId));
        firstRequest.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.IdempotencyKeyHeaderName, key);
        using var first = await admin.SendAsync(firstRequest);
        using var secondRequest = new HttpRequestMessage(
            HttpMethod.Post, ReflectionBusinessApiFactory.RequeueRoute(eventId));
        secondRequest.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.IdempotencyKeyHeaderName, key);
        using var second = await admin.SendAsync(secondRequest);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstJson = JsonDocument.Parse(
            await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(
            await second.Content.ReadAsStringAsync());
        firstJson.RootElement.GetProperty("result").GetString()
            .Should().Be("Requeued");
        firstJson.RootElement.GetProperty("generation").GetInt32()
            .Should().Be(1);
        secondJson.RootElement.GetProperty("result").GetString()
            .Should().Be("AlreadyRequeued");
        secondJson.RootElement.GetProperty("generation").GetInt32()
            .Should().Be(1);

        var state = await business.ReadRequeueStateAsync(eventId);
        state.Should().Be(new BusinessRequeueState("Pending", 1, 1));
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-STATUS-042")]
    public async Task STEP17_DET_STATUS_042_PendingOutboxEventCannotBeRequeued()
    {
        await using var business = await ReflectionBusinessApiFactory.CreateAsync();
        var eventId = await business.SeedOutboxEventAsync("Pending");
        using var admin = business.CreateAdminClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, ReflectionBusinessApiFactory.RequeueRoute(eventId));
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.IdempotencyKeyHeaderName,
            $"step17-status-042-{Guid.NewGuid():N}");

        using var response = await admin.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertProblemCodeAsync(
            response, "booking_status_event_not_requeueable");
        var state = await business.ReadRequeueStateAsync(eventId);
        state.Should().Be(new BusinessRequeueState("Pending", 0, 0));
    }

    private const string CallbackPath = "/api/v1/internal/bookings/status";

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static HttpRequestMessage CreateSignedCallback(
        HttpClient client,
        BookingStatusChangedMessage message) =>
        CreateSignedPost(
            client,
            CallbackPath,
            JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions),
            $"step17-status-{Guid.NewGuid():N}");

    private static HttpRequestMessage CreateSignedPost(
        HttpClient client,
        string path,
        byte[] body,
        string idempotencyKey)
    {
        var uri = new Uri(client.BaseAddress!, path);
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var canonical = InternalServiceCanonicalRequest.Build(
            BookingStatusCallbackFactory.ServiceId,
            HttpMethod.Post.Method,
            uri.AbsolutePath,
            [],
            timestamp,
            nonce,
            idempotencyKey,
            InternalServiceCanonicalRequest.ComputeSha256Hex(body));
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(BookingStatusCallbackFactory.Secret));
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        AddHmacHeaders(
            request,
            timestamp,
            nonce,
            Convert.ToHexString(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant(),
            idempotencyKey);
        return request;
    }

    private static HttpRequestMessage CreateSignedGet(
        HttpClient client,
        string path)
    {
        var uri = new Uri(client.BaseAddress!, path);
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var canonical = InternalServiceCanonicalRequest.Build(
            BookingStatusCallbackFactory.ServiceId,
            HttpMethod.Get.Method,
            uri.AbsolutePath,
            [],
            timestamp,
            nonce,
            string.Empty,
            InternalServiceCanonicalRequest.ComputeSha256Hex([]));
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(BookingStatusCallbackFactory.Secret));
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddHmacHeaders(
            request,
            timestamp,
            nonce,
            Convert.ToHexString(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant());
        return request;
    }

    private static void AddHmacHeaders(
        HttpRequestMessage request,
        string timestamp,
        string nonce,
        string signature,
        string? idempotencyKey = null)
    {
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.ServiceIdHeaderName,
            BookingStatusCallbackFactory.ServiceId);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.TimestampHeaderName,
            timestamp);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.NonceHeaderName,
            nonce);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.SignatureHeaderName,
            signature);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.IdempotencyKeyHeaderName,
                idempotencyKey);
        }
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            "corr-step17-status");
    }

    private static async Task SendSuccessfulCallbackAsync(
        HttpClient client,
        BookingStatusChangedMessage message)
    {
        using var request = CreateSignedCallback(client, message);
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        string code)
    {
        response.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/problem+json");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        document.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    private static async Task AssertCustomerStateAsync(
        WebApplicationFactory<Program> factory,
        string status,
        long sequence,
        int? inboxCount,
        int transportCount)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await context.CustomerBookings.AsNoTracking().SingleAsync();
        booking.Status.Should().Be(status);
        booking.BusinessStatusSequence.Should().Be(sequence);
        if (inboxCount.HasValue)
        {
            (await context.ProcessedBookingStatusMessages.CountAsync())
                .Should().Be(inboxCount.Value);
        }
        (await context.CustomerInternalIdempotencyRecords.CountAsync())
            .Should().Be(transportCount);
    }

    private static void ConfigureShortLease(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:InProgressRecoverySeconds", "1");
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:InProgressLeaseRenewalFraction", "0.1");
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:InProgressLeaseSafetyMarginMilliseconds", "100");
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:InProgressLeaseOperationTimeoutMilliseconds", "100");
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:InProgressLeaseRetryBackoffMilliseconds", "50");
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:InProgressWaitMilliseconds", "2000");
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:InProgressPollMilliseconds", "20");
    }

    private static void ReplaceLeaseService(
        IServiceCollection services,
        LeaseFaultControl control)
    {
        services.RemoveAll<ICustomerInternalIdempotencyLeaseService>();
        services.AddScoped<ICustomerInternalIdempotencyLeaseService>(provider =>
            new FaultingLeaseService(
                new CustomerInternalIdempotencyLeaseService(
                    provider.GetRequiredService<ApplicationDbContext>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<
                        IOptions<CustomerInternalServiceOptions>>()),
                control));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        condition().Should().BeTrue();
    }

    private static async Task WaitForLeaseExpiryAsync(
        WebApplicationFactory<Program> factory,
        string key,
        TimeProvider timeProvider)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await context.CustomerInternalIdempotencyRecords
                .AsNoTracking()
                .SingleAsync(value => value.IdempotencyKey == key);
            if (record.LeaseExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                return;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("The first transport lease did not expire.");
    }

    private sealed class ThrowAfterResponseHandler(HttpMessageHandler inner) :
        DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var response = await base.SendAsync(request, cancellationToken);
            _ = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            throw new HttpRequestException(
                "The transport aborted after the server committed the response.");
        }
    }

    private sealed class LeaseFaultControl(
        int transientRenewalFailures = 0,
        bool loseOwnershipOnRenewal = false)
    {
        private int _remainingTransientFailures = transientRenewalFailures;
        private int _renewCalls;
        public bool LoseOwnershipOnRenewal { get; } = loseOwnershipOnRenewal;
        public int RenewCalls => Volatile.Read(ref _renewCalls);

        public bool ShouldFailTransiently()
        {
            Interlocked.Increment(ref _renewCalls);
            return Interlocked.Decrement(ref _remainingTransientFailures) >= 0;
        }
    }

    private sealed class FaultingLeaseService(
        ICustomerInternalIdempotencyLeaseService inner,
        LeaseFaultControl control) : ICustomerInternalIdempotencyLeaseService
    {
        public Task<CustomerInternalIdempotencyClaim> ClaimAsync(
            string serviceId,
            string operation,
            string key,
            string requestHash,
            CancellationToken cancellationToken) =>
            inner.ClaimAsync(
                serviceId, operation, key, requestHash, cancellationToken);

        public Task<DateTimeOffset?> RenewAsync(
            Guid recordId,
            Guid ownerToken,
            CancellationToken cancellationToken)
        {
            if (control.ShouldFailTransiently())
            {
                return Task.FromException<DateTimeOffset?>(
                    new InvalidOperationException("Step 17 transient renewal failure."));
            }
            return control.LoseOwnershipOnRenewal
                ? Task.FromResult<DateTimeOffset?>(null)
                : inner.RenewAsync(recordId, ownerToken, cancellationToken);
        }

        public Task<bool> CompleteAsync(
            Guid recordId,
            Guid ownerToken,
            int statusCode,
            string contentType,
            byte[] responseBody,
            CancellationToken cancellationToken) =>
            inner.CompleteAsync(
                recordId,
                ownerToken,
                statusCode,
                contentType,
                responseBody,
                cancellationToken);

        public Task<bool> ReleaseAsync(
            Guid recordId,
            Guid ownerToken,
            CancellationToken cancellationToken) =>
            inner.ReleaseAsync(recordId, ownerToken, cancellationToken);
    }

    private sealed class FirstEndpointPauseControl
    {
        public TaskCompletionSource Paused { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
    }

    private sealed class PauseFirstBookingStatusInboxService(
        BookingStatusInboxService inner,
        FirstEndpointPauseControl control) : IBookingStatusInboxService
    {
        public async Task<BookingStatusCallbackResponse> ApplyAsync(
            BookingStatusChangedMessage message,
            string requestHash,
            bool reconciliation,
            string correlationId,
            CancellationToken cancellationToken)
        {
            var result = await inner.ApplyAsync(
                message,
                requestHash,
                reconciliation,
                correlationId,
                cancellationToken);
            if (Interlocked.Increment(ref control.Calls) == 1)
            {
                control.Paused.TrySetResult();
                await control.Release.Task;
            }
            return result;
        }

        public Task<BookingStatusCallbackResponse> ReconcileAsync(
            Guid bookingReference,
            string correlationId,
            CancellationToken cancellationToken) =>
            inner.ReconcileAsync(
                bookingReference, correlationId, cancellationToken);

        public Task<BookingStatusReadResponse?> GetCurrentAsync(
            Guid bookingReference,
            CancellationToken cancellationToken) =>
            inner.GetCurrentAsync(bookingReference, cancellationToken);
    }

    private sealed class ReconcileCallbackBarrier
    {
        private int _arrivals;
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                Release.TrySetResult();
            }
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class BarrierBookingStatusInboxService(
        BookingStatusInboxService inner,
        ReconcileCallbackBarrier barrier) : IBookingStatusInboxService
    {
        public async Task<BookingStatusCallbackResponse> ApplyAsync(
            BookingStatusChangedMessage message,
            string requestHash,
            bool reconciliation,
            string correlationId,
            CancellationToken cancellationToken)
        {
            await barrier.ArriveAsync();
            return await inner.ApplyAsync(
                message,
                requestHash,
                reconciliation,
                correlationId,
                cancellationToken);
        }

        public async Task<BookingStatusCallbackResponse> ReconcileAsync(
            Guid bookingReference,
            string correlationId,
            CancellationToken cancellationToken)
        {
            await barrier.ArriveAsync();
            return await inner.ReconcileAsync(
                bookingReference, correlationId, cancellationToken);
        }

        public Task<BookingStatusReadResponse?> GetCurrentAsync(
            Guid bookingReference,
            CancellationToken cancellationToken) =>
            inner.GetCurrentAsync(bookingReference, cancellationToken);
    }

    private sealed class AuthoritativeStatusBusinessClient(
        AuthoritativeBookingStatusResponse response) : IBusinessApiClient
    {
        public Task<AuthoritativeBookingStatusResponse?> GetReservationStatusAsync(
            Guid bookingReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthoritativeBookingStatusResponse?>(response);

        public Task<CatalogSnapshotResponse> GetCatalogSnapshotAsync(
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ValidateAppointmentResponse> ValidateAppointmentAsync(
            ValidateAppointmentRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreateReservationResponse> CreateReservationAsync(
            CreateReservationRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record BusinessOutboxRow(
        Guid EventId,
        string DeliveryState,
        int DeliveryGeneration,
        long Sequence);

    private sealed record BusinessRequeueState(
        string DeliveryState,
        int DeliveryGeneration,
        int HistoryCount);

    private sealed class ReflectionBusinessApiFactory : IAsyncDisposable
    {
        private const string JwtSecret =
            "Step17BusinessStatusJwtSecret_MinimumThirtyTwoCharacters";
        private const string JwtIssuer = "Ghseeli.Step17.BusinessStatus";
        private const string JwtAudience = "Ghseeli.Step17.BusinessStatus.Clients";
        private readonly string _connectionString;
        private readonly object _rootFactory;
        private readonly object _configuredFactory;
        private readonly string? _copiedDepsPath;
        private IServiceProvider? _services;

        private ReflectionBusinessApiFactory(
            object rootFactory,
            object configuredFactory,
            string connectionString,
            string? copiedDepsPath)
        {
            _rootFactory = rootFactory;
            _configuredFactory = configuredFactory;
            _connectionString = connectionString;
            _copiedDepsPath = copiedDepsPath;
        }

        public static async Task<ReflectionBusinessApiFactory> CreateAsync()
        {
            var solutionRoot = FindSolutionRoot();
            var configuration =
#if DEBUG
                "Debug";
#else
                "Release";
#endif
            var businessOutput = Path.Combine(
                solutionRoot,
                "Ghseeli.BusinessApi",
                "bin",
                configuration,
                "net8.0");
            var assemblyPath = Path.Combine(
                businessOutput, "Ghseeli.BusinessApi.dll");
            if (!File.Exists(assemblyPath))
            {
                throw new InvalidOperationException(
                    "Build Ghseeli.BusinessApi before running Step 17 cross-API tests.");
            }

            var sourceDeps = Path.Combine(
                businessOutput, "Ghseeli.BusinessApi.deps.json");
            var targetDeps = Path.Combine(
                AppContext.BaseDirectory, "Ghseeli.BusinessApi.deps.json");
            string? copiedDepsPath = null;
            if (!File.Exists(targetDeps))
            {
                File.Copy(sourceDeps, targetDeps);
                copiedDepsPath = targetDeps;
            }

            var assembly = BusinessAssemblyLoader.LoadFrom(assemblyPath);
            var entryPoint = assembly.GetType(
                "Ghseeli.BusinessApi.Controllers.BookingStatusOutboxAdminController",
                throwOnError: true)!;
            var factoryType = typeof(WebApplicationFactory<>)
                .MakeGenericType(entryPoint);
            var rootFactory = Activator.CreateInstance(factoryType)!;
            var databaseName = $"GhseeliStep17Status_{Guid.NewGuid():N}";
            var connectionString =
                $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
            var dbContextType = assembly.GetType(
                "Ghseeli.BusinessApi.Persistence.BusinessDbContext",
                throwOnError: true)!;

            Action<IWebHostBuilder> configure = builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:BusinessConnection", connectionString);
                builder.UseSetting("BusinessJwtSettings:SecretKey", JwtSecret);
                builder.UseSetting("BusinessJwtSettings:Issuer", JwtIssuer);
                builder.UseSetting("BusinessJwtSettings:Audience", JwtAudience);
                builder.UseSetting(
                    "CustomerBookingStatusClient:BaseUrl",
                    "https://customer.invalid");
                builder.UseSetting(
                    "CustomerBookingStatusClient:ServiceId",
                    "step17-business-status");
                builder.UseSetting(
                    "CustomerBookingStatusClient:ActiveSecret",
                    "Step17BusinessStatusCallbackSecret_Minimum32Chars");
                builder.UseSetting(
                    "CustomerBookingStatusClient:DisableDeliveryInTesting",
                    "true");
                builder.ConfigureServices(services =>
                {
                    var optionsType = typeof(DbContextOptions<>)
                        .MakeGenericType(dbContextType);
                    foreach (var descriptor in services
                                 .Where(descriptor =>
                                     descriptor.ServiceType == dbContextType ||
                                     descriptor.ServiceType == optionsType)
                                 .ToArray())
                    {
                        services.Remove(descriptor);
                    }

                    AddReflectedDbContext(
                        services, dbContextType, connectionString);
                    using var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    var context = (DbContext)scope.ServiceProvider
                        .GetRequiredService(dbContextType);
                    context.Database.EnsureDeleted();
                    context.Database.EnsureCreated();
                });
            };
            var withBuilder = factoryType.GetMethod(
                nameof(WebApplicationFactory<Program>.WithWebHostBuilder),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(Action<IWebHostBuilder>)],
                modifiers: null)!;
            var configuredFactory = withBuilder.Invoke(
                rootFactory, [configure])!;
            var result = new ReflectionBusinessApiFactory(
                rootFactory,
                configuredFactory,
                connectionString,
                copiedDepsPath);
            using var warmup = result.CreateClient();
            _ = result.Services;
            await Task.CompletedTask;
            return result;
        }

        public HttpClient CreateAdminClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateAdminToken());
            return client;
        }

        public async Task<Guid> SeedOutboxEventAsync(string state)
        {
            var reservationId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            await InsertReservationAsync(reservationId, Guid.NewGuid());
            await InsertOutboxAsync(
                eventId,
                reservationId,
                sequence: 1,
                state);
            return eventId;
        }

        public async Task SeedOutboxPairAsync(
            Guid reservationId,
            Guid bookingReference,
            Guid firstEventId,
            Guid secondEventId)
        {
            await InsertReservationAsync(reservationId, bookingReference);
            await InsertOutboxAsync(
                firstEventId,
                reservationId,
                sequence: 1,
                state: "DeadLetter");
            await InsertOutboxAsync(
                secondEventId,
                reservationId,
                sequence: 2,
                state: "Pending");
        }

        public async Task<bool> DeliverNextAsync()
        {
            using var scope = Services.CreateScope();
            var dispatcherType = EntryAssembly.GetType(
                "Ghseeli.BusinessApi.Services.IBookingStatusOutboxDispatcher",
                throwOnError: true)!;
            var dispatcher = scope.ServiceProvider.GetRequiredService(dispatcherType);
            var method = dispatcherType.GetMethod("DeliverNextAsync")!;
            var task = (Task<bool>)method.Invoke(
                dispatcher, [CancellationToken.None])!;
            return await task;
        }

        public async Task<IReadOnlyList<BusinessOutboxRow>> ReadOutboxAsync(
            params Guid[] eventIds)
        {
            var rows = new List<BusinessOutboxRow>();
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            foreach (var eventId in eventIds)
            {
                await using var command = new SqlCommand(
                    """
                    SELECT [Id], [DeliveryState], [DeliveryGeneration], [Sequence]
                    FROM [dbo].[BookingStatusOutboxMessages]
                    WHERE [Id] = @id
                    """,
                    connection);
                command.Parameters.AddWithValue("@id", eventId);
                await using var reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                rows.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt64(3)));
            }
            return rows;
        }

        public async Task<BusinessRequeueState> ReadRequeueStateAsync(Guid eventId)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                """
                SELECT m.[DeliveryState],
                       m.[DeliveryGeneration],
                       (SELECT COUNT(*)
                        FROM [dbo].[BookingStatusRequeueHistory] h
                        WHERE h.[BookingStatusOutboxMessageId] = m.[Id])
                FROM [dbo].[BookingStatusOutboxMessages] m
                WHERE m.[Id] = @id
                """,
                connection);
            command.Parameters.AddWithValue("@id", eventId);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return new(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2));
        }

        public static string RequeueRoute(Guid eventId) =>
            $"/api/v1/business/admin/booking-status-outbox/{eventId:D}/requeue";

        public async ValueTask DisposeAsync()
        {
            if (_configuredFactory is IDisposable configured)
            {
                configured.Dispose();
            }
            if (_rootFactory is IDisposable root)
            {
                root.Dispose();
            }
            SqlConnection.ClearAllPools();
            try
            {
                var builder = new SqlConnectionStringBuilder(_connectionString)
                {
                    InitialCatalog = "master"
                };
                await using var connection = new SqlConnection(
                    builder.ConnectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(
                    $"""
                     IF DB_ID(N'{new SqlConnectionStringBuilder(_connectionString).InitialCatalog}') IS NOT NULL
                     BEGIN
                         ALTER DATABASE [{new SqlConnectionStringBuilder(_connectionString).InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                         DROP DATABASE [{new SqlConnectionStringBuilder(_connectionString).InitialCatalog}];
                     END
                     """,
                    connection);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                if (_copiedDepsPath is not null)
                {
                    File.Delete(_copiedDepsPath);
                }
            }
        }

        private Assembly EntryAssembly =>
            _configuredFactory.GetType().GenericTypeArguments.Single().Assembly;

        private IServiceProvider Services =>
            _services ??= (IServiceProvider)_configuredFactory.GetType()
                .GetProperty("Services")!
                .GetValue(_configuredFactory)!;

        private HttpClient CreateClient()
        {
            var method = _configuredFactory.GetType().GetMethod(
                "CreateClient",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(WebApplicationFactoryClientOptions)],
                modifiers: null)!;
            return (HttpClient)method.Invoke(
                _configuredFactory,
                [
                    new WebApplicationFactoryClientOptions
                    {
                        BaseAddress = new Uri("https://localhost")
                    }
                ])!;
        }

        private static void AddReflectedDbContext(
            IServiceCollection services,
            Type dbContextType,
            string connectionString)
        {
            var addDbContext = typeof(EntityFrameworkServiceCollectionExtensions)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(method =>
                    method.Name == "AddDbContext" &&
                    method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 &&
                    method.GetParameters().Length == 4 &&
                    method.GetParameters()[1].ParameterType ==
                    typeof(Action<DbContextOptionsBuilder>));
            var configure = new Action<DbContextOptionsBuilder>(options =>
                options.UseSqlServer(connectionString));
            _ = addDbContext.MakeGenericMethod(dbContextType).Invoke(
                null,
                [
                    services,
                    configure,
                    ServiceLifetime.Scoped,
                    ServiceLifetime.Scoped
                ]);
        }

        private async Task InsertReservationAsync(
            Guid reservationId,
            Guid bookingReference)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                """
                INSERT INTO [dbo].[AppointmentReservations]
                    ([Id], [PublicId], [CustomerBookingReference], [OrderGuid],
                     [RequestHash], [BranchId], [CatalogVersion], [Currency],
                     [ItemSubtotal], [TotalDurationMinutes],
                     [RequestedSlotStartUtc], [RequestedSlotEndUtc], [Status],
                     [StatusSequence], [StatusChangedAtUtc], [CreatedAtUtc],
                     [ExpiresAtUtc])
                VALUES
                    (@id, @publicId, @reference, @orderGuid, @hash, @branchId,
                     1, N'ILS', 0, 60, @start, @end, N'Pending', 0,
                     @changed, @created, NULL)
                """,
                connection);
            var now = DateTime.UtcNow;
            command.Parameters.AddWithValue("@id", reservationId);
            command.Parameters.AddWithValue("@publicId", Guid.NewGuid());
            command.Parameters.AddWithValue("@reference", bookingReference);
            command.Parameters.AddWithValue("@orderGuid", Guid.NewGuid());
            command.Parameters.AddWithValue("@hash", new string('a', 64));
            command.Parameters.AddWithValue("@branchId", Guid.NewGuid());
            command.Parameters.AddWithValue("@start", now.AddHours(1));
            command.Parameters.AddWithValue("@end", now.AddHours(2));
            command.Parameters.AddWithValue(
                "@changed", new DateTimeOffset(now, TimeSpan.Zero));
            command.Parameters.AddWithValue("@created", now);
            await command.ExecuteNonQueryAsync();
        }

        private async Task InsertOutboxAsync(
            Guid eventId,
            Guid reservationId,
            long sequence,
            string state)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                """
                INSERT INTO [dbo].[BookingStatusOutboxMessages]
                    ([Id], [AppointmentReservationId], [RequestJson],
                     [RequestHash], [WorkOrderPublicId], [Status], [Sequence],
                     [CorrelationId], [DeliveryState], [DeliveryGeneration],
                     [AttemptCount], [CreatedAtUtc], [NextAttemptAtUtc],
                     [DeliveredAtUtc], [DeadLetteredAtUtc], [LeaseToken],
                     [LeaseOwner], [LeaseExpiresAtUtc], [LastErrorCode],
                     [RequeuedAtUtc], [RequeuedByAdminUserId],
                     [RequeueRequestId])
                VALUES
                    (@id, @reservationId, N'{}', @hash, @workOrderId,
                     N'Confirmed', @sequence, N'step17-status',
                     @state, 0, 0, @created, @next, NULL, @deadLettered,
                     NULL, NULL, NULL, NULL, NULL, NULL, NULL)
                """,
                connection);
            var now = DateTimeOffset.UtcNow;
            command.Parameters.AddWithValue("@id", eventId);
            command.Parameters.AddWithValue("@reservationId", reservationId);
            command.Parameters.AddWithValue("@hash", new string('b', 64));
            command.Parameters.AddWithValue("@workOrderId", Guid.NewGuid());
            command.Parameters.AddWithValue("@sequence", sequence);
            command.Parameters.AddWithValue("@state", state);
            command.Parameters.AddWithValue("@created", now);
            command.Parameters.AddWithValue("@next", now);
            command.Parameters.AddWithValue(
                "@deadLettered",
                state == "DeadLetter" ? now : DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        private static string CreateAdminToken()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            };
            var token = new JwtSecurityToken(
                issuer: JwtIssuer,
                audience: JwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                    SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string FindSolutionRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName, "GhseeliApis.sln")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the GhseeliApis solution root.");
        }
    }
}
