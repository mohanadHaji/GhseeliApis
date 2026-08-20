using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Interfaces;

public interface ICatalogPublicationService
{
    Task<CatalogSnapshotResponse> GetSnapshotAsync(Guid companyId);
}
