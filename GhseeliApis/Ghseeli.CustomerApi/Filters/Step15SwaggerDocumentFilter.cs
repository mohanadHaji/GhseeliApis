using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GhseeliApis.Filters;

public sealed class Step15SwaggerDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        document.Paths.Remove("/");
        document.Components.Schemas["ProblemDetails"] = ProblemSchema();
        if (document.Components.Schemas.ContainsKey("CreateCustomerPaymentIntentRequest"))
        {
            document.Components.Schemas["CreateCustomerPaymentIntentRequest"] =
                PaymentRequestSchema();
        }
        HardenSchemas(document.Components.Schemas.Values);

        document.Info.Description =
            "Customer API. Prices and payment amounts are server-authoritative; " +
            "same-body replay is required for idempotent booking and payment operations.";
    }

    private static OpenApiSchema ProblemSchema() => new()
    {
        Type = "object",
        Required = new HashSet<string>
        {
            "type", "title", "status", "detail", "code", "correlationId"
        },
        Properties = new Dictionary<string, OpenApiSchema>
        {
            ["type"] = new() { Type = "string", Format = "uri" },
            ["title"] = new() { Type = "string" },
            ["status"] = new() { Type = "integer", Format = "int32" },
            ["detail"] = new() { Type = "string" },
            ["code"] = new() { Type = "string" },
            ["correlationId"] = new() { Type = "string", MaxLength = 64 },
            ["language"] = new()
            {
                Type = "string",
                Nullable = true,
                Enum = [new OpenApiString("ar"), new OpenApiString("he")]
            },
            ["fieldErrors"] = new()
            {
                Type = "object",
                AdditionalProperties = new OpenApiSchema
                {
                    Type = "array",
                    Items = new OpenApiSchema { Type = "string" }
                }
            }
        }
    };

    private static OpenApiSchema PaymentRequestSchema() => new()
    {
        Type = "object",
        AdditionalPropertiesAllowed = false,
        Required = new HashSet<string> { "bookingId", "method" },
        Properties = new Dictionary<string, OpenApiSchema>
        {
            ["bookingId"] = new() { Type = "string", Format = "uuid" },
            ["method"] = new()
            {
                Type = "string",
                Enum =
                [
                    new OpenApiString("Card"),
                    new OpenApiString("Wallet"),
                    new OpenApiString("CashOnArrival"),
                    new OpenApiString("ThirdParty")
                ]
            }
        }
    };

    private static void HardenSchemas(IEnumerable<OpenApiSchema> schemas)
    {
        foreach (var schema in schemas)
        {
            if (schema.Properties.TryGetValue("currency", out var currency))
            {
                currency.Type = "string";
                currency.MinLength = 3;
                currency.MaxLength = 3;
            }

            if (schema.Properties.TryGetValue("quantity", out var quantity))
            {
                quantity.Minimum = 1;
            }
        }
    }
}
