namespace GhseeliApis.Models;

public sealed class BusinessFavourite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid BusinessSourceId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsDemo { get; set; }
}
