using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ghseeli.BusinessApi.Swagger;

public sealed class VehicleContractSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ReservationVehicleSnapshot) ||
            !schema.Properties.TryGetValue("imageUrl", out var imageUrl))
        {
            return;
        }

        imageUrl.Nullable = true;
        imageUrl.MaxLength = 500;
        imageUrl.Pattern = @"^https:\/\/(?![^\/]*@).+$";
        imageUrl.Description =
            "Optional absolute HTTPS vehicle image URL without embedded credentials; maximum length 500 characters.";
    }
}
