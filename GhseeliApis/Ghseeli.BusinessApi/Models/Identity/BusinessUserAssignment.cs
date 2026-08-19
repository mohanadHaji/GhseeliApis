namespace Ghseeli.BusinessApi.Models;

public class BusinessUserAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public BusinessMembershipRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public BusinessUser User { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public Branch? Branch { get; set; }
}
