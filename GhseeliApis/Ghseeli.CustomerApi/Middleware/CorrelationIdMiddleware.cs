using Ghseeli.IntegrationContracts.InternalHttp;

namespace GhseeliApis.Middleware;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = InternalServiceHeaderValueValidator.GetOrCreateCorrelationId(
            context.Request.Headers[InternalServiceWireConstants.CorrelationIdHeaderName].ToString());

        context.Request.Headers[InternalServiceWireConstants.CorrelationIdHeaderName] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[InternalServiceWireConstants.CorrelationIdHeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
