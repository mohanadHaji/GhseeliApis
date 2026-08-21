namespace GhseeliApis.Models;

public sealed class CheckoutDraftSelection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CheckoutDraftItemId { get; set; }
    public CheckoutDraftItem CheckoutDraftItem { get; set; } = null!;
    public Guid AddonGroupSourceId { get; set; }
    public Guid AddonChoiceSourceId { get; set; }
    public int Quantity { get; set; }
    public int DisplayOrder { get; set; }
}
