using GhseeliApis.Services.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GhseeliApis.Filters;

public sealed class SwaggerAuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = "/" + context.ApiDescription.RelativePath!.Split('?')[0];
        var method = context.ApiDescription.HttpMethod ?? "GET";
        operation.OperationId ??=
            $"{method}_{path}".Replace("/", "_").Replace("{", "").Replace("}", "");
        operation.Summary ??= $"{method} {path}";
        operation.Description ??= $"Customer API operation for {method} {path}.";
        operation.Tags ??= [new OpenApiTag { Name = Tag(path) }];

        operation.Security = Security(path, context);
        AddContractParameters(operation, path, method);
        AddProblemResponses(operation, path, method, context);
        AddExamples(operation, path);
    }

    private static List<OpenApiSecurityRequirement> Security(
        string path,
        OperationFilterContext context)
    {
        if (path.StartsWith("/api/v1/internal/bookings", StringComparison.Ordinal))
        {
            return [Requirement(
                "HmacServiceId",
                "HmacTimestamp",
                "HmacNonce",
                "HmacSignature")];
        }

        if (path == "/api/lahza/webhook")
        {
            return [Requirement("LahzaSignature")];
        }

        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any() ||
            path is "/api/v1/devices/register" or "/api/Health" or "/api/Health/db" or
                "/api/Auth/external-login" or "/api/Auth/external-login-callback")
        {
            return [];
        }

        if (path.StartsWith("/api/v1/", StringComparison.Ordinal))
        {
            return path.StartsWith("/api/v1/bookings/", StringComparison.Ordinal) ||
                   path.StartsWith("/api/v1/payments/", StringComparison.Ordinal)
                ? [Requirement("CustomerBearer", "DeviceToken")]
                : [Requirement("DeviceToken")];
        }

        return metadata.OfType<IAuthorizeData>().Any()
            ? [Requirement("CustomerBearer")]
            : [];
    }

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

    private static void AddContractParameters(
        OpenApiOperation operation,
        string path,
        string method)
    {
        operation.Parameters ??= [];
        if (IsLocalized(path))
        {
            AddParameter(operation, "language", ParameterLocation.Query, false, schema =>
            {
                schema.Enum =
                [
                    new OpenApiString("ar"),
                    new OpenApiString("he")
                ];
            });
            AddParameter(operation, "Accept-Language", ParameterLocation.Header, false);
            AddParameter(operation, "X-Correlation-Id", ParameterLocation.Header, false,
                schema => schema.MaxLength = 64);
        }

        if ((path == "/api/v1/payments/intents" && method == "POST") ||
            (path == "/api/v1/internal/bookings/status" && method == "POST") ||
            (path.EndsWith("/reconcile", StringComparison.Ordinal) && method == "POST"))
        {
            AddParameter(operation, "Idempotency-Key", ParameterLocation.Header, true,
                schema => schema.MaxLength = 128);
        }

        if (path == "/api/v1/bookings/from-draft" && method == "POST")
        {
            AddParameter(operation, "X-Order-Guid", ParameterLocation.Header, true,
                schema => schema.Format = "uuid");
        }

        if (path.StartsWith("/api/v1/internal/bookings", StringComparison.Ordinal))
        {
            AddParameter(operation, "X-Service-Id", ParameterLocation.Header, true,
                schema => schema.MaxLength = 128);
            AddParameter(operation, "X-Timestamp", ParameterLocation.Header, true,
                schema => schema.MaxLength = 32);
            AddParameter(operation, "X-Nonce", ParameterLocation.Header, true,
                schema => schema.MaxLength = 128);
            AddParameter(operation, "X-Signature", ParameterLocation.Header, true,
                schema => schema.MaxLength = 256);
        }
    }

    private static void AddParameter(
        OpenApiOperation operation,
        string name,
        ParameterLocation location,
        bool required,
        Action<OpenApiSchema>? configure = null)
    {
        var existing = operation.Parameters.FirstOrDefault(parameter =>
            parameter.Name == name && parameter.In == location);
        if (existing is not null)
        {
            existing.Required = required;
            configure?.Invoke(existing.Schema);
            return;
        }

        var schema = new OpenApiSchema { Type = "string" };
        configure?.Invoke(schema);
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = location,
            Required = required,
            Schema = schema,
            Description = $"{name} request header."
        });
    }

    private static void AddProblemResponses(
        OpenApiOperation operation,
        string path,
        string method,
        OperationFilterContext context)
    {
        var statuses = new HashSet<int> { 405, 500 };
        var securityNames = operation.Security
            .SelectMany(requirement => requirement.Keys)
            .Select(scheme => scheme.Reference?.Id)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        if (securityNames.Count > 0)
        {
            statuses.Add(401);
        }
        if (securityNames.Contains("CustomerBearer"))
        {
            statuses.Add(403);
        }
        if (IsLocalized(path) || method is "POST" or "PUT" or "PATCH")
        {
            statuses.Add(400);
        }
        if (context.ApiDescription.ParameterDescriptions.Any(parameter =>
                parameter.Source.Id == "Body") ||
            path == "/api/lahza/webhook")
        {
            statuses.Add(413);
            statuses.Add(415);
        }
        if (path.StartsWith("/api/v1/catalog", StringComparison.Ordinal) ||
            path is "/api/v1/configuration" or "/api/v1/pricing/reprice" or
                "/api/v1/checkout/reprice" or "/api/v1/bookings/from-draft" or
                "/api/v1/payments/intents" or "/api/lahza/webhook" ||
            path.StartsWith("/api/v1/internal/bookings", StringComparison.Ordinal))
        {
            statuses.Add(503);
        }
        if (path is "/api/v1/bookings/from-draft" or "/api/v1/payments/intents" ||
            path.StartsWith("/api/v1/internal/bookings", StringComparison.Ordinal))
        {
            statuses.Add(409);
        }
        if (path is "/api/v1/bookings/from-draft" or "/api/v1/payments/intents")
        {
            statuses.Add(404);
        }
        if (path == "/api/v1/bookings/from-draft")
        {
            statuses.Add(410);
        }
        if (path is "/api/v1/bookings/from-draft" or "/api/v1/payments/intents")
        {
            statuses.Add(502);
        }

        foreach (var status in statuses)
        {
            var key = status.ToString();
            if (!operation.Responses.TryGetValue(key, out var response))
            {
                response = new OpenApiResponse { Description = $"HTTP {status} problem." };
                operation.Responses[key] = response;
            }

            response.Headers["X-Correlation-Id"] = new OpenApiHeader
            {
                Description = "Request correlation identifier.",
                Schema = new OpenApiSchema { Type = "string", MaxLength = 64 }
            };
            response.Headers["Cache-Control"] = new OpenApiHeader
            {
                Description = "Always no-store.",
                Schema = new OpenApiSchema { Type = "string", Example = new OpenApiString("no-store") }
            };
            response.Content["application/problem+json"] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.Schema,
                        Id = "ProblemDetails"
                    }
                },
                Examples = ProblemExamples(path)
            };
        }

        foreach (var response in operation.Responses
                     .Where(item => int.TryParse(item.Key, out var status) && status >= 400)
                     .Select(item => item.Value))
        {
            response.Headers["X-Correlation-Id"] = new OpenApiHeader
            {
                Description = "Request correlation identifier.",
                Schema = new OpenApiSchema { Type = "string", MaxLength = 64 }
            };
            response.Headers["Cache-Control"] = new OpenApiHeader
            {
                Description = "Always no-store.",
                Schema = new OpenApiSchema { Type = "string" }
            };
            response.Content["application/problem+json"] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.Schema,
                        Id = "ProblemDetails"
                    }
                },
                Examples = ProblemExamples(path)
            };
        }
    }

    private static IDictionary<string, OpenApiExample> ProblemExamples(string path)
    {
        if (!IsLocalized(path))
        {
            return new Dictionary<string, OpenApiExample>
            {
                ["safeProblem"] = new()
                {
                    Value = ProblemExample(
                        "Request rejected.",
                        "The request could not be processed.",
                        null)
                }
            };
        }

        return new Dictionary<string, OpenApiExample>
        {
            ["arabic"] = new()
            {
                Value = ProblemExample(
                    "تعذر إكمال الطلب.",
                    "الطلب غير صالح.",
                    "ar")
            },
            ["hebrew"] = new()
            {
                Value = ProblemExample(
                    "לא ניתן להשלים את הבקשה.",
                    "הבקשה אינה חוקית.",
                    "he")
            }
        };
    }

    private static OpenApiObject ProblemExample(
        string title,
        string detail,
        string? language)
    {
        var value = new OpenApiObject
        {
            ["type"] = new OpenApiString("https://api.ghseeli.example/errors/request_invalid"),
            ["title"] = new OpenApiString(title),
            ["status"] = new OpenApiInteger(400),
            ["detail"] = new OpenApiString(detail),
            ["code"] = new OpenApiString("request_invalid"),
            ["correlationId"] = new OpenApiString("<correlation-id>")
        };
        if (language is not null)
        {
            value["language"] = new OpenApiString(language);
        }
        return value;
    }

    private static void AddExamples(OpenApiOperation operation, string path)
    {
        if (operation.RequestBody?.Content.TryGetValue(
                "application/json", out var requestMedia) == true)
        {
            requestMedia.Example = path switch
            {
                "/api/v1/bookings/from-draft" => new OpenApiObject
                {
                    ["expectedVersion"] = new OpenApiInteger(2),
                    ["cancellationPolicyAcknowledged"] = new OpenApiBoolean(true)
                },
                "/api/v1/payments/intents" => new OpenApiObject
                {
                    ["bookingId"] = new OpenApiString(
                        "11111111-1111-1111-1111-111111111111"),
                    ["method"] = new OpenApiString("Card")
                },
                _ => requestMedia.Example
            };
        }

        if (path.StartsWith("/api/v1/internal/bookings", StringComparison.Ordinal))
        {
            foreach (var parameter in operation.Parameters.Where(parameter =>
                         parameter.In == ParameterLocation.Header))
            {
                parameter.Example = parameter.Name switch
                {
                    "X-Service-Id" => new OpenApiString("<service-id>"),
                    "X-Timestamp" => new OpenApiString("<unix-timestamp>"),
                    "X-Nonce" => new OpenApiString("<unique-nonce>"),
                    "X-Signature" => new OpenApiString("<hmac-signature>"),
                    "Idempotency-Key" => new OpenApiString("<opaque-idempotency-key>"),
                    _ => parameter.Example
                };
            }
        }

        if (operation.Responses.TryGetValue("200", out var success) &&
            success.Content.TryGetValue("application/json", out var successMedia))
        {
            successMedia.Example = path switch
            {
                "/api/v1/pricing/reprice" => PricingExample(),
                "/api/v1/checkout/reprice" => DraftPricingExample(),
                "/api/v1/bookings/from-draft" => new OpenApiObject
                {
                    ["orderGuid"] = new OpenApiString(
                        "22222222-2222-2222-2222-222222222222"),
                    ["reference"] = new OpenApiString(
                        "33333333-3333-3333-3333-333333333333"),
                    ["currency"] = new OpenApiString("ILS"),
                    ["grandTotal"] = new OpenApiDouble(79.50)
                },
                _ => successMedia.Example
            };
        }
    }

    private static OpenApiObject PricingExample() => new()
    {
        ["language"] = new OpenApiString("ar"),
        ["intent"] = IntentExample(),
        ["pricing"] = new OpenApiObject
        {
            ["catalogVersion"] = new OpenApiLong(42),
            ["currency"] = new OpenApiString("ILS"),
            ["quotedAtUtc"] = new OpenApiString("2026-09-05T15:00:00Z"),
            ["baseSubtotal"] = new OpenApiDouble(70),
            ["addonSubtotal"] = new OpenApiDouble(9.50),
            ["itemSubtotal"] = new OpenApiDouble(79.50),
            ["serviceFee"] = new OpenApiDouble(0),
            ["serviceFeeMode"] = new OpenApiString("None"),
            ["serviceFeeFlatAmount"] = new OpenApiDouble(0),
            ["serviceFeePercentageRate"] = new OpenApiDouble(0),
            ["taxableSubtotal"] = new OpenApiDouble(79.50),
            ["taxRatePercent"] = new OpenApiDouble(0),
            ["taxAppliesToServiceFee"] = new OpenApiBoolean(false),
            ["tax"] = new OpenApiDouble(0),
            ["grandTotal"] = new OpenApiDouble(79.50),
            ["totalDurationMinutes"] = new OpenApiInteger(45),
            ["items"] = new OpenApiArray
            {
                new OpenApiObject
                {
                    ["offeringSourceId"] = new OpenApiString(
                        "44444444-4444-4444-4444-444444444444"),
                    ["baseSubtotal"] = new OpenApiDouble(70),
                    ["addonSubtotal"] = new OpenApiDouble(9.50),
                    ["itemSubtotal"] = new OpenApiDouble(79.50),
                    ["totalDurationMinutes"] = new OpenApiInteger(45),
                    ["selections"] = new OpenApiArray
                    {
                        new OpenApiObject
                        {
                            ["addonGroupSourceId"] = new OpenApiString(
                                "55555555-5555-5555-5555-555555555555"),
                            ["addonChoiceSourceId"] = new OpenApiString(
                                "66666666-6666-6666-6666-666666666666"),
                            ["selectionType"] = new OpenApiString("SingleChoice"),
                            ["quantity"] = new OpenApiInteger(1),
                            ["unitPriceAdjustment"] = new OpenApiDouble(9.50),
                            ["totalPriceAdjustment"] = new OpenApiDouble(9.50),
                            ["unitDurationAdjustmentMinutes"] = new OpenApiInteger(5),
                            ["totalDurationAdjustmentMinutes"] = new OpenApiInteger(5),
                            ["isDefaultApplied"] = new OpenApiBoolean(false)
                        }
                    }
                }
            }
        },
        ["paymentCapabilities"] = new OpenApiObject
        {
            ["methods"] = new OpenApiArray
            {
                new OpenApiObject
                {
                    ["method"] = new OpenApiString("CreditCard"),
                    ["enabled"] = new OpenApiBoolean(false),
                    ["reasonCode"] = new OpenApiString("provider_unavailable")
                }
            }
        }
    };

    private static OpenApiObject DraftPricingExample()
    {
        var pricing = PricingExample();
        return new OpenApiObject
        {
            ["language"] = pricing["language"],
            ["orderGuid"] = new OpenApiString(
                "22222222-2222-2222-2222-222222222222"),
            ["version"] = new OpenApiInteger(2),
            ["expiresAt"] = new OpenApiString("2026-09-05T15:30:00Z"),
            ["requiresReprice"] = new OpenApiBoolean(false),
            ["intent"] = pricing["intent"],
            ["pricing"] = pricing["pricing"],
            ["paymentCapabilities"] = pricing["paymentCapabilities"]
        };
    }

    private static OpenApiObject IntentExample() => new()
    {
        ["businessSourceId"] = new OpenApiString(
            "77777777-7777-7777-7777-777777777777"),
        ["branchSourceId"] = new OpenApiString(
            "88888888-8888-8888-8888-888888888888"),
        ["catalogVersion"] = new OpenApiLong(42),
        ["requestedSlotStartUtc"] = new OpenApiString("2026-09-06T08:00:00Z"),
        ["vehicle"] = new OpenApiObject
        {
            ["vehicleType"] = new OpenApiString("Sedan"),
            ["licensePlate"] = new OpenApiString("TEST-123"),
            ["make"] = new OpenApiString("Example"),
            ["model"] = new OpenApiString("Model"),
            ["color"] = new OpenApiString("White")
        },
        ["location"] = new OpenApiObject
        {
            ["addressLine"] = new OpenApiString("Example address"),
            ["city"] = new OpenApiString("Example city"),
            ["area"] = new OpenApiString("Example area"),
            ["latitude"] = new OpenApiDouble(32.0853),
            ["longitude"] = new OpenApiDouble(34.7818)
        },
        ["items"] = new OpenApiArray
        {
            new OpenApiObject
            {
                ["offeringSourceId"] = new OpenApiString(
                    "44444444-4444-4444-4444-444444444444"),
                ["selections"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["addonGroupSourceId"] = new OpenApiString(
                            "55555555-5555-5555-5555-555555555555"),
                        ["addonChoiceSourceId"] = new OpenApiString(
                            "66666666-6666-6666-6666-666666666666"),
                        ["quantity"] = new OpenApiInteger(1)
                    }
                }
            }
        }
    };

    private static bool IsLocalized(string path) =>
        path.StartsWith("/api/", StringComparison.Ordinal) &&
        !path.StartsWith("/api/Health", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/api/lahza", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/api/v1/internal/", StringComparison.Ordinal) &&
        path != "/api/v1/devices/register";

    private static string Tag(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Customer";
}
