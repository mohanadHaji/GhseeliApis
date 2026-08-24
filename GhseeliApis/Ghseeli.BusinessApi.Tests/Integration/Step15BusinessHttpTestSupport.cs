using FluentAssertions;
using Ghseeli.BusinessApi.Tests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Integration;

internal static class Step15BusinessHttpTestSupport
{
    public const string ArabicTitle = "تعذر إكمال الطلب.";
    public const string HebrewTitle = "לא ניתן להשלים את הבקשה.";

    public static HttpClient CreateOwnerClient(CatalogApiFactory factory) =>
        factory.CreateAuthenticatedClient(factory.OwnerUserId, Ghseeli.BusinessApi.Constants.BusinessRoles.Owner);

    public static async Task AssertLocalizedProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string language,
        string title,
        string detail,
        bool expectFieldErrors = false)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, payload);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        response.Headers.Should().NotContain(header =>
            header.Key == "ETag" || header.Key == "Last-Modified" || header.Key == "Set-Cookie");
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle();
        response.Content.Headers.ContentLanguage.Should().ContainSingle().Which.Should().Be(language);
        response.Headers.Vary.Should().Contain(value =>
            value.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.GetProperty("type").GetString().Should()
            .Be($"https://api.ghseeli.example/errors/{code}");
        root.GetProperty("title").GetString().Should().Be(title);
        root.GetProperty("status").GetInt32().Should().Be((int)status);
        root.GetProperty("detail").GetString().Should().Be(detail);
        root.GetProperty("code").GetString().Should().Be(code);
        root.GetProperty("language").GetString().Should().Be(language);
        root.GetProperty("correlationId").GetString().Should()
            .Be(response.Headers.GetValues("X-Correlation-Id").Single());
        root.TryGetProperty("fieldErrors", out _).Should().Be(expectFieldErrors);
        AssertRedacted(payload);
    }

    public static async Task AssertEnglishInternalProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, payload);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
        response.Headers.Vary.Should().NotContain(value =>
            value.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.GetProperty("code").GetString().Should().Be(code);
        root.TryGetProperty("language", out _).Should().BeFalse();
        AssertRedacted(payload);
    }

    public static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
        response.Headers.GetValues("Permissions-Policy").Should().ContainSingle();
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle();
    }

    public static void AssertRedacted(string payload)
    {
        payload.Should().NotContain("System.");
        payload.Should().NotContain("Microsoft.");
        payload.Should().NotContain("Bearer ");
        payload.Should().NotContain("Password1!");
        payload.Should().NotContain(CatalogApiFactory.InternalServiceActiveSecret);
        payload.Should().NotContain("X-Internal-Signature");
        payload.ToLowerInvariant().Should().NotContain("connection string");
        payload.ToLowerInvariant().Should().NotContain("stack trace");
    }
}
