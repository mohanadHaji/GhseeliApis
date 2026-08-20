namespace Ghseeli.BusinessApi.Models;

public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public string? ServiceAreaDescriptionAr { get; set; }
    public string? ServiceAreaDescriptionHe { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public long CatalogVersion { get; set; } = 1;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    public ICollection<ServiceCategory> Categories { get; set; } =
        new List<ServiceCategory>();
    public ICollection<BusinessUserAssignment> Assignments { get; set; } =
        new List<BusinessUserAssignment>();
}
