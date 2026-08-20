using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Validation.Availability;

public interface IInternalAppointmentRequestValidator
{
    void Validate(ValidateAppointmentRequest request);
}
