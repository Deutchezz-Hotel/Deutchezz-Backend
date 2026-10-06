using Deutchezz.Backend.Domain.Common;
using Deutchezz.Backend.Domain.Enums;

namespace Deutchezz.Backend.Domain.Entities;

public class RoomType : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public int MaximumOccupancy { get; set; }
    public string? BedType { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
    public ICollection<BookingRoomType> BookingRoomTypes { get; set; } = new List<BookingRoomType>();
}