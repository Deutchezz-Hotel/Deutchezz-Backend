using Deutchezz.Application.Common;
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

        string? normalizedPhone = null;
        if (request.PhoneNumber is not null)
        {
            if (!PhoneNumber.TryNormalize(request.PhoneNumber, out normalizedPhone))
                return Error.Validation("auth.invalid_phone",
                    "Enter a valid phone number, for example 0803 123 4567 or +234 803 123 4567");

            if (normalizedPhone is not null &&
                await db.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone && u.Id != userId, cancellationToken))
                return Error.Validation("auth.phone_in_use", "That phone number can't be used.");
        }

        if (request.FirstName is not null)
            user.FirstName = request.FirstName;
        if (request.LastName is not null)
            user.LastName = request.LastName;
        if (request.PhoneNumber is not null)
            user.PhoneNumber = normalizedPhone;

        // Keep linked guest rows in step so booking displays and guest lists show the
        // account's current name/phone after a profile edit.
        var linkedGuests = await db.Guests
            .Where(g => g.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var guest in linkedGuests)
        {
            if (request.FirstName is not null)
                guest.FirstName = request.FirstName;
            if (request.LastName is not null)
                guest.LastName = request.LastName;
            if (request.PhoneNumber is not null)
                guest.PhoneNumber = normalizedPhone;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ProfileResponse(user.Id, user.FirstName ?? "", user.LastName ?? "", user.Email ?? "", user.PhoneNumber);
    }
}