using Ghseeli.BusinessApi.DTOs.Auth;
using Ghseeli.BusinessApi.DTOs.Availability;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ghseeli.BusinessApi.Swagger;

public sealed class BusinessRequestSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type.Name.EndsWith("Request", StringComparison.Ordinal))
        {
            schema.AdditionalPropertiesAllowed = false;
        }

        if (context.Type == typeof(RegisterOwnerRequest))
        {
            Require(schema, "email", "password", "fullName", "companyNameAr");
            SetNullable(schema, "companyNameHe", nullable: true);
            schema.Example = BilingualExample("companyNameAr");
            return;
        }

        if (context.Type == typeof(CreateServiceCategoryRequest))
        {
            Require(schema, "nameAr");
            SetNullable(schema, "nameHe", nullable: true);
            schema.Example = BilingualExample("nameAr");
            return;
        }

        if (context.Type == typeof(CreateBranchRequest))
        {
            Require(schema, "nameAr", "addressAr");
            SetNullable(schema, "nameHe", nullable: true);
            SetNullable(schema, "addressHe", nullable: true);
            schema.Example = BilingualExample("nameAr");
            return;
        }

        if (context.Type == typeof(CreateServiceOfferingRequest))
        {
            Require(schema, "categoryId", "nameAr");
            SetNullable(schema, "nameHe", nullable: true);
            SetDecimal(schema, "basePrice");
            schema.Example = BilingualExample("nameAr");
            return;
        }

        if (context.Type == typeof(CreateAddonGroupRequest))
        {
            Require(schema, "nameAr");
            SetNullable(schema, "nameHe", nullable: true);
            SetMinimum(schema, "minimumSelections", 0);
            SetMinimum(schema, "maximumSelections", 1);
            schema.Example = BilingualExample("nameAr");
            return;
        }

        if (context.Type == typeof(CreateAddonChoiceRequest))
        {
            Require(schema, "nameAr");
            SetNullable(schema, "nameHe", nullable: true);
            schema.Properties.TryAdd("minimumQuantity", new OpenApiSchema
            {
                Type = "integer",
                Format = "int32",
                Minimum = 1
            });
            schema.Properties.TryAdd("maximumQuantity", new OpenApiSchema
            {
                Type = "integer",
                Format = "int32",
                Minimum = 1,
                Nullable = true
            });
            SetMinimum(schema, "minimumQuantity", 1);
            SetMinimum(schema, "maximumQuantity", 1);
            SetDecimal(schema, "priceAdjustment");
            schema.Example = BilingualExample("nameAr");
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
            if (schema.Properties.TryGetValue("currency", out var currency))
            {
                currency.MinLength = 3;
                currency.MaxLength = 3;
            }
        }

        if (context.Type == typeof(BusinessProblemDetails))
        {
            Require(schema, "type", "title", "status", "detail", "code", "correlationId");
            schema.Description =
                "Localized examples: تعذر إكمال الطلب. | לא ניתן להשלים את הבקשה.";
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

    private static void SetMinimum(OpenApiSchema schema, string propertyName, decimal minimum)
    {
        if (schema.Properties.TryGetValue(propertyName, out var property))
        {
            property.Minimum = minimum;
        }
    }

    private static void SetDecimal(OpenApiSchema schema, string propertyName)
    {
        if (schema.Properties.TryGetValue(propertyName, out var property))
        {
            property.Type = "number";
            property.Format = "decimal";
            property.MultipleOf = 0.01m;
        }
    }

    private static OpenApiObject BilingualExample(string arabicProperty) =>
        new()
        {
            [arabicProperty] = new OpenApiString("مثال عربي"),
            ["nameHe"] = new OpenApiNull()
        };

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
