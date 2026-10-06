using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;

namespace Deutchezz.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid? UserId { get; set; }
    public Guid? GuestId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}