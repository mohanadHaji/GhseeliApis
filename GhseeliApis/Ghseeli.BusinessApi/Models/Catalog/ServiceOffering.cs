namespace Ghseeli.BusinessApi.Models;

public class ServiceOffering
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public Guid? BranchId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public decimal BasePrice { get; set; }
    public int DurationMinutes { get; set; }
    public string? ImageUrl { get; set; }
    public string? ReferenceCode { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ServiceCategory Category { get; set; } = null!;
    public Branch? Branch { get; set; }
    public ICollection<AddonGroup> AddonGroups { get; set; } =
        new List<AddonGroup>();
}
