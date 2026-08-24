using Ghseeli.Common.Logging;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using Microsoft.EntityFrameworkCore;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GhseeliApis.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWithoutDeviceTokenAttribute : Attribute;

public static class DeviceHttpContextExtensions
{
    private const string DeviceIdKey = "Ghseeli.DeviceId";
    private const string InstallationIdKey = "Ghseeli.InstallationId";

    public static Guid? GetDeviceId(this HttpContext context) =>
        context.Items.TryGetValue(DeviceIdKey, out var value) && value is Guid id ? id : null;

    public static Guid? GetInstallationId(this HttpContext context) =>
        context.Items.TryGetValue(InstallationIdKey, out var value) && value is Guid id ? id : null;

    internal static void SetDeviceIdentity(
        this HttpContext context,
        Guid deviceId,
        Guid installationId)
    {
        context.Items[DeviceIdKey] = deviceId;
        context.Items[InstallationIdKey] = installationId;
    }
}

public sealed class DeviceTokenMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    private readonly RequestDelegate _next;
    private readonly IAppLogger _logger;

    public DeviceTokenMiddleware(RequestDelegate next, IAppLogger logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IDeviceRegistrationService deviceService)
    {
        if (!RequiresDeviceToken(context))
        {
            await _next(context);
            return;
        }

        var token = context.Request.Headers[DeviceTokenDefaults.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            await WriteProblemAsync(context, DeviceProblemCodes.TokenMissing);
            return;
        }

        if (!DeviceTokenHasher.IsValidFormat(token))
        {
            await WriteProblemAsync(context, DeviceProblemCodes.TokenInvalid);
            return;
        }

        var result = await deviceService.AuthenticateAsync(token, context.RequestAborted);
        if (!result.IsAuthenticated)
        {
            await WriteProblemAsync(
                context,
                result.Code ?? DeviceProblemCodes.TokenInvalid);
            return;
        }

        context.SetDeviceIdentity(result.DeviceId!.Value, result.InstallationId!.Value);

        try
        {
            await deviceService.UpdateLastSeenAsync(result.DeviceId.Value, context.RequestAborted);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                $"Unable to update last-seen metadata for device {result.DeviceId.Value}: {exception.GetType().Name}");
        }

        await _next(context);
    }

    private static bool RequiresDeviceToken(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (context.Request.Path.StartsWithSegments(
                "/api/v1/internal",
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

    private static Task WriteProblemAsync(HttpContext context, string code)
    {
        var explicitLanguage = context.Request.Query.ContainsKey("language")
            ? context.Request.Query["language"].ToString()
            : null;
        var language = explicitLanguage is not null
            ? ConfigurationLanguageResolver.Resolve(explicitLanguage, null)
            : ConfigurationLanguageResolver.Resolve(
                null,
                context.Request.Headers.AcceptLanguage.ToString());
        var payload = DeviceProblemDetailsFactory.Create(
            StatusCodes.Status401Unauthorized,
            code,
            language,
            context.TraceIdentifier);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
