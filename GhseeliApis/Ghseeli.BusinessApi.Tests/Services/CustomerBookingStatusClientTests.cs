using FluentAssertions;
using Ghseeli.BusinessApi.Services;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Net;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies callback retry identity and per-attempt HMAC replay material.
/// </summary>
public sealed class CustomerBookingStatusClientTests
{
    [Fact]
    public async Task DeliverAsync_RepeatedSemanticEvent_KeepsIdentityAndBodyButRefreshesNonce()
    {
        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new CustomerBookingStatusClientOptions
        {
            BaseUrl = "https://customer.example",
            ServiceId = "ghseeli-business",
            ActiveSecret = "callback-secret-at-least-thirty-two-characters",
            RequireHttps = true
        });
        var client = new CustomerBookingStatusClient(
            httpClient,
            options,
            new TestEnvironment());
        var eventId = Guid.NewGuid();
        var idempotencyKey = $"booking-status-{Guid.NewGuid():N}";
        const string body = "{\"contractVersion\":\"v1\"}";

        await client.DeliverAsync(
            body, eventId, idempotencyKey, "corr-stable", CancellationToken.None);
        await client.DeliverAsync(
            body, eventId, idempotencyKey, "corr-stable", CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
        handler.Requests.Select(value => value.Body).Should().OnlyContain(value => value == body);
        handler.Requests.Select(value => value.IdempotencyKey)
            .Should().OnlyContain(value => value == idempotencyKey);
        idempotencyKey.Should().NotBe(eventId.ToString("N"));
        handler.Requests.Select(value => value.CorrelationId)
            .Should().OnlyContain(value => value == "corr-stable");
        handler.Requests.Select(value => value.Nonce).Distinct().Should().HaveCount(2);
        handler.Requests.Select(value => value.Timestamp).Distinct().Should().HaveCount(2);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RequestRecord(
                await request.Content!.ReadAsStringAsync(cancellationToken),
                Header(request, InternalServiceWireConstants.IdempotencyKeyHeaderName),
                Header(request, InternalServiceWireConstants.CorrelationIdHeaderName),
                Header(request, InternalServiceWireConstants.NonceHeaderName),
                Header(request, InternalServiceWireConstants.TimestampHeaderName)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static string Header(HttpRequestMessage request, string name) =>
            request.Headers.GetValues(name).Single();
    }

    private sealed record RequestRecord(
        string Body,
        string IdempotencyKey,
        string CorrelationId,
        string Nonce,
        string Timestamp);

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
