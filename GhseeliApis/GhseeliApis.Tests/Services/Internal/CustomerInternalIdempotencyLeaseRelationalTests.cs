using FluentAssertions;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Internal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace GhseeliApis.Tests.Services.Internal;

/// <summary>
/// Verifies durable ownership and renewable Customer transport-idempotency leases.
/// </summary>
public sealed class CustomerInternalIdempotencyLeaseRelationalTests
{
    [Fact]
    public async Task LongEndpoint_RenewsWhileDuplicateAndCleanupRun_ExecutesOnce()
    {
        await using var database = await SqlDatabase.CreateAsync();
        var optionsValue = OptionsValue();
        optionsValue.InProgressWaitMilliseconds = 5_000;
        optionsValue.InProgressPollMilliseconds = 20;
        optionsValue.InProgressLeaseRenewalFraction = 0.25;
        optionsValue.Services =
        [
            new CustomerInternalServiceDefinition
            {
                ServiceId = "business",
                ActiveSecret = Secret,
                AllowedOperations = ["reconcile"]
            }
        ];
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(optionsValue));
        services.AddSingleton<IAppLogger>(Mock.Of<IAppLogger>());
        services.AddDbContext<ApplicationDbContext>(builder =>
            builder.UseSqlServer(database.ConnectionString));
        services.AddScoped<
            ICustomerInternalIdempotencyLeaseService,
            CustomerInternalIdempotencyLeaseService>();
        services.AddScoped<
            ICustomerInternalIdempotencyCleanupService,
            CustomerInternalIdempotencyCleanupService>();
        await using var provider = services.BuildServiceProvider();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns("Development");
        var executionCount = 0;
        var middleware = new CustomerInternalServiceMiddleware(async context =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(TimeSpan.FromMilliseconds(2_500), context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"ok\":true}", context.RequestAborted);
        });

        var firstTask = SendAsync(
            provider,
            environment.Object,
            middleware,
            optionsValue,
            "same",
            Guid.NewGuid().ToString("N"));
        await Task.Delay(1_200);
        DateTimeOffset leaseBeforeCleanup;
        await using (var inspectionScope = provider.CreateAsyncScope())
        {
            var context = inspectionScope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();
            leaseBeforeCleanup = (await context.CustomerInternalIdempotencyRecords
                .AsNoTracking().SingleAsync()).LeaseExpiresAtUtc!.Value;
            leaseBeforeCleanup.Should().BeAfter(DateTimeOffset.UtcNow);
            var cleanup = inspectionScope.ServiceProvider
                .GetRequiredService<ICustomerInternalIdempotencyCleanupService>();
            (await cleanup.CleanupExpiredAsync(CancellationToken.None)).Should().Be(0);
        }
        var duplicateTask = SendAsync(
            provider,
            environment.Object,
            middleware,
            optionsValue,
            "same",
            Guid.NewGuid().ToString("N"));

        var responses = await Task.WhenAll(firstTask, duplicateTask);

        responses.Should().OnlyContain(value =>
            value.StatusCode == StatusCodes.Status200OK &&
            value.Body == "{\"ok\":true}");
        executionCount.Should().Be(1);
        await using var verificationScope = provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        var completed = await verification.CustomerInternalIdempotencyRecords
            .AsNoTracking().SingleAsync();
        completed.State.Should().Be(CustomerInternalIdempotencyState.Completed);
        completed.OwnerToken.Should().BeNull();
        completed.LeaseExpiresAtUtc.Should().BeNull();
        completed.ExpiresAtUtc.Should().BeAfter(leaseBeforeCleanup);
    }

    [Fact]
    public async Task RenewedLease_SurvivesMultipleLeasePeriodsDuplicateAndCleanup()
    {
        var clock = new AdjustableLeaseClock(new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        await using var database = await SqlDatabase.CreateAsync();
        var options = Options.Create(OptionsValue());
        CustomerInternalIdempotencyClaim acquired;
        await using (var context = database.CreateContext())
        {
            var service = new CustomerInternalIdempotencyLeaseService(context, clock, options);
            acquired = await service.ClaimAsync(
                "business", "reconcile", "long", Hash('a'), CancellationToken.None);
        }

        for (var index = 0; index < 4; index++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(700));
            await using var renewalContext = database.CreateContext();
            var renewal = new CustomerInternalIdempotencyLeaseService(
                renewalContext, clock, options);
            (await renewal.RenewAsync(
                acquired.RecordId, acquired.OwnerToken, CancellationToken.None))
                .Should().NotBeNull();

            await using var cleanupContext = database.CreateContext();
            var cleanup = new CustomerInternalIdempotencyCleanupService(
                cleanupContext, clock, options);
            (await cleanup.CleanupExpiredAsync(CancellationToken.None)).Should().Be(0);

            await using var duplicateContext = database.CreateContext();
            var duplicate = new CustomerInternalIdempotencyLeaseService(
                duplicateContext, clock, options);
            (await duplicate.ClaimAsync(
                "business", "reconcile", "long", Hash('a'), CancellationToken.None))
                .Kind.Should().Be(CustomerInternalIdempotencyClaimKind.InProgress);
        }

        await using var verification = database.CreateContext();
        var record = await verification.CustomerInternalIdempotencyRecords.SingleAsync();
        record.OwnerToken.Should().Be(acquired.OwnerToken);
        record.LeaseExpiresAtUtc.Should().BeAfter(clock.GetUtcNow());
    }

    [Fact]
    public async Task ForcedReclaim_RejectsStaleCompletionAndOnlyNewOwnerCanComplete()
    {
        var clock = new AdjustableLeaseClock(new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        await using var database = await SqlDatabase.CreateAsync();
        var options = Options.Create(OptionsValue());
        CustomerInternalIdempotencyClaim stale;
        await using (var context = database.CreateContext())
        {
            stale = await new CustomerInternalIdempotencyLeaseService(context, clock, options)
                .ClaimAsync("business", "callback", "reclaim", Hash('a'), CancellationToken.None);
        }
        clock.Advance(TimeSpan.FromSeconds(2));
        CustomerInternalIdempotencyClaim current;
        await using (var context = database.CreateContext())
        {
            current = await new CustomerInternalIdempotencyLeaseService(context, clock, options)
                .ClaimAsync("business", "callback", "reclaim", Hash('a'), CancellationToken.None);
        }

        current.Kind.Should().Be(CustomerInternalIdempotencyClaimKind.Acquired);
        current.OwnerToken.Should().NotBe(stale.OwnerToken);
        await using (var context = database.CreateContext())
        {
            var service = new CustomerInternalIdempotencyLeaseService(context, clock, options);
            (await service.ReleaseAsync(
                stale.RecordId, stale.OwnerToken, CancellationToken.None)).Should().BeFalse();
            (await service.CompleteAsync(
                stale.RecordId, stale.OwnerToken, 200, "application/json", [1],
                CancellationToken.None)).Should().BeFalse();
            (await service.CompleteAsync(
                current.RecordId, current.OwnerToken, 202, "application/json", [2],
                CancellationToken.None)).Should().BeTrue();
        }

        await using var verification = database.CreateContext();
        var record = await verification.CustomerInternalIdempotencyRecords.SingleAsync();
        record.ResponseStatusCode.Should().Be(202);
        record.ResponseBody.Should().Equal(2);
    }

    [Fact]
    public async Task ExpiredOrphan_ConcurrentReclaimHasExactlyOneNewOwner()
    {
        var clock = new AdjustableLeaseClock(new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        await using var database = await SqlDatabase.CreateAsync();
        var options = Options.Create(OptionsValue());
        await using (var context = database.CreateContext())
        {
            await new CustomerInternalIdempotencyLeaseService(context, clock, options)
                .ClaimAsync("business", "callback", "race", Hash('a'), CancellationToken.None);
        }
        clock.Advance(TimeSpan.FromSeconds(2));

        async Task<CustomerInternalIdempotencyClaim> ReclaimAsync()
        {
            await using var context = database.CreateContext();
            return await new CustomerInternalIdempotencyLeaseService(context, clock, options)
                .ClaimAsync("business", "callback", "race", Hash('a'), CancellationToken.None);
        }

        var claims = await Task.WhenAll(ReclaimAsync(), ReclaimAsync());

        claims.Count(value =>
            value.Kind == CustomerInternalIdempotencyClaimKind.Acquired).Should().Be(1);
        claims.Count(value =>
            value.Kind == CustomerInternalIdempotencyClaimKind.InProgress).Should().Be(1);
        await using var verification = database.CreateContext();
        (await verification.CustomerInternalIdempotencyRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CrashWithoutHeartbeat_EventuallyReclaimsOnlyOnce()
    {
        var clock = new AdjustableLeaseClock(new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        await using var database = await SqlDatabase.CreateAsync();
        var options = Options.Create(OptionsValue());
        await using (var context = database.CreateContext())
        {
            await new CustomerInternalIdempotencyLeaseService(context, clock, options)
                .ClaimAsync("business", "reconcile", "crash", Hash('a'), CancellationToken.None);
        }
        clock.Advance(TimeSpan.FromSeconds(2));
        await using var reclaimContext = database.CreateContext();
        var service = new CustomerInternalIdempotencyLeaseService(
            reclaimContext, clock, options);

        var reclaimed = await service.ClaimAsync(
            "business", "reconcile", "crash", Hash('a'), CancellationToken.None);
        var duplicate = await service.ClaimAsync(
            "business", "reconcile", "crash", Hash('a'), CancellationToken.None);

        reclaimed.Kind.Should().Be(CustomerInternalIdempotencyClaimKind.Acquired);
        duplicate.Kind.Should().Be(CustomerInternalIdempotencyClaimKind.InProgress);
    }

    [Fact]
    public async Task EndpointCancellationAfterExecutionBegins_PreservesOwnedClaim()
    {
        await using var database = await SqlDatabase.CreateAsync();
        var optionsValue = OptionsValue();
        optionsValue.Services =
        [
            new CustomerInternalServiceDefinition
            {
                ServiceId = "business",
                ActiveSecret = Secret,
                AllowedOperations = ["reconcile"]
            }
        ];
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(optionsValue));
        services.AddSingleton<IAppLogger>(Mock.Of<IAppLogger>());
        services.AddDbContext<ApplicationDbContext>(builder =>
            builder.UseSqlServer(database.ConnectionString));
        services.AddScoped<
            ICustomerInternalIdempotencyLeaseService,
            CustomerInternalIdempotencyLeaseService>();
        services.AddScoped<
            ICustomerInternalIdempotencyCleanupService,
            CustomerInternalIdempotencyCleanupService>();
        await using var provider = services.BuildServiceProvider();
        var environment = Mock.Of<IWebHostEnvironment>(value =>
            value.EnvironmentName == "Development");
        var middleware = new CustomerInternalServiceMiddleware(
            _ => throw new OperationCanceledException("ambiguous endpoint cancellation"));

        Func<Task> action = async () => await SendAsync(
            provider,
            environment,
            middleware,
            optionsValue,
            "ambiguous",
            Guid.NewGuid().ToString("N"));

        await action.Should().ThrowAsync<OperationCanceledException>();
        await using var verification = database.CreateContext();
        var record = await verification.CustomerInternalIdempotencyRecords
            .AsNoTracking().SingleAsync();
        record.State.Should().Be(CustomerInternalIdempotencyState.InProgress);
        record.OwnerToken.Should().NotBeNull();
        record.LeaseExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CompletionAfterLeaseExpiry_IsRejectedAsStale()
    {
        var clock = new AdjustableLeaseClock(new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        await using var database = await SqlDatabase.CreateAsync();
        var options = Options.Create(OptionsValue());
        CustomerInternalIdempotencyClaim claim;
        await using (var context = database.CreateContext())
        {
            claim = await new CustomerInternalIdempotencyLeaseService(context, clock, options)
                .ClaimAsync("business", "callback", "stale-complete", Hash('a'),
                    CancellationToken.None);
        }
        clock.Advance(TimeSpan.FromSeconds(2));

        await using var completionContext = database.CreateContext();
        var completed = await new CustomerInternalIdempotencyLeaseService(
            completionContext, clock, options).CompleteAsync(
                claim.RecordId,
                claim.OwnerToken,
                200,
                "application/json",
                [1],
                CancellationToken.None);

        completed.Should().BeFalse();
    }

    private static CustomerInternalServiceOptions OptionsValue() => new()
    {
        InProgressRecoverySeconds = 1,
        IdempotencyLifetimeSeconds = 60,
        ExpiredRecordCleanupBatchSize = 100
    };

    private static string Hash(char value) => new(value, 64);

    private static async Task<(int StatusCode, string Body)> SendAsync(
        ServiceProvider provider,
        IWebHostEnvironment environment,
        CustomerInternalServiceMiddleware middleware,
        CustomerInternalServiceOptions options,
        string key,
        string nonce)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        context.SetEndpoint(new Endpoint(
            null,
            new EndpointMetadataCollection(
                new CustomerInternalOperationAttribute("reconcile")),
            "lease-test"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/internal/reconcile";
        context.Request.IsHttps = true;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        context.Response.Body = new MemoryStream();
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var bodyHash = InternalServiceCanonicalRequest.ComputeSha256Hex(
            Encoding.UTF8.GetBytes("{}"));
        var canonical = InternalServiceCanonicalRequest.Build(
            "business",
            HttpMethods.Post,
            context.Request.Path,
            [],
            timestamp,
            nonce,
            key,
            bodyHash);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        context.Request.Headers[InternalServiceWireConstants.ServiceIdHeaderName] = "business";
        context.Request.Headers[InternalServiceWireConstants.TimestampHeaderName] = timestamp;
        context.Request.Headers[InternalServiceWireConstants.NonceHeaderName] = nonce;
        context.Request.Headers[InternalServiceWireConstants.IdempotencyKeyHeaderName] = key;
        context.Request.Headers[InternalServiceWireConstants.SignatureHeaderName] =
            Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        await middleware.InvokeAsync(
            context,
            Options.Create(options),
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            environment,
            scope.ServiceProvider.GetRequiredService<IAppLogger>(),
            scope.ServiceProvider.GetRequiredService<
                ICustomerInternalIdempotencyCleanupService>(),
            scope.ServiceProvider.GetRequiredService<
                ICustomerInternalIdempotencyLeaseService>(),
            provider.GetRequiredService<IServiceScopeFactory>());
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private const string Secret =
        "customer-idempotency-lease-test-secret-at-least-thirty-two";

    private sealed class AdjustableLeaseClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class SqlDatabase : IAsyncDisposable
    {
        private readonly string _name = $"CustomerIdempotencyLease_{Guid.NewGuid():N}";

        public static async Task<SqlDatabase> CreateAsync()
        {
            var database = new SqlDatabase();
            await using var context = database.CreateContext();
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public ApplicationDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
                .Options);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var context = CreateContext();
                await context.Database.EnsureDeletedAsync();
            }
            catch (SqlException)
            {
            }
        }

        public string ConnectionString =>
            $"Server=(localdb)\\MSSQLLocalDB;Database={_name};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }
}
