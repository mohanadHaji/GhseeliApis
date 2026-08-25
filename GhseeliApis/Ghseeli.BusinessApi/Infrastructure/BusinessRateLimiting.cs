using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace Ghseeli.BusinessApi.Infrastructure;

public sealed class BusinessRateLimitingOptions
{
    public const string SectionName = "BusinessRateLimiting";

    public int QueueLimit { get; set; }
    public string MissingPartitionKey { get; set; } = "unavailable";
    public BusinessRateLimitPolicyOptions BusinessAuth { get; set; } = new(10, 60);
    public BusinessRateLimitPolicyOptions BusinessAuthAggregate { get; set; } = new(50, 300);
    public BusinessRateLimitPolicyOptions BusinessReads { get; set; } = new(300, 60);
    public BusinessRateLimitPolicyOptions BusinessMutations { get; set; } = new(120, 60);
    public BusinessRateLimitPolicyOptions ValidInternal { get; set; } = new(600, 60);
    public BusinessRateLimitPolicyOptions InvalidInternal { get; set; } = new(60, 60);
    public BusinessRateLimitPolicyOptions OtherAnonymous { get; set; } = new(60, 60);
}

public sealed class BusinessRateLimitPolicyOptions
{
    public BusinessRateLimitPolicyOptions()
    {
    }

    public BusinessRateLimitPolicyOptions(int permitLimit, int windowSeconds)
    {
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
    }

    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

public sealed class BusinessRateLimitingOptionsValidator :
    IValidateOptions<BusinessRateLimitingOptions>
{
    private readonly IConfiguration _configuration;

    public BusinessRateLimitingOptionsValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, BusinessRateLimitingOptions options)
    {
        var failures = new List<string>();
        if (options.QueueLimit != 0)
        {
            failures.Add($"{BusinessRateLimitingOptions.SectionName}:QueueLimit must be zero.");
        }

        if (string.IsNullOrWhiteSpace(options.MissingPartitionKey) ||
            options.MissingPartitionKey.Length > 64)
        {
            failures.Add(
                $"{BusinessRateLimitingOptions.SectionName}:MissingPartitionKey must contain 1-64 characters.");
        }

        ValidatePolicy(options.BusinessAuth, "BusinessAuth", failures);
        ValidatePolicy(options.BusinessAuthAggregate, "BusinessAuthAggregate", failures);
        ValidatePolicy(options.BusinessReads, "BusinessReads", failures);
        ValidatePolicy(options.BusinessMutations, "BusinessMutations", failures);
        ValidatePolicy(options.ValidInternal, "ValidInternal", failures);
        ValidatePolicy(options.InvalidInternal, "InvalidInternal", failures);
        ValidatePolicy(options.OtherAnonymous, "OtherAnonymous", failures);
        ValidateForwardedHeaders(failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePolicy(
        BusinessRateLimitPolicyOptions? policy,
        string policyName,
        ICollection<string> failures)
    {
        if (policy is null || policy.PermitLimit <= 0 || policy.WindowSeconds <= 0)
        {
            failures.Add(
                $"{BusinessRateLimitingOptions.SectionName}:{policyName} must use a positive permit limit and window.");
        }
    }

    private void ValidateForwardedHeaders(ICollection<string> failures)
    {
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

public sealed class BusinessRateLimitPartitionMiddleware
{
    internal const string AccountPartitionItemKey = "Ghseeli.RateLimit.Account";
    private const long MaximumAuthBodyBytes = 65_536;
    private readonly RequestDelegate _next;

    public BusinessRateLimitPartitionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<BusinessRateLimitingOptions> optionsAccessor)
    {
        if (BusinessRateLimitPartitioner.IsBusinessAuthRequest(context))
        {
            context.Items[AccountPartitionItemKey] = await ReadAccountPartitionAsync(
                context,
                optionsAccessor.Value.MissingPartitionKey);
        }

        await _next(context);
    }

    private static async Task<string> ReadAccountPartitionAsync(
        HttpContext context,
        string missingPartitionKey)
    {
        if (context.Request.ContentLength is > MaximumAuthBodyBytes)
        {
            return missingPartitionKey;
        }

        context.Request.EnableBuffering(
            (int)MaximumAuthBodyBytes + 1,
            MaximumAuthBodyBytes + 1);
        try
        {
            context.Request.Body.Position = 0;
            using var document = await JsonDocument.ParseAsync(
                context.Request.Body,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16
                },
                context.RequestAborted);
            var emailMatches = document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement
                    .EnumerateObject()
                    .Where(property =>
                        property.Name.Equals("email", StringComparison.OrdinalIgnoreCase))
                    .Select(property => property.Value)
                    .Take(2)
                    .ToArray()
                : [];
            var emailElement = emailMatches.Length == 1
                ? emailMatches[0]
                : default;
            if (emailElement.ValueKind != JsonValueKind.String)
            {
                return missingPartitionKey;
            }

            var normalized = emailElement.GetString()?.Trim().ToUpperInvariant();
            return string.IsNullOrWhiteSpace(normalized)
                ? missingPartitionKey
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
                    .ToLowerInvariant();
        }
        catch (JsonException)
        {
            return missingPartitionKey;
        }
        finally
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }
        }
    }
}

public static class BusinessRateLimitPartitioner
{
    public static RateLimitPartition<string> GetPrimaryPartition(HttpContext context)
    {
        var options = context.RequestServices
            .GetRequiredService<IOptions<BusinessRateLimitingOptions>>().Value;

        if (IsExemptHealthRequest(context))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        if (context.Request.Path.StartsWithSegments(
                "/api/v1/internal",
                StringComparison.OrdinalIgnoreCase))
        {
            return GetInternalPartition(context, options);
        }

        if (IsBusinessAuthRequest(context))
        {
            var account = context.Items.TryGetValue(
                    BusinessRateLimitPartitionMiddleware.AccountPartitionItemKey,
                    out var value)
                ? value?.ToString()
                : null;
            return Fixed(
                $"business-auth:{ClientIp(context, options)}:{account ?? options.MissingPartitionKey}:{RouteFamily(context)}",
                options.BusinessAuth,
                options.QueueLimit);
        }

        if (context.User.Identity?.IsAuthenticated == true &&
            context.Request.Path.StartsWithSegments(
                "/api/v1/business",
                StringComparison.OrdinalIgnoreCase))
        {
            var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub")
                ?? options.MissingPartitionKey;
            var company = context.User.FindFirstValue(BusinessClaimTypes.CompanyId)
                ?? options.MissingPartitionKey;
            var policy = HttpMethods.IsGet(context.Request.Method) ||
                         HttpMethods.IsHead(context.Request.Method)
                ? options.BusinessReads
                : options.BusinessMutations;
            return Fixed(
                $"business:{subject}:{company}:{RouteFamily(context)}",
                policy,
                options.QueueLimit);
        }

        return Fixed(
            $"anonymous:{ClientIp(context, options)}:{RouteFamily(context)}",
            options.OtherAnonymous,
            options.QueueLimit);
    }

    public static RateLimitPartition<string> GetAuthenticationAggregatePartition(
        HttpContext context)
    {
        if (!IsBusinessAuthRequest(context))
        {
            return RateLimitPartition.GetNoLimiter("not-business-auth");
        }

        var options = context.RequestServices
            .GetRequiredService<IOptions<BusinessRateLimitingOptions>>().Value;
        return Fixed(
            $"business-auth-aggregate:{ClientIp(context, options)}",
            options.BusinessAuthAggregate,
            options.QueueLimit);
    }

    internal static bool IsBusinessAuthRequest(HttpContext context)
    {
        return HttpMethods.IsPost(context.Request.Method) &&
               context.Request.Path.StartsWithSegments(
                   "/api/v1/business/auth",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static RateLimitPartition<string> GetInternalPartition(
        HttpContext context,
        BusinessRateLimitingOptions options)
    {
        var serviceId = context.User.FindFirstValue(BusinessClaimTypes.InternalServiceId);
        var operation = context.GetEndpoint()?
            .Metadata
            .GetMetadata<InternalServiceOperationAttribute>()?
            .Operation;
        if (!string.IsNullOrWhiteSpace(serviceId) &&
            !string.IsNullOrWhiteSpace(operation))
        {
            return Fixed(
                $"internal-valid:{serviceId}:{operation}",
                options.ValidInternal,
                options.QueueLimit);
        }

        return Fixed(
            $"internal-invalid:{ClientIp(context, options)}",
            options.InvalidInternal,
            options.QueueLimit);
    }

    private static RateLimitPartition<string> Fixed(
        string key,
        BusinessRateLimitPolicyOptions policy,
        int queueLimit)
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = policy.PermitLimit,
                QueueLimit = queueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = TimeSpan.FromSeconds(policy.WindowSeconds)
            });
    }

    private static bool IsExemptHealthRequest(HttpContext context)
    {
        return context.Request.Path.Equals(
                   "/api/health",
                   StringComparison.OrdinalIgnoreCase) &&
               (HttpMethods.IsGet(context.Request.Method) ||
                HttpMethods.IsHead(context.Request.Method));
    }

    private static string ClientIp(
        HttpContext context,
        BusinessRateLimitingOptions options)
    {
        return context.Connection.RemoteIpAddress?.ToString()
            ?? options.MissingPartitionKey;
    }

    private static string RouteFamily(HttpContext context)
    {
        var routeTemplate = context.GetEndpoint()?
            .Metadata
            .GetMetadata<ControllerActionDescriptor>()?
            .AttributeRouteInfo?
            .Template;
        if (!string.IsNullOrWhiteSpace(routeTemplate))
        {
            return routeTemplate.ToLowerInvariant();
        }

        var segments = context.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Take(5)
            .Select(segment => Guid.TryParse(segment, out _) ? "{id}" : segment.ToLowerInvariant())
            .ToArray() ?? [];
        return segments.Length == 0 ? "root" : string.Join('/', segments);
    }
}

public static class BusinessRateLimitingResponse
{
    public static async ValueTask WriteAsync(
        OnRejectedContext rejectedContext,
        CancellationToken cancellationToken)
    {
        var context = rejectedContext.HttpContext;
        var retryAfterSeconds = 1;
        if (rejectedContext.Lease.TryGetMetadata(
                MetadataName.RetryAfter,
                out var retryAfter))
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        context.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (context.Request.Path.StartsWithSegments(
                "/api/v1/internal",
                StringComparison.OrdinalIgnoreCase))
        {
            await InternalServiceProblemResponseFactory.WriteAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                "Too many requests.",
                "The request rate limit was exceeded. Retry after the indicated delay.",
                "rate_limit_exceeded");
            return;
        }

        var language = BusinessLanguage.TryResolve(context.Request, out var resolvedLanguage)
            ? resolvedLanguage
            : "ar";
        await BusinessProblemCatalog.WriteAsync(
            context,
            StatusCodes.Status429TooManyRequests,
            "rate_limit_exceeded",
            language);
    }
}
