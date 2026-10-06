using Deutchezz.Backend.Domain.Common;

namespace Deutchezz.Backend.Domain.Entities;

public class Guest : AuditableEntity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}