using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

public class RoomAssignment : BaseEntity
{
    public Guid BookingId { get; set; }
    public Guid BookingRoomTypeId { get; set; }
    public Guid RoomId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid? AssignedByUserId { get; set; }
    public DateTime? ReleasedAt { get; set; }
}