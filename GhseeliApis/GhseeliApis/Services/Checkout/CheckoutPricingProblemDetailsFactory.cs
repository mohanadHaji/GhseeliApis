using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Services.Checkout;

internal static class CheckoutPricingProblemDetailsFactory
{
    public static ProblemDetails Create(
        int statusCode,
        string code,
        string language,
        string correlationId,
        IDictionary<string, string[]>? fieldErrors = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = LocalizeTitle(statusCode, language),
            Detail = LocalizeDetail(code, language),
            Type = $"https://api.ghseeli.example/errors/{code}"
        };

        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["language"] = language;

        if (fieldErrors is not null && fieldErrors.Count > 0)
        {
            problem.Extensions["fieldErrors"] = fieldErrors;
        }

        return problem;
    }

    public static string LocalizeFieldMessage(string code, string language) =>
        code switch
        {
            ConfigurationProblemCodes.LanguageInvalid => LocalizeDetail(code, language),
            CheckoutPricingProblemCodes.Invalid => LocalizeDetail(code, language),
            CheckoutPricingProblemCodes.SelectionInvalid => LocalizeDetail(code, language),
            CheckoutPricingProblemCodes.SlotUnavailable => LocalizeDetail(code, language),
            CheckoutPricingProblemCodes.OutOfServiceArea => LocalizeDetail(code, language),
            CheckoutPricingProblemCodes.Unavailable => LocalizeDetail(code, language),
            CheckoutPricingProblemCodes.UnsupportedMediaType => LocalizeDetail(code, language),
            CheckoutPricingProblemCodes.RequestBodyTooLarge => LocalizeDetail(code, language),
            CheckoutDraftProblemCodes.NotFound => LocalizeDetail(code, language),
            CheckoutDraftProblemCodes.Expired => LocalizeDetail(code, language),
            CheckoutDraftProblemCodes.VersionConflict => LocalizeDetail(code, language),
            _ => CheckoutDraftProblemDetailsFactory.LocalizeFieldMessage(code, language)
        };

    private static string LocalizeTitle(int statusCode, string language) =>
        language == ConfigurationLanguageResolver.Hebrew
            ? statusCode == StatusCodes.Status400BadRequest
                ? "אימות התמחור נכשל."
                : statusCode == StatusCodes.Status413PayloadTooLarge
                    ? "גוף הבקשה גדול מדי."
                    : statusCode == StatusCodes.Status415UnsupportedMediaType
                        ? "סוג התוכן אינו נתמך."
                : statusCode == StatusCodes.Status404NotFound
                    ? "טיוטת התשלום המבוקשת לא נמצאה."
                    : statusCode == StatusCodes.Status409Conflict
                        ? "בקשת התמחור התנגשה."
                        : statusCode == StatusCodes.Status410Gone
                            ? "תוקף הטיוטה פג."
                            : statusCode == StatusCodes.Status503ServiceUnavailable
                                ? "התמחור הסמכותי אינו זמין כעת."
                                : "לא ניתן להשלים את הבקשה."
            : statusCode == StatusCodes.Status400BadRequest
                ? "فشل التحقق من التسعير."
                : statusCode == StatusCodes.Status413PayloadTooLarge
                    ? "حجم طلب التسعير كبير جداً."
                    : statusCode == StatusCodes.Status415UnsupportedMediaType
                        ? "نوع محتوى الطلب غير مدعوم."
                : statusCode == StatusCodes.Status404NotFound
                    ? "تعذر العثور على مسودة الدفع المطلوبة."
                    : statusCode == StatusCodes.Status409Conflict
                        ? "حدث تعارض في طلب التسعير."
                        : statusCode == StatusCodes.Status410Gone
                            ? "انتهت صلاحية المسودة."
                            : statusCode == StatusCodes.Status503ServiceUnavailable
                                ? "التسعير المعتمد غير متاح حالياً."
                                : "تعذر إكمال الطلب.";

    private static string LocalizeDetail(string code, string language) =>
        (code, language) switch
        {
            (ConfigurationProblemCodes.LanguageInvalid, ConfigurationLanguageResolver.Hebrew) =>
                "שפת הבקשה אינה נתמכת. השתמש ב-ar או ב-he.",
            (ConfigurationProblemCodes.LanguageInvalid, _) =>
                "لغة الطلب غير مدعومة. استخدم ar أو he.",
            (CheckoutPricingProblemCodes.SelectionInvalid, ConfigurationLanguageResolver.Hebrew) =>
                "הבחירות שנשלחו אינן זמינות עוד עבור הצעת המחיר.",
            (CheckoutPricingProblemCodes.SelectionInvalid, _) =>
                "العناصر المحددة لم تعد متاحة لإعادة التسعير.",
            (CheckoutPricingProblemCodes.SlotUnavailable, ConfigurationLanguageResolver.Hebrew) =>
                "מועד השירות המבוקש אינו זמין לתמחור.",
            (CheckoutPricingProblemCodes.SlotUnavailable, _) =>
                "موعد الخدمة المطلوب غير متاح للتسعير.",
            (CheckoutPricingProblemCodes.OutOfServiceArea, ConfigurationLanguageResolver.Hebrew) =>
                "مיקום השירות מחוץ לאזור הנתמך לתמחור.",
            (CheckoutPricingProblemCodes.OutOfServiceArea, _) =>
                "موقع الخدمة خارج النطاق المدعوم للتسعير.",
            (CheckoutDraftProblemCodes.NotFound, ConfigurationLanguageResolver.Hebrew) =>
                "טיוטת התשלום המבוקשת לא נמצאה.",
            (CheckoutDraftProblemCodes.NotFound, _) =>
                "لم يتم العثور على مسودة الدفع المطلوبة.",
            (CheckoutDraftProblemCodes.Expired, ConfigurationLanguageResolver.Hebrew) =>
                "תוקף טיוטת התשלום פג ולא ניתן לתמחר אותה.",
            (CheckoutDraftProblemCodes.Expired, _) =>
                "انتهت صلاحية مسودة الدفع ولا يمكن إعادة تسعيرها.",
            (CheckoutDraftProblemCodes.VersionConflict, ConfigurationLanguageResolver.Hebrew) =>
                "טיוטת התשלום השתנתה. רענן ונסה שוב.",
            (CheckoutDraftProblemCodes.VersionConflict, _) =>
                "تم تعديل مسودة الدفع. حدّث البيانات وأعد المحاولة.",
            (CheckoutPricingProblemCodes.Unavailable, ConfigurationLanguageResolver.Hebrew) =>
                "לא ניתן לקבל תמחור סמכותי כעת. נסה שוב מאוחר יותר.",
            (CheckoutPricingProblemCodes.Unavailable, _) =>
                "تعذر الحصول على تسعير معتمد حالياً. حاول مرة أخرى لاحقاً.",
            (CheckoutPricingProblemCodes.UnsupportedMediaType, ConfigurationLanguageResolver.Hebrew) =>
                "יש לשלוח את בקשת התמחור כ-application/json.",
            (CheckoutPricingProblemCodes.UnsupportedMediaType, _) =>
                "يجب إرسال طلب التسعير بصيغة application/json.",
            (CheckoutPricingProblemCodes.RequestBodyTooLarge, ConfigurationLanguageResolver.Hebrew) =>
                "גוף בקשת התמחור חורג מהמגבלה המותרת של 64KB.",
            (CheckoutPricingProblemCodes.RequestBodyTooLarge, _) =>
                "يتجاوز حجم طلب التسعير الحد المسموح وهو 64 كيلوبايت.",
            (CheckoutPricingProblemCodes.Invalid, ConfigurationLanguageResolver.Hebrew) =>
                "طلب إعادة התמחור אינו תקין.",
            _ =>
                "طلب إعادة التسعير غير صالح."
        };
}
