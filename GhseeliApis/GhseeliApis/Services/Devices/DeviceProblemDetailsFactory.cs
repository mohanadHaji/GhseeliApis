using GhseeliApis.Services.Configuration;

namespace GhseeliApis.Services.Devices;

internal static class DeviceProblemDetailsFactory
{
    public static object Create(
        int statusCode,
        string code,
        string language,
        string correlationId) =>
        new
        {
            type = $"https://api.ghseeli.example/errors/{code}",
            title = LocalizeTitle(language),
            status = statusCode,
            detail = LocalizeDetail(code, language),
            code,
            correlationId,
            language
        };

    private static string LocalizeTitle(string language) =>
        language == ConfigurationLanguageResolver.Hebrew
            ? "אימות המכשיר נכשל."
            : "فشل التحقق من الجهاز.";

    private static string LocalizeDetail(string code, string language) =>
        (code, language) switch
        {
            (DeviceProblemCodes.TokenMissing, ConfigurationLanguageResolver.Hebrew) =>
                "נדרש אסימון מכשיר.",
            (DeviceProblemCodes.TokenInvalid, ConfigurationLanguageResolver.Hebrew) =>
                "אסימון המכשיר אינו תקין.",
            (DeviceProblemCodes.TokenExpired, ConfigurationLanguageResolver.Hebrew) =>
                "פג תוקף אסימון המכשיר.",
            (DeviceProblemCodes.TokenMissing, _) =>
                "رمز الجهاز مطلوب.",
            (DeviceProblemCodes.TokenExpired, _) =>
                "انتهت صلاحية رمز الجهاز.",
            _ =>
                "رمز الجهاز غير صالح."
        };
}
