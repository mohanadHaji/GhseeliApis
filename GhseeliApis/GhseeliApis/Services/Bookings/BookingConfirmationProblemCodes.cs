namespace GhseeliApis.Services.Bookings;

public static class BookingConfirmationProblemCodes
{
    public const string Invalid = "booking_confirmation_invalid";
    public const string DraftNotFound = "checkout_draft_not_found";
    public const string DraftExpired = "checkout_draft_expired";
    public const string DraftRequiresReprice = "checkout_draft_requires_reprice";
    public const string DraftUnpriced = "checkout_draft_unpriced";
    public const string VersionConflict = "checkout_draft_version_conflict";
    public const string ReservationRejected = "booking_reservation_rejected";
    public const string Unavailable = "booking_confirmation_unavailable";
    public const string RequestBodyTooLarge = "booking_request_body_too_large";
    public const string UnsupportedMediaType = "booking_unsupported_media_type";
}
