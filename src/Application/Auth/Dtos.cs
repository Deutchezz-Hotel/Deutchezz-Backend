namespace Deutchezz.Application.Auth;

public record RegisterRequest(string FirstName, string LastName, string Email, string Password, string? PhoneNumber);

public record LoginRequest(string Email, string Password);

public record RefreshTokenRequest(string RefreshToken);

public record LoginResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt, string Role);

public record CurrentUserResponse(Guid Id, string FirstName, string LastName, string Email, string Role);