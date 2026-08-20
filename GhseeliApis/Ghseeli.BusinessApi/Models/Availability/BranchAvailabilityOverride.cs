namespace Ghseeli.BusinessApi.Models;

public class BranchAvailabilityOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public DateOnly OverrideDate { get; set; }
    public bool IsClosed { get; set; }
    public TimeSpan? StartLocalTime { get; set; }
    public TimeSpan? EndLocalTime { get; set; }
    public int? SlotDurationMinutes { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Branch Branch { get; set; } = null!;
}
