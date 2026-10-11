using Deutchezz.Application.Common;

namespace Deutchezz.Application.RoomTypes;

public record CreateRoomTypeRequest(
    string Name, string? Description, decimal BasePrice, int MaximumOccupancy, string? BedType,
    List<string>? ImageUrls = null, List<string>? Features = null);

public record UpdateRoomTypeRequest(
    string? Name, string? Description, decimal? BasePrice, int? MaximumOccupancy, string? BedType,
    List<string>? ImageUrls = null, List<string>? Features = null);

public record RoomTypeResponse(
    Guid Id, string Name, string? Description, decimal BasePrice, int MaximumOccupancy, string? BedType,
    bool IsActive, List<string> ImageUrls, List<string> Features);

public record RoomTypeListItemResponse(
    Guid Id, string Name, decimal BasePrice, int MaximumOccupancy, string? BedType, bool IsActive);

public record SetRoomTypeStatusRequest(bool IsActive);

public class RoomTypeQuery : QueryParameters
{
    private string? _name;
    public string? Name { get => _name; set => _name = value?.Trim(); }
    public bool IncludeInactive { get; set; }
}