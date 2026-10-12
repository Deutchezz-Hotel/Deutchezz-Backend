using System.Security.Cryptography;
using System.Text;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common.Options;
using Deutchezz.Application.Data;
using Deutchezz.Application.Emails;
using Deutchezz.Domain.Common;
using Deutchezz.Domain.Entities;
using Deutchezz.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Deutchezz.Application.Auth;

public class AuthService(
    UserManager<User> userManager,
    ITokenService tokenService,
    IAppDbContext db,
    IActivityLogger activityLogger,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions) : IAuthService
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
            EmailConfirmed = false,
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

        await SendVerificationEmailAsync(user, cancellationToken);

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

        if (emailOptions.Value.RequireConfirmedEmail && !user.EmailConfirmed)
            return Error.Forbidden("auth.email_unconfirmed",
                "Please confirm your email address before signing in");

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

    public async Task<Result<MessageResponse>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        // Always return the same response to avoid leaking whether an account exists.
        var user = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : await userManager.FindByEmailAsync(request.Email);

        if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var url = BuildFrontendUrl("/reset-password", user.Email, token);
            var name = $"{user.FirstName} {user.LastName}".Trim();
            var (subject, html, text) = PasswordResetEmail.Build(new PasswordResetModel(
                string.IsNullOrWhiteSpace(name) ? "there" : name, url, 60));

            // Sent synchronously with CancellationToken.None: the request token is cancelled as
            // soon as the response is written, which would abort a fire-and-forget SMTP send and
            // silently drop the reset link.
            await emailSender.SendAsync(user.Email, subject, html, text, CancellationToken.None);

            await activityLogger.LogAsync(user.Id, "user.password_reset_requested", "User", user.Id, null, cancellationToken);
        }

        return new MessageResponse("If an account exists for that email address, a reset link has been sent.");
    }

    public async Task<Result<MessageResponse>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return Error.Validation("auth.invalid_input", "Email, token and new password are required");

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Error.Validation("auth.invalid_reset", "The reset link is invalid or has expired");

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            return Error.Validation("auth.reset_failed", string.Join(", ", result.Errors.Select(e => e.Description)));

        // Proving control of the reset link also confirms ownership of the email address.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        // Invalidate any existing sessions after a password change.
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(user.Id, "user.password_reset", "User", user.Id, null, cancellationToken);
        return new MessageResponse("Your password has been reset. You can now sign in.");
    }

    public async Task<Result<MessageResponse>> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token))
            return Error.Validation("auth.invalid_input", "Email and token are required");

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Error.Validation("auth.invalid_token", "The confirmation link is invalid or has expired");

        if (user.EmailConfirmed)
            return new MessageResponse("Your email address is already confirmed.");

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return Error.Validation("auth.invalid_token", "The confirmation link is invalid or has expired");

        await activityLogger.LogAsync(user.Id, "user.email_confirmed", "User", user.Id, null, cancellationToken);
        return new MessageResponse("Your email address has been confirmed.");
    }

    private async Task SendVerificationEmailAsync(User user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return;

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var url = BuildFrontendUrl("/verify-email", user.Email, token);
        var name = $"{user.FirstName} {user.LastName}".Trim();
        var (subject, html, text) = EmailVerificationEmail.Build(new EmailVerificationModel(
            string.IsNullOrWhiteSpace(name) ? "there" : name, url));

        // Await with CancellationToken.None so the SMTP handshake is not cancelled when the
        // registration request completes (a cancelled send is swallowed by the email sender).
        await emailSender.SendAsync(user.Email, subject, html, text, CancellationToken.None);
    }

    private string BuildFrontendUrl(string path, string email, string token)
    {
        var baseUrl = emailOptions.Value.FrontendBaseUrl.TrimEnd('/');
        return $"{baseUrl}{path}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
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