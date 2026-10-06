namespace Deutchezz.Application.Auth;

public record RegisterRequest(string FullName, string Email, string Password, string? Phone);

public record LoginRequest(string Email, string Password);

public record RefreshTokenRequest(string RefreshToken);

public record AuthResult(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt, string Role);

public record CurrentUserResponse(Guid Id, string FullName, string Email, string Role);