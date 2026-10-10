using System.Security.Cryptography;
using System.Text;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Data;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Application.Auth;

public class AuthService(
    UserManager<User> userManager,
    ITokenService tokenService,
    IAppDbContext db,
    IActivityLogger activityLogger) : IAuthService
{
    public async Task<Result<LoginResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Error.Validation("auth.invalid_input", "Email and password are required");

        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
            return Error.Conflict("auth.email_in_use", "Email is already registered");

        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            EmailConfirmed = true,
            RefreshTokenHash = null,
            RefreshTokenExpiresAt = null
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Error.Validation("auth.registration_failed",
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, nameof(UserRole.Customer));

        await activityLogger.LogAsync(user.Id, "user.registered", "User", user.Id,
            $"Registered {request.Email}", cancellationToken);

        var auth = await IssueTokensAsync(user, cancellationToken);
        return auth;
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            return Error.Unauthorized("auth.invalid_credentials", "Invalid email or password");

        if (!user.IsActive)
            return Error.Forbidden("auth.inactive", "Account is inactive");

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(user.Id, "user.login", "User", user.Id, null, cancellationToken);

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<Result<LoginResponse>> RefreshAsync(string currentRefreshToken, CancellationToken cancellationToken)
    {
        var result = await tokenService.RotateRefreshTokenAsync(currentRefreshToken, cancellationToken);
        return new LoginResponse(result.accessToken, result.refreshToken, result.expiresAt, string.Empty);
    }

    public async Task<Result> LogoutAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is not null)
        {
            user.RefreshTokenHash = null;
            user.RefreshTokenExpiresAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        return Result.Success();
    }

    public async Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Error.NotFound("auth.user_not_found", "User not found");

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? nameof(UserRole.Customer);

        return new CurrentUserResponse(user.Id, user.FirstName ?? "", user.LastName ?? "", user.Email ?? "", role);
    }

    private async Task<Result<LoginResponse>> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, expiresAt) = tokenService.GenerateAccessToken(user.Id, roles);
        var refreshToken = tokenService.GenerateRefreshToken();

        user.RefreshTokenHash = HashToken(refreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        await db.SaveChangesAsync(cancellationToken);

        return new LoginResponse(accessToken, refreshToken, expiresAt, roles.FirstOrDefault() ?? nameof(UserRole.Customer));
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}