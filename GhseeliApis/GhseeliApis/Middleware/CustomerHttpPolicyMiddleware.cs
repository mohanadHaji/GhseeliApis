using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Devices;
using GhseeliApis.Services.Bookings;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

namespace GhseeliApis.Middleware;

public sealed class CustomerHttpPolicyMiddleware
{
    internal const string ResolvedLanguageItemKey = "Ghseeli.ResolvedLanguage";
    private const int MaximumRequestBodyBytes = 65_536;
    private const int MaximumAcceptLanguageLength = 16_384;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly RequestDelegate _next;

    public CustomerHttpPolicyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ApplyResponseHeaders(context);

        var localized = IsLocalized(context.Request.Path);
        var language = ResolveLanguage(context.Request);
        context.Items[ResolvedLanguageItemKey] = language;
        if (localized &&
            HasInvalidExplicitLanguage(context.Request))
        {
            await WriteProblemAsync(context, 400, "language_invalid", language);
            return;
        }

        if (context.Request.Headers.AcceptLanguage.ToString().Length >
            MaximumAcceptLanguageLength)
        {
            await WriteProblemAsync(context, 400, "request_invalid", language);
            return;
        }

        if (context.Request.Path.StartsWithSegments(
                "/api/health",
                StringComparison.OrdinalIgnoreCase) &&
            context.Request.Path.Value is not "/api/Health" and not "/api/Health/db")
        {
            await WriteProblemAsync(context, 404, "resource_not_found", language);
            return;
        }

        if (context.Request.Path.Equals(
                "/api/v1/bookings/from-draft",
                StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(
                context.Request.Headers[DeviceTokenDefaults.HeaderName]))
        {
            var problem = DeviceProblemDetailsFactory.Create(
                401,
                DeviceProblemCodes.TokenMissing,
                language,
                context.TraceIdentifier);
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(problem, JsonOptions),
                context.RequestAborted);
            return;
        }

        if (context.Request.Path.StartsWithSegments(
                "/api/v1/internal",
                StringComparison.OrdinalIgnoreCase) &&
            RequiresInternalAuthenticationBeforeRouting(context.Request) &&
            MissingInternalAuthentication(context.Request))
        {
            await WriteInternalProblemAsync(
                context,
                401,
                "internal_auth_missing_header");
            return;
        }

        if (context.Request.Path.Equals(
                "/api/v1/devices/register",
                StringComparison.OrdinalIgnoreCase) &&
            HasRequestBody(context.Request) &&
            !await IsBodyWithinLimitAsync(context.Request, context.RequestAborted))
        {
            await WriteProblemAsync(context, 413, "request_body_too_large", language);
            return;
        }

        if (context.Request.Path.Equals(
                "/api/v1/devices/register",
                StringComparison.OrdinalIgnoreCase) &&
            RequiresJson(context.Request) &&
            !IsJson(context.Request.ContentType))
        {
            await WriteProblemAsync(context, 415, "unsupported_media_type", language);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            try
            {
                await InvokeNextWithBufferedResponseAsync(context, buffer);
            }
            catch (HttpRequestException) when (
                !context.RequestAborted.IsCancellationRequested &&
                context.Request.Path.StartsWithSegments(
                    "/api/v1/catalog",
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Clear();
                var problem = CatalogProblemDetailsFactory.Create(
                    503,
                    CatalogProblemCodes.Unavailable,
                    language,
                    context.TraceIdentifier);
                context.Response.StatusCode = 503;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(problem, JsonOptions),
                    context.RequestAborted);
            }
            catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
            {
                context.Response.Clear();
                await WriteProblemAsync(context, 500, "unexpected_error", language);
            }

            if (ShouldNormalize(context, buffer))
            {
                var status = context.Response.StatusCode;
                context.Response.Body = originalBody;
                context.Response.ContentLength = null;
                if (HttpMethods.IsHead(context.Request.Method))
                {
                    return;
                }
                if (status == 400 &&
                    context.Request.Path.Equals(
                        "/api/v1/bookings/from-draft",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var problem = BookingConfirmationProblemDetailsFactory.Create(
                        status,
                        BookingConfirmationProblemCodes.Invalid,
                        language,
                        context.TraceIdentifier);
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(problem, JsonOptions),
                        context.RequestAborted);
                }
                else if (status == 415 &&
                         (context.Request.Path.Equals(
                              "/api/v1/pricing/reprice",
                              StringComparison.OrdinalIgnoreCase) ||
                          context.Request.Path.Equals(
                              "/api/v1/checkout/reprice",
                              StringComparison.OrdinalIgnoreCase)))
                {
                    var problem =
                        GhseeliApis.Services.Checkout.CheckoutPricingProblemDetailsFactory.Create(
                            status,
                            GhseeliApis.Services.Checkout.CheckoutPricingProblemCodes
                                .UnsupportedMediaType,
                            language,
                            context.TraceIdentifier);
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(problem, JsonOptions),
                        context.RequestAborted);
                }
                else
                {
                    await WriteProblemAsync(context, status, CodeForStatus(status), language);
                }
                return;
            }

            context.Response.Body = originalBody;
            buffer.Position = 0;
            if (context.Request.Path.StartsWithSegments(
                    "/swagger/v1/swagger.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Response.ContentLength = null;
                await WriteNormalizedSwaggerAsync(
                    buffer,
                    originalBody,
                    context.RequestAborted);
            }
            else
            {
                if (HttpMethods.IsHead(context.Request.Method))
                {
                    context.Response.ContentLength = null;
                }
                else
                {
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                }
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private async Task InvokeNextWithBufferedResponseAsync(
        HttpContext context,
        MemoryStream buffer)
    {
        var originalFeature = context.Features.Get<IHttpResponseFeature>();
        if (originalFeature is null)
        {
            await _next(context);
            return;
        }

        var bufferedFeature = new BufferedResponseFeature(originalFeature, buffer);
        context.Features.Set<IHttpResponseFeature>(bufferedFeature);
        try
        {
            await _next(context);
        }
        finally
        {
            if (ReferenceEquals(
                    context.Features.Get<IHttpResponseFeature>(),
                    bufferedFeature))
            {
                context.Features.Set(originalFeature);
            }
        }
    }

    private static void ApplyResponseHeaders(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.CacheControl = "no-store";
            headers.Remove("ETag");
            headers.Remove("Last-Modified");
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] =
                context.Request.Path.StartsWithSegments(
                    "/swagger", StringComparison.OrdinalIgnoreCase)
                    ? "default-src 'self'; script-src 'self' 'unsafe-inline'; " +
                      "style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
                      "font-src 'self'; connect-src 'self'; object-src 'none'; " +
                      "frame-ancestors 'none'; base-uri 'self'; form-action 'none'"
                    : "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            headers["Permissions-Policy"] = context.Request.Path.StartsWithSegments(
                "/swagger", StringComparison.OrdinalIgnoreCase)
                ? "camera=(), microphone=(), geolocation=()"
                : "camera=(), microphone=(), geolocation=(), payment=()";

            if (IsLocalized(context.Request.Path))
            {
                var language = ResolveLanguage(context.Request);
                context.Response.Headers.ContentLanguage = language;
                if (!headers.Vary.Any(value =>
                        value?.Contains("Accept-Language", StringComparison.OrdinalIgnoreCase) == true))
                {
                    headers.Append("Vary", "Accept-Language");
                }
            }

            return Task.CompletedTask;
        });
    }

    private static bool HasInvalidExplicitLanguage(HttpRequest request)
    {
        if (!request.Query.ContainsKey("language"))
        {
            return false;
        }

        var values = request.Query["language"];
        return values.Count != 1 ||
               !ConfigurationLanguageResolver.TryNormalizeOverride(values[0], out _);
    }

    private static string ResolveLanguage(HttpRequest request) =>
        ConfigurationLanguageResolver.Resolve(
            request.Query.ContainsKey("language")
                ? request.Query["language"].ToString()
                : null,
            request.Headers.AcceptLanguage.ToString());

    private static bool IsLocalized(PathString path) =>
        !path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWithSegments("/api/Health", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWithSegments("/api/lahza", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWithSegments("/api/v1/internal", StringComparison.OrdinalIgnoreCase) &&
        (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase));

    private static bool MissingInternalAuthentication(HttpRequest request) =>
        string.IsNullOrWhiteSpace(request.Headers[InternalServiceWireConstants.ServiceIdHeaderName]) ||
        string.IsNullOrWhiteSpace(request.Headers[InternalServiceWireConstants.TimestampHeaderName]) ||
        string.IsNullOrWhiteSpace(request.Headers[InternalServiceWireConstants.NonceHeaderName]) ||
        string.IsNullOrWhiteSpace(request.Headers[InternalServiceWireConstants.SignatureHeaderName]);

    private static bool RequiresInternalAuthenticationBeforeRouting(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        var value = request.Path.Value ?? string.Empty;
        const string prefix = "/api/v1/internal/bookings/";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remaining = value[prefix.Length..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (remaining.Length > 1)
        {
            return true;
        }

        return remaining.Length == 1 &&
               (string.Equals(remaining[0], "status", StringComparison.OrdinalIgnoreCase) ||
                Guid.TryParse(remaining[0], out _));
    }

    private static bool HasRequestBody(HttpRequest request) =>
        request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0;

    private static async Task<bool> IsBodyWithinLimitAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumRequestBodyBytes)
        {
            return false;
        }

        request.EnableBuffering();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                request.Body.Position = 0;
                return true;
            }

            total += read;
            if (total > MaximumRequestBodyBytes)
            {
                request.Body.Position = 0;
                return false;
            }
        }
    }

    private static bool RequiresJson(HttpRequest request) =>
        HasRequestBody(request) &&
        (HttpMethods.IsPost(request.Method) ||
         HttpMethods.IsPut(request.Method) ||
         HttpMethods.IsPatch(request.Method)) &&
        (request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase) ||
         request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase));

    private static bool IsJson(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
               mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool ShouldNormalize(HttpContext context, MemoryStream body)
    {
        var response = context.Response;
        if (response.StatusCode < 400)
        {
            return false;
        }

        if (response.StatusCode == 404 &&
            context.Request.Path.Value?.StartsWith(
                "/api/v1/payments/",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        if (!string.Equals(
                response.ContentType?.Split(';', 2)[0],
                "application/problem+json",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (body.Length == 0)
        {
            return true;
        }

        try
        {
            body.Position = 0;
            using var document = JsonDocument.Parse(body);
            body.Position = body.Length;
            return !document.RootElement.TryGetProperty("code", out _);
        }
        catch (JsonException)
        {
            body.Position = body.Length;
            return true;
        }
    }

    private static string CodeForStatus(int status) => status switch
    {
        400 => "request_invalid",
        401 => "customer_authentication_required",
        403 => "customer_authorization_forbidden",
        404 => "resource_not_found",
        405 => "method_not_allowed",
        409 => "request_conflict",
        413 => "request_body_too_large",
        415 => "unsupported_media_type",
        503 => "service_unavailable",
        _ => "unexpected_error"
    };

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string code,
        string language)
    {
        var problem = ConfigurationProblemDetailsFactory.Create(
            status,
            code,
            language,
            context.TraceIdentifier);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        context.Response.ContentLength = null;
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, JsonOptions),
            context.RequestAborted);
    }

    private static async Task WriteInternalProblemAsync(
        HttpContext context,
        int status,
        string code)
    {
        var problem = new
        {
            type = $"https://api.ghseeli.example/errors/{code}",
            title = "Internal service request was rejected.",
            status,
            detail = "Internal service authentication is required.",
            code,
            correlationId = context.TraceIdentifier
        };
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, JsonOptions),
            context.RequestAborted);
    }

    private static async Task WriteNormalizedSwaggerAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var root = await JsonNode.ParseAsync(source, cancellationToken: cancellationToken);
        if (root?["paths"] is JsonObject paths)
        {
            foreach (var path in paths)
            {
                if (path.Value is not JsonObject methods)
                {
                    continue;
                }

                foreach (var method in methods)
                {
                    if (method.Value is not JsonObject operation)
                    {
                        continue;
                    }

                    if (operation["parameters"] is JsonArray parameters)
                    {
                        foreach (var parameter in parameters.OfType<JsonObject>())
                        {
                            parameter.TryAdd("required", false);
                        }
                    }
                }
            }

            foreach (var (path, method) in new[]
            {
                ("/api/v1/devices/register", "post"),
                ("/api/Auth/register", "post"),
                ("/api/Auth/login", "post"),
                ("/api/Auth/external-login", "get"),
                ("/api/Auth/external-login-callback", "get"),
                ("/api/Health", "get"),
                ("/api/Health/db", "get")
            })
            {
                if (paths[path]?[method] is JsonObject operation)
                {
                    operation["security"] = new JsonArray();
                }
            }
        }

        await JsonSerializer.SerializeAsync(
            destination,
            root,
            JsonOptions,
            cancellationToken);
    }

    private sealed class BufferedResponseFeature(
        IHttpResponseFeature inner,
        MemoryStream buffer) : IHttpResponseFeature
    {
        public int StatusCode
        {
            get => inner.StatusCode;
            set => inner.StatusCode = value;
        }

        public string? ReasonPhrase
        {
            get => inner.ReasonPhrase;
            set => inner.ReasonPhrase = value;
        }

        public IHeaderDictionary Headers
        {
            get => inner.Headers;
            set => inner.Headers = value;
        }

#pragma warning disable CS0618
        public Stream Body
        {
            get => inner.Body;
            set => inner.Body = value;
        }
#pragma warning restore CS0618

        public bool HasStarted => inner.HasStarted || buffer.Length > 0;

        public void OnStarting(Func<object, Task> callback, object state) =>
            inner.OnStarting(callback, state);

        public void OnCompleted(Func<object, Task> callback, object state) =>
            inner.OnCompleted(callback, state);
    }
}
