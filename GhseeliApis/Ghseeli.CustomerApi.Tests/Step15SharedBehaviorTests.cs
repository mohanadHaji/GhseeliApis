using FluentAssertions;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Filters;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using System.Text;

namespace GhseeliApis.Tests;

/// <summary>
/// Defines the shared Step 15 localization, problem, transport, and filter contract.
/// </summary>
public class Step15SharedBehaviorTests
{
    [Theory]
    [InlineData("ar;q=0.2, he-IL;q=0.9", "he")]
    [InlineData("he;q=0.8, ar;q=0.8", "he")]
    [InlineData("ar;q=0.8, he;q=0.8", "ar")]
    [InlineData("en;q=1, he;q=0.7, ar;q=0.6", "he")]
    public void LanguageResolver_UsesWeightThenWireOrder(
        string header,
        string expected)
    {
        ConfigurationLanguageResolver.ResolveFromHeader(header).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("he;q=0")]
    [InlineData("*;q=1")]
    [InlineData("en-US,en;q=0.9")]
    [InlineData("he;q=bogus")]
    [InlineData("he;q=2")]
    [InlineData(",,,")]
    public void LanguageResolver_UnsupportedOrMalformedHeader_DefaultsToArabic(string? header)
    {
        ConfigurationLanguageResolver.ResolveFromHeader(header).Should().Be("ar");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ar,he")]
    [InlineData("he,he")]
    public void LanguageResolver_InvalidExplicitQuery_IsAuthoritativeAndDoesNotUseHeader(
        string query)
    {
        ConfigurationLanguageResolver.Resolve(query, "he").Should().Be(
            "ar",
            "an invalid explicit language must produce the Arabic-safe language_invalid presentation");
    }

    [Fact]
    public void LanguageResolver_ValidExplicitQuery_OverridesWeightedHeader()
    {
        ConfigurationLanguageResolver.Resolve(" AR ", "he;q=1").Should().Be("ar");
    }

    [Fact]
    public void HebrewLocalization_FallsBackPerFieldToArabic()
    {
        var method = typeof(CustomerConfigurationService).GetMethod(
            "SelectLocalizedText",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, ["he", "  الاسم العربي  ", "   "])
            .Should().Be("الاسم العربي");
    }

    [Fact]
    public void CommonProblemFactory_ProducesExactLocalizedShapeAndNormalizedFieldErrors()
    {
        var problem = ConfigurationProblemDetailsFactory.Create(
            StatusCodes.Status400BadRequest,
            "request_invalid",
            "he",
            "corr-step15",
            new Dictionary<string, string[]>
            {
                ["Items[1].Name"] = ["הערך אינו חוקי.", "הערך אינו חוקי."],
                ["Language"] = ["הערך אינו חוקי."]
            });

        problem.Type.Should().Be("https://api.ghseeli.example/errors/request_invalid");
        problem.Title.Should().Be("לא ניתן להשלים את הבקשה.");
        problem.Status.Should().Be(400);
        problem.Detail.Should().Be("הבקשה אינה חוקית.");
        problem.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("request_invalid");
        problem.Extensions.Should().ContainKey("correlationId").WhoseValue.Should().Be("corr-step15");
        problem.Extensions.Should().ContainKey("language").WhoseValue.Should().Be("he");
        problem.Extensions["fieldErrors"].Should().BeEquivalentTo(
            new SortedDictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["items[1].name"] = ["הערך אינו חוקי."],
                ["language"] = ["הערך אינו חוקי."]
            },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void LegacyStableCode_KeepsCodeAndTypeAcrossLanguages()
    {
        var arabic = CatalogProblemDetailsFactory.Create(
            404, CatalogProblemCodes.BusinessNotFound, "ar", "corr-ar");
        var hebrew = CatalogProblemDetailsFactory.Create(
            404, CatalogProblemCodes.BusinessNotFound, "he", "corr-he");

        arabic.Extensions["code"].Should().Be(CatalogProblemCodes.BusinessNotFound);
        hebrew.Extensions["code"].Should().Be(CatalogProblemCodes.BusinessNotFound);
        arabic.Type.Should().Be(hebrew.Type)
            .And.Be($"https://api.ghseeli.example/errors/{CatalogProblemCodes.BusinessNotFound}");
    }

    [Theory]
    [InlineData(404, "resource_not_found", "المورد المطلوب غير موجود.")]
    [InlineData(409, "request_conflict", "يتعارض الطلب مع الحالة الحالية.")]
    [InlineData(503, "service_unavailable", "الخدمة غير متاحة مؤقتًا.")]
    public void CommonProblemFactory_MapsGenericStatusToStableSafeCatalog(
        int status,
        string code,
        string exactDetail)
    {
        var problem = ConfigurationProblemDetailsFactory.Create(
            status, code, "ar", "step15-status");

        problem.Status.Should().Be(status);
        problem.Type.Should().Be($"https://api.ghseeli.example/errors/{code}");
        problem.Title.Should().Be("تعذر إكمال الطلب.");
        problem.Detail.Should().Be(exactDetail);
        problem.Extensions["code"].Should().Be(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad value")]
    [InlineData("line\r\ninjection")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task CorrelationMiddleware_ReplacesUnsafeValuesWithBoundedSafeId(string? supplied)
    {
        var context = new DefaultHttpContext();
        if (supplied is not null)
        {
            context.Request.Headers[InternalServiceWireConstants.CorrelationIdHeaderName] = supplied;
        }

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        context.TraceIdentifier.Should().MatchRegex("^[a-f0-9]{32}$");
        context.TraceIdentifier.Length.Should()
            .BeLessThanOrEqualTo(InternalServiceWireConstants.MaxCorrelationIdLength);
    }

    [Fact]
    public async Task CorrelationMiddleware_EchoesValidCallerValue()
    {
        const string supplied = "step15-valid_correlation:42";
        var context = new DefaultHttpContext();
        context.Request.Headers[InternalServiceWireConstants.CorrelationIdHeaderName] = supplied;
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.TraceIdentifier.Should().Be(supplied);
        context.Request.Headers[InternalServiceWireConstants.CorrelationIdHeaderName]
            .ToString().Should().Be(supplied);
    }

    [Fact]
    public async Task GenericSizeFilter_ReturnsExactProblemDetailsInsteadOfPlainText()
    {
        var context = CreateResourceContext(new string('x', 11), "/api/v1/example");
        var filter = new EnforceRequestBodySizeLimitAttribute(10);

        await filter.OnResourceExecutionAsync(
            context,
            () => throw new InvalidOperationException("downstream must not execute"));

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        result.ContentTypes.Should().ContainSingle().Which.Should()
            .Be("application/problem+json");
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Type.Should().Be(
            "https://api.ghseeli.example/errors/request_body_too_large");
        problem.Extensions["code"].Should().Be("request_body_too_large");
    }

    [Fact]
    public async Task GenericMediaFilter_ReturnsExactProblemDetailsBeforeHandler()
    {
        var context = CreateResourceContext("{}", "/api/v1/example");
        context.HttpContext.Request.ContentType = "text/plain";
        var filter = new EnforceJsonRequestContentTypeAttribute();
        var downstreamCalled = false;

        await filter.OnResourceExecutionAsync(
            context,
            () =>
            {
                downstreamCalled = true;
                throw new InvalidOperationException("must not execute");
            });

        downstreamCalled.Should().BeFalse();
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        problem.Type.Should().Be(
            "https://api.ghseeli.example/errors/unsupported_media_type");
        problem.Extensions["code"].Should().Be("unsupported_media_type");
    }

    private static ResourceExecutingContext CreateResourceContext(string body, string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new ResourceExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            []);
    }
}
