using GhseeliApis.DTOs.Catalog;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GhseeliApis.Filters;

public sealed class CatalogResponseSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(CatalogOfferingResponse))
        {
            SetNullable(schema, "qualifier");
            SetNullable(schema, "badgeCode");
            return;
        }

        if (context.Type == typeof(CatalogCategoryResponse))
        {
            SetNullable(schema, "imageUrl");
            SetNullable(schema, "colorHex");

            if (schema.Properties.TryGetValue("imageUrl", out var imageUrl))
            {
                imageUrl.MaxLength = 500;
                imageUrl.Pattern = "^https://[^\\s/@]+(?:/[^\\s]*)?$";
                imageUrl.Description =
                    "Nullable absolute HTTPS category image URL with no embedded credentials.";
            }

            if (schema.Properties.TryGetValue("colorHex", out var colorHex))
            {
                colorHex.MaxLength = 7;
                colorHex.Pattern = "^#[0-9A-F]{6}$";
                colorHex.Description = "Nullable normalized #RRGGBB category color.";
            }
        }
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
