using FluentAssertions;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.DataPartitioning;
using Ghseeli.IntegrationContracts.DataPartitioning;
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
            new TestEnvironment(),
            new BusinessDataPartitionContext());
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

    [Theory]
    [InlineData(DataPartitionNames.Production)]
    [InlineData(DataPartitionNames.Demo)]
    public async Task DeliverAsync_SignsAssignedPartition(string dataPartition)
    {
        var handler = new RecordingHandler();
        var partition = new BusinessDataPartitionContext();
        partition.SetTrustedPartition(dataPartition);
        var client = new CustomerBookingStatusClient(
            new HttpClient(handler),
            Options.Create(new CustomerBookingStatusClientOptions
            {
                BaseUrl = "https://customer.example",
                ServiceId = "ghseeli-business",
                ActiveSecret = "callback-secret-at-least-thirty-two-characters",
                RequireHttps = true
            }),
            new TestEnvironment(),
            partition);

        await client.DeliverAsync(
            "{\"contractVersion\":\"v1\"}",
            Guid.NewGuid(),
            "booking-status-partition",
            "corr-partition",
            CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            request.Uri.Query);
        query[DataPartitionNames.QueryParameter].Should().ContainSingle(dataPartition);
        request.Signature.Should().HaveLength(64);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RequestRecord(
                request.RequestUri!,
                await request.Content!.ReadAsStringAsync(cancellationToken),
                Header(request, InternalServiceWireConstants.IdempotencyKeyHeaderName),
                Header(request, InternalServiceWireConstants.CorrelationIdHeaderName),
                Header(request, InternalServiceWireConstants.NonceHeaderName),
                Header(request, InternalServiceWireConstants.TimestampHeaderName),
                Header(request, InternalServiceWireConstants.SignatureHeaderName)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static string Header(HttpRequestMessage request, string name) =>
            request.Headers.GetValues(name).Single();
    }

    private sealed record RequestRecord(
        Uri Uri,
        string Body,
        string IdempotencyKey,
        string CorrelationId,
        string Nonce,
        string Timestamp,
        string Signature);

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
