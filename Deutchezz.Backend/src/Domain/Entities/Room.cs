using Deutchezz.Backend.Domain.Common;
using Deutchezz.Backend.Domain.Enums;

namespace Deutchezz.Backend.Domain.Entities;

public class Room : AuditableEntity
{
    public string RoomNumber { get; set; } = string.Empty;
    public Guid RoomTypeId { get; set; }
    public RoomType? RoomType { get; set; }
    public string? Floor { get; set; }
    public RoomStatus Status { get; set; } = RoomStatus.Available;
    public bool IsActive { get; set; } = true;
    public ICollection<RoomAssignment> RoomAssignments { get; set; } = new List<RoomAssignment>();
}