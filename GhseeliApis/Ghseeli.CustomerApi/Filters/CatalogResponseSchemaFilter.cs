using GhseeliApis.DTOs.Catalog;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GhseeliApis.Filters;

public sealed class CatalogResponseSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(CatalogOfferingResponse))
        {
            return;
        }

        SetNullable(schema, "qualifier");
        SetNullable(schema, "badgeCode");
    }

    private static void SetNullable(OpenApiSchema schema, string propertyName)
    {
        if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
        {
            return;
        }

        if (propertySchema.Reference is not null)
        {
            var reference = propertySchema.Reference;
            propertySchema.Reference = null;
            propertySchema.AllOf =
            [
                new OpenApiSchema
                {
                    Reference = reference
                }
            ];
        }

        propertySchema.Nullable = true;
    }
}
