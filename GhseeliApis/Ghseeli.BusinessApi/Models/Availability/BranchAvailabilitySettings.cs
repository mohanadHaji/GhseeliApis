namespace Ghseeli.BusinessApi.Models;

public class BranchAvailabilitySettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public int MinimumLeadMinutes { get; set; }
    public int BookingHorizonDays { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Branch Branch { get; set; } = null!;
}
