using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ghseeli.BusinessApi.Swagger;

public sealed class BusinessProblemDetails
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public int Status { get; init; }
    public required string Detail { get; init; }
    public required string Code { get; init; }
    public required string CorrelationId { get; init; }
    public string? Language { get; init; }
    public Dictionary<string, string[]>? FieldErrors { get; init; }
}

public sealed class BusinessOperationFilter : IOperationFilter
{
    private static readonly string[] HmacSchemes =
        ["HmacServiceId", "HmacTimestamp", "HmacNonce", "HmacSignature"];
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var descriptor = context.ApiDescription.ActionDescriptor as ControllerActionDescriptor;
        var path = "/" + context.ApiDescription.RelativePath?.Split('?')[0];
        var method = context.ApiDescription.HttpMethod ?? "GET";
        operation.OperationId ??= descriptor is null
            ? $"{method}_{path.Trim('/').Replace('/', '_')}"
            : $"{descriptor.ControllerName}_{descriptor.ActionName}";
        operation.Summary ??= descriptor?.MethodInfo.Name ?? $"{method} {path}";
        operation.Description ??= Describe(path);
        operation.Tags ??= [new OpenApiTag { Name = Tag(path) }];

        operation.Security = [];
        if (path.StartsWith("/api/v1/internal/", StringComparison.Ordinal))
        {
            operation.Security.Add(Requirement(HmacSchemes));
        }
        else if (path.StartsWith("/api/v1/business/", StringComparison.Ordinal) &&
                 !path.Contains("/auth/", StringComparison.Ordinal))
        {
            operation.Security.Add(Requirement("BusinessBearer"));
        }

        operation.Parameters ??= [];
        AddHeader(operation, "X-Correlation-Id", false, 128,
            "Caller correlation identifier; unsafe values are replaced.");
        if (path.StartsWith("/api/v1/business/", StringComparison.Ordinal))
        {
            AddQueryLanguage(operation, path);
            AddHeader(operation, "Accept-Language", false, 128,
                "Fallback language selector. Query language takes precedence.");
        }
        if ((path == "/api/v1/internal/appointments/validate" ||
             path == "/api/v1/internal/reservations" ||
             path.Contains("/work-orders/", StringComparison.Ordinal) ||
             path.Contains("/booking-status-outbox/", StringComparison.Ordinal)) &&
            method == "POST")
        {
            AddHeader(operation, "Idempotency-Key", true, 128,
                "Stable idempotency key. A same-body replay returns the prior result; a different-body replay conflicts.");
        }

        var allowedStatuses = Statuses(path, method);
        foreach (var status in operation.Responses.Keys
                     .Where(status => !allowedStatuses.Contains(status))
                     .ToArray())
        {
            operation.Responses.Remove(status);
        }
        foreach (var status in allowedStatuses.Where(status => int.Parse(status) >= 400))
        {
            operation.Responses[status] = ProblemResponse(context);
        }
        var success = allowedStatuses.Single(status => int.Parse(status) < 300);
        if (!operation.Responses.ContainsKey(success))
        {
            operation.Responses[success] = new OpenApiResponse
            {
                Description = success == "201" ? "Created." :
                    success == "204" ? "Deleted." : "Successful response."
            };
        }
    }

    private static HashSet<string> Statuses(string path, string method)
    {
        if (path == "/api/health")
            return ["200"];

        if (path.StartsWith("/api/v1/internal/", StringComparison.Ordinal))
        {
            var internalStatuses = new HashSet<string>
                { "200", "401", "403", "500" };
            if (method == "POST")
            {
                internalStatuses.UnionWith(["400", "409", "413", "415"]);
            }
            if (path.Contains("/reservations/{reference}", StringComparison.Ordinal))
            {
                internalStatuses.Add("404");
            }
            return internalStatuses;
        }

        var success = method == "DELETE" ? "204" :
            method == "POST" &&
            (path.EndsWith("/branches", StringComparison.Ordinal) ||
             path.EndsWith("/categories", StringComparison.Ordinal) ||
             path.EndsWith("/offerings", StringComparison.Ordinal) ||
             path.EndsWith("/addon-groups", StringComparison.Ordinal) ||
             path.EndsWith("/choices", StringComparison.Ordinal))
                ? "201"
                : "200";
        var statuses = new HashSet<string> { success, "400", "500", "503" };
        var isAuth = path.StartsWith("/api/v1/business/auth/", StringComparison.Ordinal);
        if (!isAuth)
        {
            statuses.UnionWith(["401", "403"]);
        }
        if (path.EndsWith("/login", StringComparison.Ordinal))
        {
            statuses.Add("401");
        }
        if (method is "POST" or "PUT")
        {
            statuses.UnionWith(["413", "415"]);
        }
        if (HasResourceLookup(path, method))
        {
            statuses.Add("404");
        }
        if (method == "POST" &&
            (path.EndsWith("/offerings", StringComparison.Ordinal) ||
             path.EndsWith("/addon-groups", StringComparison.Ordinal) ||
             path.EndsWith("/choices", StringComparison.Ordinal)))
        {
            statuses.Add("404");
        }
        if (method is "POST" or "PUT" or "DELETE")
        {
            statuses.Add("409");
        }
        return statuses;
    }

    private static bool HasResourceLookup(string path, string method) =>
        path.Contains('{') ||
        path == "/api/v1/business/company" && method != "POST";

    private static string Describe(string path) =>
        path.StartsWith("/api/v1/internal/", StringComparison.Ordinal)
            ? "HMAC-only internal operation. The canonical method, path, timestamp, nonce, and body are signed; replay protection applies."
            : path.StartsWith("/api/v1/business/auth/", StringComparison.Ordinal)
                ? "Anonymous Business authentication operation with localized validation."
                : path == "/api/health"
                    ? "Anonymous language-neutral Business API health check."
                    : "Business JWT operation scoped by company assignment and owner, employee, or admin policy.";

    private static string Tag(string path) =>
        path.StartsWith("/api/v1/internal/", StringComparison.Ordinal) ? "Internal" :
        path.Contains("/auth/", StringComparison.Ordinal) ? "Authentication" :
        path == "/api/health" ? "Health" : "Business";

    private static OpenApiSecurityRequirement Requirement(params string[] names)
    {
        var requirement = new OpenApiSecurityRequirement();
        foreach (var name in names)
        {
            requirement[new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = name
                }
            }] = [];
        }
        return requirement;
    }

    private static void AddHeader(
        OpenApiOperation operation, string name, bool required, int maxLength, string description)
    {
        if (operation.Parameters.Any(parameter =>
                parameter.Name == name && parameter.In == ParameterLocation.Header))
            return;
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = new OpenApiSchema { Type = "string", MaxLength = maxLength },
            Example = new OpenApiString(name switch
            {
                "X-Correlation-Id" => "<correlation-id>",
                "Idempotency-Key" => "<idempotency-key>",
                _ => "ar, he;q=0.8"
            })
        });
    }

    private static void AddQueryLanguage(OpenApiOperation operation, string path)
    {
        if (operation.Parameters.Any(parameter =>
                parameter.Name == "language" && parameter.In == ParameterLocation.Query))
            return;
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "language",
            In = ParameterLocation.Query,
            Required = false,
            Description = "Presentation language. A single ar or he value overrides Accept-Language.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Enum =
                [
                    new OpenApiString("ar"),
                    new OpenApiString("he")
                ]
            },
            Example = new OpenApiString("ar")
        });
    }

    private static OpenApiResponse ProblemResponse(OperationFilterContext context)
    {
        var response = new OpenApiResponse();
        MakeProblem(response, context);
        return response;
    }

    private static void MakeProblem(OpenApiResponse response, OperationFilterContext context)
    {
        response.Description = "Safe correlated RFC 7807 failure.";
        response.Content ??= new Dictionary<string, OpenApiMediaType>();
        response.Content["application/problem+json"] = new OpenApiMediaType
        {
            Schema = context.SchemaGenerator.GenerateSchema(
                typeof(BusinessProblemDetails), context.SchemaRepository),
            Examples = new Dictionary<string, OpenApiExample>
            {
                ["arabic"] = new()
                {
                    Value = ProblemExample("ar", "تعذر إكمال الطلب.", "الطلب غير صالح.")
                },
                ["hebrew"] = new()
                {
                    Value = ProblemExample("he", "לא ניתן להשלים את הבקשה.", "הבקשה אינה חוקית.")
                }
            }
        };
        response.Headers ??= new Dictionary<string, OpenApiHeader>();
        response.Headers["X-Correlation-Id"] = new OpenApiHeader
        {
            Description = "Bounded correlation identifier.",
            Schema = new OpenApiSchema { Type = "string", MaxLength = 128 }
        };
        response.Headers["Cache-Control"] = new OpenApiHeader
        {
            Description = "Always no-store.",
            Schema = new OpenApiSchema { Type = "string", Example = new OpenApiString("no-store") }
        };
    }

    private static OpenApiObject ProblemExample(string language, string title, string detail) =>
        new()
        {
            ["type"] = new OpenApiString("https://api.ghseeli.example/errors/request_invalid"),
            ["title"] = new OpenApiString(title),
            ["status"] = new OpenApiInteger(400),
            ["detail"] = new OpenApiString(detail),
            ["code"] = new OpenApiString("request_invalid"),
            ["correlationId"] = new OpenApiString("<correlation-id>"),
            ["language"] = new OpenApiString(language)
        };
}

public sealed class BusinessDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        document.Paths.Remove("/");
        document.SecurityRequirements.Clear();
        if (document.Components.Schemas.TryGetValue(
                nameof(BusinessProblemDetails),
                out var problemSchema))
        {
            document.Components.Schemas["ProblemDetails"] = problemSchema;
        }
    }
}
