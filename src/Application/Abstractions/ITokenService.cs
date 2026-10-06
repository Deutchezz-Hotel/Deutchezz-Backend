namespace Deutchezz.Application.Abstractions;

public interface ITokenService
{
    (string token, DateTime expiresAt) GenerateAccessToken(Guid userId, IEnumerable<string> roles);
    string GenerateRefreshToken();
    Task<(string accessToken, string refreshToken, DateTime expiresAt)> RotateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);
}