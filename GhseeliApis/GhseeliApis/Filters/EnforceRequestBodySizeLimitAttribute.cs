using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GhseeliApis.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class EnforceRequestBodySizeLimitAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    private readonly long _maxBytes;

    public EnforceRequestBodySizeLimitAttribute(long maxBytes)
    {
        _maxBytes = maxBytes;
    }

    public int Order => int.MinValue;

    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.ContentLength.HasValue && request.ContentLength.Value > _maxBytes)
        {
            context.Result = CreatePayloadTooLargeResult();
            return;
        }

        request.EnableBuffering(
            bufferThreshold: checked((int)_maxBytes + 1),
            bufferLimit: _maxBytes + 1);

        try
        {
            await request.Body.CopyToAsync(Stream.Null, context.HttpContext.RequestAborted);
            request.Body.Position = 0;
            await next();
        }
        catch (IOException)
        {
            context.Result = CreatePayloadTooLargeResult();
        }
    }

    private ContentResult CreatePayloadTooLargeResult() =>
        new()
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
            ContentType = "text/plain; charset=utf-8",
            Content = $"Request body too large. The maximum allowed size is {_maxBytes} bytes."
        };
}
