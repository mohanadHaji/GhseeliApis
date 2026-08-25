using System.Net;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ghseeli.BusinessApi.Tests.Integration;

/// <summary>
/// Freezes the no-CORS contract for internal Business routes.
/// </summary>
public sealed class Step17CorsContractTests
{
    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-CORS-005")]
    public async Task InternalPreflightUsesResolvedContract()
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateSecureClient();
        var before = ReadSideEffects(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/v1/internal/reservations");
        request.Headers.TryAddWithoutValidation("Origin", "https://hostile.example");
        request.Headers.TryAddWithoutValidation(
            "Access-Control-Request-Method",
            "POST");
        request.Headers.TryAddWithoutValidation(
            "Access-Control-Request-Headers",
            "content-type,idempotency-key,x-ghseeli-service-id," +
            "x-ghseeli-timestamp,x-ghseeli-nonce,x-ghseeli-signature");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");
        response.Content.Headers.Allow.Should().ContainSingle("POST");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.ghseeli.example/errors/method_not_allowed");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(405);
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be("method_not_allowed");
        var correlationId = problem.RootElement
            .GetProperty("correlationId")
            .GetString();
        correlationId.Should().NotBeNullOrWhiteSpace();
        problem.RootElement.TryGetProperty("language", out _).Should().BeFalse();
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertSingleHeader(response, "X-Correlation-Id", correlationId!);
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
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Headers").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        ReadSideEffects(factory).Should().Be(before);
    }

    private static SideEffects ReadSideEffects(CatalogApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        return new SideEffects(
            context.InternalServiceNonces.Count(),
            context.InternalServiceIdempotencyRecords.Count(),
            context.AppointmentReservations.Count());
    }

    private static void AssertSingleHeader(
        HttpResponseMessage response,
        string name,
        string expected)
    {
        var values = response.Headers.TryGetValues(name, out var responseValues)
            ? responseValues.ToArray()
            : response.Content.Headers.TryGetValues(name, out var contentValues)
                ? contentValues.ToArray()
                : [];
        values.Should().ContainSingle(expected);
    }

    private sealed record SideEffects(
        int Nonces,
        int IdempotencyRecords,
        int Reservations);
}
