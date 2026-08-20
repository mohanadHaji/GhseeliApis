namespace GhseeliApis.Services.Business;

public sealed class BusinessApiClientOptions
{
    public const string SectionName = "BusinessApiClient";

    public string BaseUrl { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string ActiveSecret { get; set; } = string.Empty;
    public bool RequireHttps { get; set; } = true;
    public bool AllowInsecureHttpInDevelopment { get; set; }
    public double TimeoutSeconds { get; set; } = 15;
    public int MaxRetryAttempts { get; set; } = 2;
    public int MaxRetryAfterSeconds { get; set; } = 5;
}
