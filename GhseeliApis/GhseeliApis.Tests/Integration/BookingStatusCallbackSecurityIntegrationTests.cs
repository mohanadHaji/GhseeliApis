using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.InternalHttp;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Internal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies HMAC operation authorization, replay protection, and callback HTTP behavior.
/// </summary>
public sealed class BookingStatusCallbackSecurityIntegrationTests
{
    [Fact]
    public async Task Callback_WithValidSignature_AppliesAndEchoesCorrelation()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        using var request = CreateSignedRequest(client, message, Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues(InternalServiceWireConstants.CorrelationIdHeaderName)
            .Should().Contain("corr-step13");
    }

    [Fact]
    public async Task Callback_ReusingAcceptedNonce_IsRejectedAsReplay()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var nonce = Guid.NewGuid().ToString("N");
        using var first = CreateSignedRequest(client, message, nonce);
        using var firstResponse = await client.SendAsync(first);
        using var replay = CreateSignedRequest(client, message, nonce);

        using var replayResponse = await client.SendAsync(replay);
        var payload = await replayResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        replayResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertSingleJsonObject(payload);
        payload.Should().Contain(InternalServiceProblemCodes.ReplayNonce);
    }

    [Fact]
    public async Task Callback_SameEventWithNewTransportIdentity_ReplaysDomainResult()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        using var first = CreateSignedRequest(
            client, message, Guid.NewGuid().ToString("N"));
        using var second = CreateSignedRequest(
            client, message, Guid.NewGuid().ToString("N"));

        using var firstResponse = await client.SendAsync(first);
        using var secondResponse = await client.SendAsync(second);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
        (await context.CustomerBookings.SingleAsync()).BusinessStatusSequence.Should().Be(1);
    }

    [Fact]
    public async Task Callback_SameSemanticEventWithDifferentJsonFormatting_ReplaysDomainResult()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var compact = JsonSerializer.SerializeToUtf8Bytes(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        var indentedOptions = BusinessCatalogContract.CreateJsonSerializerOptions();
        indentedOptions.WriteIndented = true;
        var indented = JsonSerializer.SerializeToUtf8Bytes(message, indentedOptions);
        using var first = CreateSignedRequest(
            client, compact, Guid.NewGuid().ToString("N"), $"compact-{Guid.NewGuid():N}");
        using var second = CreateSignedRequest(
            client, indented, Guid.NewGuid().ToString("N"), $"indented-{Guid.NewGuid():N}");

        using var firstResponse = await client.SendAsync(first);
        using var secondResponse = await client.SendAsync(second);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Callback_SameTransportKeyAndExactBody_ReplaysStoredResponse()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var key = $"transport-{Guid.NewGuid():N}";
        var body = JsonSerializer.SerializeToUtf8Bytes(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        using var first = CreateSignedRequest(client, body, Guid.NewGuid().ToString("N"), key);
        using var second = CreateSignedRequest(client, body, Guid.NewGuid().ToString("N"), key);

        using var firstResponse = await client.SendAsync(first);
        using var secondResponse = await client.SendAsync(second);

        secondResponse.StatusCode.Should().Be(firstResponse.StatusCode);
        secondResponse.Content.Headers.ContentType!.MediaType.Should()
            .Be(firstResponse.Content.Headers.ContentType!.MediaType);
        (await secondResponse.Content.ReadAsStringAsync()).Should()
            .Be(await firstResponse.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Callback_ContentTypeParticipatesInTransportIdempotencyIdentity()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var body = JsonSerializer.SerializeToUtf8Bytes(
            factory.Message(BookingStatuses.Confirmed, 1),
            BusinessCatalogContract.CreateJsonSerializerOptions());
        var key = $"transport-content-type-{Guid.NewGuid():N}";

        using var wrong = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, contentType: "text/plain");
        using var wrongResponse = await client.SendAsync(wrong);
        var wrongPayload = await wrongResponse.Content.ReadAsStringAsync();

        using var identicalWrong = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, contentType: "TEXT/PLAIN");
        using var identicalWrongResponse = await client.SendAsync(identicalWrong);
        var identicalWrongPayload = await identicalWrongResponse.Content.ReadAsStringAsync();

        using var correctedSameKey = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key);
        using var correctedSameKeyResponse = await client.SendAsync(correctedSameKey);
        var correctedSameKeyPayload = await correctedSameKeyResponse.Content.ReadAsStringAsync();

        using var correctedNewKey = CreateSignedRequest(
            client,
            body,
            Guid.NewGuid().ToString("N"),
            $"transport-content-type-corrected-{Guid.NewGuid():N}");
        using var correctedNewKeyResponse = await client.SendAsync(correctedNewKey);

        wrongResponse.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        identicalWrongResponse.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        identicalWrongPayload.Should().Be(wrongPayload);
        correctedSameKeyResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        correctedSameKeyPayload.Should().Contain(
            InternalServiceProblemCodes.IdempotencyConflict);
        correctedNewKeyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerInternalIdempotencyRecords.CountAsync()).Should().Be(2);
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
        (await context.CustomerBookings.SingleAsync()).Status
            .Should().Be(BookingStatuses.Confirmed);
    }

    [Fact]
    public async Task Callback_MalformedContentType_PreservesBoundedExactTransportIdentity()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var body = Encoding.UTF8.GetBytes("{}");
        var key = $"malformed-content-type-{Guid.NewGuid():N}";

        using var first = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, contentType: "bad type/a");
        using var replay = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, contentType: "bad type/a");
        using var conflict = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, contentType: "bad type/b");

        using var firstResponse = await client.SendAsync(first);
        using var replayResponse = await client.SendAsync(replay);
        using var conflictResponse = await client.SendAsync(conflict);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        replayResponse.StatusCode.Should().Be(firstResponse.StatusCode);
        (await replayResponse.Content.ReadAsStringAsync()).Should()
            .Be(await firstResponse.Content.ReadAsStringAsync());
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Callback_DifferentOversizedContentTypes_ConflictWithoutPersistingRawHeader()
    {
        var logger = new Mock<IAppLogger>();
        await using var factory = new BookingStatusCallbackFactory(
            useSqlServer: true,
            logger: logger.Object);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var body = Encoding.UTF8.GetBytes("{}");
        var key = $"oversized-content-type-{Guid.NewGuid():N}";
        var firstRaw = $"application/{new string('a', 3000)}";
        var secondRaw = $"application/{new string('b', 3000)}";

        using var first = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, contentType: firstRaw);
        using var conflict = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, contentType: secondRaw);
        using var firstResponse = await client.SendAsync(first);
        using var conflictResponse = await client.SendAsync(conflict);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await context.CustomerInternalIdempotencyRecords
            .AsNoTracking().SingleAsync(value => value.IdempotencyKey == key);
        persisted.RequestHash.Should().HaveLength(64);
        persisted.RequestHash.Should().NotContain(firstRaw);
        persisted.RequestHash.Should().NotContain(secondRaw);
        logger.Invocations
            .SelectMany(invocation => invocation.Arguments.OfType<string>())
            .Should().NotContain(value =>
                value.Contains(firstRaw, StringComparison.Ordinal) ||
                value.Contains(secondRaw, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_SameTransportKeyWithDifferentExactBody_ReturnsConflict()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var key = $"transport-{Guid.NewGuid():N}";
        using var first = CreateSignedRequest(
            client,
            JsonSerializer.SerializeToUtf8Bytes(
                factory.Message(BookingStatuses.Confirmed, 1),
                BusinessCatalogContract.CreateJsonSerializerOptions()),
            Guid.NewGuid().ToString("N"),
            key);
        using var firstResponse = await client.SendAsync(first);
        using var conflict = CreateSignedRequest(
            client,
            JsonSerializer.SerializeToUtf8Bytes(
                factory.Message(BookingStatuses.Cancelled, 1),
                BusinessCatalogContract.CreateJsonSerializerOptions()),
            Guid.NewGuid().ToString("N"),
            key);

        using var conflictResponse = await client.SendAsync(conflict);
        var payload = await conflictResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        AssertSingleJsonObject(payload);
        payload.Should().Contain(InternalServiceProblemCodes.IdempotencyConflict);
        conflictResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task ReadBodyAsync_ChunkedBodyOverLimit_StopsAfterMaximumPlusOne()
    {
        var stream = new CountingChunkedStream(200_000, 1024);
        var context = new DefaultHttpContext();
        context.Request.Body = stream;
        context.Request.ContentLength = null;

        Func<Task> action = async () =>
            await CustomerInternalServiceMiddleware.ReadBodyAsync(context, 65_536);

        await action.Should().ThrowAsync<InvalidDataException>();
        stream.BytesRead.Should().Be(65_537);
    }

    [Fact]
    public async Task Callback_RelationalConcurrentIdenticalTransportRequests_ApplyOnce()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        var key = $"concurrent-{Guid.NewGuid():N}";
        using var first = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key);
        using var second = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key);

        var responses = await Task.WhenAll(
            client.SendAsync(first),
            client.SendAsync(second));

        responses.Should().OnlyContain(value => value.StatusCode == HttpStatusCode.OK);
        (await responses[0].Content.ReadAsStringAsync()).Should()
            .Be(await responses[1].Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
        (await context.CustomerInternalIdempotencyRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Callback_ClientCancellationAfterDomainCommit_CompletesAndRetryReplaysOnce()
    {
        await using var factory = new BookingStatusCallbackFactory(
            useSqlServer: true,
            pauseAfterCommit: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        var key = $"client-abort-{Guid.NewGuid():N}";
        using var first = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key);
        using var cancellation = new CancellationTokenSource();

        var firstTask = client.SendAsync(first, cancellation.Token);
        await factory.DomainCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        factory.ContinueAfterCommit.TrySetResult();
        await FluentActions.Awaiting(() => firstTask)
            .Should().ThrowAsync<OperationCanceledException>();
        await factory.WaitForTransportCompletionAsync(key);

        using var retry = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key);
        using var retryResponse = await client.SendAsync(retry);

        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.EndpointExecutionCount.Should().Be(1);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
        (await context.CustomerInternalIdempotencyRecords
            .SingleAsync(value => value.IdempotencyKey == key)).State
            .Should().Be(CustomerInternalIdempotencyState.Completed);
    }

    [Fact]
    public async Task Callback_RelationalConcurrentConflictingTransportRequests_HaveOneWinner()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var key = $"conflict-{Guid.NewGuid():N}";
        using var confirmed = CreateSignedRequest(
            client,
            JsonSerializer.SerializeToUtf8Bytes(
                factory.Message(BookingStatuses.Confirmed, 1),
                BusinessCatalogContract.CreateJsonSerializerOptions()),
            Guid.NewGuid().ToString("N"),
            key);
        using var cancelled = CreateSignedRequest(
            client,
            JsonSerializer.SerializeToUtf8Bytes(
                factory.Message(BookingStatuses.Cancelled, 1),
                BusinessCatalogContract.CreateJsonSerializerOptions()),
            Guid.NewGuid().ToString("N"),
            key);

        var responses = await Task.WhenAll(
            client.SendAsync(confirmed),
            client.SendAsync(cancelled));

        responses.Select(value => value.StatusCode).Should()
            .BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
        (await context.CustomerInternalIdempotencyRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Callback_ExpiredNonceCanBeReusedAndExpiredRowsAreCleaned()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new BookingStatusCallbackFactory(
            useSqlServer: true,
            timeProvider: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        var nonce = Guid.NewGuid().ToString("N");
        using var first = CreateSignedRequest(
            client, body, nonce, $"nonce-{Guid.NewGuid():N}", timestamp: clock.GetUtcNow());
        using var firstResponse = await client.SendAsync(first);
        clock.Advance(TimeSpan.FromSeconds(301));
        using (var seedScope = factory.Services.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.CustomerInternalServiceNonces.AddRange(
                new CustomerInternalServiceNonce
                {
                    ServiceId = "expired-other",
                    Nonce = Guid.NewGuid().ToString("N"),
                    AcceptedAtUtc = clock.GetUtcNow().AddMinutes(-10),
                    ExpiresAtUtc = clock.GetUtcNow().AddMinutes(-5)
                },
                new CustomerInternalServiceNonce
                {
                    ServiceId = "active-other",
                    Nonce = Guid.NewGuid().ToString("N"),
                    AcceptedAtUtc = clock.GetUtcNow(),
                    ExpiresAtUtc = clock.GetUtcNow().AddMinutes(5)
                });
            await context.SaveChangesAsync();
        }
        using var replay = CreateSignedRequest(
            client, body, nonce, $"nonce-{Guid.NewGuid():N}", timestamp: clock.GetUtcNow());

        using var replayResponse = await client.SendAsync(replay);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var verification = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await verification.CustomerInternalServiceNonces
            .AnyAsync(value => value.ServiceId == "expired-other")).Should().BeFalse();
        (await verification.CustomerInternalServiceNonces
            .AnyAsync(value => value.ServiceId == "active-other")).Should().BeTrue();
        (await verification.CustomerInternalServiceNonces
            .SingleAsync(value =>
                value.ServiceId == BookingStatusCallbackFactory.ServiceId &&
                value.Nonce == nonce)).ExpiresAtUtc.Should().BeAfter(clock.GetUtcNow());
    }

    [Fact]
    public async Task Callback_RelationalConcurrentNonceReuse_AllowsOnlyOneRequest()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var nonce = Guid.NewGuid().ToString("N");
        using var first = CreateSignedRequest(
            client,
            factory.Message(BookingStatuses.Confirmed, 1),
            nonce);
        using var second = CreateSignedRequest(
            client,
            factory.Message(BookingStatuses.Cancelled, 1),
            nonce);

        var responses = await Task.WhenAll(
            client.SendAsync(first),
            client.SendAsync(second));

        responses.Select(value => value.StatusCode).Should()
            .BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Unauthorized]);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Callback_ExpiredInProgressTransportRecord_IsRecoverable()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new BookingStatusCallbackFactory(
            useSqlServer: true,
            timeProvider: clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var key = $"recover-{Guid.NewGuid():N}";
        var body = JsonSerializer.SerializeToUtf8Bytes(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        using var first = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, timestamp: clock.GetUtcNow());
        using var firstResponse = await client.SendAsync(first);
        using (var seedScope = factory.Services.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await context.CustomerInternalIdempotencyRecords.SingleAsync();
            record.State = CustomerInternalIdempotencyState.InProgress;
            record.ResponseStatusCode = null;
            record.ResponseContentType = null;
            record.ResponseBody = null;
            record.ExpiresAtUtc = clock.GetUtcNow().AddSeconds(-1);
            await context.SaveChangesAsync();
        }
        using var retry = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key, timestamp: clock.GetUtcNow());

        using var retryResponse = await client.SendAsync(retry);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var verification = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var completed = await verification.CustomerInternalIdempotencyRecords.SingleAsync();
        completed.State.Should().Be(CustomerInternalIdempotencyState.Completed);
        completed.ResponseBody.Should().NotBeNull();
        (await verification.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Callback_IncompleteCompletedRecord_ReturnsUnavailableNotSuccess()
    {
        await using var factory = new BookingStatusCallbackFactory(useSqlServer: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var key = $"incomplete-{Guid.NewGuid():N}";
        var body = JsonSerializer.SerializeToUtf8Bytes(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        using var first = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key);
        using var firstResponse = await client.SendAsync(first);
        using (var seedScope = factory.Services.CreateScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await context.CustomerInternalIdempotencyRecords.SingleAsync();
            record.ResponseBody = null;
            await context.SaveChangesAsync();
        }
        using var replay = CreateSignedRequest(
            client, body, Guid.NewGuid().ToString("N"), key);

        using var replayResponse = await client.SendAsync(replay);
        var payload = await replayResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        AssertSingleJsonObject(payload);
        payload.Should().Contain(InternalServiceProblemCodes.IdempotencyUnavailable);
        payload.Should().NotContain("\"applied\":true");
    }

    [Fact]
    public async Task Callback_WithWrongSecret_IsRejectedWithoutMutation()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = CreateSignedRequest(
            client,
            factory.Message(BookingStatuses.Confirmed, 1),
            Guid.NewGuid().ToString("N"),
            "wrong-secret-that-is-at-least-thirty-two-characters");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertSingleJsonObject(await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .CustomerBookings.SingleAsync()).Status.Should().Be(BookingStatuses.Pending);
    }

    internal sealed class CountingChunkedStream : Stream
    {
        private readonly long _length;
        private readonly int _chunkSize;
        private long _position;

        public CountingChunkedStream(long length, int chunkSize)
        {
            _length = length;
            _chunkSize = chunkSize;
        }

        public long BytesRead => _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(Math.Min(buffer.Length, _chunkSize), _length - _position);
            if (count <= 0) return 0;
            buffer[..count].Fill((byte)'x');
            _position += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    [Trait("ScenarioId", "STEP13-BODY-NULL-039")]
    public async Task Callback_LiteralNullBody_ReturnsNoStoreProblemJson()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = CreateSignedRequest(
            client,
            Encoding.UTF8.GetBytes("null"),
            Guid.NewGuid().ToString("N"),
            $"transport-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertSingleJsonObject(payload);
        payload.Should().Contain(BookingStatusErrorCodes.Invalid);
    }

    [Theory]
    [InlineData("STEP13-BODY-EMPTY-038", "", "application/json", HttpStatusCode.BadRequest, BookingStatusErrorCodes.Invalid)]
    [InlineData("STEP13-BODY-MALFORMED-040", "{", "application/json", HttpStatusCode.BadRequest, BookingStatusErrorCodes.Invalid)]
    [InlineData("content-type-regression", "{}", "text/plain", HttpStatusCode.UnsupportedMediaType, BookingStatusErrorCodes.UnsupportedMediaType)]
    public async Task Callback_InvalidJsonTransport_ReturnsStableProblemWithoutInboxState(
        string scenarioId,
        string body,
        string contentType,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = CreateSignedRequest(
            client,
            Encoding.UTF8.GetBytes(body),
            Guid.NewGuid().ToString("N"),
            $"transport-{Guid.NewGuid():N}",
            contentType: contentType);
        request.Headers.AcceptLanguage.ParseAdd("he");

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertSingleJsonObject(payload);
        payload.Should().Contain(expectedCode);
        payload.Should().Contain("corr-step13");
        payload.Should().Contain("Internal booking status request was rejected.");
        payload.Should().NotContainAny("בקשת", "הזמנה", "الحجز");
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
        (await context.CustomerBookings.SingleAsync()).Status.Should().Be(BookingStatuses.Pending);
        scenarioId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Callback_ExactlySixtyFourKilobytesOfValidJson_IsAccepted()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        var json = JsonSerializer.Serialize(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        var suffix = "}";
        var prefix = json[..^suffix.Length] + ",\"padding\":\"";
        var padding = new string('x', 65_536 - Encoding.UTF8.GetByteCount(prefix + "\"}"));
        var body = Encoding.UTF8.GetBytes(prefix + padding + "\"}");
        body.Should().HaveCount(65_536);
        using var request = CreateSignedRequest(
            client,
            body,
            Guid.NewGuid().ToString("N"),
            $"transport-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Callback_BodyOverSixtyFourKilobytes_IsRejectedBeforeBinding()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var body = Encoding.UTF8.GetBytes(
            $"{{\"padding\":\"{new string('a', 65_536)}\"}}");
        using var request = CreateSignedRequest(
            client,
            body,
            Guid.NewGuid().ToString("N"),
            $"transport-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        AssertSingleJsonObject(payload);
        payload.Should().Contain(BookingStatusErrorCodes.RequestBodyTooLarge);
        payload.Should().Contain("corr-step13");
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("STEP13-BODY-VERSION-045", "version", BookingStatusErrorCodes.Invalid, HttpStatusCode.BadRequest)]
    [InlineData("STEP13-BODY-EVENT-046", "event", BookingStatusErrorCodes.Invalid, HttpStatusCode.BadRequest)]
    [InlineData("STEP13-BODY-STATUS-047", "status", BookingStatusErrorCodes.Invalid, HttpStatusCode.BadRequest)]
    [InlineData("STEP13-BODY-SEQUENCE-048", "sequence", BookingStatusErrorCodes.Invalid, HttpStatusCode.BadRequest)]
    [InlineData("STEP13-BODY-OCCURRED-049", "occurred", BookingStatusErrorCodes.Invalid, HttpStatusCode.BadRequest)]
    [InlineData("STEP13-CALLBACK-UNKNOWN-BOOKING-060", "unknown", BookingStatusErrorCodes.NotFound, HttpStatusCode.NotFound)]
    [InlineData("STEP13-CALLBACK-RESERVATION-MISMATCH-061", "reservation", BookingStatusErrorCodes.ReferenceMismatch, HttpStatusCode.Conflict)]
    [InlineData("STEP13-CALLBACK-WORKORDER-MISMATCH-062", "workOrder", BookingStatusErrorCodes.ReferenceMismatch, HttpStatusCode.Conflict)]
    public async Task Callback_DomainRejections_WriteExactlyOneProblemDocument(
        string scenarioId,
        string failure,
        string expectedCode,
        HttpStatusCode expectedStatus)
    {
        _ = scenarioId;
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var message = factory.Message(BookingStatuses.Confirmed, 1);
        if (failure == "version")
        {
            message.ContractVersion = "v999";
        }
        else if (failure == "event")
        {
            message.EventId = Guid.Empty;
        }
        else if (failure == "status")
        {
            message.Status = "Unsupported";
        }
        else if (failure == "sequence")
        {
            message.Sequence = 0;
        }
        else if (failure == "occurred")
        {
            message.OccurredAtUtc = default;
        }
        else if (failure == "unknown")
        {
            message.BookingReference = Guid.NewGuid();
        }
        else if (failure == "reservation")
        {
            message.ReservationId = Guid.NewGuid();
        }
        else
        {
            message.WorkOrderId = Guid.NewGuid();
        }
        using var request = CreateSignedRequest(
            client,
            message,
            Guid.NewGuid().ToString("N"),
            language: "he");
        request.Headers.AcceptLanguage.ParseAdd("he");

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertSingleJsonObject(payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
        document.RootElement.GetProperty("correlationId").GetString().Should().Be("corr-step13");
        document.RootElement.GetProperty("title").GetString()
            .Should().Be("Internal booking status request was rejected.");
        document.RootElement.GetProperty("detail").GetString()
            .Should().NotContainAny("בקשת", "הזמנה", "الحجز");
        document.RootElement.TryGetProperty("language", out _).Should().BeFalse();
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
    }

    [Fact]
    [Trait("ScenarioId", "STEP13-CALLBACK-EVENT-CONFLICT-055")]
    public async Task Callback_ReusedEventWithDifferentContent_WritesExactlyOneConflictDocument()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var firstMessage = factory.Message(BookingStatuses.Confirmed, 1);
        using var first = CreateSignedRequest(client, firstMessage, Guid.NewGuid().ToString("N"));
        using var firstResponse = await client.SendAsync(first);
        var conflictingMessage = factory.Message(BookingStatuses.Cancelled, 2);
        conflictingMessage.EventId = firstMessage.EventId;
        using var conflict = CreateSignedRequest(
            client,
            conflictingMessage,
            Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(conflict);
        var payload = await response.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        AssertSingleJsonObject(payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(BookingStatusErrorCodes.EventConflict);
    }

    [Fact]
    [Trait("ScenarioId", "STEP13-RECONCILE-CONFLICT-081B")]
    public async Task Callback_NewEventAtCurrentSequence_WritesExactlyOneConflictDocument()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var firstMessage = factory.Message(BookingStatuses.Confirmed, 1);
        using var first = CreateSignedRequest(client, firstMessage, Guid.NewGuid().ToString("N"));
        using var firstResponse = await client.SendAsync(first);
        var equalSequence = factory.Message(BookingStatuses.Confirmed, 1);
        using var second = CreateSignedRequest(
            client,
            equalSequence,
            Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(second);
        var payload = await response.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertSingleJsonObject(payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(BookingStatusErrorCodes.TransitionInvalid);
    }

    [Fact]
    [Trait("ScenarioId", "STEP13-ROUTE-WRONG-VERB-003")]
    public async Task Callback_PutWrongVerb_Returns405WithoutConsumingSecurityState()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/internal/bookings/status")
        {
            Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerInternalServiceNonces.CountAsync()).Should().Be(0);
        (await context.CustomerInternalIdempotencyRecords.CountAsync()).Should().Be(0);
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP13-ROUTE-UNKNOWN-004")]
    public async Task InternalBookings_UnknownPostRoute_Returns404WithoutConsumingSecurityState()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/internal/bookings/unknown")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            "corr-step13");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.GetValues(InternalServiceWireConstants.CorrelationIdHeaderName)
            .Should().Contain("corr-step13");
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerInternalServiceNonces.CountAsync()).Should().Be(0);
        (await context.CustomerInternalIdempotencyRecords.CountAsync()).Should().Be(0);
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP13-LOCALIZATION-MALFORMED-078B")]
    public async Task Read_UnsupportedExplicitLanguage_IsIgnoredByMachineContract()
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = CreateSignedReadRequest(
            client,
            Guid.NewGuid(),
            "not-supported",
            "he");

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertSingleJsonObject(payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(BookingStatusErrorCodes.NotFound);
        document.RootElement.GetProperty("title").GetString()
            .Should().Be("Internal booking status request was rejected.");
        document.RootElement.TryGetProperty("language", out _).Should().BeFalse();
        document.RootElement.GetProperty("correlationId").GetString().Should().Be("corr-step13");
    }

    [Theory]
    [InlineData("he", "ar")]
    [InlineData(null, "-, ;q=1")]
    public async Task Read_LanguageInputs_DoNotLocalizeMachineContract(
        string? queryLanguage,
        string acceptLanguage)
    {
        using var factory = new BookingStatusCallbackFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = CreateSignedReadRequest(
            client,
            Guid.NewGuid(),
            queryLanguage,
            acceptLanguage);

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        AssertSingleJsonObject(payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("title").GetString()
            .Should().Be("Internal booking status request was rejected.");
        document.RootElement.TryGetProperty("language", out _).Should().BeFalse();
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
    }

    private static HttpRequestMessage CreateSignedRequest(
        HttpClient client,
        BookingStatusChangedMessage message,
        string nonce,
        string secret = BookingStatusCallbackFactory.Secret,
        string? language = null)
    {
        var json = JsonSerializer.Serialize(
            message,
            BusinessCatalogContract.CreateJsonSerializerOptions());
        return CreateSignedRequest(
            client,
            Encoding.UTF8.GetBytes(json),
            nonce,
            $"transport-{Guid.NewGuid():N}",
            secret,
            language: language);
    }

    private static HttpRequestMessage CreateSignedRequest(
        HttpClient client,
        byte[] body,
        string nonce,
        string idempotencyKey,
        string secret = BookingStatusCallbackFactory.Secret,
        string contentType = "application/json",
        DateTimeOffset? timestamp = null,
        string? language = null)
    {
        var timestampText = (timestamp ?? DateTimeOffset.UtcNow).ToString("O");
        var query = language is null
            ? string.Empty
            : $"?language={Uri.EscapeDataString(language)}";
        var uri = new Uri(
            client.BaseAddress!,
            $"/api/v1/internal/bookings/status{query}");
        var queryPairs = language is null
            ? Array.Empty<KeyValuePair<string, string?>>()
            : [new KeyValuePair<string, string?>("language", language)];
        var canonical = InternalServiceCanonicalRequest.Build(
            BookingStatusCallbackFactory.ServiceId,
            "POST",
            uri.AbsolutePath,
            queryPairs,
            timestampText,
            nonce,
            idempotencyKey,
            InternalServiceCanonicalRequest.ComputeSha256Hex(body));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.ServiceIdHeaderName, BookingStatusCallbackFactory.ServiceId);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.TimestampHeaderName, timestampText);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.NonceHeaderName, nonce);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.SignatureHeaderName, signature);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.IdempotencyKeyHeaderName, idempotencyKey);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.CorrelationIdHeaderName, "corr-step13");
        return request;
    }

    private static HttpRequestMessage CreateSignedReadRequest(
        HttpClient client,
        Guid reference,
        string? language,
        string acceptLanguage)
    {
        var query = language is null
            ? string.Empty
            : $"?language={Uri.EscapeDataString(language)}";
        var uri = new Uri(
            client.BaseAddress!,
            $"/api/v1/internal/bookings/{reference:D}{query}");
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var queryPairs = language is null
            ? Array.Empty<KeyValuePair<string, string?>>()
            : [new KeyValuePair<string, string?>("language", language)];
        var canonical = InternalServiceCanonicalRequest.Build(
            BookingStatusCallbackFactory.ServiceId,
            "GET",
            uri.AbsolutePath,
            queryPairs,
            timestamp,
            nonce,
            string.Empty,
            InternalServiceCanonicalRequest.ComputeSha256Hex([]));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(BookingStatusCallbackFactory.Secret));
        var signature = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
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
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            "corr-step13");
        request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        return request;
    }

    private static void AssertSingleJsonObject(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }
}

internal sealed class BookingStatusCallbackFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    public const string ServiceId = "ghseeli-business";
    public const string Secret = "step13-callback-secret-at-least-thirty-two-characters";
    private readonly string _databaseName = $"BookingStatusCallbacks-{Guid.NewGuid()}";
    private readonly CustomerBooking _booking;
    private readonly bool _useSqlServer;
    private readonly TimeProvider? _timeProvider;
    private readonly IAppLogger? _logger;
    private readonly bool _pauseAfterCommit;
    private int _endpointExecutionCount;

    public TaskCompletionSource DomainCommitted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ContinueAfterCommit { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int EndpointExecutionCount => Volatile.Read(ref _endpointExecutionCount);

    public BookingStatusCallbackFactory(
        bool useSqlServer = false,
        TimeProvider? timeProvider = null,
        IAppLogger? logger = null,
        bool pauseAfterCommit = false)
    {
        _useSqlServer = useSqlServer;
        _timeProvider = timeProvider;
        _logger = logger;
        _pauseAfterCommit = pauseAfterCommit;
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "callback@example.com",
            Email = "callback@example.com",
            FullName = "Callback Customer"
        };
        _booking = new CustomerBooking
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
            CatalogVersion = 1,
            ConfirmedDraftVersion = 1,
            Status = BookingStatuses.Pending,
            StatusChangedAtUtc = DateTimeOffset.UtcNow,
            ProviderNameAr = "مزود",
            BranchNameAr = "فرع",
            VehicleType = "Sedan",
            AddressLine = "Address",
            Currency = "ILS",
            ServiceFeeMode = "None",
            QuotedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public BookingStatusChangedMessage Message(string status, long sequence) => new()
    {
        EventId = Guid.NewGuid(),
        BookingReference = _booking.PublicReference,
        ReservationId = _booking.BusinessReservationId,
        WorkOrderId = _booking.BusinessWorkOrderId,
        Status = status,
        Sequence = sequence,
        OccurredAtUtc = DateTimeOffset.UtcNow
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:CustomerConnection", "Server=(localdb)\\MSSQLLocalDB;Database=Step13;Trusted_Connection=True;");
        builder.UseSetting("JwtSettings:SecretKey", "step13-jwt-secret-at-least-thirty-two-characters");
        builder.UseSetting("CustomerInternalServiceAuthentication:RequireHttps", "true");
        builder.UseSetting("CustomerInternalServiceAuthentication:Services:0:ServiceId", ServiceId);
        builder.UseSetting("CustomerInternalServiceAuthentication:Services:0:ActiveSecret", Secret);
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:Services:0:AllowedOperations:0",
            InternalServiceOperationNames.BookingStatusCallback);
        builder.UseSetting(
            "CustomerInternalServiceAuthentication:Services:0:AllowedOperations:1",
            InternalServiceOperationNames.BookingStatusRead);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                if (_useSqlServer)
                {
                    options.UseSqlServer(
                        $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True",
                        sql => sql.EnableRetryOnFailure());
                }
                else
                {
                    options.UseInMemoryDatabase(_databaseName);
                }
            });
            if (_timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeProvider);
            }
            if (_logger is not null)
            {
                services.RemoveAll<IAppLogger>();
                services.AddSingleton(_logger);
            }
            if (_pauseAfterCommit)
            {
                services.RemoveAll<IBookingStatusInboxService>();
                services.AddScoped<BookingStatusInboxService>();
                services.AddScoped<IBookingStatusInboxService>(provider =>
                    new PausingBookingStatusInboxService(
                        provider.GetRequiredService<BookingStatusInboxService>(),
                        this));
            }
            using var scope = services.BuildServiceProvider().CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (_useSqlServer)
            {
                context.Database.EnsureDeleted();
            }
            context.Database.EnsureCreated();
            context.CustomerBookings.Add(_booking);
            context.SaveChanges();
        });
    }

    public async Task WaitForTransportCompletionAsync(string key)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await context.CustomerInternalIdempotencyRecords
                .AsNoTracking()
                .AnyAsync(value =>
                    value.IdempotencyKey == key &&
                    value.State == CustomerInternalIdempotencyState.Completed))
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("Transport completion was not persisted.");
    }

    private sealed class PausingBookingStatusInboxService(
        BookingStatusInboxService inner,
        BookingStatusCallbackFactory owner) : IBookingStatusInboxService
    {
        public async Task<BookingStatusCallbackResponse> ApplyAsync(
            BookingStatusChangedMessage message,
            string requestHash,
            bool reconciliation,
            string correlationId,
            CancellationToken cancellationToken)
        {
            var response = await inner.ApplyAsync(
                message,
                requestHash,
                reconciliation,
                correlationId,
                cancellationToken);
            Interlocked.Increment(ref owner._endpointExecutionCount);
            owner.DomainCommitted.TrySetResult();
            await owner.ContinueAfterCommit.Task.WaitAsync(cancellationToken);
            return response;
        }

        public Task<BookingStatusCallbackResponse> ReconcileAsync(
            Guid bookingReference,
            string correlationId,
            CancellationToken cancellationToken) =>
            inner.ReconcileAsync(bookingReference, correlationId, cancellationToken);

        public Task<BookingStatusReadResponse?> GetCurrentAsync(
            Guid bookingReference,
            CancellationToken cancellationToken) =>
            inner.GetCurrentAsync(bookingReference, cancellationToken);
    }

    public new async ValueTask DisposeAsync()
    {
        if (_useSqlServer)
        {
            try
            {
                using var scope = Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureDeletedAsync();
            }
            catch
            {
            }
        }
        await base.DisposeAsync();
    }
}

internal sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    public AdjustableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void Advance(TimeSpan duration) => _utcNow += duration;
}
