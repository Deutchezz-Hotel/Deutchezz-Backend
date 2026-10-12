using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Auth;

public interface IAuthService
{
    /// <summary>Stores an unverified pending registration and emails a code (no account yet).</summary>
    Task<Result<RegisterPendingResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    /// <summary>Confirms the emailed code, creates the account and signs the user in.</summary>
    Task<Result<LoginResponse>> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);

    /// <summary>Re-sends a verification code for an existing pending registration (neutral).</summary>
    Task<Result<RegisterPendingResponse>> ResendCodeAsync(ResendCodeRequest request, CancellationToken cancellationToken);

    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<Result<LoginResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task<Result> LogoutAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<MessageResponse>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);
    Task<Result<MessageResponse>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
