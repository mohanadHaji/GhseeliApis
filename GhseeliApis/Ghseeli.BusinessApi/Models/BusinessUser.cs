using Microsoft.AspNetCore.Identity;

namespace Ghseeli.BusinessApi.Models;

public class BusinessUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BusinessUserAssignment> Assignments { get; set; } =
        new List<BusinessUserAssignment>();
}
