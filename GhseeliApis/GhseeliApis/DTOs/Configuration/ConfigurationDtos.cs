namespace GhseeliApis.DTOs.Configuration;

public sealed class GetConfigurationRequest
{
    public string? Language { get; set; }
}

public sealed class ConfigurationResponse
{
    public string Language { get; set; } = string.Empty;
    public SupportContactResponse Support { get; set; } = new();
    public DisplayConfigurationResponse Display { get; set; } = new();
    public LegalConfigurationResponse Legal { get; set; } = new();
    public MaintenanceConfigurationResponse Maintenance { get; set; } = new();
}

public sealed class SupportContactResponse
{
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

public sealed class DisplayConfigurationResponse
{
    public string Name { get; set; } = string.Empty;
}

public sealed class LegalConfigurationResponse
{
    public string Notice { get; set; } = string.Empty;
    public string PrivacyPolicyUrl { get; set; } = string.Empty;
    public string TermsOfServiceUrl { get; set; } = string.Empty;
}

public sealed class MaintenanceConfigurationResponse
{
    public bool IsEnabled { get; set; }
    public string? Message { get; set; }
}
