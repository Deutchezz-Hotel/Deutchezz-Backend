using Deutchezz.Application.Common;

namespace Deutchezz.Application.Rooms;

public record CreateRoomRequest(string RoomNumber, Guid RoomTypeId, string? Floor);

public record UpdateRoomRequest(string? RoomNumber, Guid? RoomTypeId, string? Floor);

public record SetRoomStatusRequest(string Status);

public record RoomResponse(Guid Id, string RoomNumber, Guid RoomTypeId, string RoomTypeName, string Status, string? Floor, bool IsActive, string EffectiveStatus);

public record RoomListItemResponse(
    Guid Id,
    string RoomNumber,
    Guid RoomTypeId,
    string RoomTypeName,
    string Status,
    string? Floor,
    string EffectiveStatus,
    string? OccupiedUntil,
    string? AvailableAgainAt,
    string? CurrentGuestName,
    Guid? CurrentBookingId,
    string? CurrentBookingReference,
    bool IsOverdueCheckOut,
    string? NextArrivalAt,
    string? NextArrivalGuestName);

public enum RoomBoardFilter { All, Available, Occupied, Maintenance, Inactive }

public class RoomQuery : QueryParameters
{
    public RoomBoardFilter? Status { get; set; }
    public Guid? RoomTypeId { get; set; }

    /// <summary>v1.12: the Lagos-local board moment (e.g. <c>2026-10-13T13:00</c>). Defaults to now.</summary>
    public DateTime? At { get; set; }

    /// <summary>Legacy date-based board. When set (and <see cref="At"/> absent) it is interpreted as
    /// that date at the current Lagos time-of-day.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>v1.12: only rooms free for the half-open Lagos-local [freeFrom, freeTo) stay.</summary>
    public DateTime? FreeFrom { get; set; }
    public DateTime? FreeTo { get; set; }

    /// <summary>Legacy free range; interpreted with the policy default check-in/check-out times.</summary>
    public DateOnly? AvailableFrom { get; set; }
    public DateOnly? AvailableTo { get; set; }

    /// <summary>Free-text filter matched against room number and room type name.</summary>
    public string? Search { get; set; }
}