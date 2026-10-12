using System.ComponentModel.DataAnnotations.Schema;
using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

public class Guest : AuditableEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public Guid? UserId { get; set; }

    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();
}