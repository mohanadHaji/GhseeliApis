using FluentValidation;
using GhseeliApis.DTOs.Catalog;

namespace GhseeliApis.Validators.Catalog;

public sealed class GetCatalogResourceRequestValidator :
    AbstractValidator<GetCatalogResourceRequest>
{
    public GetCatalogResourceRequestValidator()
    {
        RuleFor(request => request.Language)
            .Cascade(CascadeMode.Stop)
            .MustUseSupportedLanguage()
            .When(request => request.Language is not null);
    }
}
