using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Deutchezz.Application.Abstractions;
using Deutchezz.Application.Common;
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
    IPasswordHasher<User> passwordHasher,
    ITokenService tokenService,
    IOtpCodeService otp,
    IAppDbContext db,
    IClock clock,
    IActivityLogger activityLogger,
    IEmailSender emailSender,
    IOptions<AuthOptions> authOptions) : IAuthService
{
    private const string NeutralRegisterMessage =
        "If these details can be used to create an account, we have sent a 6-digit verification code to your email address.";

    private const string InvalidCodeMessage = "The code is invalid or has expired.";

    private const string NeutralForgotMessage =
        "If an account exists for that email address, we have sent a 6-digit password reset code.";

    public async Task<Result<RegisterPendingResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
            return Error.Validation("auth.invalid_email", "A valid email address is required");

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return Error.Validation("auth.name_required", "First and last name are required");

        if (!PhoneNumber.TryNormalize(request.PhoneNumber, out var phone))
            return Error.Validation("auth.invalid_phone",
                "Enter a valid phone number, for example 0803 123 4567 or +234 803 123 4567");

        var email = NormalizeEmail(request.Email);

        var scratch = new User { UserName = email, Email = email };
        var passwordErrors = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(userManager, scratch, request.Password);
            if (!validation.Succeeded)
                passwordErrors.AddRange(validation.Errors.Select(e => e.Description));
        }

        if (passwordErrors.Count > 0)
            return Error.Validation("auth.password_requirements", string.Join(" ", passwordErrors));

        // Never reveal whether the email or phone already belongs to an account (BR-028).
        var existingByEmail = await userManager.FindByEmailAsync(email);
        var phoneTaken = phone is not null && await db.Users.AnyAsync(u => u.PhoneNumber == phone, cancellationToken);

        if (existingByEmail is not null || phoneTaken)
        {
            if (existingByEmail is not null)
                await SendExistingAccountNoticeAsync(existingByEmail, cancellationToken);

            return NeutralResponse();
        }

        var now = clock.UtcNow;
        var code = otp.Generate();

        var pending = await db.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email, cancellationToken);
        if (pending is null)
        {
            pending = new PendingRegistration { Email = email, CreatedAt = now };
            db.PendingRegistrations.Add(pending);
        }

        pending.FirstName = request.FirstName.Trim();
        pending.LastName = request.LastName.Trim();
        pending.PhoneNumber = phone;
        pending.PasswordHash = passwordHasher.HashPassword(scratch, request.Password);
        pending.CodeHash = otp.Hash(code);
        pending.CodeExpiresAt = now.AddMinutes(authOptions.Value.CodeMinutes);
        pending.ExpiresAt = now.AddHours(authOptions.Value.PendingRegistrationHours);
        pending.FailedAttempts = 0;
        pending.SendCount = 1;
        pending.SendWindowStartAt = now;
        pending.LastSentAt = now;
        pending.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await SendVerificationCodeAsync(email, DisplayName(pending.FirstName, pending.LastName), code, cancellationToken);

        return NeutralResponse();
    }

    public async Task<Result<LoginResponse>> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Code))
            return CodeError();

        var email = NormalizeEmail(request.Email);
        var pending = await db.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email, cancellationToken);
        if (pending is null)
            return CodeError();

        var now = clock.UtcNow;
        if (pending.ExpiresAt <= now || pending.CodeExpiresAt <= now)
        {
            db.PendingRegistrations.Remove(pending);
            await db.SaveChangesAsync(cancellationToken);
            return CodeError();
        }

        if (pending.FailedAttempts >= authOptions.Value.MaxFailedAttempts)
        {
            db.PendingRegistrations.Remove(pending);
            await db.SaveChangesAsync(cancellationToken);
            return CodeError();
        }

        if (!otp.Verify(pending.CodeHash, request.Code))
        {
            pending.FailedAttempts++;
            if (pending.FailedAttempts >= authOptions.Value.MaxFailedAttempts)
                db.PendingRegistrations.Remove(pending);

            await db.SaveChangesAsync(cancellationToken);
            return CodeError();
        }

        // Re-check uniqueness after the code is proven: the address/phone may have been claimed
        // by another registration in the meantime.
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            db.PendingRegistrations.Remove(pending);
            await db.SaveChangesAsync(cancellationToken);
            return Error.Conflict("auth.email_in_use", "Email is already registered");
        }

        if (pending.PhoneNumber is not null && await db.Users.AnyAsync(u => u.PhoneNumber == pending.PhoneNumber, cancellationToken))
        {
            db.PendingRegistrations.Remove(pending);
            await db.SaveChangesAsync(cancellationToken);
            return Error.Validation("auth.phone_in_use", "That phone number can't be used.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = new User
        {
            UserName = email,
            Email = email,
            FirstName = pending.FirstName,
            LastName = pending.LastName,
            PhoneNumber = pending.PhoneNumber,
            EmailConfirmed = true,
            IsActive = true,
            PasswordHash = pending.PasswordHash
        };

        var create = await userManager.CreateAsync(user);
        if (!create.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Error.Validation("auth.registration_failed",
                string.Join(", ", create.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, nameof(UserRole.Customer));

        db.PendingRegistrations.Remove(pending);
        await db.SaveChangesAsync(cancellationToken);

        await LinkExistingGuestDataAsync(user, cancellationToken);

        await activityLogger.LogAsync(user.Id, "user.registered", "User", user.Id,
            $"Registered {email}", cancellationToken);

        var auth = await IssueTokensAsync(user, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return auth;
    }

    public async Task<Result<RegisterPendingResponse>> ResendCodeAsync(ResendCodeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
            return NeutralResponse();

        var email = NormalizeEmail(request.Email);
        var pending = await db.PendingRegistrations.FirstOrDefaultAsync(p => p.Email == email, cancellationToken);
        if (pending is null)
            return NeutralResponse();

        var now = clock.UtcNow;
        var sendCount = pending.SendCount;
        var windowStart = pending.SendWindowStartAt;
        var lastSent = pending.LastSentAt;
        if (!TryConsumeSend(now, ref sendCount, ref windowStart, ref lastSent))
            return NeutralResponse();

        pending.SendCount = sendCount;
        pending.SendWindowStartAt = windowStart;
        pending.LastSentAt = lastSent;

        var code = otp.Generate();
        pending.CodeHash = otp.Hash(code);
        pending.CodeExpiresAt = now.AddMinutes(authOptions.Value.CodeMinutes);
        pending.FailedAttempts = 0;
        pending.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await SendVerificationCodeAsync(email, DisplayName(pending.FirstName, pending.LastName), code, cancellationToken);

        return NeutralResponse();
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || await userManager.IsLockedOutAsync(user))
            return InvalidCredentials();

        if (!await userManager.CheckPasswordAsync(user, request.Password))
            return InvalidCredentials();

        // Unconfirmed or inactive accounts are reported with the same generic error (BR-029).
        if (!user.EmailConfirmed || !user.IsActive)
            return InvalidCredentials();

        user.LastLoginAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await activityLogger.LogAsync(user.Id, "user.login", "User", user.Id, null, cancellationToken);

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<Result<LoginResponse>> RefreshAsync(string currentRefreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var result = await tokenService.RotateRefreshTokenAsync(currentRefreshToken, cancellationToken);
            return new LoginResponse(result.accessToken, result.refreshToken, result.expiresAt, string.Empty);
        }
        catch (Microsoft.IdentityModel.Tokens.SecurityTokenException)
        {
            return Error.Unauthorized("auth.invalid_refresh_token", "The refresh token is invalid or has expired.");
        }
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
        // Always return the same response to avoid leaking whether an account exists (BR-028).
        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email.Trim(), out _))
            return new MessageResponse(NeutralForgotMessage);

        var email = NormalizeEmail(request.Email);
        var user = await userManager.FindByEmailAsync(email);

        if (user is not null && user.IsActive && user.EmailConfirmed)
        {
            var now = clock.UtcNow;
            var code = otp.Generate();

            var existing = await db.PasswordResetCodes
                .Where(c => c.UserId == user.Id && c.UsedAt == null)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var shouldSend = true;
            if (existing is not null)
            {
                var sendCount = existing.SendCount;
                var windowStart = existing.SendWindowStartAt;
                var lastSent = existing.LastSentAt;
                shouldSend = TryConsumeSend(now, ref sendCount, ref windowStart, ref lastSent);
                if (shouldSend)
                {
                    existing.SendCount = sendCount;
                    existing.SendWindowStartAt = windowStart;
                    existing.LastSentAt = lastSent;
                    existing.CodeHash = otp.Hash(code);
                    existing.ExpiresAt = now.AddMinutes(authOptions.Value.CodeMinutes);
                    existing.FailedAttempts = 0;
                    existing.UpdatedAt = now;
                }
            }
            else
            {
                db.PasswordResetCodes.Add(new PasswordResetCode
                {
                    UserId = user.Id,
                    CodeHash = otp.Hash(code),
                    ExpiresAt = now.AddMinutes(authOptions.Value.CodeMinutes),
                    SendCount = 1,
                    SendWindowStartAt = now,
                    LastSentAt = now,
                    CreatedAt = now
                });
            }

            if (shouldSend)
            {
                await db.SaveChangesAsync(cancellationToken);

                var name = DisplayName(user.FirstName, user.LastName);
                var (subject, html, text) = PasswordResetEmail.Build(
                    new PasswordResetModel(name, code, authOptions.Value.CodeMinutes));
                await emailSender.SendAsync(email, subject, html, text, CancellationToken.None);

                await activityLogger.LogAsync(user.Id, "user.password_reset_requested", "User", user.Id, null, cancellationToken);
            }
        }

        return new MessageResponse(NeutralForgotMessage);
    }

    public async Task<Result<MessageResponse>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.NewPassword))
            return CodeError();

        var email = NormalizeEmail(request.Email);
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return CodeError();

        var now = clock.UtcNow;
        var code = await db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (code is null || code.ExpiresAt <= now)
            return CodeError();

        if (code.FailedAttempts >= authOptions.Value.MaxFailedAttempts)
        {
            db.PasswordResetCodes.Remove(code);
            await db.SaveChangesAsync(cancellationToken);
            return CodeError();
        }

        if (!otp.Verify(code.CodeHash, request.Code))
        {
            code.FailedAttempts++;
            if (code.FailedAttempts >= authOptions.Value.MaxFailedAttempts)
                db.PasswordResetCodes.Remove(code);

            await db.SaveChangesAsync(cancellationToken);
            return CodeError();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!reset.Succeeded)
            return Error.Validation("auth.password_requirements", string.Join(" ", reset.Errors.Select(e => e.Description)));

        code.UsedAt = now;
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);

        var name = DisplayName(user.FirstName, user.LastName);
        var (subject, html, text) = PasswordChangedEmail.Build(new PasswordChangedModel(name, now));
        await emailSender.SendAsync(email, subject, html, text, CancellationToken.None);

        await activityLogger.LogAsync(user.Id, "user.password_reset", "User", user.Id, null, cancellationToken);

        return new MessageResponse("Your password has been reset. You can now sign in.");
    }

    private async Task LinkExistingGuestDataAsync(User user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return;

        var normalizedEmail = user.Email.Trim().ToLowerInvariant();

        // A visitor may book anonymously before registering. Adopt those guest rows so the
        // new account immediately sees the bookings in its dashboard, and re-point any
        // notifications that were stored against the guest without a user id.
        var orphans = await db.Guests
            .Where(g => g.UserId == null && g.Email != null && g.Email.Trim().ToLower() == normalizedEmail)
            .ToListAsync(cancellationToken);

        if (orphans.Count == 0)
            return;

        var guestIds = orphans.Select(g => g.Id).ToList();

        foreach (var orphan in orphans)
        {
            orphan.UserId = user.Id;
            if (!string.IsNullOrWhiteSpace(user.FirstName)) orphan.FirstName = user.FirstName;
            if (!string.IsNullOrWhiteSpace(user.LastName)) orphan.LastName = user.LastName;
            if (!string.IsNullOrWhiteSpace(user.PhoneNumber)) orphan.PhoneNumber = user.PhoneNumber;
        }

        var notifications = await db.Notifications
            .Where(n => n.UserId == null && n.GuestId != null && guestIds.Contains(n.GuestId.Value))
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
            notification.UserId = user.Id;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendVerificationCodeAsync(string email, string name, string code, CancellationToken cancellationToken)
    {
        var (subject, html, text) = EmailVerificationEmail.Build(
            new EmailVerificationModel(name, code, authOptions.Value.CodeMinutes));

        // Await with CancellationToken.None so the SMTP handshake is not cancelled when the
        // request completes (a cancelled send is swallowed by the email sender).
        await emailSender.SendAsync(email, subject, html, text, CancellationToken.None);
    }

    private async Task SendExistingAccountNoticeAsync(User user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return;

        var name = DisplayName(user.FirstName, user.LastName);
        var (subject, html, text) = ExistingAccountEmail.Build(new ExistingAccountModel(name));
        await emailSender.SendAsync(user.Email, subject, html, text, CancellationToken.None);
    }

    private bool TryConsumeSend(DateTime now, ref int sendCount, ref DateTime windowStart, ref DateTime lastSent)
    {
        var cooldown = TimeSpan.FromSeconds(authOptions.Value.ResendCooldownSeconds);
        if (cooldown > TimeSpan.Zero && now - lastSent < cooldown)
            return false;

        if (now - windowStart >= TimeSpan.FromHours(1))
        {
            windowStart = now;
            sendCount = 0;
        }

        if (sendCount >= authOptions.Value.MaxSendsPerHour)
            return false;

        sendCount++;
        lastSent = now;
        return true;
    }

    private async Task<Result<LoginResponse>> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, expiresAt) = tokenService.GenerateAccessToken(user.Id, roles);
        var refreshToken = tokenService.GenerateRefreshToken();

        user.RefreshTokenHash = HashToken(refreshToken);
        user.RefreshTokenExpiresAt = clock.UtcNow.AddDays(7);
        await db.SaveChangesAsync(cancellationToken);

        return new LoginResponse(accessToken, refreshToken, expiresAt, roles.FirstOrDefault() ?? nameof(UserRole.Customer));
    }

    private RegisterPendingResponse NeutralResponse() =>
        new(NeutralRegisterMessage, authOptions.Value.ResendCooldownSeconds);

    private static Error CodeError() => Error.Validation("auth.invalid_or_expired_code", InvalidCodeMessage);

    private static Error InvalidCredentials() =>
        Error.Unauthorized("auth.invalid_credentials", "Invalid email or password");

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string DisplayName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "there" : name;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
