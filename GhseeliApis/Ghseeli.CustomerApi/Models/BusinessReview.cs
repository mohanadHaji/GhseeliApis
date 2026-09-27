namespace GhseeliApis.Models;

public sealed class BusinessReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerBookingId { get; set; }
    public CustomerBooking CustomerBooking { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid BusinessSourceId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool IsDemo { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
