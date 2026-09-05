using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Payments;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Proves cross-owner and cross-host authorization through real HTTP pipelines.
/// </summary>
public sealed class Step17DeterministicAuthorizationHttpTests
{
    [Fact]
    [Trait("ScenarioId", "STEP17-DET-AUTH-004")]
    public async Task STEP17_DET_AUTH_004_CrossDeviceConfirmationIsMaskedNotFound()
    {
        var firstToken = CatalogTestSupport.CreateToken(172);
        var secondToken = CatalogTestSupport.CreateToken(173);
        var firstDevice = CatalogTestSupport.CreateDevice(
            firstToken,
            Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        var secondDevice = CatalogTestSupport.CreateDevice(
            secondToken,
            Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory(
            [firstDevice, secondDevice]);
        using var client = factory.CreateApiClient();
        var userId = Guid.NewGuid();
        await Step17CustomerHttpTestSupport.SeedUserAsync(factory, userId, "auth-owner-step17");
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(
            factory,
            client,
            firstToken);
        var businessCalls = factory.BusinessApiClient.CreateReservationRequests;
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=he",
            secondToken,
            JsonContent.Create(new
            {
                expectedVersion = 1,
                cancellationPolicyAcknowledged = true
            }),
            Step17CustomerHttpTestSupport.CustomerJwt(userId),
            "corr-step17-det-auth-004");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "checkout_draft_not_found",
            "he",
            "לא ניתן לאשר את ההזמנה.",
            "טיוטת התשלום המבוקשת לא נמצאה.",
            "corr-step17-det-auth-004");

        factory.BusinessApiClient.CreateReservationRequests.Should().Be(businessCalls);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerBookings.CountAsync()).Should().Be(0);
        (await context.BookingConfirmationAttempts.CountAsync()).Should().Be(0);
        (await context.CheckoutDrafts.SingleAsync(value => value.OrderGuid == orderGuid))
            .OwnerDeviceId.Should().Be(firstDevice.Id);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-AUTH-005")]
    public async Task STEP17_DET_AUTH_005_WrongUserPaymentIsMaskedNotFound()
    {
        var ownerToken = CatalogTestSupport.CreateToken(174);
        var foreignToken = CatalogTestSupport.CreateToken(175);
        var ownerDevice = CatalogTestSupport.CreateDevice(
            ownerToken,
            Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        var foreignDevice = CatalogTestSupport.CreateDevice(
            foreignToken,
            Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory(
            [ownerDevice, foreignDevice],
            configureTestServices: services =>
            {
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton(gateway.Object);
            });
        using var client = factory.CreateApiClient();
        var ownerId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        await Step17CustomerHttpTestSupport.SeedUserAsync(factory, ownerId, "owner-step17");
        await Step17CustomerHttpTestSupport.SeedUserAsync(factory, foreignId, "foreign-step17");
        var booking = await Step17CustomerHttpTestSupport.SeedPayableBookingAsync(
            factory,
            ownerId,
            ownerDevice.Id);
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/payments/intents?language=ar",
            foreignToken,
            JsonContent.Create(new
            {
                bookingId = booking.PublicReference,
                method = "Card"
            }),
            Step17CustomerHttpTestSupport.CustomerJwt(foreignId),
            "corr-step17-det-auth-005");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "step17-det-auth-005");

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "booking_not_found",
            "ar",
            "تعذر إكمال طلب الدفع.",
            "تعذر إكمال طلب الدفع.",
            "corr-step17-det-auth-005");

        gateway.VerifyNoOtherCalls();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerPayments.CountAsync()).Should().Be(0);
        (await context.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(0);
        (await context.CustomerBookings.CountAsync()).Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-AUTH-006")]
    public async Task STEP17_DET_AUTH_006_ForeignCompanyWorkOrderIsMaskedNotFound()
    {
        await using var business = BusinessTestFactoryHandle.Create();
        business.ResetState();
        var workOrder = business.SeedPendingWorkOrder();
        using var client = business.CreateAuthenticatedClient(
            business.OtherOwnerUserId,
            "Owner");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/business/work-orders/{workOrder.PublicId:D}/transitions?language=ar")
        {
            Content = JsonContent.Create(new { status = "Confirmed" })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "step17-det-auth-006");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-step17-det-auth-006");

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "BOOKING_NOT_FOUND",
            "ar",
            "تعذر إكمال الطلب.",
            "تعذر إكمال الطلب.",
            "corr-step17-det-auth-006");

        var state = business.ReadWorkOrderState(workOrder.Id, workOrder.ReservationId);
        state.Status.Should().Be("Pending");
        state.Sequence.Should().Be(0);
        state.OutboxCount.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-AUTH-007")]
    public async Task STEP17_DET_AUTH_007_CustomerJwtIsRejectedByBusinessTransition()
    {
        await using var business = BusinessTestFactoryHandle.Create();
        business.ResetState();
        using var client = business.CreateSecureClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/business/work-orders/{Guid.NewGuid():D}/transitions?language=he")
        {
            Content = JsonContent.Create(new { status = "Confirmed" })
        };
        request.Headers.Authorization = new(
            "Bearer",
            Step17CustomerHttpTestSupport.CustomerJwt(Guid.NewGuid()));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "step17-det-auth-007");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-step17-det-auth-007");

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "business_authentication_required",
            "he",
            "לא ניתן להשלים את הבקשה.",
            "נדרש אימות לחשבון העסקי.",
            "corr-step17-det-auth-007");

        business.CountOutboxMessages().Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-AUTH-008")]
    public async Task STEP17_DET_AUTH_008_BusinessJwtIsRejectedByCustomerPayment()
    {
        var token = CatalogTestSupport.CreateToken(176);
        var device = CatalogTestSupport.CreateDevice(
            token,
            Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory(
            [device],
            configureTestServices: services =>
            {
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton(gateway.Object);
            });
        using var client = factory.CreateApiClient();
        var businessJwt = Step17CustomerHttpTestSupport.CustomerJwt(
            Guid.NewGuid(),
            "Owner",
            "Ghseeli.BusinessApi.CatalogTests",
            "Ghseeli.BusinessApi.CatalogClients",
            "CatalogIntegrationTestsSecretKey_Minimum32Characters");
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/payments/intents?language=ar",
            token,
            JsonContent.Create(new
            {
                bookingId = Guid.NewGuid(),
                method = "Card"
            }),
            businessJwt,
            "corr-step17-det-auth-008");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "step17-det-auth-008");

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "customer_authentication_required",
            "ar",
            "تعذر إكمال الطلب.",
            "مطلوب تسجيل دخول العميل.",
            "corr-step17-det-auth-008");

        gateway.VerifyNoOtherCalls();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerPayments.CountAsync()).Should().Be(0);
        (await context.CustomerPaymentIdempotencyRecords.CountAsync()).Should().Be(0);
    }

    private sealed class BusinessTestFactoryHandle : IAsyncDisposable
    {
        private static readonly object DependencyCopyLock = new();
        private readonly object _factory;
        private readonly Type _factoryType;
        private readonly Assembly _businessAssembly;

        private BusinessTestFactoryHandle(
            object factory,
            Type factoryType,
            Assembly businessAssembly)
        {
            _factory = factory;
            _factoryType = factoryType;
            _businessAssembly = businessAssembly;
        }

        internal Guid OtherOwnerUserId => GetGuidProperty("OtherOwnerUserId");

        internal static BusinessTestFactoryHandle Create()
        {
            var solutionDirectory = FindSolutionDirectory();
            var configuration = AppContext.BaseDirectory
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Reverse()
                .FirstOrDefault(segment =>
                    segment.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("Release", StringComparison.OrdinalIgnoreCase));
            configuration = configuration?.Equals(
                "Release",
                StringComparison.OrdinalIgnoreCase) == true
                ? "Release"
                : "Debug";
            var outputDirectory = Path.Combine(
                solutionDirectory,
                "Ghseeli.BusinessApi.Tests",
                "bin",
                configuration,
                "net9.0");
            var testAssemblyPath = Path.Combine(
                outputDirectory,
                "Ghseeli.BusinessApi.Tests.dll");
            File.Exists(testAssemblyPath).Should().BeTrue(
                "the solution build must produce the reusable Business TestServer factory");
            var businessDepsSource = Path.Combine(
                outputDirectory,
                "Ghseeli.BusinessApi.deps.json");
            var businessDepsTarget = Path.Combine(
                AppContext.BaseDirectory,
                "Ghseeli.BusinessApi.deps.json");
            lock (DependencyCopyLock)
            {
                if (!File.Exists(businessDepsTarget))
                {
                    File.Copy(businessDepsSource, businessDepsTarget);
                }
            }

            AssemblyLoadContext.Default.Resolving += ResolveFromBusinessOutput;
            try
            {
                var testAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
                    Path.GetFullPath(testAssemblyPath));
                var factoryType = testAssembly.GetType(
                    "Ghseeli.BusinessApi.Tests.Infrastructure.CatalogApiFactory",
                    throwOnError: true)!;
                var factory = Activator.CreateInstance(factoryType)!;
                var businessAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .Single(assembly => assembly.GetName().Name == "Ghseeli.BusinessApi");
                return new BusinessTestFactoryHandle(factory, factoryType, businessAssembly);
            }
            finally
            {
                AssemblyLoadContext.Default.Resolving -= ResolveFromBusinessOutput;
            }

            Assembly? ResolveFromBusinessOutput(
                AssemblyLoadContext context,
                AssemblyName name)
            {
                var path = Path.Combine(outputDirectory, $"{name.Name}.dll");
                return File.Exists(path)
                    ? context.LoadFromAssemblyPath(Path.GetFullPath(path))
                    : null;
            }
        }

        internal void ResetState() =>
            _factoryType.GetMethod("ResetState")!.Invoke(_factory, null);

        internal HttpClient CreateSecureClient() =>
            (HttpClient)_factoryType.GetMethod("CreateSecureClient")!
                .Invoke(_factory, null)!;

        internal HttpClient CreateAuthenticatedClient(Guid userId, params string[] roles) =>
            (HttpClient)_factoryType.GetMethod("CreateAuthenticatedClient")!
                .Invoke(_factory, [userId, roles])!;

        internal SeededBusinessWorkOrder SeedPendingWorkOrder()
        {
            var reservationType = _businessAssembly.GetType(
                "Ghseeli.BusinessApi.Models.AppointmentReservation",
                throwOnError: true)!;
            var workOrderType = _businessAssembly.GetType(
                "Ghseeli.BusinessApi.Models.WorkOrder",
                throwOnError: true)!;
            var reservationId = Guid.NewGuid();
            var workOrderId = Guid.NewGuid();
            var workOrderPublicId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var reservation = Activator.CreateInstance(reservationType)!;
            Set(reservation, "Id", reservationId);
            Set(reservation, "PublicId", Guid.NewGuid());
            Set(reservation, "CustomerBookingReference", Guid.NewGuid());
            Set(reservation, "OrderGuid", Guid.NewGuid());
            Set(reservation, "RequestHash", new string('a', 64));
            Set(reservation, "BranchId", GetGuidProperty("BranchId"));
            Set(reservation, "CatalogVersion", 1L);
            Set(reservation, "Currency", "ILS");
            Set(reservation, "RequestedSlotStartUtc", now.AddDays(1));
            Set(reservation, "RequestedSlotEndUtc", now.AddDays(1).AddHours(1));
            Set(reservation, "Status", "Pending");
            Set(reservation, "StatusSequence", 0L);
            Set(reservation, "StatusChangedAtUtc", DateTimeOffset.UtcNow);
            Set(reservation, "CreatedAtUtc", now);

            var workOrder = Activator.CreateInstance(workOrderType)!;
            Set(workOrder, "Id", workOrderId);
            Set(workOrder, "PublicId", workOrderPublicId);
            Set(workOrder, "Status", "Pending");
            Set(workOrder, "CustomerName", "Step 17 customer");
            Set(workOrder, "VehicleType", "Sedan");
            Set(workOrder, "AddressLine", "Step 17 address");
            Set(workOrder, "CreatedAtUtc", now);
            Set(reservation, "WorkOrder", workOrder);

            using var scope = Services.CreateScope();
            var context = ResolveBusinessContext(scope.ServiceProvider);
            context.Add(reservation);
            context.SaveChanges();
            return new SeededBusinessWorkOrder(
                workOrderId,
                workOrderPublicId,
                reservationId);
        }

        internal BusinessWorkOrderState ReadWorkOrderState(
            Guid workOrderId,
            Guid reservationId)
        {
            using var scope = Services.CreateScope();
            var context = ResolveBusinessContext(scope.ServiceProvider);
            var workOrderType = _businessAssembly.GetType(
                "Ghseeli.BusinessApi.Models.WorkOrder",
                throwOnError: true)!;
            var reservationType = _businessAssembly.GetType(
                "Ghseeli.BusinessApi.Models.AppointmentReservation",
                throwOnError: true)!;
            var workOrder = context.Find(workOrderType, workOrderId)!;
            var reservation = context.Find(reservationType, reservationId)!;
            return new BusinessWorkOrderState(
                (string)Get(workOrder, "Status")!,
                (long)Get(reservation, "StatusSequence")!,
                CountOutboxMessages(context));
        }

        internal int CountOutboxMessages()
        {
            using var scope = Services.CreateScope();
            var context = ResolveBusinessContext(scope.ServiceProvider);
            return CountOutboxMessages(context);
        }

        public async ValueTask DisposeAsync()
        {
            if (_factory is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_factory is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private IServiceProvider Services =>
            (IServiceProvider)_factoryType.GetProperty("Services")!.GetValue(_factory)!;

        private DbContext ResolveBusinessContext(IServiceProvider provider)
        {
            var contextType = _businessAssembly.GetType(
                "Ghseeli.BusinessApi.Persistence.BusinessDbContext",
                throwOnError: true)!;
            return (DbContext)provider.GetRequiredService(contextType);
        }

        private int CountOutboxMessages(DbContext context)
        {
            var set = (IEnumerable)context.GetType()
                .GetProperty("BookingStatusOutboxMessages")!
                .GetValue(context)!;
            return set.Cast<object>().Count();
        }

        private Guid GetGuidProperty(string name) =>
            (Guid)_factoryType.GetProperty(name)!.GetValue(_factory)!;

        private static void Set(object target, string name, object value) =>
            target.GetType().GetProperty(name)!.SetValue(target, value);

        private static object? Get(object target, string name) =>
            target.GetType().GetProperty(name)!.GetValue(target);

        private static string FindSolutionDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GhseeliApis.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate GhseeliApis.sln from the test output directory.");
        }

        internal sealed record SeededBusinessWorkOrder(
            Guid Id,
            Guid PublicId,
            Guid ReservationId);

        internal sealed record BusinessWorkOrderState(
            string Status,
            long Sequence,
            int OutboxCount);
    }
}
