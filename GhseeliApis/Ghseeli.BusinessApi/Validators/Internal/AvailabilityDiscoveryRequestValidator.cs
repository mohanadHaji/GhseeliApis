using FluentValidation;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;

namespace Ghseeli.BusinessApi.Validators.Internal;

public sealed class AvailabilityDiscoveryRequestValidator :
    AbstractValidator<AvailabilityDiscoveryRequest>
{
    public AvailabilityDiscoveryRequestValidator()
    {
        RuleFor(request => request.ContractVersion)
            .Equal(BusinessCatalogContract.Version);
        RuleFor(request => request.Date)
            .NotEqual(default(DateOnly))
            .LessThan(DateOnly.MaxValue);
        RuleFor(request => request.Candidates)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(candidates => candidates!.Count is > 0 and <= 50)
            .WithMessage("Between 1 and 50 company candidates are required.")
            .Must(candidates => candidates!
                .Select(candidate => candidate.CompanyId)
                .Distinct()
                .Count() == candidates.Count)
            .WithMessage("Duplicate company candidates are not allowed.");
        RuleForEach(request => request.Candidates).ChildRules(candidate =>
        {
            candidate.RuleFor(value => value.CompanyId).NotEmpty();
            candidate.RuleFor(value => value.BranchIds)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .Must(branchIds => branchIds!.Count is > 0 and <= 25)
                .WithMessage("Between 1 and 25 branch ids are required.")
                .Must(branchIds => branchIds!.All(branchId => branchId != Guid.Empty))
                .Must(branchIds => branchIds!.Distinct().Count() == branchIds.Count)
                .WithMessage("Duplicate branch ids are not allowed.");
        });
        When(request => request.CustomerLocation is not null, () =>
        {
            RuleFor(request => request.CustomerLocation!.Latitude)
                .Must(double.IsFinite)
                .InclusiveBetween(-90d, 90d);
            RuleFor(request => request.CustomerLocation!.Longitude)
                .Must(double.IsFinite)
                .InclusiveBetween(-180d, 180d);
        });
    }
}
