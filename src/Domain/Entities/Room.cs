using Deutchezz.Domain.Common;
using Deutchezz.Domain.Enums;

namespace Deutchezz.Domain.Entities;

public class Room : SoftDeletableEntity
{
    public string RoomNumber { get; set; } = string.Empty;
    public Guid RoomTypeId { get; set; }
    public RoomStatus Status { get; set; } = RoomStatus.Available;
    public string? Floor { get; set; }
}