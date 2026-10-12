namespace Deutchezz.Application.Auth;

public record RegisterRequest(string FirstName, string LastName, string Email, string Password, string? PhoneNumber);

public record LoginRequest(string Email, string Password);

public record RefreshTokenRequest(string RefreshToken);

public record LoginResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt, string Role);

public record CurrentUserResponse(Guid Id, string FirstName, string LastName, string Email, string Role);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record ConfirmEmailRequest(string Email, string Token);

public record MessageResponse(string Message);