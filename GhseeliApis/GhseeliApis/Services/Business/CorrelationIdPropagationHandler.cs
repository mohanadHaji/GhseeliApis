using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Http;

namespace GhseeliApis.Services.Business;

public sealed class CorrelationIdPropagationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdPropagationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(InternalServiceWireConstants.CorrelationIdHeaderName))
        {
            var currentCorrelationId = _httpContextAccessor.HttpContext?
                .Request
                .Headers[InternalServiceWireConstants.CorrelationIdHeaderName]
                .ToString();
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.CorrelationIdHeaderName,
                InternalServiceHeaderValueValidator.GetOrCreateCorrelationId(currentCorrelationId));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
