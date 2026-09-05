using System.Security.Claims;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Filters;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/payments")]
[Authorize(Policy = "UserPolicy")]
public sealed class CustomerPaymentsController : ControllerBase
{
    public const long MaxIntentRequestBodyBytes = 65_536;
    private readonly ICustomerPaymentService _service;
    private readonly IAppLogger _logger;

    public CustomerPaymentsController(ICustomerPaymentService service, IAppLogger logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("intents")]
    [ProducesResponseType(typeof(CustomerPaymentResponse), StatusCodes.Status200OK)]
    [EnforceJsonRequestContentType]
    [EnforceRequestBodySizeLimit(
        MaxIntentRequestBodyBytes,
        CustomerPaymentErrorCodes.RequestTooLarge)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCustomerPaymentIntentRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Query.ContainsKey("language") &&
            !ConfigurationLanguageResolver.TryNormalizeOverride(language, out _))
        {
            return ProblemResult(400, CustomerPaymentErrorCodes.Invalid, null);
        }
        if (request is null)
        {
            return ProblemResult(400, CustomerPaymentErrorCodes.Invalid, language);
        }
        if (request.BookingId == Guid.Empty)
        {
            return ProblemResult(
                400,
                CustomerPaymentErrorCodes.Invalid,
                language,
                new Dictionary<string, string[]>
                {
                    ["bookingId"] = ["Booking ID is required."]
                });
        }
        if (string.IsNullOrWhiteSpace(request.Method) ||
            (!string.Equals(request.Method, "Card", StringComparison.OrdinalIgnoreCase) &&
             !IsKnownDisabledMethod(request.Method)))
        {
            return ProblemResult(400, CustomerPaymentErrorCodes.Invalid, language);
        }
        if (IsKnownDisabledMethod(request.Method))
        {
            return ProblemResult(
                409,
                CustomerPaymentErrorCodes.MethodNotYetSupported,
                language);
        }
        var rawIdempotencyKeys = Request.Headers["Idempotency-Key"];
        if (idempotencyKey is null)
        {
            return ProblemResult(
                400,
                Request.Headers.ContainsKey("Idempotency-Key")
                    ? CustomerPaymentErrorCodes.IdempotencyKeyInvalid
                    : CustomerPaymentErrorCodes.IdempotencyKeyRequired,
                language);
        }
        if (rawIdempotencyKeys.Count != 1 ||
            string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > CustomerPaymentService.MaxIdempotencyKeyLength ||
            idempotencyKey.Any(char.IsWhiteSpace) ||
            idempotencyKey.Any(char.IsControl))
        {
            return ProblemResult(
                400,
                CustomerPaymentErrorCodes.IdempotencyKeyInvalid,
                language);
        }
        if (!TryGetIdentity(out var userId, out var deviceId))
        {
            return ProblemResult(401, CustomerPaymentErrorCodes.Invalid, language);
        }

        try
        {
            var response = await _service.CreateAsync(
                request,
                idempotencyKey ?? string.Empty,
                userId,
                deviceId,
                cancellationToken);
            return Ok(response);
        }
        catch (CustomerPaymentException exception)
        {
            _logger.LogWarning(
                $"Customer payment creation rejected with code {exception.Code} for booking {request.BookingId:D}.");
            return ProblemResult(exception.StatusCode, exception.Code, language);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                $"Customer payment persistence failed for booking {request.BookingId:D}: {exception.GetType().Name}.");
            return ProblemResult(503, "service_unavailable", language);
        }
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerPaymentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid id,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Query.ContainsKey("language") &&
            !ConfigurationLanguageResolver.TryNormalizeOverride(language, out _))
        {
            return ProblemResult(400, CustomerPaymentErrorCodes.Invalid, null);
        }
        if (!TryGetIdentity(out var userId, out var deviceId))
        {
            return ProblemResult(401, CustomerPaymentErrorCodes.Invalid, language);
        }

        var payment = await _service.GetAsync(id, userId, deviceId, cancellationToken);
        return payment is null
            ? ProblemResult(404, CustomerPaymentErrorCodes.NotFound, language)
            : Ok(payment);
    }

    [HttpPost("{id:guid}/verify")]
    [ProducesResponseType(typeof(CustomerPaymentResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(
        Guid id,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Query.ContainsKey("language") &&
            !ConfigurationLanguageResolver.TryNormalizeOverride(language, out _))
        {
            return ProblemResult(
                400,
                CustomerPaymentErrorCodes.Invalid,
                null);
        }
        if (!TryGetIdentity(out var userId, out var deviceId))
        {
            return ProblemResult(
                401,
                CustomerPaymentErrorCodes.Invalid,
                language);
        }

        try
        {
            var payment = await _service.VerifyAsync(
                id,
                userId,
                deviceId,
                cancellationToken);
            return payment is null
                ? ProblemResult(
                    404,
                    CustomerPaymentErrorCodes.NotFound,
                    language)
                : Ok(payment);
        }
        catch (CustomerPaymentException exception)
        {
            _logger.LogWarning(
                $"Customer payment verification rejected with code {exception.Code} for payment {id:D}.");
            return ProblemResult(
                exception.StatusCode,
                exception.Code,
                language);
        }
    }

    private bool TryGetIdentity(out Guid userId, out Guid deviceId)
    {
        var parsedUser = Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out userId) && userId != Guid.Empty;
        var currentDevice = HttpContext.GetDeviceId();
        deviceId = currentDevice ?? Guid.Empty;
        return parsedUser && deviceId != Guid.Empty;
    }

    private static bool IsKnownDisabledMethod(string method) =>
        string.Equals(method, "Wallet", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "CashOnArrival", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "ThirdParty", StringComparison.OrdinalIgnoreCase);

    private ObjectResult ProblemResult(
        int status,
        string code,
        string? language,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        var resolved = ConfigurationLanguageResolver.Resolve(
            language,
            Request.Headers.AcceptLanguage.ToString());
        var problem = CustomerPaymentProblemDetailsFactory.Create(
            status,
            code,
            resolved,
            HttpContext.TraceIdentifier,
            fieldErrors);

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}
