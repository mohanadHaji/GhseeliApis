using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Devices;
using GhseeliApis.Services.Payments;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace GhseeliApis.Middleware;

public static class CustomerRateLimitPolicyNames
{
    public const string DeviceRegistration = "customer-device-registration";
}

public sealed class CustomerRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int DeviceRegistrationPermitLimit { get; set; } = 10;
    public int DeviceRegistrationWindowSeconds { get; set; } = 600;
    public int DeviceRegistrationPeerPermitLimit { get; set; } = 30;
    public int DeviceRegistrationPeerWindowSeconds { get; set; } = 600;
    public int AuthAccountPermitLimit { get; set; } = 10;
    public int AuthAccountWindowSeconds { get; set; } = 60;
    public int AuthAggregatePermitLimit { get; set; } = 50;
    public int AuthAggregateWindowSeconds { get; set; } = 300;
    public int DevicePermitLimit { get; set; } = 300;
    public int DeviceWindowSeconds { get; set; } = 60;
    public int BearerMutationPermitLimit { get; set; } = 60;
    public int BearerMutationWindowSeconds { get; set; } = 60;
    public int PaymentIntentPermitLimit { get; set; } = 10;
    public int PaymentIntentWindowSeconds { get; set; } = 60;
    public int PaymentAggregatePermitLimit { get; set; } = 20;
    public int PaymentAggregateWindowSeconds { get; set; } = 60;
    public int ValidPaymentWebhookPermitLimit { get; set; } = 600;
    public int ValidPaymentWebhookWindowSeconds { get; set; } = 60;
    public int InvalidPaymentWebhookPermitLimit { get; set; } = 60;
    public int InvalidPaymentWebhookWindowSeconds { get; set; } = 60;
    public int AnonymousPermitLimit { get; set; } = 60;
    public int AnonymousWindowSeconds { get; set; } = 60;
}

public sealed class CustomerRateLimitOptionsValidator :
    IValidateOptions<CustomerRateLimitOptions>
{
    private readonly IConfiguration? _configuration;

    public CustomerRateLimitOptionsValidator(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(
        string? name,
        CustomerRateLimitOptions options)
    {
        var values = new[]
        {
            options.DeviceRegistrationPermitLimit,
            options.DeviceRegistrationWindowSeconds,
            options.DeviceRegistrationPeerPermitLimit,
            options.DeviceRegistrationPeerWindowSeconds,
            options.AuthAccountPermitLimit,
            options.AuthAccountWindowSeconds,
            options.AuthAggregatePermitLimit,
            options.AuthAggregateWindowSeconds,
            options.DevicePermitLimit,
            options.DeviceWindowSeconds,
            options.BearerMutationPermitLimit,
            options.BearerMutationWindowSeconds,
            options.PaymentIntentPermitLimit,
            options.PaymentIntentWindowSeconds,
            options.PaymentAggregatePermitLimit,
            options.PaymentAggregateWindowSeconds,
            options.ValidPaymentWebhookPermitLimit,
            options.ValidPaymentWebhookWindowSeconds,
            options.InvalidPaymentWebhookPermitLimit,
            options.InvalidPaymentWebhookWindowSeconds,
            options.AnonymousPermitLimit,
            options.AnonymousWindowSeconds
        };
        var failures = new List<string>();
        if (values.Any(value => value <= 0))
        {
            failures.Add("All Customer rate limits and windows must be positive.");
        }

        ValidateForwardedHeaders(failures);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateForwardedHeaders(ICollection<string> failures)
    {
        if (_configuration is null)
        {
            return;
        }

        var proxies = _configuration
            .GetSection("ForwardedHeaders:KnownProxies")
            .Get<string[]>() ?? [];
        if (proxies.Any(value =>
                !IPAddress.TryParse(value, out var address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any)))
        {
            failures.Add(
                "ForwardedHeaders:KnownProxies must contain only specific valid IP addresses.");
        }

        var networks = _configuration
            .GetSection("ForwardedHeaders:KnownNetworks")
            .Get<string[]>() ?? [];
        if (networks.Any(network => !IsValidTrustedNetwork(network)))
        {
            failures.Add(
                "ForwardedHeaders:KnownNetworks must contain valid, bounded CIDR networks.");
        }
    }

    private static bool IsValidTrustedNetwork(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var prefix) ||
            !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        var maximumPrefixLength = prefix.AddressFamily == AddressFamily.InterNetwork
            ? 32
            : prefix.AddressFamily == AddressFamily.InterNetworkV6
                ? 128
                : 0;
        return prefixLength > 0 && prefixLength <= maximumPrefixLength;
    }
}

public sealed class CustomerRateLimitPartitionMiddleware
{
    public const string DeviceInstallationPartitionItemKey =
        "Ghseeli.RateLimit.DeviceInstallation";
    internal const string AuthAccountPartitionItemKey =
        "Ghseeli.RateLimit.AuthAccount";
    internal const string PaymentBookingPartitionItemKey =
        "Ghseeli.RateLimit.PaymentBooking";
    internal const string PaymentWebhookPartitionItemKey =
        "Ghseeli.RateLimit.PaymentWebhook";
    internal const string ValidDeviceTokenPartitionItemKey =
        "Ghseeli.RateLimit.ValidDeviceToken";
    internal const string MachineProblemItemKey =
        "Ghseeli.RateLimit.MachineProblem";
    private const int MaximumInspectedBodyBytes = 65_536;

    private readonly RequestDelegate _next;

    public CustomerRateLimitPartitionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IPaymentWebhookParser paymentWebhookParser,
        IOptionsMonitor<LahzaConfigurationOptions> lahzaOptions,
        IDeviceRegistrationService deviceService)
    {
        await TryAuthenticateBearerAsync(context);
        await TryAuthenticateDeviceAsync(context, deviceService);

        var path = context.Request.Path;
        if (HttpMethods.IsPost(context.Request.Method) &&
            (path.Equals(
                 "/api/v1/devices/register",
                 StringComparison.OrdinalIgnoreCase) ||
             path.Equals(
                 "/api/Auth/register",
                 StringComparison.OrdinalIgnoreCase) ||
             path.Equals(
                 "/api/Auth/login",
                 StringComparison.OrdinalIgnoreCase) ||
             path.Equals(
                 "/api/Auth/validate",
                 StringComparison.OrdinalIgnoreCase) ||
             path.Equals(
                 "/api/v1/payments/intents",
                 StringComparison.OrdinalIgnoreCase)))
        {
            var body = await ReadBoundedBodyAsync(context.Request);
            if (body is not null)
            {
                InspectJsonBody(context, body);
            }
        }
        else if (IsAuthEntry(path))
        {
            var provider = context.Request.Query["provider"].ToString();
            context.Items[AuthAccountPartitionItemKey] =
                HashPartition(string.IsNullOrWhiteSpace(provider)
                    ? "missing"
                    : provider.Trim().ToUpperInvariant());
        }

        if (IsPaymentWebhook(context.Request))
        {
            context.Items[MachineProblemItemKey] = true;
            await ClassifyPaymentWebhookAsync(
                context,
                paymentWebhookParser,
                lahzaOptions.CurrentValue);
        }

        await _next(context);
    }

    private static async Task TryAuthenticateDeviceAsync(
        HttpContext context,
        IDeviceRegistrationService deviceService)
    {
        if (!RequiresDeviceToken(context))
        {
            return;
        }

        var token = context.Request.Headers[DeviceTokenDefaults.HeaderName].ToString();
        if (!DeviceTokenHasher.IsValidFormat(token))
        {
            return;
        }

        var result = await deviceService.AuthenticateAsync(
            token,
            context.RequestAborted);
        if (!result.IsAuthenticated)
        {
            return;
        }

        context.SetDeviceIdentity(
            result.DeviceId!.Value,
            result.InstallationId!.Value);
        context.Items[ValidDeviceTokenPartitionItemKey] = HashPartition(token);
    }

    private static async Task TryAuthenticateBearerAsync(HttpContext context)
    {
        if (!context.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var result = await context.AuthenticateAsync();
        if (result.Succeeded && result.Principal is not null)
        {
            context.User = result.Principal;
        }
    }

    private static void InspectJsonBody(HttpContext context, byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.String &&
                context.Request.Path.Equals(
                    "/api/Auth/validate",
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Items[AuthAccountPartitionItemKey] =
                    HashPartition(root.GetString() ?? "missing");
                return;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (context.Request.Path.Equals(
                    "/api/v1/devices/register",
                    StringComparison.OrdinalIgnoreCase) &&
                TryGetProperty(root, "installationId", out var installationElement) &&
                installationElement.ValueKind == JsonValueKind.String &&
                Guid.TryParse(installationElement.GetString(), out var installationId))
            {
                context.Items[DeviceInstallationPartitionItemKey] =
                    HashPartition(installationId.ToString("N"));
            }

            if (IsAuthEntry(context.Request.Path) &&
                TryGetProperty(root, "email", out var emailElement) &&
                emailElement.ValueKind == JsonValueKind.String)
            {
                var normalized = emailElement.GetString()?.Trim().ToUpperInvariant();
                context.Items[AuthAccountPartitionItemKey] =
                    HashPartition(string.IsNullOrWhiteSpace(normalized)
                        ? "missing"
                        : normalized);
            }

            if (context.Request.Path.Equals(
                    "/api/v1/payments/intents",
                    StringComparison.OrdinalIgnoreCase) &&
                TryGetProperty(root, "bookingId", out var bookingElement) &&
                bookingElement.ValueKind == JsonValueKind.String &&
                Guid.TryParse(bookingElement.GetString(), out var bookingId))
            {
                context.Items[PaymentBookingPartitionItemKey] =
                    HashPartition(bookingId.ToString("N"));
            }
        }

        catch (JsonException)
        {
            // Endpoint model validation owns malformed JSON.
        }
    }

    private static bool TryGetProperty(
        JsonElement root,
        string propertyName,
        out JsonElement value)
    {
        var matches = root.EnumerateObject()
            .Where(property => string.Equals(
                property.Name,
                propertyName,
                StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Value)
            .Take(2)
            .ToArray();
        value = matches.Length == 1 ? matches[0] : default;
        return matches.Length == 1;
    }

    private static async Task ClassifyPaymentWebhookAsync(
        HttpContext context,
        IPaymentWebhookParser parser,
        LahzaConfigurationOptions options)
    {
        context.Items[PaymentWebhookPartitionItemKey] = "invalid";
        var secret = options.SecretKey?.Trim();
        var signature = context.Request.Headers["X-Lahza-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(signature) ||
            !IsJson(context.Request.ContentType))
        {
            return;
        }

        var body = await ReadBoundedBodyAsync(context.Request);
        if (body is null)
        {
            return;
        }

        try
        {
            parser.Parse(body, signature, secret);
            context.Items[PaymentWebhookPartitionItemKey] =
                $"valid:{HashPartition(secret)}";
        }
        catch (CustomerPaymentException)
        {
            // The endpoint repeats authoritative validation and writes its exact problem.
        }
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is > MaximumInspectedBodyBytes or <= 0)
        {
            return null;
        }

        request.EnableBuffering();
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        try
        {
            while (buffer.Length <= MaximumInspectedBodyBytes)
            {
                var remaining = MaximumInspectedBodyBytes + 1 - (int)buffer.Length;
                var read = await request.Body.ReadAsync(
                    bytes.AsMemory(0, Math.Min(bytes.Length, remaining)),
                    request.HttpContext.RequestAborted);
                if (read == 0)
                {
                    return buffer.ToArray();
                }
                await buffer.WriteAsync(
                    bytes.AsMemory(0, read),
                    request.HttpContext.RequestAborted);
            }
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static bool IsAuthEntry(PathString path) =>
        path.Equals("/api/Auth/register", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/Auth/login", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/Auth/validate", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/Auth/external-login", StringComparison.OrdinalIgnoreCase) ||
        path.Equals(
            "/api/Auth/external-login-callback",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPaymentWebhook(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) &&
        request.Path.Equals("/api/lahza/webhook", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresDeviceToken(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(
                "/api/v1",
                StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(
                "/api/v1/internal",
                StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.Equals(
                "/api/v1/devices/register",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var endpoint = context.GetEndpoint();
        return endpoint is not null &&
               !string.Equals(
                   endpoint.DisplayName,
                   "405 HTTP Method Not Supported",
                   StringComparison.Ordinal) &&
               endpoint.Metadata.GetMetadata<AllowWithoutDeviceTokenAttribute>() is null;
    }

    private static bool IsJson(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
               mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static string HashPartition(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

public static class CustomerRateLimitingExtensions
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

    public static IServiceCollection AddCustomerRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configured = configuration
            .GetSection(CustomerRateLimitOptions.SectionName)
            .Get<CustomerRateLimitOptions>() ?? new CustomerRateLimitOptions();

        services.AddSingleton<IValidateOptions<CustomerRateLimitOptions>,
            CustomerRateLimitOptionsValidator>();
        services.AddOptions<CustomerRateLimitOptions>()
            .Bind(configuration.GetSection(CustomerRateLimitOptions.SectionName))
            .ValidateOnStart();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectedAsync;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(
                    context => PrimaryPartition(context, configured)),
                PartitionedRateLimiter.Create<HttpContext, string>(
                    context => AuthAggregatePartition(context, configured)),
                PartitionedRateLimiter.Create<HttpContext, string>(
                    context => StableAggregatePartition(context, configured)));
            options.AddPolicy(
                CustomerRateLimitPolicyNames.DeviceRegistration,
                context => FixedWindow(
                    DeviceRegistrationPartition(context),
                    configured.DeviceRegistrationPermitLimit,
                    configured.DeviceRegistrationWindowSeconds));
        });

        return services;
    }

    private static RateLimitPartition<string> PrimaryPartition(
        HttpContext context,
        CustomerRateLimitOptions options)
    {
        var request = context.Request;
        var path = request.Path;
        if (IsHealthExempt(request) ||
            path.Equals("/api/v1/devices/register", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("exempt");
        }

        var peer = TrustedPeerHash(context);
        if (IsAuthEntry(path))
        {
            var account = Item(
                context,
                CustomerRateLimitPartitionMiddleware.AuthAccountPartitionItemKey);
            var action = CustomerRateLimitPartitionMiddleware.HashPartition(
                path.Value?.ToLowerInvariant() ?? "missing");
            return FixedWindow(
                $"auth:{peer}:{account}:{action}",
                options.AuthAccountPermitLimit,
                options.AuthAccountWindowSeconds);
        }

        if (IsPaymentWebhook(request))
        {
            var webhook = Item(
                context,
                CustomerRateLimitPartitionMiddleware.PaymentWebhookPartitionItemKey);
            return webhook.StartsWith("valid:", StringComparison.Ordinal)
                ? FixedWindow(
                    $"payment-webhook:{webhook}",
                    options.ValidPaymentWebhookPermitLimit,
                    options.ValidPaymentWebhookWindowSeconds)
                : FixedWindow(
                    $"payment-webhook-invalid:{peer}",
                    options.InvalidPaymentWebhookPermitLimit,
                    options.InvalidPaymentWebhookWindowSeconds);
        }

        var deviceHash = DeviceTokenHash(context);
        if (path.Equals(
                "/api/v1/payments/intents",
                StringComparison.OrdinalIgnoreCase) &&
            HttpMethods.IsPost(request.Method))
        {
            var subject = SubjectHash(context);
            var booking = Item(
                context,
                CustomerRateLimitPartitionMiddleware.PaymentBookingPartitionItemKey);
            return FixedWindow(
                $"payment:{subject}:{deviceHash}:{booking}",
                options.PaymentIntentPermitLimit,
                options.PaymentIntentWindowSeconds);
        }

        if (IsDeviceRoute(path))
        {
            return FixedWindow(
                $"device:{deviceHash}:{RouteFamily(request)}",
                options.DevicePermitLimit,
                options.DeviceWindowSeconds);
        }

        if (IsMutation(request) && context.User.Identity?.IsAuthenticated == true)
        {
            return FixedWindow(
                $"bearer:{SubjectHash(context)}:{deviceHash}:{RouteFamily(request)}",
                options.BearerMutationPermitLimit,
                options.BearerMutationWindowSeconds);
        }

        return FixedWindow(
            $"anonymous:{peer}:{RouteFamily(request)}",
            options.AnonymousPermitLimit,
            options.AnonymousWindowSeconds);
    }

    private static RateLimitPartition<string> AuthAggregatePartition(
        HttpContext context,
        CustomerRateLimitOptions options) =>
        IsAuthEntry(context.Request.Path)
            ? FixedWindow(
                $"auth-aggregate:{TrustedPeerHash(context)}",
                options.AuthAggregatePermitLimit,
                options.AuthAggregateWindowSeconds)
            : RateLimitPartition.GetNoLimiter("not-auth");

    private static RateLimitPartition<string> StableAggregatePartition(
        HttpContext context,
        CustomerRateLimitOptions options)
    {
        var request = context.Request;
        if (request.Path.Equals(
                "/api/v1/devices/register",
                StringComparison.OrdinalIgnoreCase))
        {
            return FixedWindow(
                $"registration-peer-aggregate:{TrustedPeerHash(context)}",
                options.DeviceRegistrationPeerPermitLimit,
                options.DeviceRegistrationPeerWindowSeconds);
        }

        if (request.Path.Equals(
                "/api/v1/payments/intents",
                StringComparison.OrdinalIgnoreCase) &&
            HttpMethods.IsPost(request.Method))
        {
            return FixedWindow(
                $"payment-aggregate:{SubjectHash(context)}:{DeviceTokenHash(context)}",
                options.PaymentAggregatePermitLimit,
                options.PaymentAggregateWindowSeconds);
        }

        return RateLimitPartition.GetNoLimiter("not-stable-aggregate");
    }

    private static RateLimitPartition<string> FixedWindow(
        string partition,
        int permitLimit,
        int windowSeconds) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partition,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });

    private static string DeviceRegistrationPartition(HttpContext context)
    {
        var installation = Item(
            context,
            CustomerRateLimitPartitionMiddleware.DeviceInstallationPartitionItemKey);
        return installation == "missing"
            ? $"registration-peer:{TrustedPeerHash(context)}"
            : $"registration-installation:{installation}";
    }

    private static string TrustedPeerHash(HttpContext context)
    {
        var peer = context.Connection.RemoteIpAddress?
            .MapToIPv6()
            .ToString() ?? "missing";
        return CustomerRateLimitPartitionMiddleware.HashPartition(peer);
    }

    private static string DeviceTokenHash(HttpContext context)
    {
        if (context.Items.TryGetValue(
                CustomerRateLimitPartitionMiddleware.ValidDeviceTokenPartitionItemKey,
                out var value) &&
            value is string tokenHash)
        {
            return tokenHash;
        }

        return $"peer:{TrustedPeerHash(context)}";
    }

    private static string SubjectHash(HttpContext context)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? "missing";
        return CustomerRateLimitPartitionMiddleware.HashPartition(subject);
    }

    private static string Item(HttpContext context, string key) =>
        context.Items.TryGetValue(key, out var value) && value is string text
            ? text
            : "missing";

    private static bool IsHealthExempt(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) &&
        request.Path.Equals("/api/Health", StringComparison.OrdinalIgnoreCase) ||
        HttpMethods.IsGet(request.Method) &&
        request.Path.Equals("/api/Health/db", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthEntry(PathString path) =>
        path.Equals("/api/Auth/register", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/Auth/login", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/Auth/validate", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/Auth/external-login", StringComparison.OrdinalIgnoreCase) ||
        path.Equals(
            "/api/Auth/external-login-callback",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPaymentWebhook(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) &&
        request.Path.Equals("/api/lahza/webhook", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeviceRoute(PathString path) =>
        path.StartsWithSegments("/api/v1/configuration", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/v1/catalog", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/v1/checkout", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/v1/pricing", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/v1/payments", StringComparison.OrdinalIgnoreCase);

    private static bool IsMutation(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) ||
        HttpMethods.IsPut(request.Method) ||
        HttpMethods.IsPatch(request.Method) ||
        HttpMethods.IsDelete(request.Method);

    private static string RouteFamily(HttpRequest request)
    {
        var segments = (request.Path.Value ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var family = segments.Length switch
        {
            >= 3 when string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(segments[1], "v1", StringComparison.OrdinalIgnoreCase) =>
                segments[2],
            >= 2 when string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) =>
                segments[1],
            _ => segments.FirstOrDefault() ?? "root"
        };
        return $"{request.Method.ToUpperInvariant()}:{family.ToLowerInvariant()}";
    }

    private static async ValueTask WriteRejectedAsync(
        OnRejectedContext rejected,
        CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        var machine = context.Items.TryGetValue(
                CustomerRateLimitPartitionMiddleware.MachineProblemItemKey,
                out var machineValue) &&
            machineValue is true;
        var code = "rate_limit_exceeded";
        var retryAfter = rejected.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out var delay)
            ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))
            : 1;

        object payload;
        if (machine)
        {
            payload = new
            {
                type = $"https://api.ghseeli.example/errors/{code}",
                title = "Too many requests.",
                status = StatusCodes.Status429TooManyRequests,
                detail = "The request rate limit was exceeded. Retry after the indicated delay.",
                code,
                correlationId = context.TraceIdentifier
            };
            context.Response.Headers.Remove("Content-Language");
        }
        else
        {
            var language = ConfigurationLanguageResolver.Resolve(
                context.Request.Query.ContainsKey("language")
                    ? context.Request.Query["language"].ToString()
                    : null,
                context.Request.Headers.AcceptLanguage.ToString());
            var isHebrew = language == ConfigurationLanguageResolver.Hebrew;
            payload = new
            {
                type = $"https://api.ghseeli.example/errors/{code}",
                title = isHebrew ? "לא ניתן להשלים את הבקשה." : "تعذر إكمال الطلب.",
                status = StatusCodes.Status429TooManyRequests,
                detail = isHebrew
                    ? "חרגת ממגבלת הבקשות. נסה שוב מאוחר יותר."
                    : "تم تجاوز حد الطلبات. حاول مرة أخرى لاحقًا.",
                code,
                correlationId = context.TraceIdentifier,
                language
            };
            context.Response.Headers.ContentLanguage = language;
            context.Response.Headers.Append("Vary", "Accept-Language");
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.RetryAfter = retryAfter.ToString();
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);
    }
}
