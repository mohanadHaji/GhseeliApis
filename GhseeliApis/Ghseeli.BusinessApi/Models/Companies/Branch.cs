namespace Ghseeli.BusinessApi.Models;

public class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string AddressAr { get; set; } = string.Empty;
    public string? AddressHe { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Company Company { get; set; } = null!;
    public BranchAvailabilitySettings? AvailabilitySettings { get; set; }
    public BranchServiceArea? ServiceArea { get; set; }
    public ICollection<BranchRecurringSchedule> RecurringSchedules { get; set; } =
        new List<BranchRecurringSchedule>();
    public ICollection<BranchAvailabilityOverride> AvailabilityOverrides { get; set; } =
        new List<BranchAvailabilityOverride>();
    public ICollection<ServiceOffering> ServiceOfferings { get; set; } =
        new List<ServiceOffering>();
    public ICollection<BusinessUserAssignment> Assignments { get; set; } =
        new List<BusinessUserAssignment>();
}
