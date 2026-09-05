using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Payments;

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
            if (request.Body.Position > _maxBytes)
            {
                context.Result = CreatePayloadTooLargeResult(context.HttpContext);
                return;
            }
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
            var genericLanguage = ConfigurationLanguageResolver.Resolve(
                context.Request.Query.ContainsKey("language")
                    ? context.Request.Query["language"].ToString()
                    : null,
                context.Request.Headers.AcceptLanguage.ToString());
            return new ObjectResult(ConfigurationProblemDetailsFactory.Create(
                StatusCodes.Status413PayloadTooLarge,
                "request_body_too_large",
                genericLanguage,
                context.TraceIdentifier))
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge,
                ContentTypes = { "application/problem+json" }
            };
        }

        var language = ConfigurationLanguageResolver.Resolve(
            context.Request.Query.ContainsKey("language")
                ? context.Request.Query["language"].ToString()
                : null,
            context.Request.Headers.AcceptLanguage.ToString());
        if (context.Request.Path.StartsWithSegments("/api/v1/payments"))
        {
            context.Response.Headers.CacheControl = "no-store";
        }
        object problem = context.Request.Path.StartsWithSegments("/api/v1/payments")
            ? CustomerPaymentProblemDetailsFactory.Create(
                StatusCodes.Status413PayloadTooLarge,
                CustomerPaymentErrorCodes.RequestTooLarge,
                language,
                context.TraceIdentifier)
            : context.Request.Path.StartsWithSegments("/api/v1/bookings")
            ? BookingConfirmationProblemDetailsFactory.Create(
                StatusCodes.Status413PayloadTooLarge,
                _problemCode,
                language,
                context.TraceIdentifier)
            : CheckoutPricingProblemDetailsFactory.Create(
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
