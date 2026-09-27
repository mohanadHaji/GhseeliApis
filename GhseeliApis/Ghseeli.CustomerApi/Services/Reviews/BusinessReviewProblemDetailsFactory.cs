using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Services.Reviews;

public static class BusinessReviewProblemDetailsFactory
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
            Title = hebrew ? "לא ניתן להשלים את פעולת הביקורת." : "تعذر إكمال عملية التقييم.",
            Detail = (code, hebrew) switch
            {
                (BusinessReviewProblemCodes.BookingNotFound, true) => "ההזמנה המבוקשת לא נמצאה.",
                (BusinessReviewProblemCodes.BookingNotFound, false) => "لم يتم العثور على الحجز المطلوب.",
                (BusinessReviewProblemCodes.ReviewNotFound, true) => "הביקורת המבוקשת לא נמצאה.",
                (BusinessReviewProblemCodes.ReviewNotFound, false) => "لم يتم العثور على التقييم المطلوب.",
                (BusinessReviewProblemCodes.BookingNotCompleted, true) => "ניתן לכתוב ביקורת רק לאחר השלמת ההזמנה.",
                (BusinessReviewProblemCodes.BookingNotCompleted, false) => "يمكن كتابة التقييم بعد اكتمال الحجز فقط.",
                (BusinessReviewProblemCodes.VersionConflict, true) => "הביקורת השתנתה. רענן ונסה שוב.",
                (BusinessReviewProblemCodes.VersionConflict, false) => "تم تعديل التقييم. حدّث البيانات وأعد المحاولة.",
                (BusinessReviewProblemCodes.PaginationInvalid, true) => "ערכי העימוד אינם תקינים.",
                (BusinessReviewProblemCodes.PaginationInvalid, false) => "قيم التصفح غير صالحة.",
                (BusinessReviewProblemCodes.BusinessNotFound, true) => "בית העסק המבוקש לא נמצא.",
                (BusinessReviewProblemCodes.BusinessNotFound, false) => "لم يتم العثور على النشاط التجاري المطلوب.",
                (_, true) => "בקשת הביקורת אינה תקינה.",
                _ => "طلب التقييم غير صالح."
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
