using Deutchezz.Application.Common;
using Deutchezz.Domain.Common;

namespace Deutchezz.Application.RoomTypes;

public interface IRoomTypesService
{
    Task<Result<PaginatedResult<RoomTypeListItemResponse>>> ListAsync(RoomTypeQuery query, CancellationToken cancellationToken);
    Task<Result<RoomTypeResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<RoomTypeResponse>> GetByIdForAdminAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<RoomTypeResponse>> CreateAsync(CreateRoomTypeRequest request, Guid adminUserId, CancellationToken cancellationToken);
    Task<Result<RoomTypeResponse>> UpdateAsync(Guid id, UpdateRoomTypeRequest request, Guid adminUserId, CancellationToken cancellationToken);
    Task<Result<RoomTypeResponse>> SetActiveAsync(Guid id, bool isActive, Guid adminUserId, CancellationToken cancellationToken);
}