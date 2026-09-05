using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GhseeliApis.Services.Payments;

namespace GhseeliApis.Tests.Services.Payments;

/// <summary>
/// Tests signature verification and parsing of locally generated Lahza events.
/// </summary>
public sealed class LahzaWebhookParserTests
{
    [Fact]
    public void Parse_AcceptsSignedMinimalEventWithoutApiVersion()
    {
        const string secret = "sk_test_local_parser";
        const string body =
            """{"id":"evt_local","event":"customer.created","data":{"reference":"GHSEELI-LOCAL","id":1001,"amount":1000,"currency":"ILS"}}""";
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

        var parsed = new LahzaWebhookParser().Parse(
            Encoding.UTF8.GetBytes(body),
            signature,
            secret);

        parsed.EventId.Should().Be("evt_local");
        parsed.Kind.Should().Be(PaymentEventKind.Ignored);
    }

    [Fact]
    public void Parse_rejects_signature_when_one_raw_utf8_byte_changes()
    {
        const string secret = "opaque-webhook-secret";
        const string body =
            """{"id":"evt_bytes","event":"charge.success","data":{"reference":"GHSEELI-BYTES","id":1001,"amount":1000,"currency":"ILS","note":"غسيل"}}""";
        var signedBytes = Encoding.UTF8.GetBytes(body);
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signedBytes));
        var alteredBytes = signedBytes.ToArray();
        var multibyteIndex = Array.FindIndex(alteredBytes, value => value >= 0x80);
        multibyteIndex.Should().BeGreaterThanOrEqualTo(0);
        alteredBytes[multibyteIndex] ^= 0x01;

        var act = () => new LahzaWebhookParser().Parse(
            alteredBytes,
            signature,
            secret);

        var exception = act.Should().Throw<CustomerPaymentException>().Which;
        exception.StatusCode.Should().Be(400);
        exception.Code.Should().Be(CustomerPaymentErrorCodes.SignatureInvalid);
    }
}
