namespace Deutchezz.Backend.Application.DTOs.RoomTypes;

public class CreateRoomTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public int MaximumOccupancy { get; set; }
    public string? BedType { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateRoomTypeRequest : CreateRoomTypeRequest
{
}

public class RoomTypeResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public int MaximumOccupancy { get; set; }
    public string? BedType { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RoomTypeListItemResponse : RoomTypeResponse
{
}