using Ghseeli.IntegrationContracts.Bookings;
using GhseeliApis.Services.Configuration;

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
            title = language == ConfigurationLanguageResolver.Hebrew
                ? "בקשת סטטוס ההזמנה נדחתה."
                : "تم رفض طلب حالة الحجز.",
            status,
            detail = detail ?? LocalizeDetail(code, language),
            code,
            correlationId
        };

    private static string LocalizeDetail(string code, string language) =>
        (code, language) switch
        {
            (BookingStatusErrorCodes.UnsupportedMediaType, ConfigurationLanguageResolver.Hebrew) =>
                "יש לשלוח את בקשת סטטוס ההזמנה כ-application/json.",
            (BookingStatusErrorCodes.UnsupportedMediaType, _) =>
                "يجب إرسال طلب حالة الحجز بصيغة application/json.",
            (BookingStatusErrorCodes.RequestBodyTooLarge, ConfigurationLanguageResolver.Hebrew) =>
                "גוף בקשת סטטוס ההזמנה חורג מהמגבלה המותרת של 64KB.",
            (BookingStatusErrorCodes.RequestBodyTooLarge, _) =>
                "يتجاوز حجم طلب حالة الحجز الحد المسموح وهو 64 كيلوبايت.",
            (_, ConfigurationLanguageResolver.Hebrew) =>
                "חוזה סטטוס ההזמנה אינו תקין.",
            _ => "عقد حالة الحجز غير صالح."
        };
}
