using FluentValidation;

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
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage($"{fieldName} is required.")
            .Must(value => value is not null && value.Trim().Length <= maxLength)
            .WithMessage($"{fieldName} must be {maxLength} characters or fewer.");
    }

    public static IRuleBuilderOptions<T, string?> OptionalTrimmedText<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder,
        string fieldName,
        int maxLength)
    {
        return ruleBuilder
            .Must(value => string.IsNullOrWhiteSpace(value) || value.Trim().Length <= maxLength)
            .WithMessage($"{fieldName} must be {maxLength} characters or fewer.");
    }

    public static IRuleBuilderOptions<T, string?> RequiredEmailAddress<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder,
        int maxLength)
    {
        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Email is required.")
            .Must(value => value is not null && value.Trim().Length <= maxLength)
            .WithMessage($"Email must be {maxLength} characters or fewer.")
            .EmailAddress()
            .WithMessage("Email must be a valid email address.");
    }
}
