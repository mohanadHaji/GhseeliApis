using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GhseeliApis.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class EnforceJsonRequestContentTypeAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var paymentRoute = request.Path.StartsWithSegments("/api/v1/payments");
        var hasTransferEncoding = request.Headers.TransferEncoding.Count > 0;
        if (paymentRoute &&
            request.ContentLength is null or 0 &&
            !hasTransferEncoding)
        {
            await next();
            return;
        }

        var mediaType = request.ContentType?.Split(';', 2)[0].Trim();
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var language = ConfigurationLanguageResolver.Resolve(
            request.Query.ContainsKey("language")
                ? request.Query["language"].ToString()
                : null,
            request.Headers.AcceptLanguage.ToString());
        var bookingRoute = request.Path.StartsWithSegments("/api/v1/bookings");
        var pricingRoute =
            request.Path.Equals("/api/v1/pricing/reprice", StringComparison.OrdinalIgnoreCase) ||
            request.Path.Equals("/api/v1/checkout/reprice", StringComparison.OrdinalIgnoreCase);
        if (!paymentRoute && !bookingRoute && !pricingRoute)
        {
            context.Result = new ObjectResult(ConfigurationProblemDetailsFactory.Create(
                StatusCodes.Status415UnsupportedMediaType,
                "unsupported_media_type",
                language,
                context.HttpContext.TraceIdentifier))
            {
                StatusCode = StatusCodes.Status415UnsupportedMediaType,
                ContentTypes = { "application/problem+json" }
            };
            return;
        }
        object problem = paymentRoute
            ? CustomerPaymentProblemDetailsFactory.Create(
                StatusCodes.Status415UnsupportedMediaType,
                CustomerPaymentErrorCodes.PaymentUnsupportedMediaType,
                language,
                context.HttpContext.TraceIdentifier)
            : bookingRoute
            ? BookingConfirmationProblemDetailsFactory.Create(
                StatusCodes.Status415UnsupportedMediaType,
                BookingConfirmationProblemCodes.UnsupportedMediaType,
                language,
                context.HttpContext.TraceIdentifier)
            : CheckoutPricingProblemDetailsFactory.Create(
                StatusCodes.Status415UnsupportedMediaType,
                CheckoutPricingProblemCodes.UnsupportedMediaType,
                language,
                context.HttpContext.TraceIdentifier);

        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status415UnsupportedMediaType,
            ContentTypes = { "application/problem+json" }
        };
    }
}
