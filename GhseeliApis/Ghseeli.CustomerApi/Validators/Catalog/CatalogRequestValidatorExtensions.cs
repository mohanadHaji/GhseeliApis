using FluentValidation;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;

namespace GhseeliApis.Validators.Catalog;

internal static class CatalogRequestValidatorExtensions
{
    public static IRuleBuilderOptions<T, string?> MustUseSupportedLanguage<T>(
        this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .Must(language => ConfigurationLanguageResolver.TryNormalizeOverride(language, out _))
            .WithMessage("Language must be ar or he.")
            .WithErrorCode(ConfigurationProblemCodes.LanguageInvalid);
    }

    public static IRuleBuilderOptions<T, Guid?> MustUseNonEmptyCatalogId<T>(
        this IRuleBuilder<T, Guid?> ruleBuilder)
    {
        return ruleBuilder
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Catalog identifiers must be non-empty GUID values.")
            .WithErrorCode(CatalogProblemCodes.FilterMismatch);
    }
}
