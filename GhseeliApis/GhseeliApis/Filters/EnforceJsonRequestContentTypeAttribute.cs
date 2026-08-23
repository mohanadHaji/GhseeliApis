using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Configuration;
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
        var mediaType = request.ContentType?.Split(';', 2)[0].Trim();
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var language = ConfigurationLanguageResolver.Resolve(
            request.Query["language"].ToString(),
            request.Headers.AcceptLanguage.ToString());
        var bookingRoute = request.Path.StartsWithSegments("/api/v1/bookings");
        var problem = bookingRoute
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

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status415UnsupportedMediaType,
            ContentTypes = { "application/problem+json" }
        };
    }
}
