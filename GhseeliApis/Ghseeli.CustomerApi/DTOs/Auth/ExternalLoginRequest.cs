using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.DTOs.Auth;

public class ExternalLoginRequest
{
    [Required]
    public string Provider { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
