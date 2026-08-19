using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Companies;

namespace Ghseeli.BusinessApi.Validators.Companies;

public class UpdateBranchRequestValidator : AbstractValidator<UpdateBranchRequest>
{
    public UpdateBranchRequestValidator()
    {
        Include(new CreateBranchRequestValidator());
    }
}
