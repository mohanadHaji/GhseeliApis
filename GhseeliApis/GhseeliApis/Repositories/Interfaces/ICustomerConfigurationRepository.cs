using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface ICustomerConfigurationRepository
{
    Task<CustomerConfiguration?> GetActiveAsync(CancellationToken cancellationToken);
}
