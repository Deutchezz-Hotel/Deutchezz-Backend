using Deutchezz.Backend.Domain.Common;

namespace Deutchezz.Backend.Domain.Entities;

public class RoomAssignment : BaseEntity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid BookingRoomTypeId { get; set; }
    public BookingRoomType? BookingRoomType { get; set; }
    public Guid RoomId { get; set; }
    public Room? Room { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid? AssignedByUserId { get; set; }
    public User? AssignedByUser { get; set; }
}