using Ghseeli.BusinessApi.DTOs.Companies;

namespace Ghseeli.BusinessApi.Services.Validation.Companies;

public interface ICompanyRequestValidator
{
    void Validate(UpdateCompanyProfileRequest request);
    void Validate(CreateBranchRequest request);
    void Validate(UpdateBranchRequest request);
}
