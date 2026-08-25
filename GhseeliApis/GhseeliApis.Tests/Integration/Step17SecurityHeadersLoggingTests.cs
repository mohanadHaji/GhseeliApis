using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Auth;
using GhseeliApis.DTOs.Configuration;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Payments;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Freezes Step 17 failure-response headers and sensitive-value redaction.
/// </summary>
public sealed class Step17SecurityHeadersLoggingTests
{
    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-HEADERS-029")]
    public async Task STEP17_SEC_HEADERS_029_ConfigurationFailureEmitsSingleSecurityHeaders()
    {
        var token = CatalogTestSupport.CreateToken(29);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateConfigurationFactory(
            device,
            services =>
            {
                services.RemoveAll<ICustomerConfigurationService>();
                services.AddSingleton<ICustomerConfigurationService>(
                    new ThrowingConfigurationService());
            });
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/configuration");
        request.Headers.Add("X-Device-Token", token);

        using var response = await client.SendAsync(request);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        problem.RootElement.GetProperty("code").GetString().Should().Be("unexpected_error");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        problem.RootElement.GetProperty("language").GetString().Should().Be("ar");
        AssertSingleSecurityHeaders(response);
        await using var verify = CreateContext(factory);
        (await verify.CustomerPayments.CountAsync()).Should().Be(0);
        (await verify.StripeWebhookEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-LOG-030")]
    public async Task STEP17_SEC_LOG_030_InvalidStripeSignatureRedactsAllSentinels()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = new CheckoutDraftApiFactory(
            settings: new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = "whsec_step17_logging"
            },
            configureTestServices: services =>
            {
                services.AddSingleton<ILoggerProvider>(logs);
            });
        using var client = factory.CreateApiClient();
        const string email = "step17.sentinel@example.test";
        const string phone = "+972-50-777-0030";
        const string address = "17 Sentinel Street";
        const string clientSecret = "pi_step17_secret_SENTINEL";
        const string sql = "SELECT * FROM Secrets; Server=private-step17-db;Password=db-sentinel;";
        const string stack = "at Step17.Private.Stack()";
        const string rawSignature = "t=1700000000,v1=step17-raw-signature-sentinel";
        var rawBody = JsonSerializer.Serialize(new
        {
            email,
            phone,
            address,
            clientSecret,
            sql,
            stack
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/stripe/webhook");
        request.Headers.TryAddWithoutValidation("Stripe-Signature", rawSignature);
        request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(responseBody);
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CustomerPaymentErrorCodes.SignatureInvalid);
        problem.RootElement.TryGetProperty("language", out _).Should().BeFalse();
        AssertRedacted(
            responseBody,
            logs.AllText,
            rawBody,
            rawSignature,
            email,
            phone,
            address,
            clientSecret,
            sql,
            stack);
        await using var verify = CreateContext(factory);
        (await verify.StripeWebhookEvents.CountAsync()).Should().Be(0);
        (await verify.CustomerPayments.CountAsync()).Should().Be(0);
    }

    [Theory]
    [Trait("ScenarioId", "STEP17-SEC-LOG-030")]
    [InlineData("/api/Auth/register")]
    [InlineData("/api/Auth/login")]
    public async Task STEP17_SEC_LOG_030_AuthFailuresRedactExceptionSentinels(
        string path)
    {
        var logs = new CapturingLoggerProvider();
        const string email = "step17.auth.sentinel@example.test";
        const string phone = "+972-50-777-1030";
        const string address = "17 Auth Sentinel Street";
        const string clientSecret = "auth_step17_client_secret_SENTINEL";
        const string sql =
            "SELECT * FROM AuthSecrets; Server=private-auth-db;Password=step17;";
        const string stack = "at Step17.Auth.Private.Stack()";
        var exception = new SensitiveAuthException(
            $"{email}|{phone}|{address}|{clientSecret}|{sql}",
            stack);
        var userManager = CreateThrowingUserManager(exception);
        await using var factory = new CheckoutDraftApiFactory(
            configureTestServices: services =>
            {
                services.AddSingleton<ILoggerProvider>(logs);
                services.RemoveAll<UserManager<User>>();
                services.AddSingleton(userManager.Object);
            });
        using var client = factory.CreateApiClient();
        using var content = path.EndsWith(
            "/register",
            StringComparison.OrdinalIgnoreCase)
            ? JsonContent.Create(new RegisterRequest
            {
                Email = email,
                Password = "Step17Password!1",
                ConfirmPassword = "Step17Password!1",
                FullName = "Step 17 Auth Sentinel",
                PhoneNumber = phone
            })
            : JsonContent.Create(new LoginRequest
            {
                Email = email,
                Password = "Step17Password!1"
            });

        using var response = await client.PostAsync(path, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        logs.AllText.Should()
            .Contain($"exceptionType={nameof(SensitiveAuthException)}")
            .And.Contain($"exceptionCode=0x{exception.HResult:X8}");
        logs.Exceptions.Should().BeEmpty();
        AssertRedacted(
            responseBody,
            logs.AllText,
            email,
            phone,
            address,
            clientSecret,
            sql,
            stack);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-LOG-031")]
    public async Task STEP17_SEC_LOG_031_OverlongHeadersAreReplacedAndRedactedTogether()
    {
        var logs = new CapturingLoggerProvider();
        var service = new CountingConfigurationService();
        var token = CatalogTestSupport.CreateToken(31);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateConfigurationFactory(
            device,
            services =>
            {
                services.AddSingleton<ILoggerProvider>(logs);
                services.RemoveAll<ICustomerConfigurationService>();
                services.AddSingleton<ICustomerConfigurationService>(service);
            });
        using var client = CreateClient(factory);
        var overlongCorrelation = new string('c', 129);
        var overlongAcceptLanguage = new string('z', 16_385);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/configuration");
        request.Headers.Add("X-Device-Token", token);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", overlongCorrelation);
        request.Headers.TryAddWithoutValidation("Accept-Language", overlongAcceptLanguage);

        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(responseBody);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.RootElement.GetProperty("code").GetString().Should().Be("request_invalid");
        var safeCorrelation = problem.RootElement.GetProperty("correlationId").GetString();
        safeCorrelation.Should().MatchRegex("^[0-9a-f]{32}$");
        response.Headers.GetValues("X-Correlation-Id").Should()
            .ContainSingle(safeCorrelation!);
        problem.RootElement.GetProperty("language").GetString().Should().Be("ar");
        service.Calls.Should().Be(0);
        AssertSingleSecurityHeaders(response);
        responseBody.Should().NotContain(overlongCorrelation)
            .And.NotContain(overlongAcceptLanguage);
        logs.AllText.Should().NotContain(overlongCorrelation)
            .And.NotContain(overlongAcceptLanguage);
    }

    private static WebApplicationFactory<Program> CreateConfigurationFactory(
        CustomerDevice device,
        Action<IServiceCollection> configureServices)
    {
        var tokenHash = device.TokenHash.ToArray();
        return new CustomerConfigurationApiFactory(context =>
            {
                context.CustomerDevices.Add(new CustomerDevice
                {
                    Id = device.Id,
                    InstallationId = device.InstallationId,
                    Platform = device.Platform,
                    AppVersion = device.AppVersion,
                    TokenHash = tokenHash,
                    CreatedAt = device.CreatedAt,
                    UpdatedAt = device.UpdatedAt,
                    LastSeenAt = device.LastSeenAt,
                    ExpiresAt = device.ExpiresAt,
                    IsActive = device.IsActive
                });
            })
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(configureServices));
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static ApplicationDbContext CreateContext(WebApplicationFactory<Program> factory) =>
        factory.Services.CreateScope().ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

    private static void AssertSingleSecurityHeaders(HttpResponseMessage response)
    {
        AssertSingleHeader(response, "X-Correlation-Id");
        AssertSingleHeader(response, "X-Content-Type-Options", "nosniff");
        AssertSingleHeader(response, "X-Frame-Options", "DENY");
        AssertSingleHeader(response, "Referrer-Policy", "no-referrer");
        AssertSingleHeader(
            response,
            "Permissions-Policy",
            "camera=(), microphone=(), geolocation=(), payment=()");
        AssertSingleHeader(
            response,
            "Content-Security-Policy",
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
        response.Headers.ETag.Should().BeNull();
        response.Content.Headers.LastModified.Should().BeNull();
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Headers").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    private static void AssertSingleHeader(
        HttpResponseMessage response,
        string name,
        string? expected = null)
    {
        var values = response.Headers.TryGetValues(name, out var responseValues)
            ? responseValues.ToArray()
            : response.Content.Headers.TryGetValues(name, out var contentValues)
                ? contentValues.ToArray()
                : [];
        values.Should().ContainSingle();
        if (expected is not null)
        {
            values[0].Should().Be(expected);
        }
    }

    private static void AssertRedacted(
        string responseBody,
        string logs,
        params string[] sentinels)
    {
        foreach (var sentinel in sentinels)
        {
            responseBody.Should().NotContain(sentinel);
            logs.Should().NotContain(sentinel);
        }
    }

    private static Mock<UserManager<User>> CreateThrowingUserManager(
        Exception exception)
    {
        var store = new Mock<IUserStore<User>>();
        var manager = new Mock<UserManager<User>>(
            store.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        manager.Setup(value => value.FindByEmailAsync(It.IsAny<string>()))
            .ThrowsAsync(exception);
        return manager;
    }

    private sealed class ThrowingConfigurationService : ICustomerConfigurationService
    {
        public Task<ConfigurationResponse> GetActiveAsync(
            string? requestedLanguage,
            string? acceptLanguageHeader,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "step17-unhandled-configuration-dependency-failure");
    }

    private sealed class CountingConfigurationService : ICustomerConfigurationService
    {
        public int Calls { get; private set; }

        public Task<ConfigurationResponse> GetActiveAsync(
            string? requestedLanguage,
            string? acceptLanguageHeader,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ConfigurationResponse());
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();
        private readonly ConcurrentQueue<Exception> _exceptions = new();

        public string AllText => string.Join(Environment.NewLine, _entries);
        public IReadOnlyCollection<Exception> Exceptions => _exceptions.ToArray();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName, _entries, _exceptions);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            string category,
            ConcurrentQueue<string> entries,
            ConcurrentQueue<Exception> exceptions) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (exception is not null)
                {
                    exceptions.Enqueue(exception);
                }

                entries.Enqueue(
                    $"{category}|{logLevel}|{formatter(state, exception)}|{exception}");
            }
        }
    }

    private sealed class SensitiveAuthException(
        string message,
        string stackTrace) : Exception(message)
    {
        public override string? StackTrace => stackTrace;
    }
}
