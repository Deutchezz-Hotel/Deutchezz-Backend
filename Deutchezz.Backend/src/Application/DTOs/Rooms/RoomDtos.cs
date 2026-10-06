namespace Deutchezz.Backend.Application.DTOs.Rooms;

public class CreateRoomRequest
{
    public string RoomNumber { get; set; } = string.Empty;
    public Guid RoomTypeId { get; set; }
    public string? Floor { get; set; }
}

public class UpdateRoomRequest
{
    public string RoomNumber { get; set; } = string.Empty;
    public Guid RoomTypeId { get; set; }
    public string? Floor { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class RoomResponse
{
    public Guid Id { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public Guid RoomTypeId { get; set; }
    public string RoomTypeName { get; set; } = string.Empty;
    public string? Floor { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class RoomListItemResponse : RoomResponse
{
}