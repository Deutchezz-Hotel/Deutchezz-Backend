using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Deutchezz.Application.Abstractions;
using Deutchezz.Infrastructure.Data;
using Deutchezz.Application.Common.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Deutchezz.Infrastructure.Services;

public class TokenService(IOptions<JwtOptions> jwtOptions, AppDbContext context) : ITokenService
{
    public (string token, DateTime expiresAt) GenerateAccessToken(Guid userId, IEnumerable<string> roles)
    {
        var settings = jwtOptions.Value;
        var keyBytes = Encoding.UTF8.GetBytes(settings.Key);
        var expiresAt = DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public async Task<(string accessToken, string refreshToken, DateTime expiresAt)> RotateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = HashToken(refreshToken);
        var user = await context.Users.FirstOrDefaultAsync(u => u.RefreshTokenHash == hash, cancellationToken);
        if (user == null)
            throw new SecurityTokenException("Invalid refresh token");

        var roles = await context.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => context.Roles.FirstOrDefault(r => r.Id == ur.RoleId)!.Name!)
            .ToListAsync(cancellationToken);

        var (accessToken, expiresAt) = GenerateAccessToken(user.Id, roles);
        var newRefreshToken = GenerateRefreshToken();
        user.RefreshTokenHash = HashToken(newRefreshToken);
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays);
        await context.SaveChangesAsync(cancellationToken);
        return (accessToken, newRefreshToken, expiresAt);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}