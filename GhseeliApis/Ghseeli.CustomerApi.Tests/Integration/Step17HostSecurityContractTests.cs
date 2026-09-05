using FluentAssertions;
using GhseeliApis.DTOs.Auth;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Middleware;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Interfaces;
using GhseeliApis.Services.Payments;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Freezes the Step 17 Customer host CORS, device-state, and rate-limit contracts.
/// </summary>
public sealed class Step17HostSecurityContractTests
{
    private const string SwaggerContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'none'";

    [Theory]
    [InlineData(0, 600)]
    [InlineData(10, 0)]
    [InlineData(-1, 600)]
    [InlineData(10, -1)]
    public void RateLimitOptions_NonpositiveValuesFailValidation(
        int permitLimit,
        int windowSeconds)
    {
        var result = new CustomerRateLimitOptionsValidator().Validate(
            null,
            new CustomerRateLimitOptions
            {
                DeviceRegistrationPermitLimit = permitLimit,
                DeviceRegistrationWindowSeconds = windowSeconds
            });

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void RateLimitOptions_ProductionDefaultsMatchFrozenPolicy()
    {
        var options = new CustomerRateLimitOptions();

        options.Should().BeEquivalentTo(new
        {
            DeviceRegistrationPermitLimit = 10,
            DeviceRegistrationWindowSeconds = 600,
            DeviceRegistrationPeerPermitLimit = 30,
            DeviceRegistrationPeerWindowSeconds = 600,
            AuthAccountPermitLimit = 10,
            AuthAccountWindowSeconds = 60,
            AuthAggregatePermitLimit = 50,
            AuthAggregateWindowSeconds = 300,
            DevicePermitLimit = 300,
            DeviceWindowSeconds = 60,
            BearerMutationPermitLimit = 60,
            BearerMutationWindowSeconds = 60,
            PaymentIntentPermitLimit = 10,
            PaymentIntentWindowSeconds = 60,
            PaymentAggregatePermitLimit = 20,
            PaymentAggregateWindowSeconds = 60,
            ValidPaymentWebhookPermitLimit = 600,
            ValidPaymentWebhookWindowSeconds = 60,
            InvalidPaymentWebhookPermitLimit = 60,
            InvalidPaymentWebhookWindowSeconds = 60,
            AnonymousPermitLimit = 60,
            AnonymousWindowSeconds = 60
        });
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "0.0.0.0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/0")]
    public void RateLimitOptions_UnsafeForwardedHeaderTrustFailsClosed(
        string key,
        string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [key] = value
            })
            .Build();

        var result = new CustomerRateLimitOptionsValidator(configuration)
            .Validate(null, new CustomerRateLimitOptions());

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void CustomerPipeline_AppliesForwardedHeadersBeforeHsts()
    {
        var program = File.ReadAllText(
            Path.Combine(FindSolutionRoot(), "Ghseeli.CustomerApi", "Program.cs"));
        var forwardedHeaders = program.IndexOf(
            "app.UseForwardedHeaders();",
            StringComparison.Ordinal);
        var hsts = program.IndexOf("app.UseHsts();", StringComparison.Ordinal);

        forwardedHeaders.Should().BeGreaterThanOrEqualTo(0);
        hsts.Should().BeGreaterThan(forwardedHeaders);
    }

    [Theory]
    [InlineData(nameof(CustomerRateLimitOptions.DeviceRegistrationPeerPermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.DeviceRegistrationPeerWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.AuthAccountPermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.AuthAccountWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.AuthAggregatePermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.AuthAggregateWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.DevicePermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.DeviceWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.BearerMutationPermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.BearerMutationWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.PaymentIntentPermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.PaymentIntentWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.PaymentAggregatePermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.PaymentAggregateWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.ValidPaymentWebhookPermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.ValidPaymentWebhookWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.InvalidPaymentWebhookPermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.InvalidPaymentWebhookWindowSeconds))]
    [InlineData(nameof(CustomerRateLimitOptions.AnonymousPermitLimit))]
    [InlineData(nameof(CustomerRateLimitOptions.AnonymousWindowSeconds))]
    public void RateLimitOptions_EveryCategoryFailsClosedWhenNonpositive(
        string propertyName)
    {
        var options = new CustomerRateLimitOptions();
        typeof(CustomerRateLimitOptions).GetProperty(propertyName)!
            .SetValue(options, 0);

        new CustomerRateLimitOptionsValidator()
            .Validate(null, options)
            .Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("STEP17-SEC-CORS-001", null)]
    [InlineData("STEP17-SEC-CORS-002", "https://evil.example")]
    [InlineData("STEP17-SEC-CORS-003", "https://127.0.0.1:443")]
    public async Task Configuration_DoesNotOptIntoCors(
        string scenarioId,
        string? origin)
    {
        var token = Token(17);
        using var factory = CreateFactory(token);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/configuration");
        request.Headers.Add(DeviceTokenDefaults.HeaderName, token);
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, scenarioId);
        AssertNoCorsHeaders(response);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-CORS-004")]
    public async Task PaymentPreflight_UsesNormalMethodContractWithoutCors()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/v1/payments/intents");
        request.Headers.Add("Origin", "https://app.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add(
            "Access-Control-Request-Headers",
            "authorization,x-device-token,idempotency-key");

        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        Header(response, "Allow").Should().Contain("POST");
        body.RootElement.GetProperty("code").GetString()
            .Should().Be("method_not_allowed");
        AssertNoCorsHeaders(response);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-CORS-006")]
    public async Task SwaggerUi_UsesSelfHostedContentSecurityPolicyWithoutCors()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/swagger/index.html");
        request.Headers.Add("Origin", "null");
        request.Headers.Add("Accept", "text/html");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, "Content-Security-Policy")
            .Should().Be(SwaggerContentSecurityPolicy);
        Header(response, "Permissions-Policy")
            .Should().Be("camera=(), microphone=(), geolocation=()");
        AssertNoCorsHeaders(response);
    }

    [Fact]
    public void CustomerDevice_DefinesPersistentActiveState()
    {
        typeof(CustomerDevice).GetProperty("IsActive")
            .Should().NotBeNull("inactive devices require a persistent fail-closed state");
        typeof(DeviceProblemCodes).GetField("TokenInactive")?.GetValue(null)
            .Should().Be("device_token_inactive");
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DEVICE-002")]
    public async Task InactiveDevice_CannotReachConfiguration()
    {
        var token = Token(18);
        using var factory = CreateFactory(token, isDeviceActive: false);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/configuration");
        request.Headers.Add(DeviceTokenDefaults.HeaderName, token);

        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.RootElement.GetProperty("code").GetString()
            .Should().Be(DeviceProblemCodes.TokenInactive);
        body.RootElement.GetProperty("detail").GetString()
            .Should().Be("الجهاز غير نشط.");
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-007")]
    public async Task DeviceRegistration_UsesIndependentInstallationBucketsBehindOnePeer()
    {
        using var factory = CreateRateFactory();
        using var client = CreateClient(factory);
        var firstInstallation = Guid.NewGuid();
        var secondInstallation = Guid.NewGuid();
        var tokens = new Dictionary<Guid, string?>();

        foreach (var installationId in new[]
                 {
                     firstInstallation,
                     secondInstallation
                 })
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "/api/v1/devices/register")
                {
                    Content = JsonContent.Create(new
                    {
                        installationId,
                        platform = "Android",
                        appVersion = $"17.0.{attempt}"
                    })
                };
                if (tokens.TryGetValue(installationId, out var token))
                {
                    request.Headers.Add(DeviceTokenDefaults.HeaderName, token);
                }

                using var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                using var body = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());
                tokens[installationId] =
                    body.RootElement.GetProperty("token").GetString();
            }
        }

        using var limitedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/devices/register")
        {
            Content = JsonContent.Create(new
            {
                installationId = firstInstallation,
                platform = "Android",
                appVersion = "17.0.3"
            })
        };
        limitedRequest.Headers.Add(
            DeviceTokenDefaults.HeaderName,
            tokens[firstInstallation]);

        using var limitedResponse = await client.SendAsync(limitedRequest);
        using var problem = JsonDocument.Parse(
            await limitedResponse.Content.ReadAsStringAsync());

        limitedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        AssertRetryAfterWithinTestWindow(limitedResponse);
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be("rate_limit_exceeded");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var devices = await dbContext.CustomerDevices
            .Where(device =>
                device.InstallationId == firstInstallation ||
                device.InstallationId == secondInstallation)
            .ToListAsync();
        devices.Should().HaveCount(2);
        foreach (var device in devices)
        {
            device.AppVersion.Should().Be("17.0.2");
            DeviceTokenHasher.Matches(
                    device.TokenHash,
                    tokens[device.InstallationId]!)
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task DeviceRegistration_RotatingInstallationIdsCannotBypassPeerAggregate()
    {
        using var factory = CreateRateFactory();
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/devices/register")
            {
                Content = JsonContent.Create(new
                {
                    installationId = Guid.NewGuid(),
                    platform = "Android",
                    appVersion = "17.0.0"
                })
            };
            using var response = await client.SendAsync(request);

            if (attempt <= 4)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
            }
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await dbContext.CustomerDevices.CountAsync()).Should().Be(4);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-008")]
    public async Task Login_FourthRequestForAccountIsRateLimited()
    {
        var auth = new ControlledAuthService();
        using var factory = CreateRateFactory(auth: auth);
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var response = await PostLoginAsync(
                client,
                "same-account@example.test");
            if (attempt <= 3)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                await AssertLoginSuccessAsync(response);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
            }
        }

        auth.LoginCalls.Should().Be(3);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-009")]
    public async Task Login_SeventhAccountFromPeerIsRateLimitedByAggregate()
    {
        var auth = new ControlledAuthService();
        using var factory = CreateRateFactory(auth: auth);
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 7; attempt++)
        {
            using var response = await PostLoginAsync(
                client,
                $"account-{attempt}@example.test");
            if (attempt <= 6)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                await AssertLoginSuccessAsync(response);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
            }
        }

        auth.LoginCalls.Should().Be(6);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-010")]
    public async Task PaymentIntent_ThirdAttemptIsRateLimitedBeforeSideEffects()
    {
        var token = Token(19);
        var payment = new ControlledPaymentService();
        using var factory = CreateRateFactory(token, payment: payment);
        using var client = CreateClient(factory);
        var bookingId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        string? successfulBody = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/payments/intents");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                CreateJwt(userId));
            request.Headers.Add(DeviceTokenDefaults.HeaderName, token);
            request.Headers.Add("Idempotency-Key", "same-safe-key");
            request.Content = JsonContent.Create(new
            {
                bookingId,
                method = "Card"
            });

            using var response = await client.SendAsync(request);
            if (attempt <= 2)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                AssertSecureNoStore(response);
                var body = await response.Content.ReadAsStringAsync();
                successfulBody ??= body;
                body.Should().Be(successfulBody);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
            }
        }

        payment.CreateCalls.Should().Be(2);
        payment.ProviderCalls.Should().Be(1);
    }

    [Fact]
    public async Task PaymentIntent_RotatingBookingIdsCannotBypassSubjectDeviceAggregate()
    {
        var token = Token(22);
        var payment = new ControlledPaymentService();
        using var factory = CreateRateFactory(token, payment: payment);
        using var client = CreateClient(factory);
        var userId = Guid.NewGuid();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/payments/intents");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                CreateJwt(userId));
            request.Headers.Add(DeviceTokenDefaults.HeaderName, token);
            request.Headers.Add("Idempotency-Key", $"aggregate-safe-key-{attempt}");
            request.Content = JsonContent.Create(new
            {
                bookingId = Guid.NewGuid(),
                method = "Card"
            });

            using var response = await client.SendAsync(request);
            if (attempt <= 2)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
            }
        }

        payment.CreateCalls.Should().Be(2);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-011")]
    public async Task LahzaWebhook_FourthInvalidSignatureIsMachineRateLimited()
    {
        var service = new ControlledPaymentWebhookService();
        using var factory = CreateRateFactory(webhook: service);
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var response = await PostWebhookAsync(client, "invalid");
            if (attempt <= 3)
            {
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                using var problem = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());
                problem.RootElement.GetProperty("code").GetString()
                    .Should().Be(CustomerPaymentErrorCodes.SignatureInvalid);
            }
            else
            {
                await AssertMachineRateLimitAsync(response);
            }
        }

        service.Calls.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-012")]
    public async Task LahzaWebhook_ValidDeliveryIsIndependentOfExhaustedInvalidBucket()
    {
        var service = new ControlledPaymentWebhookService();
        using var factory = CreateRateFactory(webhook: service);
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var invalid = await PostWebhookAsync(client, "invalid");
            invalid.StatusCode.Should().Be(
                attempt <= 3
                    ? HttpStatusCode.BadRequest
                    : HttpStatusCode.TooManyRequests);
        }

        using var valid = await PostWebhookAsync(client, "valid");

        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        service.Calls.Should().Be(1);
    }

    [Fact]
    public async Task LahzaWebhook_SixthValidDeliveryUsesVerifiedIdentityBucket()
    {
        var service = new ControlledPaymentWebhookService();
        using var factory = CreateRateFactory(webhook: service);
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            using var response = await PostWebhookAsync(client, "valid");
            if (attempt <= 5)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
            else
            {
                await AssertMachineRateLimitAsync(response);
            }
        }

        service.Calls.Should().Be(5);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-015")]
    public async Task Login_ForgedForwardedForDoesNotChangeSocketPeerPartition()
    {
        var auth = new ControlledAuthService();
        using var factory = CreateRateFactory(auth: auth);
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var request = LoginRequest("same-account@example.test");
            request.Headers.TryAddWithoutValidation(
                "X-Forwarded-For",
                $"10.0.0.{attempt}");
            using var response = await client.SendAsync(request);
            if (attempt <= 3)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                await AssertLoginSuccessAsync(response);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
                (await response.Content.ReadAsStringAsync())
                    .Should().NotContain("10.0.0.");
            }
        }

        auth.LoginCalls.Should().Be(3);
    }

    [Fact]
    public async Task DeviceRead_FourthRequestUsesDeviceRouteBucket()
    {
        var token = Token(20);
        using var factory = CreateRateFactory(token);
        using var client = CreateClient(factory);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/v1/configuration");
            request.Headers.Add(DeviceTokenDefaults.HeaderName, token);
            using var response = await client.SendAsync(request);
            if (attempt <= 3)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
            }
        }
    }

    [Fact]
    public async Task BearerMutation_ThirdRequestUsesIdentityDeviceRouteBucket()
    {
        var token = Token(21);
        using var factory = CreateRateFactory(token);
        using var client = CreateClient(factory);
        var userId = Guid.NewGuid();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/bookings/from-draft");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                CreateJwt(userId));
            request.Headers.Add(DeviceTokenDefaults.HeaderName, token);
            request.Content = JsonContent.Create(new { });
            using var response = await client.SendAsync(request);
            if (attempt <= 2)
            {
                response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
            }
        }
    }

    [Fact]
    public async Task Swagger_IsLimitedWhileHealthRoutesRemainExempt()
    {
        using var factory = CreateRateFactory();
        using var client = CreateClient(factory);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var health = await client.GetAsync("/api/Health");
            health.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
            using var head = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, "/api/Health"));
            head.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var response = await client.GetAsync("/swagger/index.html");
            if (attempt <= 3)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
            else
            {
                await AssertLocalizedRateLimitAsync(response);
            }
        }
    }

    private static CustomerConfigurationApiFactory CreateFactory(
        string? token = null,
        bool isDeviceActive = true) =>
        new(context =>
        {
            context.CustomerConfigurations.Add(new CustomerConfiguration
            {
                Id = Guid.NewGuid(),
                IsActive = true,
                DisplayNameAr = "غسيلي",
                DisplayNameHe = "גסילי",
                IsMaintenanceModeEnabled = false
            });
            if (token is not null)
            {
                context.CustomerDevices.Add(new CustomerDevice
                {
                    Id = Guid.NewGuid(),
                    InstallationId = Guid.NewGuid(),
                    Platform = "Android",
                    AppVersion = "17.0.0",
                    TokenHash = DeviceTokenHasher.Hash(token),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    IsActive = isDeviceActive
                });
            }
        });

    private static WebApplicationFactory<Program> CreateRateFactory(
            string? token = null,
            ControlledAuthService? auth = null,
            ControlledPaymentService? payment = null,
            ControlledPaymentWebhookService? webhook = null)
    {
            var baseFactory = CreateFactory(token);
            return baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("RateLimiting:DeviceRegistrationPermitLimit", "2");
                builder.UseSetting("RateLimiting:DeviceRegistrationWindowSeconds", "60");
                builder.UseSetting("RateLimiting:DeviceRegistrationPeerPermitLimit", "4");
                builder.UseSetting("RateLimiting:DeviceRegistrationPeerWindowSeconds", "60");
                builder.UseSetting("RateLimiting:AuthAccountPermitLimit", "3");
                builder.UseSetting("RateLimiting:AuthAccountWindowSeconds", "60");
                builder.UseSetting("RateLimiting:AuthAggregatePermitLimit", "6");
                builder.UseSetting("RateLimiting:AuthAggregateWindowSeconds", "60");
                builder.UseSetting("RateLimiting:DevicePermitLimit", "3");
                builder.UseSetting("RateLimiting:DeviceWindowSeconds", "60");
                builder.UseSetting("RateLimiting:BearerMutationPermitLimit", "2");
                builder.UseSetting("RateLimiting:BearerMutationWindowSeconds", "60");
                builder.UseSetting("RateLimiting:PaymentIntentPermitLimit", "2");
                builder.UseSetting("RateLimiting:PaymentIntentWindowSeconds", "60");
                builder.UseSetting("RateLimiting:PaymentAggregatePermitLimit", "2");
                builder.UseSetting("RateLimiting:PaymentAggregateWindowSeconds", "60");
                builder.UseSetting("RateLimiting:ValidPaymentWebhookPermitLimit", "5");
                builder.UseSetting("RateLimiting:ValidPaymentWebhookWindowSeconds", "60");
                builder.UseSetting("RateLimiting:InvalidPaymentWebhookPermitLimit", "3");
                builder.UseSetting("RateLimiting:InvalidPaymentWebhookWindowSeconds", "60");
                builder.UseSetting("RateLimiting:AnonymousPermitLimit", "3");
                builder.UseSetting("RateLimiting:AnonymousWindowSeconds", "60");
                builder.UseSetting("Lahza:SecretKey", "sk_test_rate");
                builder.ConfigureTestServices(services =>
                {
                    if (auth is not null)
                    {
                        services.RemoveAll<IAuthService>();
                        services.AddSingleton<IAuthService>(auth);
                    }
                    if (payment is not null)
                    {
                        services.RemoveAll<ICustomerPaymentService>();
                        services.AddSingleton<ICustomerPaymentService>(payment);
                    }
                    if (webhook is not null)
                    {
                        services.RemoveAll<IPaymentWebhookService>();
                        services.AddSingleton<IPaymentWebhookService>(webhook);
                    }
                });
            });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
            factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://127.0.0.1")
            });

    private static async Task<HttpResponseMessage> PostLoginAsync(
            HttpClient client,
            string email)
    {
            using var request = LoginRequest(email);
            return await client.SendAsync(request);
    }

    private static HttpRequestMessage LoginRequest(string email) =>
            new(HttpMethod.Post, "/api/Auth/login")
            {
                Content = JsonContent.Create(new
                {
                    email,
                    password = "Safe-Test-Password-17!"
                })
            };

    private static async Task<HttpResponseMessage> PostWebhookAsync(
            HttpClient client,
            string signature)
    {
            const string body =
                """{"id":"evt_rate_test","event":"customer.created","data":{"reference":"GHSEELI-RATE-TEST","id":1001,"amount":1000,"currency":"ILS"}}""";
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/lahza/webhook");
            request.Headers.TryAddWithoutValidation(
                "X-Lahza-Signature",
                signature == "valid"
                    ? CreateLahzaSignature(body)
                    : signature);
            request.Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json");
            return await client.SendAsync(request);
    }

    private static string CreateLahzaSignature(string body)
    {
        const string secret = "sk_test_rate";
        return Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(body)));
    }

    private static async Task AssertLocalizedRateLimitAsync(
            HttpResponseMessage response)
    {
            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            AssertSecureNoStore(response);
            response.Content.Headers.ContentType!.MediaType
                .Should().Be("application/problem+json");
            AssertRetryAfterWithinTestWindow(response);
            response.Headers.Should().NotContain(header =>
                header.Key.StartsWith("RateLimit", StringComparison.OrdinalIgnoreCase));
            response.Content.Headers.ContentLanguage.Should().ContainSingle("ar");
            response.Headers.Vary.Select(value => value.ToString())
                .Should().Contain("Accept-Language");
            using var body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            body.RootElement.EnumerateObject().Select(value => value.Name)
                .Should().BeEquivalentTo(
                    "type", "title", "status", "detail", "code",
                    "correlationId", "language");
            body.RootElement.GetProperty("type").GetString().Should()
                .Be("https://api.ghseeli.example/errors/rate_limit_exceeded");
            body.RootElement.GetProperty("status").GetInt32().Should().Be(429);
            body.RootElement.GetProperty("code").GetString()
                .Should().Be("rate_limit_exceeded");
            body.RootElement.GetProperty("title").GetString()
                .Should().Be("تعذر إكمال الطلب.");
            body.RootElement.GetProperty("detail").GetString()
                .Should().Be("تم تجاوز حد الطلبات. حاول مرة أخرى لاحقًا.");
            body.RootElement.GetProperty("language").GetString().Should().Be("ar");
            body.RootElement.GetProperty("correlationId").GetString()
                .Should().NotBeNullOrWhiteSpace();
    }

    private static async Task AssertMachineRateLimitAsync(
            HttpResponseMessage response)
    {
            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            AssertSecureNoStore(response);
            response.Content.Headers.ContentType!.MediaType
                .Should().Be("application/problem+json");
            AssertRetryAfterWithinTestWindow(response);
            response.Headers.Should().NotContain(header =>
                header.Key.StartsWith("RateLimit", StringComparison.OrdinalIgnoreCase));
            response.Content.Headers.ContentLanguage.Should().BeEmpty();
            using var body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            body.RootElement.EnumerateObject().Select(value => value.Name)
                .Should().BeEquivalentTo(
                    "type", "title", "status", "detail", "code", "correlationId");
            body.RootElement.GetProperty("title").GetString()
                .Should().Be("Too many requests.");
            body.RootElement.GetProperty("detail").GetString().Should()
                .Be("The request rate limit was exceeded. Retry after the indicated delay.");
            body.RootElement.GetProperty("type").GetString().Should()
                .Be("https://api.ghseeli.example/errors/rate_limit_exceeded");
            body.RootElement.GetProperty("status").GetInt32().Should().Be(429);
            body.RootElement.GetProperty("code").GetString()
                .Should().Be("rate_limit_exceeded");
            body.RootElement.GetProperty("correlationId").GetString()
                .Should().NotBeNullOrWhiteSpace();
            body.RootElement.TryGetProperty("language", out _).Should().BeFalse();
            response.Headers.Vary.Select(value => value.ToString())
                .Should().NotContain("Accept-Language");
    }

    private static async Task AssertLoginSuccessAsync(HttpResponseMessage response)
    {
        AssertSecureNoStore(response);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("token").GetString()
            .Should().Be("safe-test-token");
    }

    private static void AssertRetryAfterWithinTestWindow(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        retryAfter.Should().NotBeNull();
        retryAfter!.Value.Should().BeGreaterThan(TimeSpan.Zero)
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
    }

    private static void AssertSecureNoStore(HttpResponseMessage response)
    {
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        var swagger = response.RequestMessage?.RequestUri?.AbsolutePath
            .StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) == true;
        Header(response, "Content-Security-Policy").Should()
            .Be(swagger
                ? SwaggerContentSecurityPolicy
                : "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
        Header(response, "Permissions-Policy").Should()
            .Be(swagger
                ? "camera=(), microphone=(), geolocation=()"
                : "camera=(), microphone=(), geolocation=(), payment=()");
    }

    private static string CreateJwt(Guid userId)
    {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                    "CustomerConfigurationTestsSecret_Minimum32Chars")),
                SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: "GhseeliApis.ConfigurationTests",
                audience: "GhseeliApis.ConfigurationClients",
                claims:
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                    new Claim(ClaimTypes.Role, "User")
                ],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void AssertNoCorsHeaders(HttpResponseMessage response)
    {
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Headers").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.Single()
            : response.Content.Headers.TryGetValues(name, out values)
                ? values.Single()
                : string.Empty;

    private static string Token(byte value) =>
        WebEncoders.Base64UrlEncode(Enumerable.Repeat(value, 32).ToArray());

    private static string FindSolutionRoot()
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

        throw new DirectoryNotFoundException("Could not find GhseeliApis.sln.");
    }

    private sealed class ControlledAuthService : IAuthService
    {
        public int LoginCalls { get; private set; }

        public Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            LoginCalls++;
            return Task.FromResult<AuthResponse?>(new AuthResponse
            {
                UserId = Guid.NewGuid(),
                Email = "redacted@example.test",
                FullName = "Rate Test",
                Token = "safe-test-token",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            });
        }

        public Task<AuthResponse?> RegisterAsync(
            RegisterRequest request,
            string role = "User") =>
            throw new NotSupportedException();

        public Task<string> GenerateJwtTokenAsync(
            Guid userId,
            string email,
            string fullName) =>
            throw new NotSupportedException();

        public Task<bool> ValidateTokenAsync(string token) =>
            throw new NotSupportedException();

        public Task<ExternalLoginCallbackResponse?> ExternalLoginCallbackAsync(
            ExternalLoginInfo info) =>
            throw new NotSupportedException();

        public Task<bool> LinkExternalLoginAsync(
            Guid userId,
            ExternalLoginInfo info) =>
            throw new NotSupportedException();

        public Task<bool> RemoveExternalLoginAsync(
            Guid userId,
            string loginProvider) =>
            throw new NotSupportedException();

        public Task<IList<ExternalLoginInfoDto>> GetExternalLoginsAsync(Guid userId) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledPaymentService : ICustomerPaymentService
    {
        private CustomerPaymentResponse? _response;
        public int CreateCalls { get; private set; }
        public int ProviderCalls { get; private set; }

        public Task<CustomerPaymentResponse> CreateAsync(
            CreateCustomerPaymentIntentRequest request,
            string idempotencyKey,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            if (_response is null)
            {
                ProviderCalls++;
                _response = new CustomerPaymentResponse
                {
                    Id = Guid.NewGuid(),
                    BookingId = request.BookingId,
                    Amount = 12.34m,
                    Currency = "ILS",
                    Method = "Card",
                    Status = "Pending",
                    Provider = PaymentProviders.Lahza,
                    ProviderStatus = "requires_confirmation",
                    ProviderReference = "GHSEELI-SAFE",
                    CheckoutUrl = "https://checkout.lahza.test/pay/GHSEELI-SAFE",
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
            }

            return Task.FromResult(_response);
        }

        public Task<CustomerPaymentResponse?> GetAsync(
            Guid paymentId,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CustomerPaymentResponse?>(null);

        public Task<CustomerPaymentResponse?> VerifyAsync(
            Guid paymentId,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CustomerPaymentResponse?>(null);
    }

    private sealed class ControlledPaymentWebhookService : IPaymentWebhookService
    {
        public int Calls { get; private set; }

        public Task ProcessAsync(
            VerifiedPaymentEvent paymentEvent,
            ReadOnlyMemory<byte> rawBody,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
