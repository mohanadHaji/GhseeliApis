using FluentValidation;
using GhseeliApis.DTOs.Catalog;

namespace GhseeliApis.Validators.Catalog;

public sealed class GetAvailableSlotsRequestValidator :
    AbstractValidator<GetAvailableSlotsRequest>
{
    private const int MaximumItems = 10;
    private const int MaximumSelectionsPerItem = 25;
    private const int MaximumSelectionQuantity = 100;

    public GetAvailableSlotsRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(request => request.Language)
            .MustUseSupportedLanguage()
            .When(request => request.Language is not null);
        RuleFor(request => request.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("Date is required.")
            .Must(date => date >= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime.Date))
            .When(request => request.Date != default)
            .WithMessage("Date cannot be in the past.")
            .Must(date => date <= DateOnly.FromDateTime(
                timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(366)))
            .When(request => request.Date != default)
            .WithMessage("Date cannot be more than 366 days in the future.");
        RuleFor(request => request.Items)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(items => items!.Count is > 0 and <= MaximumItems)
            .WithMessage($"Between 1 and {MaximumItems} items are required.")
            .Must(items => items!.All(item => item is not null))
            .WithMessage("Items cannot contain null values.")
            .Must(items => items!
                .Select(item => item.OfferingId)
                .Distinct()
                .Count() == items.Count)
            .WithMessage("Duplicate offerings are not allowed.");
        RuleForEach(request => request.Items)
            .ChildRules(item =>
            {
                item.RuleFor(value => value.OfferingId).NotEmpty();
                item.RuleFor(value => value.SelectedAddons)
                    .Cascade(CascadeMode.Stop)
                    .NotNull()
                    .Must(values => values!.Count <= MaximumSelectionsPerItem)
                    .WithMessage($"No more than {MaximumSelectionsPerItem} selections are allowed.")
                    .Must(values => values!.All(value => value is not null))
                    .WithMessage("Selections cannot contain null values.")
                    .Must(values => values!
                        .Select(value => value.AddonChoiceId)
                        .Distinct()
                        .Count() == values.Count)
                    .WithMessage("Duplicate add-on choices are not allowed.");
                item.RuleForEach(value => value.SelectedAddons)
                    .ChildRules(selection =>
                    {
                        selection.RuleFor(value => value.AddonChoiceId).NotEmpty();
                        selection.RuleFor(value => value.Quantity)
                            .InclusiveBetween(0, MaximumSelectionQuantity);
                    });
            });
        When(request => request.CustomerLocation is not null, () =>
        {
            RuleFor(request => request.CustomerLocation!.Latitude)
                .NotNull()
                .Must(value => value.HasValue && double.IsFinite(value.Value))
                .InclusiveBetween(-90d, 90d);
            RuleFor(request => request.CustomerLocation!.Longitude)
                .NotNull()
                .Must(value => value.HasValue && double.IsFinite(value.Value))
                .InclusiveBetween(-180d, 180d);
        });
    }
}
