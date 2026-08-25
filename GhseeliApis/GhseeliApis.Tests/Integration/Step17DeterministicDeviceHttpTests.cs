using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Devices;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Proves deterministic device expiry and rotation at the Customer HTTP boundary.
/// </summary>
public sealed class Step17DeterministicDeviceHttpTests
{
    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DEVICE-001")]
    public async Task STEP17_DET_DEVICE_001_ExpiredDeviceCannotReachConfiguration()
    {
        var token = CatalogTestSupport.CreateToken(171);
        var device = CatalogTestSupport.CreateDevice(token, Step17CustomerHttpTestSupport.FixedNow);
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory([device]);
        using var client = factory.CreateApiClient();
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Get,
            "/api/v1/configuration?language=he",
            token,
            correlationId: "corr-step17-det-device-001");

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            DeviceProblemCodes.TokenExpired,
            "he",
            "אימות המכשיר נכשל.",
            "פג תוקף אסימון המכשיר.",
            "corr-step17-det-device-001");

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerConfigurations.CountAsync()).Should().Be(0);
        (await context.CheckoutDrafts.CountAsync()).Should().Be(0);
        var stored = await context.CustomerDevices.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(device.Id);
        stored.ExpiresAt.Should().Be(Step17CustomerHttpTestSupport.FixedNow);
        stored.TokenHash.Should().Equal(device.TokenHash);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DEVICE-003")]
    public async Task STEP17_DET_DEVICE_003_RotatedTokenBlocksConfirmationBeforeBusiness()
    {
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory();
        using var client = factory.CreateApiClient();
        var installationId = Guid.NewGuid();
        var registration = new
        {
            installationId,
            platform = "Android",
            appVersion = "17.0"
        };

        using var issueResponse = await client.PostAsJsonAsync(
            "/api/v1/devices/register",
            registration);
        using var issueDocument = await Step17CustomerHttpTestSupport.JsonAsync(issueResponse);
        issueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var oldToken = issueDocument.RootElement.GetProperty("token").GetString()!;
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(
            factory,
            client,
            oldToken);
        (await Step17CustomerHttpTestSupport.RepriceDraftAsync(
            client,
            oldToken,
            orderGuid,
            expectedVersion: 1)).Should().Be(2);

        using var rotateRequest = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/devices/register",
            oldToken,
            JsonContent.Create(registration),
            correlationId: "corr-step17-det-device-003-rotate");
        using var rotateResponse = await client.SendAsync(rotateRequest);
        using var rotateDocument = await Step17CustomerHttpTestSupport.JsonAsync(rotateResponse);
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentToken = rotateDocument.RootElement.GetProperty("token").GetString()!;
        currentToken.Should().NotBe(oldToken);
        using (var verificationScope = factory.Services.CreateScope())
        {
            var verificationContext =
                verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storedDevice = await verificationContext.CustomerDevices
                .AsNoTracking()
                .SingleAsync();
            DeviceTokenHasher.Matches(storedDevice.TokenHash, currentToken).Should().BeTrue();
            DeviceTokenHasher.Matches(storedDevice.TokenHash, oldToken).Should().BeFalse();
        }

        var userId = Guid.NewGuid();
        await Step17CustomerHttpTestSupport.SeedUserAsync(factory, userId, "rotated-step17");
        var customerJwt = Step17CustomerHttpTestSupport.CustomerJwt(userId);
        var draftBefore = await Step17CustomerHttpTestSupport.ReadDraftStateAsync(
            factory,
            orderGuid);
        var businessCalls = factory.BusinessApiClient.CreateReservationRequests;
        using var confirmRequest = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=ar",
            oldToken,
            JsonContent.Create(new
            {
                expectedVersion = 2,
                cancellationPolicyAcknowledged = true
            }),
            customerJwt,
            "corr-step17-det-device-003");
        confirmRequest.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));

        using var response = await client.SendAsync(confirmRequest);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            DeviceProblemCodes.TokenInvalid,
            "ar",
            "فشل التحقق من الجهاز.",
            "رمز الجهاز غير صالح.",
            "corr-step17-det-device-003");

        factory.BusinessApiClient.CreateReservationRequests.Should().Be(businessCalls);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerDevices.CountAsync()).Should().Be(1);
        (await context.CustomerBookings.CountAsync()).Should().Be(0);
        (await context.BookingConfirmationAttempts.CountAsync()).Should().Be(0);
        (await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid))
            .Should().Be(draftBefore);
    }
}

internal static class Step17CustomerHttpTestSupport
{
    private const string CustomerJwtSecret = "CheckoutDraftApiTestsSecret_Minimum32Chars";
    internal static readonly DateTimeOffset FixedNow =
        new(2030, 1, 15, 9, 0, 0, TimeSpan.Zero);

    internal static CheckoutDraftApiFactory CreateFactory(
        IEnumerable<CustomerDevice>? devices = null,
        CatalogSnapshotResponse? snapshot = null,
        Action<IServiceCollection>? configureTestServices = null,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new(
            snapshot,
            devices,
            FixedNow,
            settings,
            configureTestServices);

    internal static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string? deviceToken = null,
        HttpContent? content = null,
        string? bearer = null,
        string correlationId = "corr-step17-deterministic")
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        if (deviceToken is not null)
        {
            request.Headers.TryAddWithoutValidation(DeviceTokenDefaults.HeaderName, deviceToken);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    internal static string CustomerJwt(
        Guid userId,
        string role = "User",
        string issuer = "GhseeliApis.CheckoutDraftTests",
        string audience = "GhseeliApis.CheckoutDraftClients",
        string secret = CustomerJwtSecret)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials));
    }

    internal static async Task<JsonDocument> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        return JsonDocument.Parse(body);
    }

    internal static async Task<JsonDocument> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string language,
        string title,
        string detail,
        string correlationId)
    {
        var document = await JsonAsync(response);
        var problem = document.RootElement;
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        problem.GetProperty("type").GetString()
            .Should().Be($"https://api.ghseeli.example/errors/{code}");
        problem.GetProperty("title").GetString().Should().Be(title);
        problem.GetProperty("status").GetInt32().Should().Be((int)status);
        problem.GetProperty("detail").GetString().Should().Be(detail);
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("language").GetString().Should().Be(language);
        problem.GetProperty("correlationId").GetString().Should().Be(correlationId);
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle(correlationId);
        response.Content.Headers.ContentLanguage.Should().ContainSingle(language);
        response.Headers.Vary.Select(value => value).Should().Contain("Accept-Language");
        AssertSafeHeaders(response);
        return document;
    }

    internal static void AssertSafeHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
        response.Headers.GetValues("Permissions-Policy").Should().ContainSingle(
            "camera=(), microphone=(), geolocation=(), payment=()");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
        response.Headers.ETag.Should().BeNull();
        response.Content.Headers.LastModified.Should().BeNull();
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out _).Should().BeFalse();
        response.Headers.TryGetValues("Access-Control-Allow-Headers", out _).Should().BeFalse();
        response.Headers.TryGetValues("Access-Control-Allow-Credentials", out _).Should().BeFalse();
    }

    internal static async Task<Guid> CreateDraftAsync(
        CheckoutDraftApiFactory factory,
        HttpClient client,
        string token)
    {
        using var request = Request(
            HttpMethod.Post,
            "/api/v1/checkout/drafts?language=ar",
            token,
            JsonContent.Create(CheckoutDraftTestSupport.CreateValidCreateRequest(
                factory.Snapshot,
                FixedNow.AddHours(2))));
        using var response = await client.SendAsync(request);
        using var document = await JsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, document.RootElement.GetRawText());
        return document.RootElement.GetProperty("orderGuid").GetGuid();
    }

    internal static async Task<int> RepriceDraftAsync(
        HttpClient client,
        string token,
        Guid orderGuid,
        int expectedVersion)
    {
        using var request = Request(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=ar",
            token,
            JsonContent.Create(new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = expectedVersion
            }));
        request.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));
        using var response = await client.SendAsync(request);
        using var document = await JsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, document.RootElement.GetRawText());
        return document.RootElement.GetProperty("version").GetInt32();
    }

    internal static UpdateCheckoutDraftRequest UpdateRequest(
        CatalogSnapshotResponse snapshot,
        int expectedVersion,
        string color = "Green")
    {
        var request = CheckoutDraftTestSupport.CreateValidUpdateRequest(
            snapshot,
            FixedNow.AddHours(3),
            expectedVersion);
        request.Vehicle.Color = color;
        return request;
    }

    internal static async Task<string> ReadDraftStateAsync(
        CheckoutDraftApiFactory factory,
        Guid orderGuid)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var draft = await context.CheckoutDrafts
            .AsNoTracking()
            .Include(value => value.Items)
                .ThenInclude(value => value.Selections)
            .Include(value => value.PricingSnapshot)
                .ThenInclude(value => value!.Items)
                    .ThenInclude(value => value.Selections)
            .SingleAsync(value => value.OrderGuid == orderGuid);

        return JsonSerializer.Serialize(new
        {
            draft.Id,
            draft.OrderGuid,
            draft.OwnerDeviceId,
            draft.BusinessSourceId,
            draft.BranchSourceId,
            draft.CatalogVersion,
            draft.PublicVersion,
            draft.RequestedSlotStartUtc,
            draft.VehicleType,
            draft.LicensePlate,
            draft.VehicleMake,
            draft.VehicleModel,
            draft.VehicleColor,
            draft.AddressLine,
            draft.City,
            draft.Area,
            draft.Latitude,
            draft.Longitude,
            draft.RequiresReprice,
            draft.CreatedAt,
            draft.UpdatedAt,
            draft.ExpiresAt,
            draft.ConfirmationClaimedAtUtc,
            draft.ConfirmationClaimedVersion,
            draft.ConfirmationBookingReference,
            RowVersion = Convert.ToBase64String(draft.RowVersion),
            Items = draft.Items
                .OrderBy(value => value.DisplayOrder)
                .Select(value => new
                {
                    value.Id,
                    value.OfferingSourceId,
                    value.DisplayOrder,
                    Selections = value.Selections
                        .OrderBy(selection => selection.DisplayOrder)
                        .Select(selection => new
                        {
                            selection.Id,
                            selection.AddonGroupSourceId,
                            selection.AddonChoiceSourceId,
                            selection.Quantity,
                            selection.DisplayOrder
                        })
                }),
            Pricing = draft.PricingSnapshot is null
                ? null
                : new
                {
                    draft.PricingSnapshot.Id,
                    draft.PricingSnapshot.CatalogVersion,
                    draft.PricingSnapshot.Currency,
                    draft.PricingSnapshot.QuotedAtUtc,
                    draft.PricingSnapshot.BaseSubtotal,
                    draft.PricingSnapshot.AddonSubtotal,
                    draft.PricingSnapshot.ItemSubtotal,
                    draft.PricingSnapshot.ServiceFee,
                    draft.PricingSnapshot.ServiceFeeMode,
                    draft.PricingSnapshot.ServiceFeeFlatAmount,
                    draft.PricingSnapshot.ServiceFeePercentageRate,
                    draft.PricingSnapshot.TaxableSubtotal,
                    draft.PricingSnapshot.TaxRatePercent,
                    draft.PricingSnapshot.TaxAppliesToServiceFee,
                    draft.PricingSnapshot.Tax,
                    draft.PricingSnapshot.GrandTotal,
                    draft.PricingSnapshot.TotalDurationMinutes,
                    Items = draft.PricingSnapshot.Items
                        .OrderBy(value => value.DisplayOrder)
                        .Select(value => new
                        {
                            value.Id,
                            value.OfferingSourceId,
                            value.DisplayOrder,
                            value.BaseSubtotal,
                            value.AddonSubtotal,
                            value.ItemSubtotal,
                            value.TotalDurationMinutes,
                            Selections = value.Selections
                                .OrderBy(selection => selection.DisplayOrder)
                                .Select(selection => new
                                {
                                    selection.Id,
                                    selection.AddonGroupSourceId,
                                    selection.AddonChoiceSourceId,
                                    selection.SelectionType,
                                    selection.Quantity,
                                    selection.UnitPriceAdjustment,
                                    selection.TotalPriceAdjustment,
                                    selection.UnitDurationAdjustmentMinutes,
                                    selection.TotalDurationAdjustmentMinutes,
                                    selection.IsDefaultApplied,
                                    selection.DisplayOrder
                                })
                        })
                }
        });
    }

    internal static async Task SeedUserAsync(
        CheckoutDraftApiFactory factory,
        Guid userId,
        string marker)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Users.Add(new User
        {
            Id = userId,
            UserName = $"{marker}@example.invalid",
            NormalizedUserName = $"{marker}@EXAMPLE.INVALID",
            Email = $"{marker}@example.invalid",
            NormalizedEmail = $"{marker}@EXAMPLE.INVALID",
            FullName = marker,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        });
        await context.SaveChangesAsync();
    }

    internal static async Task<CustomerBooking> SeedPayableBookingAsync(
        CheckoutDraftApiFactory factory,
        Guid userId,
        Guid deviceId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = new CustomerBooking
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            UserId = userId,
            OwnerDeviceId = deviceId,
            BusinessReservationId = Guid.NewGuid(),
            BusinessWorkOrderId = Guid.NewGuid(),
            BusinessSourceId = factory.Snapshot.Company.Id,
            BranchSourceId = factory.Snapshot.Branches.Single().Id,
            CatalogVersion = factory.Snapshot.CatalogVersion,
            ConfirmedDraftVersion = 2,
            Status = "Pending",
            BusinessStatusSequence = 0,
            StatusChangedAtUtc = FixedNow,
            RequestedSlotStartUtc = FixedNow.AddHours(2),
            RequestedSlotEndUtc = FixedNow.AddHours(3),
            ProviderNameAr = "مزود",
            BranchNameAr = "فرع",
            VehicleType = "Sedan",
            AddressLine = "العنوان",
            Latitude = 32.1m,
            Longitude = 34.8m,
            Currency = "ILS",
            BaseSubtotal = 120m,
            AddonSubtotal = 0m,
            ItemSubtotal = 120m,
            ServiceFeeMode = "None",
            TaxableSubtotal = 120m,
            GrandTotal = 120m,
            TotalDurationMinutes = 60,
            QuotedAtUtc = FixedNow,
            CreatedAtUtc = FixedNow
        };
        context.CustomerBookings.Add(booking);
        await context.SaveChangesAsync();
        return booking;
    }
}
