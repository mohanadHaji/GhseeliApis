using Ghseeli.BusinessApi.DTOs.Auth;
using Ghseeli.BusinessApi.DTOs.Availability;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ghseeli.BusinessApi.Swagger;

public sealed class BusinessRequestSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(RegisterOwnerRequest))
        {
            Require(schema, "email", "password", "fullName", "companyNameAr");
            return;
        }

        if (context.Type == typeof(CreateServiceCategoryRequest))
        {
            Require(schema, "nameAr");
            return;
        }

        if (context.Type == typeof(UpdateBranchAvailabilitySettingsRequest))
        {
            Require(schema, "timeZoneId", "minimumLeadMinutes", "bookingHorizonDays");
            return;
        }

        if (context.Type == typeof(ValidateAppointmentRequest))
        {
            Require(schema, "branchId", "offeringId", "requestedSlotStartUtc", "currency");
            SetNullable(schema, "selectedAddons", nullable: true);
            SetNullable(schema, "customerLocation", nullable: true);
        }
    }

    private static void Require(OpenApiSchema schema, params string[] propertyNames)
    {
        schema.Required ??= new SortedSet<string>(StringComparer.Ordinal);
        foreach (var propertyName in propertyNames)
        {
            schema.Required.Add(propertyName);
            SetNullable(schema, propertyName, nullable: false);
        }
    }

    private static void SetNullable(
        OpenApiSchema schema,
        string propertyName,
        bool nullable)
    {
        if (schema.Properties.TryGetValue(propertyName, out var propertySchema))
        {
            propertySchema.Nullable = nullable;
        }
    }
}
