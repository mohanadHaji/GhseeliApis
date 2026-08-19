using Ghseeli.BusinessApi.DTOs.Auth;

namespace Ghseeli.BusinessApi.Services.Validation.Auth;

public interface IBusinessAuthRequestValidator
{
    void Validate(RegisterOwnerRequest request);
    void Validate(BusinessLoginRequest request);
}
