using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Services.Catalog;

internal static class CatalogProblemDetailsFactory
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
            CatalogProblemCodes.FilterMismatch => LocalizeDetail(code, language),
            _ => LocalizeDetail(CatalogProblemCodes.Unavailable, language)
        };

    private static string LocalizeTitle(int statusCode, string language) =>
        language == ConfigurationLanguageResolver.Hebrew
            ? statusCode == StatusCodes.Status400BadRequest
                ? "אימות הבקשה נכשל."
                : "לא ניתן להשלים את הבקשה."
            : statusCode == StatusCodes.Status400BadRequest
                ? "فشل التحقق من صحة الطلب."
                : "تعذر إكمال الطلب.";

    private static string LocalizeDetail(string code, string language) =>
        (code, language) switch
        {
            (ConfigurationProblemCodes.LanguageInvalid, ConfigurationLanguageResolver.Hebrew) =>
                "שפת הבקשה אינה נתמכת. השתמש ב-ar או ב-he.",
            (ConfigurationProblemCodes.LanguageInvalid, _) =>
                "لغة الطلب غير مدعومة. استخدم ar أو he.",
            (CatalogProblemCodes.BusinessNotFound, ConfigurationLanguageResolver.Hebrew) =>
                "העסק המבוקש לא נמצא.",
            (CatalogProblemCodes.BusinessNotFound, _) =>
                "لم يتم العثور على النشاط التجاري المطلوب.",
            (CatalogProblemCodes.OfferingNotFound, ConfigurationLanguageResolver.Hebrew) =>
                "השירות המבוקש לא נמצא.",
            (CatalogProblemCodes.OfferingNotFound, _) =>
                "لم يتم العثور على الخدمة المطلوبة.",
            (CatalogProblemCodes.FilterMismatch, ConfigurationLanguageResolver.Hebrew) =>
                "מסנני הקטלוג שסופקו אינם שייכים לאותו עסק.",
            (CatalogProblemCodes.FilterMismatch, _) =>
                "مرشحات الكتالوج المقدمة لا تنتمي إلى نفس النشاط التجاري.",
            (CatalogProblemCodes.StaleVersion, ConfigurationLanguageResolver.Hebrew) =>
                "קטלוג השירותים השתנה. יש לרענן ולנסות שוב.",
            (CatalogProblemCodes.StaleVersion, _) =>
                "تم تحديث كتالوج الخدمات. يرجى التحديث والمحاولة مرة أخرى.",
            (CatalogProblemCodes.Unavailable, ConfigurationLanguageResolver.Hebrew) =>
                "קטלוג השירותים אינו זמין כעת.",
            _ =>
                "كتالوج الخدمات غير متاح حالياً."
        };
}
