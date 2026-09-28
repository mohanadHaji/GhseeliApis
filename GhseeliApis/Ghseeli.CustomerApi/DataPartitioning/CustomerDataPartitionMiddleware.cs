using System.Text.Json;
using System.Security.Claims;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.Middleware;

namespace GhseeliApis.DataPartitioning;

public sealed class CustomerDataPartitionMiddleware
{
    private readonly RequestDelegate _next;

    public CustomerDataPartitionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ICustomerDataPartitionContext dataPartition,
        IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("DemoData:PublicApisOnly") &&
            context.GetEndpoint()?.Metadata.GetMetadata<DemoSeedDataOnlyAttribute>() is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            dataPartition.SetTrustedPartition(DataPartitionNames.Demo);
            await _next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claim = context.User.FindFirst(DataPartitionNames.ClaimType)?.Value
                ?? DataPartitionNames.Production;
            if (!DataPartitionNames.IsSupported(claim) ||
                (dataPartition.IsAssigned &&
                 !string.Equals(claim, dataPartition.Partition, StringComparison.Ordinal)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    title = "Data partition mismatch.",
                    status = StatusCodes.Status403Forbidden,
                    code = "data_partition_mismatch",
                    correlationId = context.TraceIdentifier
                }));
                return;
            }
            dataPartition.SetTrustedPartition(claim);
        }

        await _next(context);
    }
}
