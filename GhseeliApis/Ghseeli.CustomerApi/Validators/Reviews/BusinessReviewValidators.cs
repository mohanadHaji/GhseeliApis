using FluentValidation;
using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Services.Reviews;

namespace GhseeliApis.Validators.Reviews;

public sealed class PutBusinessReviewRequestValidator : AbstractValidator<PutBusinessReviewRequest>
{
    public PutBusinessReviewRequestValidator()
    {
        RuleFor(request => request.Rating)
            .InclusiveBetween(1, 5)
            .WithErrorCode(BusinessReviewProblemCodes.Invalid);
        RuleFor(request => request.Comment)
            .Must(value => value is null || value.Trim().Length <= 1000)
            .WithErrorCode(BusinessReviewProblemCodes.Invalid);
        RuleFor(request => request.ExpectedRowVersion)
            .Must(BeValidBase64)
            .When(request => request.ExpectedRowVersion is not null)
            .WithErrorCode(BusinessReviewProblemCodes.Invalid);
    }

    private static bool BeValidBase64(string? value)
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

public sealed class GetBusinessReviewsRequestValidator : AbstractValidator<GetBusinessReviewsRequest>
{
    public GetBusinessReviewsRequestValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1)
            .WithErrorCode(BusinessReviewProblemCodes.PaginationInvalid);
        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, 50)
            .WithErrorCode(BusinessReviewProblemCodes.PaginationInvalid);
        RuleFor(request => request.Language)
            .Must(value => value is null || value is "ar" or "he")
            .WithErrorCode(BusinessReviewProblemCodes.Invalid);
    }
}
