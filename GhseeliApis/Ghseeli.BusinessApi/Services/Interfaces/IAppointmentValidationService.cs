using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Interfaces;

public interface IAppointmentValidationService
{
    Task<ValidateAppointmentResponse> ValidateAsync(ValidateAppointmentRequest request);
}

public interface IAvailableSlotsService
{
    Task<AvailableSlotsResponse> GetAsync(
        AvailableSlotsRequest request,
        CancellationToken cancellationToken);
}

public interface IAvailabilityDiscoveryService
{
    Task<AvailabilityDiscoveryResponse> DiscoverAsync(
        AvailabilityDiscoveryRequest request,
        CancellationToken cancellationToken);
}
