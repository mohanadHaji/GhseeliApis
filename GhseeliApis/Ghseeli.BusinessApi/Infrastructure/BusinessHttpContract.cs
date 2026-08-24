using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using Ghseeli.BusinessApi.InternalServices;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Infrastructure;

internal static class BusinessLanguage
{
    public const string ItemKey = "BusinessLanguage";

    public static bool TryResolve(HttpRequest request, out string language)
    {
        language = "ar";
        if (request.Query.ContainsKey("language"))
        {
            var values = request.Query["language"];
            if (values.Count != 1)
            {
                return false;
            }

            var value = values[0]?.Trim();
            if (string.Equals(value, "ar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "he", StringComparison.OrdinalIgnoreCase))
            {
                language = value!.ToLowerInvariant();
                return true;
            }

            return false;
        }

        var header = request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return true;
        }

        try
        {
            var candidates = new List<(string Language, decimal Quality, int Order)>();
            var order = 0;
            foreach (var part in header.Split(',', StringSplitOptions.TrimEntries))
            {
                var sections = part.Split(';', StringSplitOptions.TrimEntries);
                var range = sections[0];
                decimal quality = 1;
                if (sections.Length > 2 ||
                    sections.Skip(1).Any(section =>
                    {
                        if (!section.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
                            return true;
                        return !decimal.TryParse(
                            section[2..],
                            System.Globalization.NumberStyles.AllowDecimalPoint,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out quality);
                    }))
                {
                    return true;
                }

                if (quality is <= 0 or > 1)
                {
                    order++;
                    continue;
                }

                var normalized = range.Split('-')[0].ToLowerInvariant();
                if (normalized is "ar" or "he")
                {
                    candidates.Add((normalized, quality, order));
                }
                order++;
            }

            var selected = candidates
                .OrderByDescending(candidate => candidate.Quality)
                .ThenBy(candidate => candidate.Order)
                .FirstOrDefault();
            if (selected.Language is not null)
            {
                language = selected.Language;
            }
        }
        catch
        {
            language = "ar";
        }

        return true;
    }
}

internal static class BusinessProblemCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly IReadOnlyDictionary<string, (string Arabic, string Hebrew)> Details =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["language_invalid"] = ("اللغة المطلوبة غير مدعومة.", "השפה המבוקשת אינה נתמכת."),
            ["request_invalid"] = ("الطلب غير صالح.", "הבקשה אינה חוקית."),
            ["business_authentication_required"] = ("مطلوب تسجيل دخول حساب العمل.", "נדרש אימות לחשבון העסקי."),
            ["business_authorization_forbidden"] = ("لا يملك حساب العمل صلاحية تنفيذ هذا الطلب.", "לחשבון העסקי אין הרשאה לבצע בקשה זו."),
            ["resource_not_found"] = ("المورد المطلوب غير موجود.", "המשאב המבוקש לא נמצא."),
            ["method_not_allowed"] = ("طريقة HTTP غير مسموحة لهذا المسار.", "שיטת HTTP אינה מותרת עבור נתיב זה."),
            ["request_conflict"] = ("يتعارض الطلب مع الحالة الحالية.", "הבקשה מתנגשת עם המצב הנוכחי."),
            ["BOOKING_STATUS_INVALID"] = ("طلب تغيير حالة أمر العمل غير صالح.", "בקשת שינוי סטטוס הזמנת העבודה אינה חוקית."),
            ["BOOKING_TRANSITION_INVALID"] = ("لا يمكن تنفيذ تغيير حالة أمر العمل المطلوب.", "לא ניתן לבצע את שינוי סטטוס הזמנת העבודה המבוקש."),
            ["BOOKING_TRANSITION_CONFLICT"] = ("تم تعديل أمر العمل. حدّث البيانات وأعد المحاولة.", "הזמנת העבודה השתנתה. יש לרענן ולנסות שוב."),
            ["booking_status_event_not_found"] = ("لم يتم العثور على حدث حالة الحجز.", "אירוע סטטוס ההזמנה לא נמצא."),
            ["booking_status_event_not_requeueable"] = ("لا يمكن إعادة الحدث في حالته الحالية.", "לא ניתן להחזיר את האירוע במצבו הנוכחי."),
            ["booking_status_requeue_idempotency_key_invalid"] = ("مفتاح طلب إعادة الإرسال مطلوب ويجب ألا يتجاوز 128 حرفاً.", "נדרש מפתח בקשת החזרה שאורכו אינו עולה על 128 תווים."),
            ["request_body_too_large"] = ("حجم نص الطلب يتجاوز الحد المسموح.", "גוף הבקשה חורג מהמגבלה המותרת."),
            ["unsupported_media_type"] = ("نوع محتوى الطلب غير مدعوم.", "סוג התוכן של הבקשה אינו נתמך."),
            ["unexpected_error"] = ("حدث خطأ غير متوقع.", "אירעה שגיאה בלתי צפויה."),
            ["service_unavailable"] = ("الخدمة غير متاحة مؤقتًا.", "השירות אינו זמין זמנית.")
        };

    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string language,
        SortedDictionary<string, string[]>? fieldErrors = null)
    {
        var detail = Details.TryGetValue(code, out var localized)
            ? language == "he" ? localized.Hebrew : localized.Arabic
            : language == "he" ? "לא ניתן להשלים את הבקשה." : "تعذر إكمال الطلب.";
        var payload = new Dictionary<string, object?>
        {
            ["type"] = $"https://api.ghseeli.example/errors/{code}",
            ["title"] = language == "he" ? "לא ניתן להשלים את הבקשה." : "تعذر إكمال الطلب.",
            ["status"] = status,
            ["detail"] = detail,
            ["code"] = code,
            ["correlationId"] = context.TraceIdentifier,
            ["language"] = language
        };
        if (fieldErrors is not null)
        {
            payload["fieldErrors"] = fieldErrors;
        }

        var allow = context.Response.Headers.Allow.ToString();
        var wwwAuthenticate = context.Response.Headers.WWWAuthenticate.ToString();
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentLanguage = language;
        context.Response.Headers.Append("Vary", "Accept-Language");
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=()";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        if (!string.IsNullOrWhiteSpace(allow))
        {
            context.Response.Headers.Allow = allow;
        }
        if (!string.IsNullOrWhiteSpace(wwwAuthenticate))
        {
            context.Response.Headers.WWWAuthenticate = wwwAuthenticate;
        }
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static bool IsGeneric(string code) => Details.ContainsKey(code);
}

public sealed class BusinessHttpContractMiddleware
{
    private const long MaximumBodyBytes = 65_536;
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    public BusinessHttpContractMiddleware(
        RequestDelegate next,
        IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        AddSecurityHeaders(context, _environment);
        var isInternal = context.Request.Path.StartsWithSegments(
            "/api/v1/internal", StringComparison.OrdinalIgnoreCase);
        var isSwagger = context.Request.Path.StartsWithSegments(
            "/swagger", StringComparison.OrdinalIgnoreCase);
        var isHealth =
            context.Request.Path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.Equals("/api/health/", StringComparison.OrdinalIgnoreCase);
        var isLocalized = !isInternal && !isSwagger && !isHealth;

        if (isSwagger)
        {
            context.Response.Headers.CacheControl = "no-store";
            await RewriteSwaggerAsync(context);
            return;
        }

        if (!isLocalized)
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                await _next(context);
            }
            catch (Exception exception) when (
                isInternal && exception is not OperationCanceledException)
            {
                await InternalServiceProblemResponseFactory.WriteAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Internal service request failed.",
                    "The internal service request could not be completed.",
                    "internal_unexpected_error");
            }
            return;
        }

        if (!BusinessLanguage.TryResolve(context.Request, out var language))
        {
            await BusinessProblemCatalog.WriteAsync(
                context, StatusCodes.Status400BadRequest, "language_invalid", "ar");
            return;
        }
        context.Items[BusinessLanguage.ItemKey] = language;

        if (context.Request.ContentLength > MaximumBodyBytes)
        {
            await BusinessProblemCatalog.WriteAsync(
                context, StatusCodes.Status413PayloadTooLarge,
                "request_body_too_large", language);
            return;
        }
        if ((HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)) &&
            context.Request.ContentLength is null &&
            !context.Request.Path.Value!.EndsWith("/requeue", StringComparison.OrdinalIgnoreCase))
        {
            var bufferedBody = await ReadBoundedBodyAsync(
                context.Request.Body,
                MaximumBodyBytes,
                context.RequestAborted);
            if (bufferedBody.IsTooLarge)
            {
                await BusinessProblemCatalog.WriteAsync(
                    context, StatusCodes.Status413PayloadTooLarge,
                    "request_body_too_large", language);
                return;
            }
            if (bufferedBody.Bytes.Length == 0)
            {
                await BusinessProblemCatalog.WriteAsync(
                    context, StatusCodes.Status400BadRequest, "request_invalid", language,
                    new SortedDictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["request"] = [BusinessFieldErrors.GenericMessage(language)]
                    });
                return;
            }

            var replayableBody = new MemoryStream(bufferedBody.Bytes, writable: false);
            context.Request.Body = replayableBody;
            context.Response.RegisterForDispose(replayableBody);
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        Exception? failure = null;
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        context.Response.Body = originalBody;
        if (failure is not null)
        {
            var unavailable = failure is HttpRequestException or DbUpdateException;
            await BusinessProblemCatalog.WriteAsync(
                context,
                unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError,
                unavailable ? "service_unavailable" : "unexpected_error",
                language);
            return;
        }

        if (context.Response.StatusCode >= 400)
        {
            var code = ExtractExistingCode(buffer) ?? StatusCode(context.Response.StatusCode);
            var fields = context.Response.StatusCode == StatusCodes.Status400BadRequest
                ? BusinessFieldErrors.Extract(buffer, language)
                : null;
            await BusinessProblemCatalog.WriteAsync(
                context, context.Response.StatusCode, code, language, fields);
            return;
        }

        if (isHealth)
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
            return;
        }

        context.Response.Headers.ContentLanguage = language;
        context.Response.Headers.Append("Vary", "Accept-Language");
        context.Response.Headers.CacheControl = "no-store";
        buffer.Position = 0;
        if (context.Request.Method == HttpMethods.Get &&
            context.Response.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var json = await JsonNode.ParseAsync(buffer);
            if (json is not null)
            {
                Localize(json, language);
                context.Response.ContentLength = null;
                await context.Response.WriteAsync(json.ToJsonString(new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }));
                return;
            }
        }
        await buffer.CopyToAsync(originalBody);
    }

    private async Task RewriteSwaggerAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        await _next(context);
        context.Response.Body = originalBody;
        if (context.Response.StatusCode != StatusCodes.Status200OK ||
            !context.Request.Path.Value!.EndsWith("swagger.json", StringComparison.OrdinalIgnoreCase))
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
            return;
        }

        buffer.Position = 0;
        var document = await JsonNode.ParseAsync(buffer);
        if (document?["paths"] is JsonObject paths)
        {
            foreach (var path in paths)
            {
                if (path.Value is not JsonObject methods) continue;
                foreach (var method in methods)
                {
                    if (method.Value is not JsonObject operation ||
                        method.Key is not ("get" or "post" or "put" or "delete" or "patch"))
                        continue;
                    operation["security"] ??= new JsonArray();
                    if (operation["parameters"] is JsonArray parameters)
                    {
                        foreach (var parameter in parameters.OfType<JsonObject>())
                        {
                            parameter["required"] ??= false;
                        }
                    }
                }
            }
        }

        context.Response.ContentLength = null;
        await context.Response.WriteAsync(document!.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        }));
    }

    private static void AddSecurityHeaders(
        HttpContext context,
        IWebHostEnvironment environment)
    {
        context.Response.OnStarting(() =>
        {
            var isSwagger = context.Request.Path.StartsWithSegments(
                "/swagger", StringComparison.OrdinalIgnoreCase);
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] = isSwagger
                ? "camera=(), microphone=(), geolocation=()"
                : "camera=(), microphone=(), geolocation=(), payment=()";
            context.Response.Headers["Content-Security-Policy"] =
                isSwagger
                    ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'none'"
                    : "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            if (environment.IsProduction() && context.Request.IsHttps)
            {
                context.Response.Headers.StrictTransportSecurity =
                    "max-age=2592000";
            }
            return Task.CompletedTask;
        });
    }

    private static async Task<(byte[] Bytes, bool IsTooLarge)> ReadBoundedBodyAsync(
        Stream body,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream((int)maximumBytes);
        var chunk = new byte[8192];
        while (buffer.Length <= maximumBytes)
        {
            var remaining = maximumBytes + 1 - buffer.Length;
            var read = await body.ReadAsync(
                chunk.AsMemory(0, (int)Math.Min(chunk.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                return (buffer.ToArray(), false);
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return ([], true);
    }

    private static string StatusCode(int status) => status switch
    {
        400 => "request_invalid",
        401 => "business_authentication_required",
        403 => "business_authorization_forbidden",
        404 => "resource_not_found",
        405 => "method_not_allowed",
        409 => "request_conflict",
        413 => "request_body_too_large",
        415 => "unsupported_media_type",
        503 => "service_unavailable",
        _ => "unexpected_error"
    };

    private static string? ExtractExistingCode(MemoryStream buffer)
    {
        try
        {
            buffer.Position = 0;
            using var document = JsonDocument.Parse(buffer);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeValue)
                ? codeValue.GetString()
                : null;
            return code;
        }
        catch
        {
            return null;
        }
    }

    private static void Localize(JsonNode node, string language)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null) Localize(child, language);
            return;
        }
        if (node is not JsonObject value) return;

        foreach (var child in value.ToList())
            if (child.Value is not null) Localize(child.Value, language);

        foreach (var arabic in value.ToList().Where(item =>
                     item.Key.EndsWith("Ar", StringComparison.Ordinal) &&
                     item.Value is JsonValue))
        {
            var stem = arabic.Key[..^2];
            var hebrewKey = stem + "He";
            if (!value.ContainsKey(hebrewKey)) continue;
            if (language == "he")
            {
                var hebrew = value[hebrewKey]?.GetValue<string?>();
                if (string.IsNullOrWhiteSpace(hebrew))
                    value[hebrewKey] = arabic.Value?.DeepClone();
                value.Remove(arabic.Key);
            }
            else
            {
                value.Remove(hebrewKey);
            }
        }
    }
}

internal static class BusinessFieldErrors
{
    public static string GenericMessage(string language) =>
        language == "he" ? "הערך אינו חוקי." : "القيمة غير صالحة.";

    public static SortedDictionary<string, string[]> Extract(
        MemoryStream buffer, string language)
    {
        var result = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        try
        {
            buffer.Position = 0;
            using var document = JsonDocument.Parse(buffer);
            var root = document.RootElement;
            if (!root.TryGetProperty("errors", out var errors) &&
                !root.TryGetProperty("fieldErrors", out errors))
            {
                result["request"] = [GenericMessage(language)];
                return result;
            }
            foreach (var property in errors.EnumerateObject())
            {
                var key = NormalizePath(property.Name);
                result[key] = [GenericMessage(language)];
            }

        }
        catch
        {
            result["request"] = [GenericMessage(language)];
        }
        return result;
    }

    private static string NormalizePath(string value) =>
        string.Join(
            '.',
            value.Trim().TrimStart('$', '.')
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => string.IsNullOrEmpty(segment)
                    ? segment
                    : char.ToLowerInvariant(segment[0]) + segment[1..]))
        is { Length: > 0 } normalized ? normalized : "request";
}
