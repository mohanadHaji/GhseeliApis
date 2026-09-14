using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.DTOs.Auth;

public sealed class RequestOtpRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public sealed class ConfirmOtpRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\d{6}$")]
    public string Code { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class OtpRequestAcceptedResponse
{
    public bool Accepted { get; set; } = true;
}
