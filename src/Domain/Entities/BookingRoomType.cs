using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

public class BookingRoomType : BaseEntity
{
    public Guid BookingId { get; set; }
    public Guid RoomTypeId { get; set; }
    public int Quantity { get; set; }
    public decimal RatePerNight { get; set; }
    public int NumberOfNights { get; set; }
    public decimal Subtotal { get; set; }
}