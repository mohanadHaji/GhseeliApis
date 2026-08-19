using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ghseeli.BusinessApi.Swagger;

public class StringEnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var enumType = Nullable.GetUnderlyingType(context.Type) ?? context.Type;
        if (!enumType.IsEnum)
        {
            return;
        }

        schema.Type = "string";
        schema.Format = null;
        schema.Enum = Enum.GetNames(enumType)
            .Select(name => (IOpenApiAny)new OpenApiString(name))
            .ToList();

        var allowedValues = string.Join(", ", Enum.GetNames(enumType));
        var prefix = string.IsNullOrWhiteSpace(schema.Description)
            ? string.Empty
            : $"{schema.Description}{Environment.NewLine}{Environment.NewLine}";
        schema.Description = $"{prefix}Allowed values: {allowedValues}";
    }
}
