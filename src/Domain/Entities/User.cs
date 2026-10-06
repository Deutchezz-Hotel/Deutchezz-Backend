using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

public class User : Microsoft.AspNetCore.Identity.IdentityUser<Guid>
{
    public string? FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Role : Microsoft.AspNetCore.Identity.IdentityRole<Guid>
{
}