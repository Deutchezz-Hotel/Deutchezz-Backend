using Deutchezz.Backend.Domain.Common;

namespace Deutchezz.Backend.Domain.Entities;

public class BookingRoomType : BaseEntity
{
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid RoomTypeId { get; set; }
    public RoomType? RoomType { get; set; }
    public int Quantity { get; set; }
    public decimal RatePerNight { get; set; }
    public int NumberOfNights { get; set; }
    public decimal Subtotal { get; set; }
    public ICollection<RoomAssignment> RoomAssignments { get; set; } = new List<RoomAssignment>();
}