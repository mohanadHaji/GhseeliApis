namespace Ghseeli.BusinessApi.Models;

public class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameHe { get; set; } = string.Empty;
    public string AddressAr { get; set; } = string.Empty;
    public string AddressHe { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Company Company { get; set; } = null!;
    public ICollection<BusinessUserAssignment> Assignments { get; set; } =
        new List<BusinessUserAssignment>();
}
