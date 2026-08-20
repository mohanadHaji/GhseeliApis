using Ghseeli.BusinessApi.Models;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Availability;

public interface ICatalogSelectionValidator
{
    CatalogSelectionValidationResult Validate(
        ServiceOffering offering,
        IReadOnlyCollection<ValidateAppointmentAddonSelectionRequest> requestedSelections);
}
