using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Profile;

public interface IProfileService
{
    Task<Result<ProfileResponse>> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<ProfileResponse>> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken);
}