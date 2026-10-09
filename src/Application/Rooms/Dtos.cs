using Deutchezz.Application.Common;

namespace Deutchezz.Application.Rooms;

public record CreateRoomRequest(string RoomNumber, Guid RoomTypeId, string? Floor);

public record UpdateRoomRequest(string? RoomNumber, Guid? RoomTypeId, string? Floor);

public record SetRoomStatusRequest(string Status);

public record RoomResponse(Guid Id, string RoomNumber, Guid RoomTypeId, string RoomTypeName, string Status, string? Floor, bool IsActive);

public enum RoomBoardFilter { All, Available, Occupied, Maintenance, Inactive }

public class RoomQuery : QueryParameters
{
    public RoomBoardFilter? Status { get; set; }
    public Guid? RoomTypeId { get; set; }
}