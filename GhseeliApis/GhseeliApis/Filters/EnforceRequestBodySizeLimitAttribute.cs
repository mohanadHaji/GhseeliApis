using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Configuration;

namespace GhseeliApis.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class EnforceRequestBodySizeLimitAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    private readonly long _maxBytes;
    private readonly string? _problemCode;

    public EnforceRequestBodySizeLimitAttribute(long maxBytes, string? problemCode = null)
    {
        _maxBytes = maxBytes;
        _problemCode = problemCode;
    }

    public int Order => int.MinValue;

    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.ContentLength.HasValue && request.ContentLength.Value > _maxBytes)
        {
            context.Result = CreatePayloadTooLargeResult(context.HttpContext);
            return;
        }

        request.EnableBuffering(
            bufferThreshold: checked((int)_maxBytes + 1),
            bufferLimit: _maxBytes + 1);

        try
        {
            await request.Body.CopyToAsync(Stream.Null, context.HttpContext.RequestAborted);
            request.Body.Position = 0;
        }
        catch (IOException)
        {
            context.Result = CreatePayloadTooLargeResult(context.HttpContext);
            return;
        }

        await next();
    }

    private IActionResult CreatePayloadTooLargeResult(HttpContext context)
    {
        if (_problemCode is null)
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge,
                ContentType = "text/plain; charset=utf-8",
                Content = $"Request body too large. The maximum allowed size is {_maxBytes} bytes."
            };
        }

        var language = ConfigurationLanguageResolver.Resolve(
            context.Request.Query["language"].ToString(),
            context.Request.Headers.AcceptLanguage.ToString());
        var problem = CheckoutPricingProblemDetailsFactory.Create(
            StatusCodes.Status413PayloadTooLarge,
            _problemCode,
            language,
            context.TraceIdentifier);

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
            ContentTypes = { "application/problem+json" }
        };
    }
}
