using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GhseeliApis.Services.Payments;

namespace GhseeliApis.Tests.Services.Payments;

/// <summary>
/// Tests signature verification and parsing of locally generated Stripe events.
/// </summary>
public sealed class StripeWebhookParserTests
{
    [Fact]
    public void Parse_AcceptsSignedMinimalEventWithoutApiVersion()
    {
        const string secret = "whsec_local_parser_test";
        const string body =
            """{"id":"evt_local","object":"event","type":"customer.created","data":{"object":{"id":"cus_local","object":"customer"}}}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes($"{timestamp}.{body}")))
            .ToLowerInvariant();

        var parsed = new StripeWebhookParser().Parse(
            body,
            $"t={timestamp},v1={signature}",
            secret);

        parsed.EventId.Should().Be("evt_local");
        parsed.Kind.Should().Be(StripePaymentEventKind.Ignored);
    }
}
