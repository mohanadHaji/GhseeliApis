using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Services.Payments;

public static class CustomerPaymentProblemDetailsFactory
{
    public static ProblemDetails Create(
        int status,
        string code,
        string language,
        string correlationId,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        if (code is "customer_authentication_required" or
            "customer_authorization_forbidden")
        {
            return ConfigurationProblemDetailsFactory.Create(
                status,
                code,
                language,
                correlationId,
                fieldErrors?.ToDictionary(entry => entry.Key, entry => entry.Value));
        }

        var hebrew = language == ConfigurationLanguageResolver.Hebrew;
        var (title, detail) = code switch
        {
            CustomerPaymentErrorCodes.RequestTooLarge => hebrew
                ? ("בקשת התשלום גדולה מדי.", "בקשת התשלום חורגת מהמגבלה המותרת.")
                : ("طلب الدفع كبير جداً.", "يتجاوز طلب الدفع الحد المسموح."),
            CustomerPaymentErrorCodes.PaymentUnsupportedMediaType => hebrew
                ? ("בקשת התשלום אינה תקינה.", "יש לשלוח את בקשת התשלום כ-JSON.")
                : ("طلب الدفع غير صالح.", "يجب إرسال طلب الدفع بصيغة JSON."),
            CustomerPaymentErrorCodes.NotFound => hebrew
                ? ("לא ניתן להשלים את בקשת התשלום.", "התשלום לא נמצא.")
                : ("تعذر إكمال طلب الدفع.", "تعذر العثور على الدفعة."),
            CustomerPaymentErrorCodes.MethodUnavailable => hebrew
                ? ("לא ניתן להשלים את בקשת התשלום.", "אמצעי התשלום אינו זמין.")
                : ("تعذر إكمال طلب الدفع.", "طريقة الدفع غير متاحة."),
            CustomerPaymentErrorCodes.ProviderUnavailable => hebrew
                ? ("לא ניתן להשלים את בקשת התשלום.", "ספק התשלום אינו זמין זמנית.")
                : ("تعذر إكمال طلب الدفع.", "مزود الدفع غير متاح مؤقتاً."),
            CustomerPaymentErrorCodes.Ineligible => hebrew
                ? ("לא ניתן להשלים את בקשת התשלום.", "ההזמנה אינה כשירה לתשלום.")
                : ("تعذر إكمال طلب الدفع.", "الحجز غير مؤهل للدفع."),
            _ => hebrew
                ? ("לא ניתן להשלים את בקשת התשלום.", "לא ניתן להשלים את בקשת התשלום.")
                : ("تعذر إكمال طلب الدفع.", "تعذر إكمال طلب الدفع.")
        };

        var problem = new ProblemDetails
        {
            Type = $"https://api.ghseeli.example/errors/{code}",
            Status = status,
            Title = title,
            Detail = detail
        };
        problem.Extensions["code"] = code;
        problem.Extensions["language"] = language;
        problem.Extensions["correlationId"] = correlationId;
        if (fieldErrors is not null && fieldErrors.Count > 0)
        {
            problem.Extensions["fieldErrors"] = fieldErrors;
        }

        return problem;
    }
}
