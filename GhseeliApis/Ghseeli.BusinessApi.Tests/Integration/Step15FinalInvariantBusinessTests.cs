using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ghseeli.BusinessApi.Tests.Integration;

/// <summary>
/// Final cross-cutting Business proofs for the frozen Step 15 invariants.
/// </summary>
public sealed class Step15FinalInvariantBusinessTests
{
    [Fact]
    [Trait("ScenarioId", "STEP15-TRANSPORT-QUERY-ENCODING-129")]
    public async Task Encoded_query_names_values_and_duplicates_have_runtime_semantics()
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateSecureClient();

        using var encoded = await client.GetAsync(
            "/api/v1/business/company?lang%75age=%68%65");
        using var duplicate = await client.GetAsync(
            "/api/v1/business/company?lang%75age=ar&language=%68%65");

        encoded.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        encoded.Content.Headers.ContentLanguage.Should().ContainSingle("he");
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("language_invalid");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-SWAGGER-DETERMINISTIC-151")]
    public async Task Repeated_OpenAPI_generation_is_byte_stable_with_unique_operations_and_resolved_refs()
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateSecureClient();

        var first = await client.GetByteArrayAsync("/swagger/v1/swagger.json");
        var second = await client.GetByteArrayAsync("/swagger/v1/swagger.json");

        second.Should().Equal(first, "repeated generation on one immutable host must be byte deterministic");
        using var document = JsonDocument.Parse(first);
        var operationIds = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => IsHttpMethod(operation.Name))
            .Select(operation => operation.Value.GetProperty("operationId").GetString())
            .ToArray();
        operationIds.Should().NotContainNulls().And.OnlyHaveUniqueItems();
        AssertAllLocalReferencesResolve(document.RootElement);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-DB-FAILURE-162")]
    public async Task Database_failpoint_maps_retryably_and_has_no_committed_side_effect()
    {
        const string databaseDetail =
            "Server=private-business-db;Password=raw-password; controlled save failure";
        var service = new ControlledDeadLetterService((_, _, _, _) =>
            throw new DbUpdateException(databaseDetail));
        await using var factory = new ControlledBusinessFactory(services =>
        {
            services.RemoveAll<IBookingStatusDeadLetterService>();
            services.AddSingleton<IBookingStatusDeadLetterService>(service);
        });
        using var client = factory.CreateAuthenticatedClient(
            factory.AdminUserId,
            Ghseeli.BusinessApi.Constants.BusinessRoles.Admin);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/business/admin/booking-status-outbox/{Guid.NewGuid():D}/requeue");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "step15-db-failpoint");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("service_unavailable");
        body.Should().NotContain(databaseDetail)
            .And.NotContain("private-business-db")
            .And.NotContain("raw-password");
        service.CommittedCount.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-CONCURRENCY-163")]
    public async Task Parallel_same_key_Business_mutations_have_one_winner_through_HTTP_middleware()
    {
        var service = new OneWinnerDeadLetterService();
        await using var factory = new ControlledBusinessFactory(services =>
        {
            services.RemoveAll<IBookingStatusDeadLetterService>();
            services.AddSingleton<IBookingStatusDeadLetterService>(service);
        });
        using var client = factory.CreateAuthenticatedClient(
            factory.AdminUserId,
            Ghseeli.BusinessApi.Constants.BusinessRoles.Admin);
        var eventId = Guid.Parse("16316316-3163-4163-8163-163163163163");

        var responses = await Task.WhenAll(
            SendRequeueAsync(client, eventId, "same-business-http-key"),
            SendRequeueAsync(client, eventId, "same-business-http-key"));
        var bodies = await Task.WhenAll(responses.Select(x => x.Content.ReadAsStringAsync()));

        responses.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.OK);
        bodies[0].Should().Be(bodies[1]);
        service.WinnerCount.Should().Be(1);
        service.CallCount.Should().Be(2);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> SendRequeueAsync(
        HttpClient client,
        Guid eventId,
        string key)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/business/admin/booking-status-outbox/{eventId:D}/requeue");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static bool IsHttpMethod(string name) =>
        name is "get" or "put" or "post" or "delete" or "patch" or "options" or "head" or "trace";

    private static void AssertAllLocalReferencesResolve(JsonElement root)
    {
        foreach (var reference in EnumerateReferences(root))
        {
            reference.Should().StartWith("#/");
            var current = root;
            foreach (var rawSegment in reference[2..].Split('/'))
            {
                var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
                current.TryGetProperty(segment, out current).Should().BeTrue(
                    $"OpenAPI reference '{reference}' must resolve");
            }
        }
    }

    private static IEnumerable<string> EnumerateReferences(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("$ref"))
                {
                    yield return property.Value.GetString()!;
                }
                foreach (var nested in EnumerateReferences(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in EnumerateReferences(item))
            {
                yield return nested;
            }
        }
    }

    private sealed class ControlledBusinessFactory(
        Action<IServiceCollection> configureServices) : CatalogApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(configureServices);
        }
    }

    private sealed class ControlledDeadLetterService(
        Func<Guid, Guid, string, CancellationToken, DeadLetterRequeueResponse> action)
        : IBookingStatusDeadLetterService
    {
        private int _committed;
        public int CommittedCount => Volatile.Read(ref _committed);

        public Task<DeadLetterRequeueResponse?> RequeueAsync(
            Guid adminUserId,
            Guid eventId,
            string requestId,
            CancellationToken cancellationToken)
        {
            var response = action(adminUserId, eventId, requestId, cancellationToken);
            Interlocked.Increment(ref _committed);
            return Task.FromResult<DeadLetterRequeueResponse?>(response);
        }
    }

    private sealed class OneWinnerDeadLetterService : IBookingStatusDeadLetterService
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<DeadLetterRequeueResponse?>>> _results =
            new(StringComparer.Ordinal);
        private int _calls;
        private int _winners;

        public int CallCount => Volatile.Read(ref _calls);
        public int WinnerCount => Volatile.Read(ref _winners);

        public Task<DeadLetterRequeueResponse?> RequeueAsync(
            Guid adminUserId,
            Guid eventId,
            string requestId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return _results.GetOrAdd(
                requestId,
                _ => new Lazy<Task<DeadLetterRequeueResponse?>>(
                    async () =>
                    {
                        Interlocked.Increment(ref _winners);
                        await Task.Delay(50, cancellationToken);
                        return new DeadLetterRequeueResponse(
                            eventId,
                            DeadLetterRequeueOutcomes.Requeued,
                            BookingStatusOutboxStates.Pending,
                            1);
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
    }
}
