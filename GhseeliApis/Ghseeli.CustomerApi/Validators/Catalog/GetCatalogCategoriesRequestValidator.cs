using FluentValidation;
using GhseeliApis.DTOs.Catalog;

namespace GhseeliApis.Validators.Catalog;

public sealed class GetCatalogCategoriesRequestValidator :
    AbstractValidator<GetCatalogCategoriesRequest>
{
    public GetCatalogCategoriesRequestValidator()
    {
        RuleFor(request => request.Language)
            .Cascade(CascadeMode.Stop)
            .MustUseSupportedLanguage()
            .When(request => request.Language is not null);

        RuleFor(request => request.BusinessId)
            .MustUseNonEmptyCatalogId();
    }
}
