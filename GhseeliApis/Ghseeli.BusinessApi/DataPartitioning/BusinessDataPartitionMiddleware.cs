using Ghseeli.IntegrationContracts.DataPartitioning;

namespace Ghseeli.BusinessApi.DataPartitioning;

public sealed class BusinessDataPartitionMiddleware
{
    private readonly RequestDelegate _next;

    public BusinessDataPartitionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IBusinessDataPartitionContext dataPartition)
    {
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
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Data partition mismatch.",
                    status = StatusCodes.Status403Forbidden,
                    code = "data_partition_mismatch",
                    correlationId = context.TraceIdentifier
                });
                return;
            }
            dataPartition.SetTrustedPartition(claim);
        }

        await _next(context);
    }
}
