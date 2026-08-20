using FluentAssertions;
using Ghseeli.IntegrationContracts.InternalHttp;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies the deterministic internal-service canonical request vector.
/// </summary>
public class InternalServiceCanonicalRequestTests
{
    [Fact]
    public void Build_WhenIdempotencyKeyIsMissing_UsesDeterministicEmptyCanonicalLine()
    {
        var canonical = InternalServiceCanonicalRequest.Build(
            "customer-api-tests",
            HttpMethod.Post.Method,
            "/api/v1/internal/appointments/validate",
            [
                new KeyValuePair<string, string?>("z", "1"),
                new KeyValuePair<string, string?>("companyId", "11111111-1111-1111-1111-111111111111"),
                new KeyValuePair<string, string?>("a", "2")
            ],
            "2026-08-20T12:00:00.0000000Z",
            "1234567890abcdef1234567890abcdef",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        canonical.Should().Be(string.Join('\n',
            InternalServiceWireConstants.SignatureVersion,
            "customer-api-tests",
            HttpMethod.Post.Method,
            "/api/v1/internal/appointments/validate?a=2&companyId=11111111-1111-1111-1111-111111111111&z=1",
            "2026-08-20T12:00:00.0000000Z",
            "1234567890abcdef1234567890abcdef",
            string.Empty,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void Build_WhenIdempotencyKeyIsPresent_IncludesItBeforeBodyHash()
    {
        var canonical = InternalServiceCanonicalRequest.Build(
            "customer-api-tests",
            HttpMethod.Post.Method,
            "/api/v1/internal/appointments/validate",
            [new KeyValuePair<string, string?>("companyId", "11111111-1111-1111-1111-111111111111")],
            "2026-08-20T12:00:00.0000000Z",
            "1234567890abcdef1234567890abcdef",
            "idem-fixed-vector",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        canonical.Should().Be(string.Join('\n',
            InternalServiceWireConstants.SignatureVersion,
            "customer-api-tests",
            HttpMethod.Post.Method,
            "/api/v1/internal/appointments/validate?companyId=11111111-1111-1111-1111-111111111111",
            "2026-08-20T12:00:00.0000000Z",
            "1234567890abcdef1234567890abcdef",
            "idem-fixed-vector",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
    }
}
