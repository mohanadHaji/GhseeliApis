using System.Text;
using Ghseeli.Common.Logging;
using GhseeliApis.Services.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/lahza")]
public sealed class LahzaWebhookController : ControllerBase
{
    public const long MaxBodyBytes = 65_536;
    private readonly IPaymentWebhookParser _parser;
    private readonly IPaymentWebhookService _service;
    private readonly IOptionsMonitor<LahzaConfigurationOptions> _options;
    private readonly IAppLogger _logger;

    public LahzaWebhookController(
        IPaymentWebhookParser parser,
        IPaymentWebhookService service,
        IOptionsMonitor<LahzaConfigurationOptions> options,
        IAppLogger logger)
    {
        _parser = parser;
        _service = service;
        _options = options;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook(
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.ContentType is null ||
            !Request.ContentType.StartsWith(
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            return ProblemResult(
                415,
                CustomerPaymentErrorCodes.WebhookUnsupportedMediaType);
        }

        if (Request.ContentLength > MaxBodyBytes)
        {
            return ProblemResult(
                413,
                CustomerPaymentErrorCodes.WebhookTooLarge);
        }

        var secret = _options.CurrentValue.SecretKey?.Trim();
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogError("Lahza webhook configuration is invalid.");
            return ProblemResult(
                503,
                CustomerPaymentErrorCodes.WebhookConfiguration);
        }

        byte[] rawBody;
        try
        {
            rawBody = await ReadBoundedBodyAsync(
                Request.Body,
                cancellationToken);
        }
        catch (CustomerPaymentException exception)
        {
            return ProblemResult(exception.StatusCode, exception.Code);
        }

        var signature = Request.Headers["X-Lahza-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return ProblemResult(
                400,
                CustomerPaymentErrorCodes.SignatureMissing);
        }

        try
        {
            var paymentEvent = _parser.Parse(rawBody, signature, secret);
            await _service.ProcessAsync(
                paymentEvent,
                rawBody,
                cancellationToken);
        }
        catch (CustomerPaymentException exception)
        {
            return ProblemResult(exception.StatusCode, exception.Code);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                $"Verified Lahza webhook processing did not complete durably: {exception.GetType().Name}.");
            return ProblemResult(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous);
        }

        return Ok(new { received = true });
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        while (true)
        {
            var read = await body.ReadAsync(
                bytes.AsMemory(0, bytes.Length),
                cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaxBodyBytes)
            {
                throw new CustomerPaymentException(
                    413,
                    CustomerPaymentErrorCodes.WebhookTooLarge);
            }
            await buffer.WriteAsync(
                bytes.AsMemory(0, read),
                cancellationToken);
        }
        return buffer.ToArray();
    }

    private ObjectResult ProblemResult(int status, string code) =>
        new(new ProblemDetails
        {
            Type = $"https://api.ghseeli.example/errors/{code}",
            Status = status,
            Title = "Lahza webhook request could not be processed.",
            Detail = "Lahza webhook request could not be processed.",
            Extensions =
            {
                ["code"] = code,
                ["correlationId"] = HttpContext.TraceIdentifier
            }
        })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
