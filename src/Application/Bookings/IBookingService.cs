using Deutchezz.Application.Common;
using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Bookings;

public interface IBookingService
{
    Task<Result<CreateBookingResponse>> CreateAsync(CreateBookingRequest request, Guid? userId, CancellationToken cancellationToken);
    Task<Result<BookingResponse>> LookupAsync(BookingLookupRequest request, CancellationToken cancellationToken);
    Task<Result<BookingResponse>> CancelAsync(Guid? id, BookingLookupRequest? lookup, string? reason, Guid? userId, CancellationToken cancellationToken);
    Task<Result<BookingResponse>> GetByIdForOwnerAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<Result<PaginatedResult<BookingSummaryResponse>>> SearchAsync(BookingSearchQuery query, CancellationToken cancellationToken);
    Task<Result<BookingResponse>> GetDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<BookingResponse>> AssignAndCheckInAsync(Guid id, CheckInRequest request, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result<BookingResponse>> CheckOutAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken);
    Task<Result<BookingResponse>> MarkNoShowAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken);
}