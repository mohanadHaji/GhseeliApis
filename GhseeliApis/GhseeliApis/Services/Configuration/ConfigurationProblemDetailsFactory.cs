using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Services.Configuration;

internal static class ConfigurationProblemDetailsFactory
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
            Title = LocalizeTitle(statusCode, code, language),
            Detail = LocalizeDetail(code, language),
            Type = $"https://api.ghseeli.example/errors/{code}"
        };

        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["language"] = language;

        if (fieldErrors is not null && fieldErrors.Count > 0)
        {
            problem.Extensions["fieldErrors"] = new SortedDictionary<string, string[]>(
                fieldErrors
                    .GroupBy(entry => entry.Key.Trim().ToLowerInvariant(), StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.SelectMany(entry => entry.Value)
                            .Where(message => !string.IsNullOrWhiteSpace(message))
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal),
                StringComparer.Ordinal);
        }

        return problem;
    }

    public static string LocalizeFieldMessage(string code, string language) =>
        code == ConfigurationProblemCodes.LanguageInvalid
            ? LocalizeDetail(code, language)
            : LocalizeDetail(ConfigurationProblemCodes.Unavailable, language);

    private static string LocalizeTitle(int statusCode, string code, string language) =>
        code == ConfigurationProblemCodes.LanguageInvalid
            ? language == ConfigurationLanguageResolver.Hebrew
                ? "אימות הבקשה נכשל."
                : "فشل التحقق من صحة الطلب."
            : language == ConfigurationLanguageResolver.Hebrew
                ? "לא ניתן להשלים את הבקשה."
                : "تعذر إكمال الطلب.";

    private static string LocalizeDetail(string code, string language) =>
        (code, language) switch
        {
            (ConfigurationProblemCodes.LanguageInvalid, ConfigurationLanguageResolver.Hebrew) =>
                "השפה המבוקשת אינה נתמכת.",
            (ConfigurationProblemCodes.LanguageInvalid, _) =>
                "اللغة المطلوبة غير مدعومة.",
            ("language_invalid", ConfigurationLanguageResolver.Hebrew) =>
                "השפה המבוקשת אינה נתמכת.",
            ("language_invalid", _) => "اللغة المطلوبة غير مدعومة.",
            ("request_invalid", ConfigurationLanguageResolver.Hebrew) => "הבקשה אינה חוקית.",
            ("request_invalid", _) => "الطلب غير صالح.",
            ("customer_authentication_required", ConfigurationLanguageResolver.Hebrew) =>
                "נדרש אימות לקוח.",
            ("customer_authentication_required", _) => "مطلوب تسجيل دخول العميل.",
            ("customer_authorization_forbidden", ConfigurationLanguageResolver.Hebrew) =>
                "ללקוח אין הרשאה לבצע בקשה זו.",
            ("customer_authorization_forbidden", _) =>
                "لا يملك العميل صلاحية تنفيذ هذا الطلب.",
            ("resource_not_found", ConfigurationLanguageResolver.Hebrew) =>
                "המשאב המבוקש לא נמצא.",
            ("resource_not_found", _) => "المورد المطلوب غير موجود.",
            ("method_not_allowed", ConfigurationLanguageResolver.Hebrew) =>
                "שיטת HTTP אינה מותרת עבור נתיב זה.",
            ("method_not_allowed", _) => "طريقة HTTP غير مسموحة لهذا المسار.",
            ("request_body_too_large", ConfigurationLanguageResolver.Hebrew) =>
                "גוף הבקשה חורג מהמגבלה המותרת.",
            ("request_body_too_large", _) => "حجم نص الطلب يتجاوز الحد المسموح.",
            ("unsupported_media_type", ConfigurationLanguageResolver.Hebrew) =>
                "סוג התוכן של הבקשה אינו נתמך.",
            ("unsupported_media_type", _) => "نوع محتوى الطلب غير مدعوم.",
            ("request_conflict", ConfigurationLanguageResolver.Hebrew) =>
                "הבקשה מתנגשת עם המצב הנוכחי.",
            ("request_conflict", _) => "يتعارض الطلب مع الحالة الحالية.",
            ("unexpected_error", ConfigurationLanguageResolver.Hebrew) =>
                "אירעה שגיאה בלתי צפויה.",
            ("unexpected_error", _) => "حدث خطأ غير متوقع.",
            ("service_unavailable", ConfigurationLanguageResolver.Hebrew) =>
                "השירות אינו זמין זמנית.",
            ("service_unavailable", _) => "الخدمة غير متاحة مؤقتًا.",
            (ConfigurationProblemCodes.Unavailable, ConfigurationLanguageResolver.Hebrew) =>
                "תצורת היישום אינה זמינה כעת.",
            _ =>
                "إعدادات التطبيق غير متاحة حالياً."
        };
}
