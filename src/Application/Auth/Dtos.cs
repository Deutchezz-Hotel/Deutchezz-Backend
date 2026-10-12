namespace Deutchezz.Application.Auth;

public record RegisterRequest(string FirstName, string LastName, string Email, string Password, string? PhoneNumber);

/// <summary>Neutral acknowledgment for register/resend/forgot (BR-028).</summary>
public record RegisterPendingResponse(string Message, int ResendAvailableInSeconds);

public record VerifyEmailRequest(string Email, string Code);

public record ResendCodeRequest(string Email);

public record LoginRequest(string Email, string Password);

public record RefreshTokenRequest(string RefreshToken);

public record LoginResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt, string Role);

public record CurrentUserResponse(Guid Id, string FirstName, string LastName, string Email, string Role);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Code, string NewPassword);

public record MessageResponse(string Message);
