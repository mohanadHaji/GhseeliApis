using FluentValidation;
using Ghseeli.IntegrationContracts.Vehicles;
using GhseeliApis.DTOs.Catalog;

namespace GhseeliApis.Validators.Catalog;

public sealed class AvailabilitySearchRequestValidator :
    AbstractValidator<AvailabilitySearchRequest>
{
    public AvailabilitySearchRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(request => request.VehicleType)
            .Must(Enum.IsDefined)
            .WithMessage("Vehicle type is invalid.");
        RuleFor(request => request.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("Date is required.")
            .Must(date => date >= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .When(request => request.Date != default)
            .WithMessage("Date cannot be in the past.")
            .Must(date => date <= DateOnly.FromDateTime(
                timeProvider.GetUtcNow().UtcDateTime.AddDays(366)))
            .When(request => request.Date != default)
            .WithMessage("Date cannot be more than 366 days in the future.");
        RuleFor(request => request.CategoryId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Category id is invalid.");
        RuleFor(request => request.Language)
            .MustUseSupportedLanguage()
            .When(request => request.Language is not null);
        RuleFor(request => request)
            .Must(value => value.Latitude.HasValue == value.Longitude.HasValue)
            .WithName("location")
            .WithMessage("Latitude and longitude must be supplied together.");
        RuleFor(request => request.Latitude)
            .Must(value => !value.HasValue ||
                           (double.IsFinite(value.Value) && value.Value is >= -90d and <= 90d))
            .WithMessage("Latitude must be between -90 and 90.");
        RuleFor(request => request.Longitude)
            .Must(value => !value.HasValue ||
                           (double.IsFinite(value.Value) && value.Value is >= -180d and <= 180d))
            .WithMessage("Longitude must be between -180 and 180.");
    }
}
