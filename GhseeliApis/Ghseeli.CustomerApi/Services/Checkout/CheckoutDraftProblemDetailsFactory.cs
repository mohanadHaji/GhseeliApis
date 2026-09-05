using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Services.Checkout;

internal static class CheckoutDraftProblemDetailsFactory
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
            CheckoutDraftProblemCodes.Invalid => LocalizeDetail(code, language),
            CheckoutDraftProblemCodes.SelectionInvalid => LocalizeDetail(code, language),
            CheckoutDraftProblemCodes.SlotUnavailable => LocalizeDetail(code, language),
            CheckoutDraftProblemCodes.OutOfServiceArea => LocalizeDetail(code, language),
            CheckoutDraftProblemCodes.VersionConflict => LocalizeDetail(code, language),
            CheckoutDraftFieldErrorCodes.Required => language == ConfigurationLanguageResolver.Hebrew
                ? "שדה זה נדרש."
                : "هذا الحقل مطلوب.",
            CheckoutDraftFieldErrorCodes.GuidRequired => language == ConfigurationLanguageResolver.Hebrew
                ? "נדרש מזהה חוקי."
                : "يلزم معرّف صالح.",
            CheckoutDraftFieldErrorCodes.MaxLength => language == ConfigurationLanguageResolver.Hebrew
                ? "הערך חורג מהאורך המותר."
                : "القيمة تتجاوز الطول المسموح.",
            CheckoutDraftFieldErrorCodes.CoordinateFinite => language == ConfigurationLanguageResolver.Hebrew
                ? "הערך המספרי חייב להיות סופי."
                : "يجب أن تكون القيمة الرقمية منتهية.",
            CheckoutDraftFieldErrorCodes.CoordinateRange => language == ConfigurationLanguageResolver.Hebrew
                ? "הערך המספרי מחוץ לטווח המותר."
                : "القيمة الرقمية خارج النطاق المسموح.",
            CheckoutDraftFieldErrorCodes.RequestedSlotRequired => language == ConfigurationLanguageResolver.Hebrew
                ? "נדרש מועד שירות מבוקש."
                : "موعد الخدمة المطلوب مطلوب.",
            CheckoutDraftFieldErrorCodes.CollectionRequired => language == ConfigurationLanguageResolver.Hebrew
                ? "יש לספק לפחות פריט אחד."
                : "يجب توفير عنصر واحد على الأقل.",
            CheckoutDraftFieldErrorCodes.CollectionTooMany => language == ConfigurationLanguageResolver.Hebrew
                ? "חרגת מהמגבלה המרבית המותרת."
                : "تم تجاوز الحد الأقصى المسموح.",
            CheckoutDraftFieldErrorCodes.DuplicateOffering => language == ConfigurationLanguageResolver.Hebrew
                ? "אי אפשר לשלוח את אותו השירות יותר מפעם אחת."
                : "لا يمكن تكرار نفس الخدمة أكثر من مرة.",
            CheckoutDraftFieldErrorCodes.DuplicateAddonChoice => language == ConfigurationLanguageResolver.Hebrew
                ? "אי אפשר לשלוח את אותה התוספת יותר מפעם אחת לכל שירות."
                : "لا يمكن تكرار نفس الإضافة أكثر من مرة لكل خدمة.",
            CheckoutDraftFieldErrorCodes.SelectionQuantityRange => language == ConfigurationLanguageResolver.Hebrew
                ? "כמות התוספת חייבת להיות בין 0 ל-100."
                : "يجب أن تكون كمية الإضافة بين 0 و100.",
            CheckoutDraftFieldErrorCodes.ExpectedVersionInvalid => language == ConfigurationLanguageResolver.Hebrew
                ? "גרסת הטיוטה הצפויה חייבת להיות גדולה מאפס."
                : "يجب أن تكون نسخة المسودة المتوقعة أكبر من صفر.",
            _ => LocalizeDetail(CheckoutDraftProblemCodes.Invalid, language)
        };

    private static string LocalizeTitle(int statusCode, string language) =>
        language == ConfigurationLanguageResolver.Hebrew
            ? statusCode == StatusCodes.Status400BadRequest
                ? "אימות הבקשה נכשל."
                : statusCode == StatusCodes.Status404NotFound
                    ? "הטיוטה המבוקשת לא נמצאה."
                    : statusCode == StatusCodes.Status409Conflict
                        ? "טיוטת התשלום התנגשה."
                        : statusCode == StatusCodes.Status410Gone
                            ? "תוקף הטיוטה פג."
                            : "לא ניתן להשלים את הבקשה."
            : statusCode == StatusCodes.Status400BadRequest
                ? "فشل التحقق من صحة الطلب."
                : statusCode == StatusCodes.Status404NotFound
                    ? "تعذر العثور على المسودة المطلوبة."
                    : statusCode == StatusCodes.Status409Conflict
                        ? "حدث تعارض في مسودة الدفع."
                        : statusCode == StatusCodes.Status410Gone
                            ? "انتهت صلاحية المسودة."
                            : "تعذر إكمال الطلب.";

    private static string LocalizeDetail(string code, string language) =>
        (code, language) switch
        {
            (ConfigurationProblemCodes.LanguageInvalid, ConfigurationLanguageResolver.Hebrew) =>
                "שפת הבקשה אינה נתמכת. השתמש ב-ar או ב-he.",
            (ConfigurationProblemCodes.LanguageInvalid, _) =>
                "لغة الطلب غير مدعومة. استخدم ar أو he.",
            (CheckoutDraftProblemCodes.SelectionInvalid, ConfigurationLanguageResolver.Hebrew) =>
                "פריטי טיוטת התשלום שנבחרו אינם זמינים כעת.",
            (CheckoutDraftProblemCodes.SelectionInvalid, _) =>
                "عناصر مسودة الدفع المحددة غير متاحة حالياً.",
            (CheckoutDraftProblemCodes.SlotUnavailable, ConfigurationLanguageResolver.Hebrew) =>
                "מועד השירות המבוקש אינו זמין.",
            (CheckoutDraftProblemCodes.SlotUnavailable, _) =>
                "موعد الخدمة المطلوب غير متاح.",
            (CheckoutDraftProblemCodes.OutOfServiceArea, ConfigurationLanguageResolver.Hebrew) =>
                "מיקום השירות מחוץ לאזור השירות.",
            (CheckoutDraftProblemCodes.OutOfServiceArea, _) =>
                "موقع الخدمة خارج نطاق التغطية.",
            (CheckoutDraftProblemCodes.NotFound, ConfigurationLanguageResolver.Hebrew) =>
                "טיוטת התשלום המבוקשת לא נמצאה.",
            (CheckoutDraftProblemCodes.NotFound, _) =>
                "لم يتم العثور على مسودة الدفع المطلوبة.",
            (CheckoutDraftProblemCodes.Expired, ConfigurationLanguageResolver.Hebrew) =>
                "תוקף טיוטת התשלום פג ולא ניתן להשתמש בה.",
            (CheckoutDraftProblemCodes.Expired, _) =>
                "انتهت صلاحية مسودة الدفع ولا يمكن استخدامها.",
            (CheckoutDraftProblemCodes.VersionConflict, ConfigurationLanguageResolver.Hebrew) =>
                "טיוטת התשלום השתנתה. רענן ונסה שוב.",
            (CheckoutDraftProblemCodes.VersionConflict, _) =>
                "تم تعديل مسودة الدفع. حدّث البيانات وأعد المحاولة.",
            (CheckoutDraftProblemCodes.Invalid, ConfigurationLanguageResolver.Hebrew) =>
                "נתוני טיוטת התשלום אינם תקינים.",
            _ =>
                "بيانات مسودة الدفع غير صالحة."
        };
}
