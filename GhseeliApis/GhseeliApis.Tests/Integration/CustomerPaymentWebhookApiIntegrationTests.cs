using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Services.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises the complete Stripe webhook HTTP boundary with controlled verified events.
/// </summary>
public sealed class CustomerPaymentWebhookApiIntegrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task Webhook_NonJsonTransportIsRejectedBeforeConfigurationOrParsing(
        string? contentType)
    {
        var parser = new ControlledWebhookParser();
        var service = new ControlledWebhookService();
        await using var factory = CreateFactory(parser, service);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest("{}", "valid", contentType);

        using var response = await client.SendAsync(request);

        await AssertWebhookProblemAsync(
            response,
            HttpStatusCode.UnsupportedMediaType,
            CustomerPaymentErrorCodes.WebhookUnsupportedMediaType);
        parser.Calls.Should().Be(0);
        service.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-webhook-secret")]
    public async Task Webhook_InvalidConfigurationFailsClosedBeforeBodyOrSignature(
        string? secret)
    {
        var parser = new ControlledWebhookParser();
        var service = new ControlledWebhookService();
        await using var factory = CreateFactory(parser, service, secret);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest("{}", null, "application/json");

        using var response = await client.SendAsync(request);

        await AssertWebhookProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            CustomerPaymentErrorCodes.WebhookConfiguration);
        parser.Calls.Should().Be(0);
        service.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Webhook_MissingSignatureIsDistinctAndHasNoSideEffects()
    {
        var parser = new ControlledWebhookParser();
        var service = new ControlledWebhookService();
        await using var factory = CreateFactory(parser, service);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest("{}", null, "application/json");

        using var response = await client.SendAsync(request);

        await AssertWebhookProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            CustomerPaymentErrorCodes.SignatureMissing);
        parser.Calls.Should().Be(0);
        service.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(400, CustomerPaymentErrorCodes.SignatureInvalid)]
    [InlineData(400, CustomerPaymentErrorCodes.EventInvalid)]
    [InlineData(409, CustomerPaymentErrorCodes.WebhookConflict)]
    public async Task Webhook_VerificationAndConflictFailuresUseSafeExactProblems(
        int status,
        string code)
    {
        var parser = new ControlledWebhookParser
        {
            Failure = new CustomerPaymentException(
                status,
                code,
                "whsec_leak stripe-signature customer@example.com")
        };
        var service = new ControlledWebhookService();
        await using var factory = CreateFactory(parser, service);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest("{}", "signature-secret", "application/json");

        using var response = await client.SendAsync(request);

        await AssertWebhookProblemAsync(response, (HttpStatusCode)status, code);
        service.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Webhook_Over65536BytesStopsBeforeParser(bool streamed)
    {
        var parser = new ControlledWebhookParser();
        var service = new ControlledWebhookService();
        await using var factory = CreateFactory(parser, service);
        using var client = factory.CreateApiClient();
        var bytes = Encoding.UTF8.GetBytes(new string('x', 65_537));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook");
        request.Headers.Add("Stripe-Signature", "valid");
        request.Content = streamed
            ? new UnknownLengthContent(bytes)
            : new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request);

        await AssertWebhookProblemAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            CustomerPaymentErrorCodes.WebhookTooLarge);
        parser.Calls.Should().Be(0);
        service.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Webhook_ExactBoundaryPassesSizeGateAndPreservesExactBody()
    {
        var parser = new ControlledWebhookParser
        {
            Failure = new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.EventInvalid)
        };
        var service = new ControlledWebhookService();
        await using var factory = CreateFactory(parser, service);
        using var client = factory.CreateApiClient();
        var body = new string('x', 65_536);
        using var request = CreateRequest(body, "valid", "application/json; charset=utf-8");

        using var response = await client.SendAsync(request);

        await AssertWebhookProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            CustomerPaymentErrorCodes.EventInvalid);
        parser.RawBody.Should().Be(body);
    }

    [Fact]
    public async Task Webhook_SuccessIsAnonymousAndReturnsExactSafeAcknowledgement()
    {
        var parser = new ControlledWebhookParser();
        var service = new ControlledWebhookService();
        await using var factory = CreateFactory(parser, service);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest("""{"id":"evt_safe"}""", "valid", "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer malformed");
        request.Headers.TryAddWithoutValidation("X-Device-Token", "malformed device token");
        request.Headers.Add("X-Correlation-Id", "step14-webhook-correlation");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Correlation-Id")
            .Should().ContainSingle("step14-webhook-correlation");
        document.RootElement.EnumerateObject().Should().ContainSingle();
        document.RootElement.GetProperty("received").GetBoolean().Should().BeTrue();
        body.Should().NotContain("language")
            .And.NotContain("payment")
            .And.NotContain("booking")
            .And.NotContain("disposition");
        parser.RawBody.Should().Be("""{"id":"evt_safe"}""");
        service.RawBody.Should().Be(parser.RawBody);
    }

    [Fact]
    public async Task Webhook_PersistenceFailureIsRetryableAndNeverAcknowledged()
    {
        var logger = new FullExceptionCapturingLogger();
        var parser = new ControlledWebhookParser();
        var service = new ControlledWebhookService
        {
            Failure = new InvalidOperationException(
                "SELECT secret FROM payment sk_test_unsafe whsec_unsafe customer@example.com")
        };
        await using var factory = CreateFactory(parser, service, logger: logger);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest("{}", "valid", "application/json");

        using var response = await client.SendAsync(request);

        await AssertWebhookProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            CustomerPaymentErrorCodes.GatewayAmbiguous);
        logger.AllText.Should().NotContain("SELECT secret")
            .And.NotContain("sk_test_unsafe")
            .And.NotContain("whsec_unsafe")
            .And.NotContain("customer@example.com");
    }

    private static CheckoutDraftApiFactory CreateFactory(
        ControlledWebhookParser parser,
        ControlledWebhookService service,
        string? webhookSecret = "whsec_test_only",
        IAppLogger? logger = null) =>
        new(
            settings: new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = webhookSecret
            },
            configureTestServices: services =>
            {
                services.RemoveAll<IStripeWebhookParser>();
                services.RemoveAll<IStripeWebhookService>();
                services.AddSingleton<IStripeWebhookParser>(parser);
                services.AddSingleton<IStripeWebhookService>(service);
                if (logger is not null)
                {
                    services.RemoveAll<IAppLogger>();
                    services.AddSingleton(logger);
                }
            });

    private sealed class FullExceptionCapturingLogger : IAppLogger
    {
        private readonly List<string> _entries = [];

        public string AllText => string.Join(Environment.NewLine, _entries);

        public void LogInfo(string message) => _entries.Add(message);
        public void LogWarning(string message) => _entries.Add(message);
        public void LogError(string message) => _entries.Add(message);
        public void LogError(string message, Exception exception) =>
            _entries.Add($"{message}{Environment.NewLine}{exception}");
    }

    private static HttpRequestMessage CreateRequest(
        string body,
        string? signature,
        string? contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook");
        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        }
        request.Content = new StringContent(body, Encoding.UTF8);
        request.Content.Headers.ContentType = contentType is null
            ? null
            : MediaTypeHeaderValue.Parse(contentType);
        return request;
    }

    private static async Task AssertWebhookProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.GetProperty("status").GetInt32().Should().Be((int)status);
        root.GetProperty("code").GetString().Should().Be(code);
        root.GetProperty("title").GetString()
            .Should().Be("Stripe webhook request could not be processed.");
        root.GetProperty("detail").GetString()
            .Should().Be("Stripe webhook request could not be processed.");
        root.TryGetProperty("language", out _).Should().BeFalse();
        var correlation = root.GetProperty("correlationId").GetString();
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle(correlation);
        body.Should().NotContain("whsec_")
            .And.NotContain("stripe-signature")
            .And.NotContain("customer@example.com")
            .And.NotContain("SELECT secret");
    }

    private sealed class ControlledWebhookParser : IStripeWebhookParser
    {
        public int Calls { get; private set; }
        public string? RawBody { get; private set; }
        public Exception? Failure { get; set; }

        public VerifiedStripeEvent Parse(
            string rawBody,
            string signature,
            string webhookSecret)
        {
            Calls++;
            RawBody = rawBody;
            if (Failure is not null)
            {
                throw Failure;
            }

            return new VerifiedStripeEvent(
                "evt_safe",
                "unknown.event",
                StripePaymentEventKind.Ignored,
                string.Empty,
                null,
                0,
                string.Empty,
                new Dictionary<string, string>());
        }
    }

    private sealed class ControlledWebhookService : IStripeWebhookService
    {
        public int Calls { get; private set; }
        public string? RawBody { get; private set; }
        public Exception? Failure { get; set; }

        public Task ProcessAsync(
            VerifiedStripeEvent stripeEvent,
            string rawBody,
            CancellationToken cancellationToken)
        {
            Calls++;
            RawBody = rawBody;
            return Failure is null
                ? Task.CompletedTask
                : Task.FromException(Failure);
        }
    }

    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            stream.WriteAsync(content).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
