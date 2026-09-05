using Ghseeli.IntegrationContracts.Bookings;
namespace GhseeliApis.Services.Bookings;

public static class BookingStatusProblemDetailsFactory
{
    public static object Create(
        int status,
        string code,
        string language,
        string correlationId,
        string? detail = null) => new
        {
            type = $"https://api.ghseeli.example/errors/{code}",
            title = "Internal booking status request was rejected.",
            status,
            detail = StableDetail(code),
            code,
            correlationId
        };

    private static string StableDetail(string code) =>
        code switch
        {
            BookingStatusErrorCodes.UnsupportedMediaType =>
                "The booking status request must use application/json.",
            BookingStatusErrorCodes.RequestBodyTooLarge =>
                "The booking status request exceeds the 64KB limit.",
            BookingStatusErrorCodes.NotFound =>
                "The booking reference was not found.",
            BookingStatusErrorCodes.ReferenceMismatch =>
                "The booking references do not match.",
            BookingStatusErrorCodes.TransitionInvalid =>
                "The booking status transition is invalid.",
            BookingStatusErrorCodes.TransitionConflict =>
                "The booking status transition conflicts with current state.",
            BookingStatusErrorCodes.EventConflict =>
                "The booking status event conflicts with an existing event.",
            _ => "The booking status contract is invalid."
        };
}
