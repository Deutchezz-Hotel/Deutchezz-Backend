using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

public class Guest : AuditableEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public Guid? UserId { get; set; }
}