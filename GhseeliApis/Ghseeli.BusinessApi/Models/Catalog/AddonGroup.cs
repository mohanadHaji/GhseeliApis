namespace Ghseeli.BusinessApi.Models;

public class AddonGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServiceOfferingId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public AddonSelectionType SelectionType { get; set; }
    public bool IsRequired { get; set; }
    public int MinimumSelections { get; set; }
    public int? MaximumSelections { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ServiceOffering ServiceOffering { get; set; } = null!;
    public ICollection<AddonChoice> Choices { get; set; } = new List<AddonChoice>();
}
