using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Services;

namespace Ghseeli.BusinessApi.Validators;

internal static class BusinessValidationRuleBuilderExtensions
{
    public static IRuleBuilderOptions<T, string?> RequiredTrimmedText<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder,
        string fieldName,
        int maxLength)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .Must(BusinessTextNormalizer.HasMeaningfulText)
            .WithMessage($"{fieldName} is required.")
            .Must(value => (BusinessTextNormalizer.NormalizeOptional(value) ?? string.Empty).Length <= maxLength)
            .WithMessage($"{fieldName} must be {maxLength} characters or fewer.");
    }

    public static IRuleBuilderOptions<T, string?> OptionalTrimmedText<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder,
        string fieldName,
        int maxLength)
    {
        return ruleBuilder
            .Must(value =>
            {
                var normalized = BusinessTextNormalizer.NormalizeOptional(value);
                return normalized is null || normalized.Length <= maxLength;
            })
            .WithMessage($"{fieldName} must be {maxLength} characters or fewer.");
    }

    public static IRuleBuilderOptions<T, string?> RequiredEmailAddress<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder,
        int maxLength)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .Must(BusinessTextNormalizer.HasMeaningfulText)
            .WithMessage("Email is required.")
            .Must(value => (BusinessTextNormalizer.NormalizeOptional(value) ?? string.Empty).Length <= maxLength)
            .WithMessage($"Email must be {maxLength} characters or fewer.")
            .EmailAddress()
            .WithMessage("Email must be a valid email address.");
    }

    public static IRuleBuilderOptions<T, decimal> SupportedMoneyAmount<T>(
        this IRuleBuilderInitial<T, decimal> ruleBuilder,
        string fieldName)
    {
        return ruleBuilder
            .Must(BusinessMoney.IsWithinSupportedRange)
            .WithMessage(
                $"{fieldName} must be between 0.00 and {BusinessValueLimits.MaximumMoneyAmount:0.00} after rounding to two decimal places.");
    }
}
