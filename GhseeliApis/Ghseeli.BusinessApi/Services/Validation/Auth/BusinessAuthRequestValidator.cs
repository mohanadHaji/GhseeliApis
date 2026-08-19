using FluentValidation;
using Ghseeli.BusinessApi.DTOs.Auth;

namespace Ghseeli.BusinessApi.Services.Validation.Auth;

public class BusinessAuthRequestValidator : IBusinessAuthRequestValidator
{
    private readonly IValidator<RegisterOwnerRequest> _registerOwnerValidator;
    private readonly IValidator<BusinessLoginRequest> _loginValidator;

    public BusinessAuthRequestValidator(
        IValidator<RegisterOwnerRequest> registerOwnerValidator,
        IValidator<BusinessLoginRequest> loginValidator)
    {
        _registerOwnerValidator = registerOwnerValidator;
        _loginValidator = loginValidator;
    }

    public void Validate(RegisterOwnerRequest request)
    {
        Validate(request, _registerOwnerValidator);
    }

    public void Validate(BusinessLoginRequest request)
    {
        Validate(request, _loginValidator);
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
