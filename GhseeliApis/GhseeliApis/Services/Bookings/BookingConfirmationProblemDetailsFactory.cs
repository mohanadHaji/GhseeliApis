using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Services.Bookings;

internal static class BookingConfirmationProblemDetailsFactory
{
    public static ProblemDetails Create(
        int statusCode,
        string code,
        string language,
        string correlationId,
        string? businessErrorCode = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = language == ConfigurationLanguageResolver.Hebrew
                ? "לא ניתן לאשר את ההזמנה."
                : "تعذر تأكيد الحجز.",
            Detail = Localize(code, language),
            Type = $"https://api.ghseeli.example/errors/{code}"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["language"] = language;
        if (!string.IsNullOrWhiteSpace(businessErrorCode))
        {
            problem.Extensions["businessErrorCode"] = businessErrorCode;
        }
        return problem;
    }

    private static string Localize(string code, string language) =>
        (code, language) switch
        {
            (BookingConfirmationProblemCodes.DraftNotFound, ConfigurationLanguageResolver.Hebrew) =>
                "טיוטת התשלום המבוקשת לא נמצאה.",
            (BookingConfirmationProblemCodes.DraftNotFound, _) =>
                "لم يتم العثور على مسودة الدفع المطلوبة.",
            (BookingConfirmationProblemCodes.DraftExpired, ConfigurationLanguageResolver.Hebrew) =>
                "תוקף טיוטת התשלום פג.",
            (BookingConfirmationProblemCodes.DraftExpired, _) =>
                "انتهت صلاحية مسودة الدفع.",
            (BookingConfirmationProblemCodes.DraftRequiresReprice, ConfigurationLanguageResolver.Hebrew) =>
                "יש לתמחר מחדש את טיוטת התשלום לפני האישור.",
            (BookingConfirmationProblemCodes.DraftRequiresReprice, _) =>
                "يجب إعادة تسعير مسودة الدفع قبل التأكيد.",
            (BookingConfirmationProblemCodes.DraftUnpriced, ConfigurationLanguageResolver.Hebrew) =>
                "אין לטיוטת התשלום מחיר מאומת.",
            (BookingConfirmationProblemCodes.DraftUnpriced, _) =>
                "لا تحتوي مسودة الدفع على سعر موثوق.",
            (BookingConfirmationProblemCodes.VersionConflict, ConfigurationLanguageResolver.Hebrew) =>
                "טיוטת התשלום השתנתה. רענן ונסה שוב.",
            (BookingConfirmationProblemCodes.VersionConflict, _) =>
                "تم تعديل مسودة الدفع. حدّث البيانات وأعد المحاولة.",
            (BookingConfirmationProblemCodes.ReservationRejected, ConfigurationLanguageResolver.Hebrew) =>
                "לא ניתן לשמור את מועד השירות המבוקש.",
            (BookingConfirmationProblemCodes.ReservationRejected, _) =>
                "تعذر حجز موعد الخدمة المطلوب.",
            (BookingConfirmationProblemCodes.Unavailable, ConfigurationLanguageResolver.Hebrew) =>
                "שירות אישור ההזמנה אינו זמין כעת. נסה שוב עם אותה טיוטה.",
            (BookingConfirmationProblemCodes.Unavailable, _) =>
                "خدمة تأكيد الحجز غير متاحة حالياً. أعد المحاولة باستخدام المسودة نفسها.",
            (BookingConfirmationProblemCodes.RequestBodyTooLarge, ConfigurationLanguageResolver.Hebrew) =>
                "גוף בקשת אישור ההזמנה גדול מדי.",
            (BookingConfirmationProblemCodes.RequestBodyTooLarge, _) =>
                "حجم طلب تأكيد الحجز أكبر من الحد المسموح.",
            (BookingConfirmationProblemCodes.UnsupportedMediaType, ConfigurationLanguageResolver.Hebrew) =>
                "יש לשלוח את בקשת אישור ההזמנה כ-JSON.",
            (BookingConfirmationProblemCodes.UnsupportedMediaType, _) =>
                "يجب إرسال طلب تأكيد الحجز بصيغة JSON.",
            (BookingConfirmationProblemCodes.Invalid, ConfigurationLanguageResolver.Hebrew) =>
                "בקשת אישור ההזמנה אינה תקינה.",
            _ => "طلب تأكيد الحجز غير صالح."
        };
}
