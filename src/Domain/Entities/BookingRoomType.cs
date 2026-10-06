namespace Deutchezz.Domain.Entities;

public class BookingRoomType
{
    public Guid BookingId { get; set; }
    public Guid RoomTypeId { get; set; }
    public int Quantity { get; set; }
    public decimal NightlyRateSnapshot { get; set; }
    public decimal LineTotal { get; set; }
}