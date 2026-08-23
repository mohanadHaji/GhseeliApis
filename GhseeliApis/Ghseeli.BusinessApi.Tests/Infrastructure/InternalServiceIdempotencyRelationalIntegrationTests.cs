using FluentAssertions;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Verifies relational replay protection and idempotency concurrency guarantees with a real SQL Server provider.
/// </summary>
public class InternalServiceIdempotencyRelationalIntegrationTests
{
    [Fact]
    public async Task ValidateAppointment_WhenSameIdempotencyKeyAndBodyReplay_ReturnsStoredResponseAndExecutesOnce()
    {
        await using var factory = new RelationalInternalServiceFactory();
        using var client = factory.CreateSecureClient();

        var firstResponse = await SendValidationAsync(client, "idem-replay");
        var secondResponse = await SendValidationAsync(client, "idem-replay");

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondBody.Should().Be(firstBody);
        factory.AppointmentValidationService.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task ValidateAppointment_WhenSameIdempotencyKeyAndBodyArriveConcurrently_ExecutesOnce()
    {
        await using var factory = new RelationalInternalServiceFactory();
        factory.AppointmentValidationService.BlockExecution();
        using var client = factory.CreateSecureClient();

        var firstTask = SendValidationAsync(client, "idem-concurrent");
        await factory.AppointmentValidationService.WaitForEntryAsync();

        var secondTask = SendValidationAsync(client, "idem-concurrent");
        factory.AppointmentValidationService.ReleaseExecution();

        var responses = await Task.WhenAll(firstTask, secondTask);
        var bodies = await Task.WhenAll(
            responses[0].Content.ReadAsStringAsync(),
            responses[1].Content.ReadAsStringAsync());

        responses.Select(response => response.StatusCode).Should().OnlyContain(status => status == HttpStatusCode.OK);
        bodies[0].Should().Be(bodies[1]);
        factory.AppointmentValidationService.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task ValidateAppointment_WhenIdempotencyRecordExpires_AllowsReexecution()
    {
        await using var factory = new RelationalInternalServiceFactory(idempotencyLifetimeSeconds: 1);
        using var client = factory.CreateSecureClient();

        var firstResponse = await SendValidationAsync(client, "idem-expiry");
        await Task.Delay(TimeSpan.FromSeconds(2));
        var secondResponse = await SendValidationAsync(client, "idem-expiry");

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondBody.Should().NotBe(firstBody);
        factory.AppointmentValidationService.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task ValidateAppointment_WhenNonceIsReusedConcurrently_OnlyOneRequestSucceeds()
    {
        await using var factory = new RelationalInternalServiceFactory();
        using var client = factory.CreateSecureClient();
        var sharedNonce = Guid.NewGuid().ToString("N");

        var firstTask = SendValidationAsync(client, "idem-nonce-1", nonce: sharedNonce);
        var secondTask = SendValidationAsync(client, "idem-nonce-2", nonce: sharedNonce);

        var responses = await Task.WhenAll(firstTask, secondTask);
        var orderedStatuses = responses.Select(response => response.StatusCode).OrderBy(status => (int)status).ToArray();

        orderedStatuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.Unauthorized);
        factory.AppointmentValidationService.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task ValidateAppointment_WhenFirstExecutionThrows_AllowsSuccessfulRetryWithSameIdempotencyKey()
    {
        await using var factory = new RelationalInternalServiceFactory();
        factory.AppointmentValidationService.EnqueueException(
            new InvalidOperationException("Simulated validation failure."));
        using var client = factory.CreateSecureClient();

        using var firstResponse = await SendValidationAsync(client, "idem-throw-then-retry");
        firstResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var retryResponse = await SendValidationAsync(client, "idem-throw-then-retry");

        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.AppointmentValidationService.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task CreateReservation_WhenSameOrderAndIdempotencyKeyAreReplayed_ExecutesOnce()
    {
        await using var factory = new RelationalInternalServiceFactory();
        using var client = factory.CreateSecureClient();
        var request = CreateReservationRequest();

        var firstResponse = await SendReservationAsync(client, "reservation-replay", request);
        var secondResponse = await SendReservationAsync(client, "reservation-replay", request);

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondBody.Should().Be(firstBody);
        factory.ReservationService.InvocationCount.Should().Be(1);
    }

    [Theory]
    [InlineData("""{"items":[null]}""")]
    [InlineData("""{"items":[{"selectedAddons":null}]}""")]
    [InlineData("""{"items":[{"selectedAddons":[null]}]}""")]
    public async Task CreateReservation_WhenNestedCollectionContentIsNull_ReturnsReplayableProblemInsteadOfSuccess(
        string malformedFragment)
    {
        await using var factory = new RelationalInternalServiceFactory();
        using var client = factory.CreateSecureClient();
        var request = CreateReservationRequest();
        var body = JsonSerializer.SerializeToNode(
            request,
            BusinessCatalogContract.CreateJsonSerializerOptions())!.AsObject();
        var malformed = JsonNode.Parse(malformedFragment)!.AsObject();
        body["items"] = malformed["items"]!.DeepClone();
        var idempotencyKey = $"reservation-null-{Guid.NewGuid():N}";

        var firstResponse = await SendReservationAsync(
            client,
            idempotencyKey,
            body);
        var secondResponse = await SendReservationAsync(
            client,
            idempotencyKey,
            body);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, firstBody);
        firstResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        secondResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, secondBody);
        secondBody.Should().Be(firstBody);
        firstBody.Should().Contain(ReservationErrorCodes.Invalid);
        factory.ReservationService.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task ValidateAppointment_WhenStoredResponseExceedsCap_AllowsSuccessfulRetryWithSameIdempotencyKey()
    {
        await using var factory = new RelationalInternalServiceFactory(maxStoredResponseBytes: 1024);
        factory.AppointmentValidationService.EnqueueResponseFactory(CreateOversizedResponse);
        using var client = factory.CreateSecureClient();

        var firstResponse = await SendValidationAsync(client, "idem-oversized-response");
        var firstContent = await firstResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        firstContent.Should().Contain(InternalServiceProblemCodes.IdempotencyUnavailable);

        var retryResponse = await SendValidationAsync(client, "idem-oversized-response");

        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.AppointmentValidationService.InvocationCount.Should().Be(2);
    }

    private static async Task<HttpResponseMessage> SendValidationAsync(
        HttpClient client,
        string idempotencyKey,
        string? nonce = null)
    {
        var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new ValidateAppointmentRequest
            {
                BranchId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                OfferingId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                RequestedSlotStartUtc = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero),
                Currency = "ILS"
            },
            idempotencyKey: idempotencyKey,
            nonce: nonce);

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendReservationAsync(
        HttpClient client,
        string idempotencyKey,
        object body)
    {
        var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/reservations",
            body,
            idempotencyKey: idempotencyKey);
        return await client.SendAsync(request);
    }

    private static CreateReservationRequest CreateReservationRequest() =>
        new()
        {
            BookingReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            ExpectedCatalogVersion = 3,
            RequestedSlotStartUtc = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero),
            Currency = "ILS",
            ExpectedItemSubtotal = 100m,
            ExpectedTotalDurationMinutes = 30,
            Customer = new ReservationCustomerSnapshot { Name = "Customer" },
            Vehicle = new ReservationVehicleSnapshot { VehicleType = "Sedan" },
            Location = new ReservationLocationSnapshot
            {
                AddressLine = "Street 1",
                Latitude = 32.1,
                Longitude = 34.8
            },
            CancellationPolicyAcknowledged = true,
            Items =
            [
                new CreateReservationItemRequest
                {
                    OfferingId = Guid.NewGuid(),
                    ExpectedBaseSubtotal = 100m,
                    ExpectedItemSubtotal = 100m,
                    ExpectedDurationMinutes = 30
                }
            ]
        };

    private sealed class RelationalInternalServiceFactory : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly string _databaseName = $"GhseeliStep6_{Guid.NewGuid():N}";
        private readonly int _idempotencyLifetimeSeconds;
        private readonly int _maxStoredResponseBytes;

        public RelationalInternalServiceFactory(
            int idempotencyLifetimeSeconds = 300,
            int maxStoredResponseBytes = InternalServiceWireConstants.MaxStoredResponseBytes)
        {
            _idempotencyLifetimeSeconds = idempotencyLifetimeSeconds;
            _maxStoredResponseBytes = maxStoredResponseBytes;
        }

        public FakeAppointmentValidationService AppointmentValidationService =>
            Services.GetRequiredService<FakeAppointmentValidationService>();
        public FakeReservationService ReservationService =>
            Services.GetRequiredService<FakeReservationService>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:BusinessConnection", ConnectionString);
            builder.UseSetting("BusinessJwtSettings:SecretKey", "RelationalStep6Secret_Minimum32Characters");
            builder.UseSetting("BusinessJwtSettings:Issuer", "Ghseeli.BusinessApi.Tests");
            builder.UseSetting("BusinessJwtSettings:Audience", "Ghseeli.BusinessClients.Tests");
            builder.UseSetting("InternalServiceAuthentication:RequireHttps", "true");
            builder.UseSetting("InternalServiceAuthentication:AllowInsecureHttpInDevelopment", "false");
            builder.UseSetting("InternalServiceAuthentication:AllowedClockSkewSeconds", "120");
            builder.UseSetting("InternalServiceAuthentication:NonceLifetimeSeconds", "300");
            builder.UseSetting("InternalServiceAuthentication:IdempotencyLifetimeSeconds", _idempotencyLifetimeSeconds.ToString());
            builder.UseSetting("InternalServiceAuthentication:MaxStoredResponseBytes", _maxStoredResponseBytes.ToString());
            builder.UseSetting("InternalServiceAuthentication:Services:0:ServiceId", CatalogApiFactory.InternalServiceId);
            builder.UseSetting("InternalServiceAuthentication:Services:0:ActiveSecret", CatalogApiFactory.InternalServiceActiveSecret);
            builder.UseSetting("InternalServiceAuthentication:Services:0:AllowedOperations:0", InternalServiceOperationNames.CatalogSnapshot);
            builder.UseSetting("InternalServiceAuthentication:Services:0:AllowedOperations:1", InternalServiceOperationNames.AppointmentValidate);
            builder.UseSetting("InternalServiceAuthentication:Services:0:AllowedOperations:2", InternalServiceOperationNames.ReservationCreate);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<BusinessDbContext>));
                services.RemoveAll<BusinessDbContext>();
                services.AddDbContext<BusinessDbContext>(options =>
                    options.UseSqlServer(ConnectionString));

                services.RemoveAll<ICatalogPublicationService>();
                services.RemoveAll<IAppointmentValidationService>();
                services.RemoveAll<IReservationService>();
                services.AddSingleton<FakeCatalogPublicationService>();
                services.AddSingleton<ICatalogPublicationService>(serviceProvider =>
                    serviceProvider.GetRequiredService<FakeCatalogPublicationService>());
                services.AddSingleton<FakeAppointmentValidationService>();
                services.AddSingleton<IAppointmentValidationService>(serviceProvider =>
                    serviceProvider.GetRequiredService<FakeAppointmentValidationService>());
                services.AddSingleton<FakeReservationService>();
                services.AddSingleton<IReservationService>(serviceProvider =>
                    serviceProvider.GetRequiredService<FakeReservationService>());

                using var scope = services.BuildServiceProvider().CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
                context.Database.EnsureDeleted();
                context.Database.EnsureCreated();
            });
        }

        public HttpClient CreateSecureClient()
        {
            return CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
        }

        public new async ValueTask DisposeAsync()
        {
            try
            {
                using var scope = Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
                await context.Database.EnsureDeletedAsync();
            }
            catch
            {
            }

            await base.DisposeAsync();
        }

        private string ConnectionString =>
            $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }

    private sealed class FakeCatalogPublicationService : ICatalogPublicationService
    {
        public Task<CatalogSnapshotResponse> GetSnapshotAsync(Guid companyId)
        {
            return Task.FromResult(new CatalogSnapshotResponse
            {
                Company = new CatalogSnapshotCompany
                {
                    Id = companyId,
                    NameAr = "شركة"
                }
            });
        }
    }

    private sealed class FakeAppointmentValidationService : IAppointmentValidationService
    {
        private readonly ConcurrentQueue<Func<ValidateAppointmentRequest, Task<ValidateAppointmentResponse>>> _plannedResults = new();
        private int _invocationCount;
        private TaskCompletionSource<bool> _enteredExecution =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _releaseExecution =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _shouldBlock;

        public int InvocationCount => _invocationCount;

        public void BlockExecution()
        {
            _shouldBlock = true;
            _enteredExecution = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseExecution = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForEntryAsync() => _enteredExecution.Task;

        public void ReleaseExecution()
        {
            _releaseExecution.TrySetResult(true);
            _shouldBlock = false;
        }

        public void EnqueueException(Exception exception)
        {
            _plannedResults.Enqueue(_ => Task.FromException<ValidateAppointmentResponse>(exception));
        }

        public void EnqueueResponseFactory(Func<ValidateAppointmentRequest, ValidateAppointmentResponse> factory)
        {
            _plannedResults.Enqueue(request => Task.FromResult(factory(request)));
        }

        public async Task<ValidateAppointmentResponse> ValidateAsync(ValidateAppointmentRequest request)
        {
            var executionNumber = Interlocked.Increment(ref _invocationCount);
            _enteredExecution.TrySetResult(true);

            if (_shouldBlock)
            {
                await _releaseExecution.Task;
            }

            if (_plannedResults.TryDequeue(out var plannedResult))
            {
                return await plannedResult(request);
            }

            return new ValidateAppointmentResponse
            {
                ContractVersion = BusinessCatalogContract.Version,
                Valid = true,
                CatalogVersion = 1,
                Currency = request.Currency,
                BranchId = request.BranchId,
                OfferingId = request.OfferingId,
                TotalPrice = executionNumber,
                Availability = new AppointmentAvailabilityFacts
                {
                    IsAvailable = true,
                    RequestedSlotStartUtc = request.RequestedSlotStartUtc.UtcDateTime,
                    RequestedSlotEndUtc = request.RequestedSlotStartUtc.UtcDateTime.AddMinutes(30)
                }
            };
        }
    }

    private sealed class FakeReservationService : IReservationService
    {
        private int _invocationCount;
        public int InvocationCount => _invocationCount;

        public Task<CreateReservationResponse> CreateAsync(
            CreateReservationRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            if (request.Items is null ||
                request.Items.Any(item =>
                    item is null ||
                    item.SelectedAddons is null ||
                    item.SelectedAddons.Any(selection => selection is null)))
            {
                throw new ReservationRejectedException(
                    ReservationErrorCodes.Invalid,
                    "The reservation request is invalid.");
            }

            return Task.FromResult(new CreateReservationResponse
            {
                BookingReference = request.BookingReference,
                ReservationId = Guid.NewGuid(),
                WorkOrderId = Guid.NewGuid(),
                Status = ReservationStatuses.Reserved,
                CatalogVersion = request.ExpectedCatalogVersion,
                Currency = request.Currency,
                ItemSubtotal = request.ExpectedItemSubtotal,
                TotalDurationMinutes = request.ExpectedTotalDurationMinutes,
                RequestedSlotStartUtc = request.RequestedSlotStartUtc,
                RequestedSlotEndUtc = request.RequestedSlotStartUtc.AddMinutes(
                    request.ExpectedTotalDurationMinutes)
            });
        }
    }

    private static ValidateAppointmentResponse CreateOversizedResponse(ValidateAppointmentRequest request)
    {
        return new ValidateAppointmentResponse
        {
            ContractVersion = BusinessCatalogContract.Version,
            Valid = false,
            CatalogVersion = 1,
            Currency = request.Currency,
            BranchId = request.BranchId,
            OfferingId = request.OfferingId,
            Errors =
            [
                new AppointmentValidationIssue
                {
                    Code = "oversized-response",
                    Message = new string('x', 4096)
                }
            ],
            Availability = new AppointmentAvailabilityFacts
            {
                IsAvailable = false,
                RequestedSlotStartUtc = request.RequestedSlotStartUtc.UtcDateTime,
                RequestedSlotEndUtc = request.RequestedSlotStartUtc.UtcDateTime.AddMinutes(30)
            }
        };
    }
}
