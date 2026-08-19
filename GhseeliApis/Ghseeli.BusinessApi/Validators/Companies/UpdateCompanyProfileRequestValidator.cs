using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Companies;

namespace Ghseeli.BusinessApi.Validators.Companies;

public class UpdateCompanyProfileRequestValidator : AbstractValidator<UpdateCompanyProfileRequest>
{
    public UpdateCompanyProfileRequestValidator()
    {
        RuleFor(request => request.NameAr)
            .RequiredTrimmedText("Arabic company name", 200);

        RuleFor(request => request.NameHe)
            .OptionalTrimmedText("Hebrew company name", 200);

        RuleFor(request => request.Phone)
            .OptionalTrimmedText("Phone", 30);
    }
}
