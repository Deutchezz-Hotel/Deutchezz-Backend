using Deutchezz.Application.Common;
using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Rooms;

public interface IRoomsService
{
    Task<Result<PaginatedResult<RoomListItemResponse>>> ListAsync(RoomQuery query, CancellationToken cancellationToken);
    Task<Result<RoomResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<RoomResponse>> CreateAsync(CreateRoomRequest request, Guid adminUserId, CancellationToken cancellationToken);
    Task<Result<RoomResponse>> UpdateAsync(Guid id, UpdateRoomRequest request, Guid adminUserId, CancellationToken cancellationToken);
    Task<Result> SetStatusAsync(Guid id, string status, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result> DeactivateAsync(Guid id, Guid adminUserId, CancellationToken cancellationToken);
}