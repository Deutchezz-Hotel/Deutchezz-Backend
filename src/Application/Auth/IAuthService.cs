using Deutchezz.Domain.Common;

namespace Deutchezz.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResult>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<Result<AuthResult>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task<Result> LogoutAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}