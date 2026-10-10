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

        return new ProfileResponse(user.Id, user.FirstName ?? "", user.LastName ?? "", user.Email ?? "", user.PhoneNumber);
    }

    public async Task<Result<ProfileResponse>> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Error.NotFound("profile.user_not_found", "User not found");

        if (request.FirstName is not null)
            user.FirstName = request.FirstName;
        if (request.LastName is not null)
            user.LastName = request.LastName;
        if (request.PhoneNumber is not null)
            user.PhoneNumber = request.PhoneNumber;

        await db.SaveChangesAsync(cancellationToken);

        return new ProfileResponse(user.Id, user.FirstName ?? "", user.LastName ?? "", user.Email ?? "", user.PhoneNumber);
    }
}