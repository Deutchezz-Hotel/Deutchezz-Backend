using Deutchezz.Application.Common;
using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Bookings;

public interface IBookingService
{
    Task<Result<CreateBookingResponse>> CreateAsync(CreateBookingRequest request, Guid? userId, string idempotencyKey, CancellationToken cancellationToken);
    Task<Result<CreateBookingResponse>> ReceptionCreateAsync(ReceptionCreateBookingRequest request, Guid staffUserId, string idempotencyKey, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> LookupAsync(BookingLookupRequest request, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> CancelAsync(Guid? id, BookingLookupRequest? lookup, string? reason, Guid? userId, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> ReceptionCancelAsync(Guid id, ReceptionCancelBookingRequest request, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> GetByIdForOwnerAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<Result<PaginatedResult<BookingListItemResponse>>> ListForCustomerAsync(Guid userId, BookingSearchQuery query, CancellationToken cancellationToken);
    Task<Result<PaginatedResult<BookingListItemResponse>>> SearchAsync(BookingSearchQuery query, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> GetDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> AssignRoomsAsync(Guid id, RoomAssignmentRequest request, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result<CheckInResponse>> CheckInAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result<CheckOutResponse>> CheckOutAsync(Guid id, CheckOutRequest? request, Guid staffUserId, bool actorIsAdministrator, CancellationToken cancellationToken);
    Task<Result<ChangeRoomTypeResponse>> ChangeRoomTypeAsync(Guid id, ChangeRoomTypeRequest request, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> ReassignRoomAsync(Guid id, ReassignRoomRequest request, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> UnassignRoomAsync(Guid id, UnassignRoomRequest request, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result<BookingDetailResponse>> RestoreAsync(Guid id, Guid adminUserId, CancellationToken cancellationToken);
}