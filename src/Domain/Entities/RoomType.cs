using Deutchezz.Domain.Common;

namespace Deutchezz.Domain.Entities;

public class RoomType : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public int MaximumOccupancy { get; set; }
    public string? BedType { get; set; }
    public bool IsActive { get; set; } = true;
}