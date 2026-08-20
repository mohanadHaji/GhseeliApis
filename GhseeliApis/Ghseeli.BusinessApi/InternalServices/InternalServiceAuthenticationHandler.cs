using Ghseeli.BusinessApi.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Ghseeli.BusinessApi.InternalServices;

public sealed class InternalServiceAuthenticationHandler :
    AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly InternalServiceRequestValidator _validator;

    public InternalServiceAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        InternalServiceRequestValidator validator)
        : base(options, logger, encoder)
    {
        _validator = validator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/api/v1/internal", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var validationResult = await _validator.ValidateAsync(Context, Context.RequestAborted);
        if (!validationResult.IsAuthenticated)
        {
            Context.Items[InternalServiceHttpContextKeys.AuthenticationFailure] = validationResult.Failure!;
            return AuthenticateResult.Fail(validationResult.Failure!.Code);
        }

        var claims = new List<Claim>
        {
            new(BusinessClaimTypes.InternalServiceId, validationResult.Service!.ServiceId)
        };
        claims.AddRange(validationResult.Service.AllowedOperations.Select(operation =>
            new Claim(BusinessClaimTypes.InternalAllowedOperation, operation)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.TryGetValue(InternalServiceHttpContextKeys.AuthenticationFailure, out var failure) &&
            failure is InternalServiceAuthenticationFailure authFailure)
        {
            return InternalServiceProblemResponseFactory.WriteAsync(Context, authFailure);
        }

        return InternalServiceProblemResponseFactory.WriteAsync(
            Context,
            StatusCodes.Status401Unauthorized,
            "Internal service request was rejected.",
            "The internal service request could not be authenticated.",
            Ghseeli.IntegrationContracts.InternalHttp.InternalServiceProblemCodes.InvalidSignature);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        return InternalServiceProblemResponseFactory.WriteAsync(
            Context,
            StatusCodes.Status403Forbidden,
            "Internal service is not allowed to call this operation.",
            "The authenticated internal service is not authorized for the targeted operation.",
            Ghseeli.IntegrationContracts.InternalHttp.InternalServiceProblemCodes.ServiceForbidden);
    }
}
