using Deutchezz.Application.Common;

namespace Deutchezz.Application.Rooms;

public record CreateRoomRequest(string RoomNumber, Guid RoomTypeId, string? Floor);

public record UpdateRoomRequest(string? RoomNumber, Guid? RoomTypeId, string? Floor);

public record SetRoomStatusRequest(string Status);

public record RoomResponse(Guid Id, string RoomNumber, Guid RoomTypeId, string RoomTypeName, string Status, string? Floor, bool IsActive, string EffectiveStatus);

public record RoomListItemResponse(Guid Id, string RoomNumber, Guid RoomTypeId, string RoomTypeName, string Status, string? Floor, string EffectiveStatus);

public enum RoomBoardFilter { All, Available, Occupied, Maintenance, Inactive }

public class RoomQuery : QueryParameters
{
    public RoomBoardFilter? Status { get; set; }
    public Guid? RoomTypeId { get; set; }
    /// <summary>When set, status is computed for this stay date (date-based occupancy board).</summary>
    public DateOnly? Date { get; set; }
    /// <summary>Free-text filter matched against room number and room type name.</summary>
    public string? Search { get; set; }
}