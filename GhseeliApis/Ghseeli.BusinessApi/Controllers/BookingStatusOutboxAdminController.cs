using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Services;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/business/admin/booking-status-outbox")]
[Authorize(Policy = BusinessPolicies.Admin)]
public sealed class BookingStatusOutboxAdminController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly IBookingStatusDeadLetterService _service;

    public BookingStatusOutboxAdminController(IBookingStatusDeadLetterService service) =>
        _service = service;

    [HttpPost("{eventId:guid}/requeue")]
    [ProducesResponseType<DeadLetterRequeueResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Requeue(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var adminUserId))
        {
            await BusinessAuthenticationProblemResponseFactory.WriteAsync(
                HttpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is required.",
                "A valid Business API access token is required.",
                BusinessAuthenticationProblemCodes.AuthenticationRequired);
            return new EmptyResult();
        }
        try
        {
            var requestIds = Request.Headers[
                InternalServiceWireConstants.IdempotencyKeyHeaderName];
            var requestId = requestIds.ToString();
            if (requestIds.Count != 1 ||
                !InternalServiceHeaderValueValidator.IsValidIdempotencyKey(requestId))
            {
                return ProblemResult(
                    StatusCodes.Status400BadRequest,
                    "booking_status_requeue_idempotency_key_invalid",
                    "مفتاح طلب إعادة الإرسال مطلوب ويجب ألا يتجاوز 128 حرفاً.",
                    "נדרש מפתח בקשת החזרה שאורכו אינו עולה על 128 תווים.");
            }
            var response = await _service.RequeueAsync(
                adminUserId,
                eventId,
                requestId,
                cancellationToken);
            return response is null
                ? ProblemResult(
                    StatusCodes.Status404NotFound,
                    "booking_status_event_not_found",
                    "لم يتم العثور على حدث حالة الحجز.",
                    "אירוע סטטוס ההזמנה לא נמצא.")
                : JsonResult(response);
        }
        catch (DeadLetterRequeueConflictException)
        {
            return ProblemResult(
                StatusCodes.Status409Conflict,
                "booking_status_event_not_requeueable",
                "لا يمكن إعادة الحدث في حالته الحالية.",
                "לא ניתן להחזיר את האירוע במצבו הנוכחי.");
        }
    }

    private ContentResult JsonResult(DeadLetterRequeueResponse response) =>
        new()
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(response, JsonOptions)
        };

    private ContentResult ProblemResult(
        int status,
        string code,
        string detailAr,
        string detailHe)
    {
        var hebrew = Request.GetTypedHeaders().AcceptLanguage?
            .Any(value => value.Value.Value?.StartsWith("he", StringComparison.OrdinalIgnoreCase) == true)
            == true;
        return new ContentResult
        {
            StatusCode = status,
            ContentType = "application/problem+json",
            Content = JsonSerializer.Serialize(new
            {
                type = $"https://api.ghseeli.example/errors/{code}",
                title = hebrew ? "הבקשה נדחתה." : "تم رفض الطلب.",
                status,
                detail = hebrew ? detailHe : detailAr,
                code,
                correlationId = HttpContext.TraceIdentifier
            }, JsonOptions)
        };
    }
}
