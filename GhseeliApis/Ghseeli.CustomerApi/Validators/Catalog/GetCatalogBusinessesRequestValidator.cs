using FluentValidation;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;

namespace GhseeliApis.Validators.Catalog;

public sealed class GetCatalogBusinessesRequestValidator :
    AbstractValidator<GetCatalogBusinessesRequest>
{
    public GetCatalogBusinessesRequestValidator()
    {
        RuleFor(request => request.Language)
            .Cascade(CascadeMode.Stop)
            .MustUseSupportedLanguage()
            .When(request => request.Language is not null);

        RuleFor(request => request.BranchId)
            .MustUseNonEmptyCatalogId();

        RuleFor(request => request.CategoryId)
            .MustUseNonEmptyCatalogId();

        RuleFor(request => request.Search)
            .Must(value =>
                ConfigurationTextNormalizer.NormalizeOptional(value) is not { Length: > 100 })
            .WithMessage("Search cannot exceed 100 normalized characters.")
            .WithErrorCode(CatalogProblemCodes.FilterMismatch);

        RuleFor(request => request.Top)
            .Must(value => !value.HasValue || value.Value is 5 or 10)
            .WithMessage("Top must be 5 or 10.")
            .WithErrorCode(CatalogProblemCodes.TopInvalid);
    }
}
