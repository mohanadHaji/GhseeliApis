namespace GhseeliApis.Models;

public sealed class CustomerConfiguration
{
    public Guid Id { get; set; }
    public bool IsActive { get; set; }
    public string SupportEmail { get; set; } = string.Empty;
    public string SupportPhone { get; set; } = string.Empty;
    public string DisplayNameAr { get; set; } = string.Empty;
    public string? DisplayNameHe { get; set; }
    public string LegalNoticeAr { get; set; } = string.Empty;
    public string? LegalNoticeHe { get; set; }
    public string PrivacyPolicyUrl { get; set; } = string.Empty;
    public string TermsOfServiceUrl { get; set; } = string.Empty;
    public bool IsMaintenanceModeEnabled { get; set; }
    public string? MaintenanceMessageAr { get; set; }
    public string? MaintenanceMessageHe { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
