namespace Ghseeli.BusinessApi.Models;

public static class BusinessVerticalDefaults
{
    public static readonly Guid CarWashId =
        Guid.Parse("a842f536-17b7-4be6-a18d-1bdc6245094c");
    public const string CarWashCode = "car_wash";
}

public sealed class BusinessVertical
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public bool IsActive { get; set; }
    public bool RegistrationEnabled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public ICollection<CompanyBusinessVertical> Companies { get; set; } =
        new List<CompanyBusinessVertical>();
    public ICollection<ServiceCategory> Categories { get; set; } =
        new List<ServiceCategory>();
}

public sealed class CompanyBusinessVertical
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid BusinessVerticalId { get; set; } = BusinessVerticalDefaults.CarWashId;
    public BusinessVertical BusinessVertical { get; set; } = null!;
    public bool IsPrimary { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
