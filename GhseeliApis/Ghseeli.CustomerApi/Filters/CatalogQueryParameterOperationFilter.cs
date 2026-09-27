using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GhseeliApis.Filters;

public sealed class CatalogQueryParameterOperationFilter : IOperationFilter
{
    private const string BusinessesPath = "api/v1/catalog/businesses";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(
                context.ApiDescription.RelativePath?.Split('?')[0],
                BusinessesPath,
                StringComparison.Ordinal) ||
            !string.Equals(
                context.ApiDescription.HttpMethod,
                "GET",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ConfigureSearch(operation.Parameters);
        ConfigureTop(operation.Parameters);
    }

    private static void ConfigureSearch(IList<OpenApiParameter> parameters)
    {
        var search = FindQueryParameter(parameters, "search");
        search.Required = false;
        search.Description =
            "Optional business-name search. The value is trimmed and Unicode Form C normalized " +
            "before case-insensitive matching against Arabic and Hebrew business names.";
        search.Schema = new OpenApiSchema
        {
            Type = "string",
            Nullable = true,
            MaxLength = 100
        };
    }

    private static void ConfigureTop(IList<OpenApiParameter> parameters)
    {
        var top = FindQueryParameter(parameters, "top");
        top.Required = false;
        top.Description =
            "Optional ranked result limit. Values 5 and 10 rank by average rating, rating count, " +
            "display order, Arabic name, and stable identifier before applying the limit.";
        top.Schema = new OpenApiSchema
        {
            Type = "integer",
            Format = "int32",
            Nullable = true,
            Enum =
            [
                new OpenApiInteger(5),
                new OpenApiInteger(10)
            ]
        };
    }

    private static OpenApiParameter FindQueryParameter(
        IEnumerable<OpenApiParameter> parameters,
        string name) =>
        parameters.Single(parameter =>
            parameter.In == ParameterLocation.Query &&
            string.Equals(parameter.Name, name, StringComparison.Ordinal));
}
