using FluentValidation;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Validation.Availability;

public class InternalAppointmentRequestValidator : IInternalAppointmentRequestValidator
{
    private readonly IValidator<ValidateAppointmentRequest> _validator;

    public InternalAppointmentRequestValidator(
        IValidator<ValidateAppointmentRequest> validator)
    {
        _validator = validator;
    }

    public void Validate(ValidateAppointmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = _validator.Validate(request);
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
