namespace GhseeliApis.DTOs.Auth;

public class ExternalLoginCallbackResponse
{
    public bool IsNewUser { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
