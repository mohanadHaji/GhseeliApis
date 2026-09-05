using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GhseeliApis.Services.Payments;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Tests.Services.Payments;

/// <summary>
/// Contract tests for Lahza transaction initialization, verification, and webhooks.
/// </summary>
public sealed class LahzaPaymentGatewayTests
{
    [Fact]
    public void Configuration_accepts_non_placeholder_secret_without_prefix_assumptions()
    {
        LahzaConfiguration.IsConfigured(new LahzaConfigurationOptions
        {
            BaseUrl = "https://api.lahza.io",
            SecretKey = "opaque-production-secret"
        }).Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_sends_server_owned_values_and_maps_hosted_checkout()
    {
        var handler = new RecordingHandler(
            """
            {
              "status": true,
              "message": "Authorization URL created",
              "data": {
                "authorization_url": "https://checkout.lahza.io/test-access",
                "access_code": "test-access",
                "reference": "GHSEELI-0123456789ABCDEF0123456789ABCDEF"
              }
            }
            """);
        var gateway = CreateGateway(handler);
        var command = new PaymentInitializationCommand(
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1234,
            "ILS",
            "customer@example.test",
            "GHSEELI-0123456789ABCDEF0123456789ABCDEF");

        var result = await gateway.InitializeAsync(command, default);

        result.ProviderReference.Should().Be(command.ProviderReference);
        result.CheckoutUrl.Should().Be(
            new Uri("https://checkout.lahza.io/test-access"));
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri.Should().Be(
            new Uri("https://api.lahza.io/transaction/initialize"));
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be("sk_test_configured");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("amount").GetString().Should().Be("1234");
        body.RootElement.GetProperty("currency").GetString().Should().Be("ILS");
        body.RootElement.GetProperty("email").GetString().Should().Be(
            "customer@example.test");
        body.RootElement.GetProperty("reference").GetString().Should().Be(
            command.ProviderReference);
        body.RootElement.GetProperty("channels").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal("card");
    }

    [Fact]
    public async Task Verify_maps_nested_transaction_status_not_envelope_status()
    {
        var handler = new RecordingHandler(
            """
            {
              "status": true,
              "message": "Verification successful",
              "data": {
                "id": 690075529,
                "status": "success",
                "reference": "GHSEELI-REF",
                "amount": 1234,
                "currency": "ILS"
              }
            }
            """);
        var gateway = CreateGateway(handler);

        var result = await gateway.VerifyAsync("GHSEELI-REF", default);

        result.Status.Should().Be("success");
        result.ProviderReference.Should().Be("GHSEELI-REF");
        result.Amount.Should().Be(1234);
        result.Currency.Should().Be("ILS");
        result.ProviderTransactionId.Should().Be("690075529");
        handler.Request!.RequestUri.Should().Be(
            new Uri("https://api.lahza.io/transaction/verify/GHSEELI-REF"));
    }

    [Fact]
    public void Webhook_parser_verifies_exact_raw_body_with_hmac_sha256()
    {
        const string secret = "sk_test_configured";
        const string body =
            """
            {"event":"charge.success","data":{"id":42,"status":"success","reference":"GHSEELI-REF","amount":1234,"currency":"ILS"}}
            """;
        var signature = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(secret),
                    Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

        var parsed = new LahzaWebhookParser().Parse(
            Encoding.UTF8.GetBytes(body),
            signature,
            secret);

        parsed.EventType.Should().Be("charge.success");
        parsed.Kind.Should().Be(PaymentEventKind.Succeeded);
        parsed.ProviderReference.Should().Be("GHSEELI-REF");
        parsed.ProviderTransactionId.Should().Be("42");
        parsed.Amount.Should().Be(1234);
        parsed.Currency.Should().Be("ILS");
    }

    [Fact]
    public void Webhook_parser_rejects_signature_for_changed_body()
    {
        const string secret = "sk_test_configured";
        const string body =
            """{"event":"charge.success","data":{"reference":"GHSEELI-REF"}}""";
        var signature = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(secret),
                    Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

        var act = () => new LahzaWebhookParser().Parse(
            Encoding.UTF8.GetBytes(body + " "),
            signature,
            secret);

        var exception = act.Should().Throw<CustomerPaymentException>().Which;
        exception.StatusCode.Should().Be(400);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.SignatureInvalid);
    }

    [Theory]
    [InlineData("http://api.lahza.io", "")]
    [InlineData("https://api.lahza.io", "http://customer.example/callback")]
    public void Configured_provider_rejects_insecure_urls(
        string baseUrl,
        string callbackUrl)
    {
        var result = new LahzaConfigurationOptionsValidator().Validate(
            null,
            new LahzaConfigurationOptions
            {
                BaseUrl = baseUrl,
                SecretKey = "sk_test_configured",
                CallbackUrl = callbackUrl
            });

        result.Failed.Should().BeTrue();
    }

    private static LahzaPaymentGateway CreateGateway(HttpMessageHandler handler)
    {
        var options = new MockOptionsMonitor<LahzaConfigurationOptions>(
            new LahzaConfigurationOptions
            {
                BaseUrl = "https://api.lahza.io",
                SecretKey = "sk_test_configured"
            });
        return new LahzaPaymentGateway(
            new HttpClient(handler),
            options);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class MockOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
