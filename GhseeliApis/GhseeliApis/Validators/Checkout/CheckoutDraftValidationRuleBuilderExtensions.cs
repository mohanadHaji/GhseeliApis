using FluentValidation;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Configuration;

namespace GhseeliApis.Validators.Checkout;

internal static class CheckoutDraftValidationRuleBuilderExtensions
{
    public static IRuleBuilderOptions<T, string?> MustUseSupportedLanguage<T>(
        this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .Must(language => ConfigurationLanguageResolver.TryNormalizeOverride(language, out _))
            .WithMessage("Language must be ar or he.")
            .WithErrorCode(ConfigurationProblemCodes.LanguageInvalid);
    }

    public static IRuleBuilderOptions<T, Guid> MustUseNonEmptySourceId<T>(
        this IRuleBuilder<T, Guid> ruleBuilder)
    {
        return ruleBuilder
            .Must(value => value != Guid.Empty)
            .WithMessage("A non-empty GUID is required.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.GuidRequired);
    }

    public static IRuleBuilderOptions<T, string?> MustBeMeaningfulRequiredText<T>(
        this IRuleBuilder<T, string?> ruleBuilder,
        int maxLength)
    {
        return ruleBuilder
            .Must(value => ConfigurationTextNormalizer.NormalizeOptional(value) is not null)
            .WithMessage("A non-empty value is required.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.Required)
            .MaximumLength(maxLength)
            .WithMessage("The supplied text is too long.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.MaxLength);
    }

    public static IRuleBuilderOptions<T, string?> MustRespectMaxLength<T>(
        this IRuleBuilder<T, string?> ruleBuilder,
        int maxLength)
    {
        return ruleBuilder
            .MaximumLength(maxLength)
            .WithMessage("The supplied text is too long.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.MaxLength);
    }
}
