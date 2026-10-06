using Deutchezz.Backend.Domain.Common;
using Deutchezz.Backend.Domain.Enums;

namespace Deutchezz.Backend.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}