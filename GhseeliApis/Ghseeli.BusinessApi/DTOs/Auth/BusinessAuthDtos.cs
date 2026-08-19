using System.ComponentModel.DataAnnotations;

namespace Ghseeli.BusinessApi.DTOs.Auth;

public class RegisterOwnerRequest
{
    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    [Required, MaxLength(200)]
    public string CompanyNameAr { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CompanyNameHe { get; set; } = string.Empty;
}

public class BusinessLoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
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
