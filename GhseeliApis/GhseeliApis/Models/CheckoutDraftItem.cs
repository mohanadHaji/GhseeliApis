namespace GhseeliApis.Models;

public sealed class CheckoutDraftItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CheckoutDraftId { get; set; }
    public CheckoutDraft CheckoutDraft { get; set; } = null!;
    public Guid OfferingSourceId { get; set; }
    public int DisplayOrder { get; set; }

    public ICollection<CheckoutDraftSelection> Selections { get; set; } =
        new List<CheckoutDraftSelection>();
}
