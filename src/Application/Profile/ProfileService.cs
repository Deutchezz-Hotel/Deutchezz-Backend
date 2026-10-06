using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Profile;

public class ProfileService(IAppDbContext db) : IProfileService
{
    public async Task<Result<ProfileResponse>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound("profile.user_not_found", "User not found");

        return new ProfileResponse(user.Id, user.FullName ?? "", user.Email ?? "", user.PhoneNumber);
    }

    public async Task<Result<ProfileResponse>> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound("profile.user_not_found", "User not found");

        if (request.FullName is not null)
            user.FullName = request.FullName;
        if (request.Phone is not null)
            user.PhoneNumber = request.Phone;

        await db.SaveChangesAsync(cancellationToken);

        return new ProfileResponse(user.Id, user.FullName ?? "", user.Email ?? "", user.PhoneNumber);
    }
}