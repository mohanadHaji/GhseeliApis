using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Interfaces;

public interface IAppointmentValidationService
{
    Task<ValidateAppointmentResponse> ValidateAsync(ValidateAppointmentRequest request);
}
