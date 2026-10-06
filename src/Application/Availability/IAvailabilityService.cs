using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Availability;

public interface IAvailabilityService
{
    Task<Result<AvailabilityResponse>> SearchAsync(DateOnly checkInDate, DateOnly checkOutDate, CancellationToken cancellationToken);
    Task<Result<CalendarResponse>> CalendarAsync(Guid roomTypeId, int year, int month, CancellationToken cancellationToken);
}