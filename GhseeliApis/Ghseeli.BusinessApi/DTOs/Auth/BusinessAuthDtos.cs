namespace Ghseeli.BusinessApi.DTOs.Auth;

public class RegisterOwnerRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string CompanyNameAr { get; set; } = string.Empty;
    public string? CompanyNameHe { get; set; }
}

public class BusinessLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class BusinessAuthResponse
{
    public Guid UserId { get; set; }
    public Guid? CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
}
