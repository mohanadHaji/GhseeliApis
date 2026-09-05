using FluentValidation;
using GhseeliApis.DTOs.Catalog;

namespace GhseeliApis.Validators.Catalog;

public sealed class GetCatalogBusinessOfferingsRequestValidator :
    AbstractValidator<GetCatalogBusinessOfferingsRequest>
{
    public GetCatalogBusinessOfferingsRequestValidator()
    {
        RuleFor(request => request.Language)
            .Cascade(CascadeMode.Stop)
            .MustUseSupportedLanguage()
            .When(request => request.Language is not null);

        RuleFor(request => request.BranchId)
            .MustUseNonEmptyCatalogId();

        RuleFor(request => request.CategoryId)
            .MustUseNonEmptyCatalogId();
    }
}
