using FluentValidation;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Services.Checkout;

namespace GhseeliApis.Validators.Checkout;

public abstract class CheckoutDraftMutationRequestValidatorBase<TRequest>
    : AbstractValidator<TRequest>
    where TRequest : CheckoutDraftMutationRequestBase
{
    protected const int MaxItems = 10;
    protected const int MaxSelectionsPerItem = 25;
    protected const int MaxSelectionQuantity = 100;

    protected CheckoutDraftMutationRequestValidatorBase()
    {
        RuleFor(request => request.BusinessSourceId)
            .MustUseNonEmptySourceId();

        RuleFor(request => request.BranchSourceId)
            .MustUseNonEmptySourceId();

        RuleFor(request => request.RequestedSlotStartUtc)
            .Cascade(CascadeMode.Stop)
            .Must(value => value != default)
            .WithMessage("Requested slot is required.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.RequestedSlotRequired)
            .Must(value => value.Offset == TimeSpan.Zero)
            .WithMessage("Requested slot must use the UTC offset.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.RequestedSlotMustBeUtc);

        RuleFor(request => request.Vehicle)
            .NotNull()
            .WithMessage("Vehicle is required.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.Required);

        When(request => request.Vehicle is not null, () =>
        {
            RuleFor(request => request.Vehicle.VehicleType)
                .MustBeMeaningfulRequiredText(50);
            RuleFor(request => request.Vehicle.LicensePlate)
                .MustRespectMaxLength(50);
            RuleFor(request => request.Vehicle.Make)
                .MustRespectMaxLength(150);
            RuleFor(request => request.Vehicle.Model)
                .MustRespectMaxLength(150);
            RuleFor(request => request.Vehicle.Color)
                .MustRespectMaxLength(50);
        });

        RuleFor(request => request.Location)
            .NotNull()
            .WithMessage("Location is required.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.Required);

        When(request => request.Location is not null, () =>
        {
            RuleFor(request => request.Location.AddressLine)
                .MustBeMeaningfulRequiredText(300);
            RuleFor(request => request.Location.City)
                .MustRespectMaxLength(120);
            RuleFor(request => request.Location.Area)
                .MustRespectMaxLength(120);
            RuleFor(request => request.Location.Latitude)
                .Must(BeFinite)
                .WithMessage("Latitude must be finite.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.CoordinateFinite)
                .InclusiveBetween(-90d, 90d)
                .WithMessage("Latitude must be between -90 and 90.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.CoordinateRange);
            RuleFor(request => request.Location.Longitude)
                .Must(BeFinite)
                .WithMessage("Longitude must be finite.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.CoordinateFinite)
                .InclusiveBetween(-180d, 180d)
                .WithMessage("Longitude must be between -180 and 180.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.CoordinateRange);
        });

        RuleFor(request => request.Items)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("At least one item is required.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.CollectionRequired)
            .Must(items => items!.Count > 0)
            .WithMessage("At least one item is required.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.CollectionRequired)
            .Must(items => items!.Count <= MaxItems)
            .WithMessage("Too many items were supplied.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.CollectionTooMany)
            .Must(HaveUniqueOfferings)
            .WithMessage("Duplicate offerings are not allowed.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.DuplicateOffering);

        RuleForEach(request => request.Items)
            .NotNull()
            .WithMessage("Item is required.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.Required);

        RuleForEach(request => request.Items)
            .SetValidator(new CheckoutDraftItemRequestValidator());
    }

    private static bool BeFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool HaveUniqueOfferings(IReadOnlyCollection<CheckoutDraftItemRequest>? items) =>
        items is null || items.Any(item => item is null) || items
            .Select(item => item!.OfferingSourceId)
            .Distinct()
            .Count() == items.Count;

    private sealed class CheckoutDraftItemRequestValidator : AbstractValidator<CheckoutDraftItemRequest>
    {
        public CheckoutDraftItemRequestValidator()
        {
            RuleFor(item => item.OfferingSourceId)
                .MustUseNonEmptySourceId();

            RuleFor(item => item.Selections)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .WithMessage("Selections are required.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.CollectionRequired)
                .Must(selections => selections!.Count <= MaxSelectionsPerItem)
                .WithMessage("Too many selections were supplied.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.CollectionTooMany)
                .Must(HaveUniqueAddonChoices)
                .WithMessage("Duplicate add-on choices are not allowed.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.DuplicateAddonChoice);

            RuleForEach(item => item.Selections)
                .NotNull()
                .WithMessage("Selection is required.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.Required);

            RuleForEach(item => item.Selections)
                .SetValidator(new CheckoutDraftSelectionRequestValidator());
        }

        private static bool HaveUniqueAddonChoices(
            IReadOnlyCollection<CheckoutDraftSelectionRequest>? selections) =>
            selections is null || selections.Any(selection => selection is null) || selections
                .Select(selection => selection!.AddonChoiceSourceId)
                .Distinct()
                .Count() == selections.Count;
    }

    private sealed class CheckoutDraftSelectionRequestValidator
        : AbstractValidator<CheckoutDraftSelectionRequest>
    {
        public CheckoutDraftSelectionRequestValidator()
        {
            RuleFor(selection => selection.AddonChoiceSourceId)
                .MustUseNonEmptySourceId();

            RuleFor(selection => selection.Quantity)
                .InclusiveBetween(0, MaxSelectionQuantity)
                .WithMessage("Selection quantity is out of range.")
                .WithErrorCode(CheckoutDraftFieldErrorCodes.SelectionQuantityRange);
        }
    }
}

public sealed class CreateCheckoutDraftRequestValidator
    : CheckoutDraftMutationRequestValidatorBase<CreateCheckoutDraftRequest>
{
}
