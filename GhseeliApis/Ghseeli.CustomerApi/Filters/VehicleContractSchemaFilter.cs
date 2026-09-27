using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Booking;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.DTOs.Vehicle;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GhseeliApis.Filters;

public sealed class VehicleContractSchemaFilter : ISchemaFilter
{
    private const string HttpsImagePattern = @"^https:\/\/(?![^\/]*@).+$";
    private static readonly HashSet<Type> VehicleSchemas =
    [
        typeof(CreateVehicleRequest),
        typeof(UpdateVehicleRequest),
        typeof(VehicleResponse),
        typeof(CheckoutDraftVehicleRequest),
        typeof(CheckoutDraftVehicleResponse),
        typeof(ConfirmedBookingVehicleResponse),
        typeof(ReservationVehicleSnapshot)
    ];

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!VehicleSchemas.Contains(context.Type) ||
            !schema.Properties.TryGetValue("imageUrl", out var imageUrl))
        {
            return;
        }

        imageUrl.Nullable = true;
        imageUrl.MaxLength = 500;
        imageUrl.Pattern = HttpsImagePattern;
        imageUrl.Description =
            "Optional absolute HTTPS vehicle image URL without embedded credentials; maximum length 500 characters.";
    }
}
