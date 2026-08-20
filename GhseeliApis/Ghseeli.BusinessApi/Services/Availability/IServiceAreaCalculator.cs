using Ghseeli.BusinessApi.Models;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Availability;

public interface IServiceAreaCalculator
{
    ServiceAreaEvaluationResult Evaluate(
        Branch branch,
        BranchServiceArea? serviceArea,
        AppointmentCustomerLocationFacts? customerLocation);
}
