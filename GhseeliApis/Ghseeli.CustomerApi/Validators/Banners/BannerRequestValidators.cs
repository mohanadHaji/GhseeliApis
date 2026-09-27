using FluentValidation;
using GhseeliApis.DTOs.Banners;
using GhseeliApis.Services.Banners;
using GhseeliApis.Validation;

namespace GhseeliApis.Validators.Banners;

public sealed class CreateBannerRequestValidator : AbstractValidator<CreateBannerRequest>
{
    public CreateBannerRequestValidator()
    {
        RuleFor(request => request.ImageUrl)
            .Must(BannerImageUrlValidation.IsValid)
            .WithErrorCode(BannerProblemCodes.Invalid);
        RuleFor(request => request.DisplayOrder)
            .InclusiveBetween(0, 10000)
            .WithErrorCode(BannerProblemCodes.Invalid);
    }
}

public sealed class UpdateBannerRequestValidator : AbstractValidator<UpdateBannerRequest>
{
    public UpdateBannerRequestValidator()
    {
        RuleFor(request => request.ImageUrl)
            .Must(BannerImageUrlValidation.IsValid)
            .WithErrorCode(BannerProblemCodes.Invalid);
        RuleFor(request => request.DisplayOrder)
            .InclusiveBetween(0, 10000)
            .WithErrorCode(BannerProblemCodes.Invalid);
        RuleFor(request => request.ExpectedRowVersion)
            .Must(BannerRowVersion.IsValid)
            .WithErrorCode(BannerProblemCodes.Invalid);
    }
}
