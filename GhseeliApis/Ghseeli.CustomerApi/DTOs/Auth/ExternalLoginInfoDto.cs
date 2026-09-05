namespace GhseeliApis.DTOs.Auth;

public class ExternalLoginInfoDto
{
    public string LoginProvider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string? ProviderDisplayName { get; set; }
}
