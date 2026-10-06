using Deutchezz.Application.Common;

namespace Deutchezz.Application.RoomTypes;

public record CreateRoomTypeRequest(string Name, string? Description, decimal NightlyRate, int MaxOccupancy);

public record UpdateRoomTypeRequest(string? Name, string? Description, decimal? NightlyRate, int? MaxOccupancy);

public record RoomTypeResponse(
    Guid Id, string Name, string? Description, decimal NightlyRate, int MaxOccupancy, bool IsActive);

public class RoomTypeQuery : QueryParameters
{
    private string? _name;
    public string? Name { get => _name; set => _name = value?.Trim(); }
    public bool IncludeInactive { get; set; }
}