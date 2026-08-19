using FluentValidation;
using Ghseeli.BusinessApi.DTOs.Companies;

namespace Ghseeli.BusinessApi.Services.Validation.Companies;

public class CompanyRequestValidator : ICompanyRequestValidator
{
    private readonly IValidator<UpdateCompanyProfileRequest> _updateCompanyValidator;
    private readonly IValidator<CreateBranchRequest> _createBranchValidator;
    private readonly IValidator<UpdateBranchRequest> _updateBranchValidator;

    public CompanyRequestValidator(
        IValidator<UpdateCompanyProfileRequest> updateCompanyValidator,
        IValidator<CreateBranchRequest> createBranchValidator,
        IValidator<UpdateBranchRequest> updateBranchValidator)
    {
        _updateCompanyValidator = updateCompanyValidator;
        _createBranchValidator = createBranchValidator;
        _updateBranchValidator = updateBranchValidator;
    }

    public void Validate(UpdateCompanyProfileRequest request)
    {
        Validate(request, _updateCompanyValidator);
    }

    public void Validate(CreateBranchRequest request)
    {
        Validate(request, _createBranchValidator);
    }

    public void Validate(UpdateBranchRequest request)
    {
        Validate(request, _updateBranchValidator);
    }

    private static void Validate<T>(T request, IValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = validator.Validate(request);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}
