using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Availability;

public interface IAvailabilityService
{
    Task<Result<AvailabilityResponse>> SearchAsync(AvailabilitySearchRequest request, CancellationToken cancellationToken);
}