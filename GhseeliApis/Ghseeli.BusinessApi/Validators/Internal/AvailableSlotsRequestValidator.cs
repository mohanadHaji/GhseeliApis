using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Validators.Internal;

public sealed class AvailableSlotsRequestValidator :
    AbstractValidator<AvailableSlotsRequest>
{
    private const int MaximumItems = 10;
    private const int MaximumSelectionsPerItem = 25;

    public AvailableSlotsRequestValidator()
    {
        RuleFor(request => request.ContractVersion)
            .Equal(Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.Version)
            .WithMessage("Contract version is unsupported.");
        RuleFor(request => request.CompanyId)
            .NotEmpty()
            .WithMessage("Company id is required.");
        RuleFor(request => request.BranchId)
            .NotEmpty()
            .WithMessage("Branch id is required.");
        RuleFor(request => request.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("Date is required.")
            .LessThan(DateOnly.MaxValue)
            .WithMessage("Date exceeds the supported range.");
        RuleFor(request => request.ExpectedCatalogVersion)
            .GreaterThan(0)
            .When(request => request.ExpectedCatalogVersion.HasValue);
        RuleFor(request => request.Currency)
            .NotEmpty()
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("Currency must be a three-letter code.");
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
                item.RuleFor(value => value.OfferingId)
                    .NotEmpty()
                    .WithMessage("Offering id is required.");
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
                            .InclusiveBetween(0, BusinessValueLimits.MaximumSelectionQuantity);
                    });
            });
        When(request => request.CustomerLocation is not null, () =>
        {
            RuleFor(request => request.CustomerLocation!.Latitude)
                .Must(double.IsFinite)
                .InclusiveBetween(-90d, 90d);
            RuleFor(request => request.CustomerLocation!.Longitude)
                .Must(double.IsFinite)
                .InclusiveBetween(-180d, 180d);
        });
    }
}
