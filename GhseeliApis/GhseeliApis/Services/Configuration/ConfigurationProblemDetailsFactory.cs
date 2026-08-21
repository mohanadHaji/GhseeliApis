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
        code == ConfigurationProblemCodes.LanguageInvalid
            ? LocalizeDetail(code, language)
            : LocalizeDetail(ConfigurationProblemCodes.Unavailable, language);

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
            (ConfigurationProblemCodes.Unavailable, ConfigurationLanguageResolver.Hebrew) =>
                "תצורת היישום אינה זמינה כעת.",
            _ =>
                "إعدادات التطبيق غير متاحة حالياً."
        };
}
