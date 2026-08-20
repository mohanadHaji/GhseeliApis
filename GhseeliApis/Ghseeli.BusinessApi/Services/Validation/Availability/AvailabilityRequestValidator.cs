using FluentValidation;
using Ghseeli.BusinessApi.DTOs.Availability;

namespace Ghseeli.BusinessApi.Services.Validation.Availability;

public class AvailabilityRequestValidator : IAvailabilityRequestValidator
{
    private readonly IValidator<UpdateBranchAvailabilitySettingsRequest> _settingsValidator;
    private readonly IValidator<CreateRecurringScheduleRequest> _createScheduleValidator;
    private readonly IValidator<UpdateRecurringScheduleRequest> _updateScheduleValidator;
    private readonly IValidator<CreateAvailabilityOverrideRequest> _createOverrideValidator;
    private readonly IValidator<UpdateAvailabilityOverrideRequest> _updateOverrideValidator;
    private readonly IValidator<UpsertBranchServiceAreaRequest> _serviceAreaValidator;

    public AvailabilityRequestValidator(
        IValidator<UpdateBranchAvailabilitySettingsRequest> settingsValidator,
        IValidator<CreateRecurringScheduleRequest> createScheduleValidator,
        IValidator<UpdateRecurringScheduleRequest> updateScheduleValidator,
        IValidator<CreateAvailabilityOverrideRequest> createOverrideValidator,
        IValidator<UpdateAvailabilityOverrideRequest> updateOverrideValidator,
        IValidator<UpsertBranchServiceAreaRequest> serviceAreaValidator)
    {
        _settingsValidator = settingsValidator;
        _createScheduleValidator = createScheduleValidator;
        _updateScheduleValidator = updateScheduleValidator;
        _createOverrideValidator = createOverrideValidator;
        _updateOverrideValidator = updateOverrideValidator;
        _serviceAreaValidator = serviceAreaValidator;
    }

    public void Validate(UpdateBranchAvailabilitySettingsRequest request)
    {
        Validate(request, _settingsValidator);
    }

    public void Validate(CreateRecurringScheduleRequest request)
    {
        Validate(request, _createScheduleValidator);
    }

    public void Validate(UpdateRecurringScheduleRequest request)
    {
        Validate(request, _updateScheduleValidator);
    }

    public void Validate(CreateAvailabilityOverrideRequest request)
    {
        Validate(request, _createOverrideValidator);
    }

    public void Validate(UpdateAvailabilityOverrideRequest request)
    {
        Validate(request, _updateOverrideValidator);
    }

    public void Validate(UpsertBranchServiceAreaRequest request)
    {
        Validate(request, _serviceAreaValidator);
    }

    private static void Validate<T>(T request, IValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = validator.Validate(request);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(error => ToCamelCase(error.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        throw new AvailabilityValidationException(result.Errors[0].ErrorMessage, errors);
    }

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return propertyName;
        }

        return char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }
}
