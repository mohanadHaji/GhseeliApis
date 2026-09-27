using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Services.Banners;

public static class BannerProblemCodes
{
    public const string Invalid = "banner_invalid";
    public const string NotFound = "banner_not_found";
    public const string VersionConflict = "banner_version_conflict";
}

public static class BannerRowVersion
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(value).Length == 8;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class BannerProblemDetailsFactory
{
    public static ProblemDetails Create(
        int status,
        string code,
        string language,
        string correlationId,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        var hebrew = language == ConfigurationLanguageResolver.Hebrew;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = hebrew
                ? "לא ניתן להשלים את פעולת הבאנר."
                : "تعذر إكمال عملية اللافتة.",
            Detail = (code, hebrew) switch
            {
                (BannerProblemCodes.NotFound, true) => "הבאנר המבוקש לא נמצא.",
                (BannerProblemCodes.NotFound, false) => "لم يتم العثور على اللافتة المطلوبة.",
                (BannerProblemCodes.VersionConflict, true) =>
                    "הבאנר השתנה. רענן ונסה שוב.",
                (BannerProblemCodes.VersionConflict, false) =>
                    "تم تعديل اللافتة. حدّث البيانات وأعد المحاولة.",
                (_, true) => "בקשת הבאנר אינה תקינה.",
                _ => "طلب اللافتة غير صالح."
            },
            Type = $"https://api.ghseeli.example/errors/{code}"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["language"] = language;
        if (fieldErrors is not null)
        {
            problem.Extensions["fieldErrors"] = fieldErrors;
        }

        return problem;
    }
}
