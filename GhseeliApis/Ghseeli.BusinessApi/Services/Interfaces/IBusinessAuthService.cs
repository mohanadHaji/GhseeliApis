using Ghseeli.BusinessApi.DTOs.Auth;

namespace Ghseeli.BusinessApi.Services.Interfaces;

public interface IBusinessAuthService
{
    Task<BusinessAuthResponse> RegisterOwnerAsync(RegisterOwnerRequest request);
    Task<BusinessAuthResponse> LoginAsync(BusinessLoginRequest request);
}
